// Mori.SkyScope — Pointer/wheel/keyboard gestures as a pure reducer: (state, event) → (state, effects).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Scene;

/// <summary>
/// Pointer/wheel/keyboard gestures as a pure reducer: (state, event) → (state, effects).
/// Renderers translate platform events into <see cref="InputEvent"/>s and apply <see cref="Effect"/>s to a
/// camera or chart scales. Mirrors <c>scene/interaction.ts</c>; pinned by <c>spec/fixtures/interaction.json</c>.
/// </summary>
public readonly record struct Modifiers(bool Shift = false, bool Ctrl = false, bool Alt = false);

/// <summary>Base of the platform-neutral input events a renderer feeds to <see cref="Interaction.Reduce"/>. Positions are screen pixels relative to the view's top-left corner.</summary>
public abstract record InputEvent
{
    /// <summary>A pointer button was pressed. <paramref name="Button"/> follows the DOM convention: 0 primary, 1 middle, 2 secondary.</summary>
    public sealed record PointerDown(double X, double Y, int Button, Modifiers Modifiers = default) : InputEvent;
    /// <summary>The pointer moved, with or without a button held.</summary>
    public sealed record PointerMove(double X, double Y, Modifiers Modifiers = default) : InputEvent;
    /// <summary>A pointer button was released; completes a click or a drag.</summary>
    public sealed record PointerUp(double X, double Y, int Button, Modifiers Modifiers = default) : InputEvent;
    /// <summary>Pointer capture was lost (window blur, touch cancel); any gesture in progress is abandoned.</summary>
    public sealed record PointerCancel : InputEvent;
    /// <summary>Wheel rotation over the view. Positive <paramref name="DeltaY"/> (scrolling down) zooms out; the factor is <c>WheelZoomBase ^ (−DeltaY / WheelZoomDivisor)</c>.</summary>
    public sealed record Wheel(double X, double Y, double DeltaY, Modifiers Modifiers = default) : InputEvent;
    /// <summary>Primary-button double click; reduces to <see cref="Effect.Reset"/>.</summary>
    public sealed record DoubleClick(double X, double Y) : InputEvent;
    /// <summary>A key went down. <paramref name="Key"/> uses DOM <c>KeyboardEvent.key</c> names, for example <c>" "</c> or <c>"Escape"</c>.</summary>
    public sealed record KeyDown(string Key) : InputEvent;
    /// <summary>A key was released; only the space bar is tracked by the reducer.</summary>
    public sealed record KeyUp(string Key) : InputEvent;
}

/// <summary>Gesture bound to the primary button: <c>Pan</c> drags the view, <c>BoxZoom</c> rubber-bands a zoom rectangle, <c>Cursor</c> and <c>Select</c> emit hover, click and drag effects for the host to interpret.</summary>
public enum Tool { Pan, BoxZoom, Cursor, Select }
/// <summary>Where a pointer gesture stands: <c>Idle</c>, <c>Pressing</c> (button down, still within the click slop) or <c>Dragging</c> (moved beyond the slop).</summary>
public enum Phase { Idle, Pressing, Dragging }

/// <summary>Immutable reducer state. Start from <see cref="Initial"/> and thread the state returned by <see cref="Interaction.Reduce"/>.</summary>
/// <param name="Tool">Gesture the primary button performs by default.</param>
/// <param name="Phase">Current gesture phase.</param>
/// <param name="Gesture">Gesture resolved at press time: the middle button or a held space bar forces <see cref="Tool.Pan"/>, shift forces <see cref="Tool.BoxZoom"/>; null while idle or for an unbound button.</param>
/// <param name="Start">Screen position of the press; null while idle.</param>
/// <param name="Last">Screen position of the last move; null while idle.</param>
/// <param name="Button">Button that started the gesture.</param>
/// <param name="SpaceHeld">True between a space key down and up.</param>
public sealed record InteractionState(Tool Tool, Phase Phase, Tool? Gesture, Vec2? Start, Vec2? Last, int Button, bool SpaceHeld)
{
    /// <summary>The idle state for <paramref name="tool"/>.</summary>
    public static InteractionState Initial(Tool tool = Tool.Pan) => new(tool, Phase.Idle, null, null, null, 0, false);
    internal InteractionState Idle() => this with { Phase = Phase.Idle, Gesture = null, Start = null, Last = null };
}

/// <summary>What one reducer step asks the host to do, in order. Camera effects are applied by <see cref="Interaction.ApplyToCamera"/>; the rest are for the host.</summary>
public abstract record Effect
{
    /// <summary>Drag the content by (<paramref name="Dx"/>, <paramref name="Dy"/>) screen pixels.</summary>
    public sealed record Pan(double Dx, double Dy) : Effect;
    /// <summary>Multiply the zoom by <paramref name="Factor"/>, keeping the screen point (<paramref name="X"/>, <paramref name="Y"/>) fixed.</summary>
    public sealed record Zoom(double X, double Y, double Factor) : Effect;
    /// <summary>The rubber band so far, as a normalized screen rectangle.</summary>
    public sealed record BoxZoomPreview(Rect Rect) : Effect;
    /// <summary>Zoom to this screen rectangle; emitted on release when the box is at least <see cref="InteractionOptions.MinBoxZoom"/> pixels in both dimensions.</summary>
    public sealed record BoxZoom(Rect Rect) : Effect;
    /// <summary>Hide the rubber band without zooming (Escape, pointer cancel, or a box that is too small).</summary>
    public sealed record BoxZoomCancel : Effect;
    /// <summary>The pointer is at (<paramref name="X"/>, <paramref name="Y"/>) with no gesture in progress; also emitted while dragging with the <see cref="Tool.Cursor"/> tool.</summary>
    public sealed record Hover(double X, double Y) : Effect;
    /// <summary>A press and release that never left the click slop.</summary>
    public sealed record Click(double X, double Y, int Button, Modifiers Modifiers) : Effect;
    /// <summary>A <see cref="Tool.Cursor"/> or <see cref="Tool.Select"/> drag began at the press position.</summary>
    public sealed record DragStart(double X, double Y) : Effect;
    /// <summary>A Cursor/Select drag moved to (<paramref name="X"/>, <paramref name="Y"/>), by (<paramref name="Dx"/>, <paramref name="Dy"/>) since the previous move.</summary>
    public sealed record Drag(double X, double Y, double Dx, double Dy) : Effect;
    /// <summary>A Cursor/Select drag was released at (<paramref name="X"/>, <paramref name="Y"/>).</summary>
    public sealed record DragEnd(double X, double Y) : Effect;
    /// <summary>Double click: hosts normally fit the whole scene.</summary>
    public sealed record Reset : Effect;
}

/// <summary>Reducer tunables, all in screen pixels except the wheel terms.</summary>
/// <param name="ClickSlop">Movement allowed before a press becomes a drag.</param>
/// <param name="WheelZoomBase">Base of the wheel zoom factor <c>base ^ (−deltaY / divisor)</c>.</param>
/// <param name="WheelZoomDivisor">Wheel delta that yields one factor of <paramref name="WheelZoomBase"/>.</param>
/// <param name="MinBoxZoom">Minimum width and height of a box zoom; smaller boxes cancel.</param>
public sealed record InteractionOptions(double ClickSlop = 3, double WheelZoomBase = 2, double WheelZoomDivisor = 400, double MinBoxZoom = 8)
{
    /// <summary>The defaults declared on the record.</summary>
    public static readonly InteractionOptions Default = new();
}

/// <summary>The pure gesture reducer and the camera applier; holds no state of its own.</summary>
public static class Interaction
{
    /// <summary>One reducer step. Never mutates <paramref name="state"/>; unknown events pass through unchanged.</summary>
    /// <returns>The next state and the effects to apply, in order (possibly none).</returns>
    public static (InteractionState State, List<Effect> Effects) Reduce(InteractionState state, InputEvent ev, InteractionOptions? options = null)
    {
        var o = options ?? InteractionOptions.Default;
        var effects = new List<Effect>();
        var s = state;
        switch (ev)
        {
            case InputEvent.KeyDown k:
                if (k.Key == " ") s = s with { SpaceHeld = true };
                else if (k.Key == "Escape" && s.Phase != Phase.Idle) { if (s.Gesture == Tool.BoxZoom && s.Phase == Phase.Dragging) effects.Add(new Effect.BoxZoomCancel()); s = s.Idle(); }
                break;
            case InputEvent.KeyUp k:
                if (k.Key == " ") s = s with { SpaceHeld = false };
                break;
            case InputEvent.PointerDown d:
            {
                if (s.Phase != Phase.Idle) break;
                Tool? gesture = d.Button == 1 || (d.Button == 0 && s.SpaceHeld) ? Tool.Pan : d.Button == 0 ? (d.Modifiers.Shift ? Tool.BoxZoom : s.Tool) : null;
                s = s with { Phase = Phase.Pressing, Gesture = gesture, Start = new Vec2(d.X, d.Y), Last = new Vec2(d.X, d.Y), Button = d.Button };
                break;
            }
            case InputEvent.PointerMove m:
            {
                if (s.Phase == Phase.Idle) { effects.Add(new Effect.Hover(m.X, m.Y)); break; }
                var start = s.Start!.Value; var last = s.Last!.Value;
                if (s.Phase == Phase.Pressing)
                {
                    if (Math.Abs(m.X - start.X) <= o.ClickSlop && Math.Abs(m.Y - start.Y) <= o.ClickSlop) break;
                    s = s with { Phase = Phase.Dragging };
                    if (s.Gesture is Tool.Select or Tool.Cursor) effects.Add(new Effect.DragStart(start.X, start.Y));
                }
                double dx = m.X - last.X, dy = m.Y - last.Y;
                switch (s.Gesture)
                {
                    case Tool.Pan: effects.Add(new Effect.Pan(dx, dy)); break;
                    case Tool.BoxZoom: effects.Add(new Effect.BoxZoomPreview(Geometry.RectNormalize(start.X, start.Y, m.X, m.Y))); break;
                    case Tool.Cursor: effects.Add(new Effect.Hover(m.X, m.Y)); effects.Add(new Effect.Drag(m.X, m.Y, dx, dy)); break;
                    case Tool.Select: effects.Add(new Effect.Drag(m.X, m.Y, dx, dy)); break;
                }
                s = s with { Last = new Vec2(m.X, m.Y) };
                break;
            }
            case InputEvent.PointerUp u:
            {
                if (s.Phase == Phase.Idle) break;
                var start = s.Start!.Value;
                if (s.Phase == Phase.Pressing) effects.Add(new Effect.Click(u.X, u.Y, s.Button, u.Modifiers));
                else if (s.Gesture == Tool.BoxZoom)
                {
                    var r = Geometry.RectNormalize(start.X, start.Y, u.X, u.Y);
                    effects.Add(r.W >= o.MinBoxZoom && r.H >= o.MinBoxZoom ? new Effect.BoxZoom(r) : new Effect.BoxZoomCancel());
                }
                else if (s.Gesture is Tool.Select or Tool.Cursor) effects.Add(new Effect.DragEnd(u.X, u.Y));
                s = s.Idle();
                break;
            }
            case InputEvent.PointerCancel:
                if (s.Phase == Phase.Dragging && s.Gesture == Tool.BoxZoom) effects.Add(new Effect.BoxZoomCancel());
                s = s.Idle();
                break;
            case InputEvent.Wheel w:
                effects.Add(new Effect.Zoom(w.X, w.Y, Math.Pow(o.WheelZoomBase, -w.DeltaY / o.WheelZoomDivisor)));
                break;
            case InputEvent.DoubleClick:
                effects.Add(new Effect.Reset());
                break;
        }
        return (s, effects);
    }

    /// <summary>Apply camera-relevant effects to a <see cref="Camera2D"/>. Returns true when handled.</summary>
    public static bool ApplyToCamera(Camera2D camera, Effect effect)
    {
        switch (effect)
        {
            case Effect.Pan p: camera.Pan(p.Dx, p.Dy); return true;
            case Effect.Zoom z: camera.ZoomAt(z.X, z.Y, z.Factor); return true;
            case Effect.BoxZoom b: camera.FitScreenRect(b.Rect); return true;
            default: return false;
        }
    }
}
