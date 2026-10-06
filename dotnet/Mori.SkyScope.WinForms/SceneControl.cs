// Mori.SkyScope — Windows Forms host for the 2D scene view: mouse and keyboard become core input events.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Render.Skia;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// Hosts a <see cref="SceneController"/>: mouse and keyboard become core input events (pan, wheel zoom, shift-drag box
/// zoom, measure, select, Q/E rotate, R reset rotation, F fit all). Repaints on interaction and on <see cref="Refresh()"/>.
/// </summary>
[ToolboxItem(true), Description("2D scene view: maps, lidar clouds, poses, paths, shapes.")]
public class SceneControl : SkiaHostControl
{
    /// <summary>The controller owning the scene, camera, tools and HUD; this control only forwards input to it and paints it.</summary>
    [Browsable(false)] public SceneController Controller { get; } = new();
    /// <summary>The scene whose layers are drawn; shortcut for <c>Controller.Scene</c>.</summary>
    [Browsable(false)] public Scene Scene => Controller.Scene;
    /// <summary>The active tool (pan, box zoom, measure, select); setting it repaints.</summary>
    [Category("Behavior"), DefaultValue(SceneTool.Pan)]
    public SceneTool Tool { get => Controller.ActiveTool; set { Controller.SetTool(value); Redraw(); } }

    /// <summary>Request a repaint after changing layers.</summary>
    public new void Refresh() => Redraw();
    /// <summary>Fit the camera to every layer and repaint.</summary>
    public void FitAll() { Controller.FitAll(); Redraw(); }

    private static int Button(MouseButtons b) => b switch { MouseButtons.Left => 0, MouseButtons.Middle => 1, MouseButtons.Right => 2, _ => 0 };
    private static Modifiers Mods() => new(ModifierKeys.HasFlag(Keys.Shift), ModifierKeys.HasFlag(Keys.Control), ModifierKeys.HasFlag(Keys.Alt));
    private static string? KeyName(Keys k) => k switch { Keys.Space => " ", Keys.Q => "q", Keys.E => "e", Keys.R => "r", Keys.F => "f", Keys.Escape => "Escape", _ => null };
    private void Dispatch(InputEvent ev) { if (Controller.Handle(ev)) Redraw(); }

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Controller.SetViewport(LogicalWidth, LogicalHeight); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Capture = true;
        var (x, y) = Logical(e);
        if (e.Clicks == 2 && e.Button == MouseButtons.Left) { Dispatch(new InputEvent.DoubleClick(x, y)); return; }
        Dispatch(new InputEvent.PointerDown(x, y, Button(e.Button), Mods()));
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var (x, y) = Logical(e); Dispatch(new InputEvent.PointerMove(x, y, Mods())); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; var (x, y) = Logical(e); Dispatch(new InputEvent.PointerUp(x, y, Button(e.Button), Mods())); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); var (x, y) = Logical(e); Dispatch(new InputEvent.Wheel(x, y, -e.Delta, Mods())); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (KeyName(e.KeyCode) is { } k) { Dispatch(new InputEvent.KeyDown(k)); e.Handled = true; } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (KeyName(e.KeyCode) is { } k) Dispatch(new InputEvent.KeyUp(k)); }

    protected override void OnPaintSurface(SkiaPaintEventArgs e)
    {
        using var painter = new SkiaPainter(e.Canvas, e.Width, e.Height, e.Scale);
        Controller.Draw(painter, e.Width, e.Height);
    }
}
