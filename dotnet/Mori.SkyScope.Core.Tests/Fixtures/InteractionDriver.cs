// Mori.SkyScope — Fixture driver for the pointer interaction reducer.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the pointer interaction reducer: feeds input events and records the effects it emits.</summary>
public sealed class InteractionDriver : IFixtureDriver
{
    /// <summary>Handles the <c>interaction</c> fixtures.</summary>
    public string Component => "interaction";
    private sealed record State(InteractionState S, List<object?> Effects);

    private static Modifiers Mods(JsonElement e) => e.TryGetProperty("modifiers", out var m)
        ? new Modifiers(m.TryGetProperty("shift", out var s) && s.GetBoolean(), m.TryGetProperty("ctrl", out var c) && c.GetBoolean(), m.TryGetProperty("alt", out var a) && a.GetBoolean())
        : default;
    private static double D(JsonElement e, string n) => e.GetProperty(n).GetDouble();

    private static InputEvent Parse(JsonElement e) => e.GetProperty("type").GetString() switch
    {
        "pointerdown" => new InputEvent.PointerDown(D(e, "x"), D(e, "y"), e.GetProperty("button").GetInt32(), Mods(e)),
        "pointermove" => new InputEvent.PointerMove(D(e, "x"), D(e, "y"), Mods(e)),
        "pointerup" => new InputEvent.PointerUp(D(e, "x"), D(e, "y"), e.GetProperty("button").GetInt32(), Mods(e)),
        "pointercancel" => new InputEvent.PointerCancel(),
        "wheel" => new InputEvent.Wheel(D(e, "x"), D(e, "y"), D(e, "deltaY"), Mods(e)),
        "dblclick" => new InputEvent.DoubleClick(D(e, "x"), D(e, "y")),
        "keydown" => new InputEvent.KeyDown(e.GetProperty("key").GetString()!),
        "keyup" => new InputEvent.KeyUp(e.GetProperty("key").GetString()!),
        var t => throw new InvalidOperationException($"unknown event {t}"),
    };

    private static string Lower(Tool t) => t switch { Tool.Pan => "pan", Tool.BoxZoom => "boxZoom", Tool.Cursor => "cursor", _ => "select" };
    private static object R(Rect r) => new { x = r.X, y = r.Y, w = r.W, h = r.H };

    /// <summary>Parses an effect object (pan, zoom, box zoom, hover, click, drag, reset); also used by the <c>effect</c> step of the trend driver.</summary>
    public static Effect ParseEffect(JsonElement e)
    {
        static Rect R(JsonElement r) => new(r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble(), r.GetProperty("w").GetDouble(), r.GetProperty("h").GetDouble());
        return e.GetProperty("type").GetString() switch
        {
            "pan" => new Effect.Pan(D(e, "dx"), D(e, "dy")),
            "zoom" => new Effect.Zoom(D(e, "x"), D(e, "y"), D(e, "factor")),
            "boxZoomPreview" => new Effect.BoxZoomPreview(R(e.GetProperty("rect"))),
            "boxZoom" => new Effect.BoxZoom(R(e.GetProperty("rect"))),
            "boxZoomCancel" => new Effect.BoxZoomCancel(),
            "hover" => new Effect.Hover(D(e, "x"), D(e, "y")),
            "click" => new Effect.Click(D(e, "x"), D(e, "y"), e.GetProperty("button").GetInt32(), Mods(e)),
            "dragStart" => new Effect.DragStart(D(e, "x"), D(e, "y")),
            "drag" => new Effect.Drag(D(e, "x"), D(e, "y"), D(e, "dx"), D(e, "dy")),
            "dragEnd" => new Effect.DragEnd(D(e, "x"), D(e, "y")),
            "reset" => new Effect.Reset(),
            var t => throw new InvalidOperationException($"unknown effect {t}"),
        };
    }

    /// <summary>Serializes an effect to the same JSON shape the TS reducer emits.</summary>
    public static object ToJson(Effect e) => e switch
    {
        Effect.Pan p => new { type = "pan", dx = p.Dx, dy = p.Dy },
        Effect.Zoom z => new { type = "zoom", x = z.X, y = z.Y, factor = z.Factor },
        Effect.BoxZoomPreview b => new { type = "boxZoomPreview", rect = R(b.Rect) },
        Effect.BoxZoom b => new { type = "boxZoom", rect = R(b.Rect) },
        Effect.BoxZoomCancel => new { type = "boxZoomCancel" },
        Effect.Hover h => new { type = "hover", x = h.X, y = h.Y },
        Effect.Click c => new { type = "click", x = c.X, y = c.Y, button = c.Button, modifiers = new { shift = c.Modifiers.Shift, ctrl = c.Modifiers.Ctrl, alt = c.Modifiers.Alt } },
        Effect.DragStart d => new { type = "dragStart", x = d.X, y = d.Y },
        Effect.Drag d => new { type = "drag", x = d.X, y = d.Y, dx = d.Dx, dy = d.Dy },
        Effect.DragEnd d => new { type = "dragEnd", x = d.X, y = d.Y },
        Effect.Reset => new { type = "reset" },
        _ => throw new InvalidOperationException(e.ToString()),
    };

    /// <summary>Starts the reducer in its initial state for the chosen <c>tool</c> (pan by default).</summary>
    public object Create(JsonElement setup)
    {
        var tool = setup.TryGetProperty("tool", out var t) ? t.GetString() switch { "boxZoom" => Tool.BoxZoom, "cursor" => Tool.Cursor, "select" => Tool.Select, _ => Tool.Pan } : Tool.Pan;
        return new State(InteractionState.Initial(tool), []);
    }

    /// <summary>Reduces one input event and appends the emitted effects.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        var (next, effects) = Interaction.Reduce(s.S, Parse(step));
        s.Effects.AddRange(effects.Select(ToJson));
        return s with { S = next };
    }

    /// <summary>Reducer phase, active gesture, space-key flag and every effect emitted so far.</summary>
    public JsonNode Snapshot(object state)
    {
        var s = (State)state;
        var phase = s.S.Phase switch { Phase.Idle => "idle", Phase.Pressing => "pressing", _ => "dragging" };
        return JsonSerializer.SerializeToNode(new { phase, gesture = s.S.Gesture is { } g ? Lower(g) : null, spaceHeld = s.S.SpaceHeld, effects = s.Effects }, Fixtures.Json)!;
    }
}
