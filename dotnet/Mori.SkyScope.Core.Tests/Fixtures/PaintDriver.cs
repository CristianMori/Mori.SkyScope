// Mori.SkyScope — Replays generic op steps onto any painter — also used to drive the real renderers in demos.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Parses paint-fixture JSON into strokes, fills and text styles and replays op steps onto any <c>IPainter</c>.</summary>
public static class PaintSteps
{
    private static double D(JsonElement e, string name, double dflt = 0) => e.TryGetProperty(name, out var v) ? v.GetDouble() : dflt;
    private static double[] Doubles(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

    /// <summary>Parses the optional <c>stroke</c> object (colour, width, dash, cap, join, opacity), or null when absent.</summary>
    public static Stroke? Stroke(JsonElement e)
    {
        if (!e.TryGetProperty("stroke", out var s)) return null;
        return new Stroke(s.GetProperty("color").GetString()!)
        {
            Width = D(s, "width", 1),
            Dash = s.TryGetProperty("dash", out var d) ? Doubles(d) : null,
            Cap = s.TryGetProperty("cap", out var c) ? Enum.Parse<LineCap>(c.GetString()!, true) : LineCap.Butt,
            Join = s.TryGetProperty("join", out var j) ? Enum.Parse<LineJoin>(j.GetString()!, true) : LineJoin.Miter,
            Opacity = D(s, "opacity", 1),
        };
    }

    /// <summary>Parses the optional <c>fill</c> object (colour, opacity), or null when absent.</summary>
    public static Fill? Fill(JsonElement e)
        => e.TryGetProperty("fill", out var f) ? new Fill(f.GetProperty("color").GetString()!) { Opacity = D(f, "opacity", 1) } : null;

    /// <summary>Parses the required <c>style</c> object into a text style, defaulting family, size, weight, alignment and baseline.</summary>
    public static TextStyle Style(JsonElement e)
    {
        var s = e.GetProperty("style");
        return new TextStyle(s.GetProperty("color").GetString()!)
        {
            Family = s.TryGetProperty("family", out var f) ? f.GetString()! : TextStyle.DefaultFamily,
            Size = D(s, "size", TextStyle.DefaultSize),
            Weight = s.TryGetProperty("weight", out var w) ? (w.ValueKind == JsonValueKind.Number ? w.GetInt32().ToString() : w.GetString()!) : "normal",
            Align = s.TryGetProperty("align", out var a) ? Enum.Parse<TextAlign>(a.GetString()!, true) : TextAlign.Left,
            Baseline = s.TryGetProperty("baseline", out var b) ? Enum.Parse<TextBaseline>(b.GetString()!, true) : TextBaseline.Alphabetic,
            Rotation = D(s, "rotation"),
            Opacity = D(s, "opacity", 1),
        };
    }

    /// <summary>Replays generic op steps onto any painter — also used to drive the real renderers in demos.</summary>
    public static void Apply(IPainter p, JsonElement s, List<object?>? queries = null)
    {
        switch (s.GetProperty("type").GetString())
        {
            case "save": p.Save(); break;
            case "restore": p.Restore(); break;
            case "translate": p.Translate(D(s, "x"), D(s, "y")); break;
            case "scale": p.Scale(D(s, "sx"), D(s, "sy")); break;
            case "rotate": p.Rotate(D(s, "radians")); break;
            case "clipRect": p.ClipRect(D(s, "x"), D(s, "y"), D(s, "w"), D(s, "h")); break;
            case "clear": p.Clear(s.TryGetProperty("color", out var c) ? c.GetString() : null); break;
            case "line": p.Line(D(s, "x1"), D(s, "y1"), D(s, "x2"), D(s, "y2"), Stroke(s)!); break;
            case "polyline": p.Polyline(Doubles(s.GetProperty("points")), Stroke(s)!, (int)D(s, "offset"), (int)D(s, "count", -1)); break;
            case "polygon": p.Polygon(Doubles(s.GetProperty("points")), Fill(s), Stroke(s)); break;
            case "rect": p.Rect(D(s, "x"), D(s, "y"), D(s, "w"), D(s, "h"), Fill(s), Stroke(s), D(s, "radius")); break;
            case "circle": p.Circle(D(s, "cx"), D(s, "cy"), D(s, "r"), Fill(s), Stroke(s)); break;
            case "arc": p.Arc(D(s, "cx"), D(s, "cy"), D(s, "r"), D(s, "start"), D(s, "end"), Stroke(s)!); break;
            case "sector": p.Sector(D(s, "cx"), D(s, "cy"), D(s, "inner"), D(s, "outer"), D(s, "start"), D(s, "end"), Fill(s), Stroke(s)); break;
            case "text": p.Text(s.GetProperty("text").GetString()!, D(s, "x"), D(s, "y"), Style(s)); break;
            case "measure": { var m = p.MeasureText(s.GetProperty("text").GetString()!, Style(s)); queries?.Add(new { width = m.Width, height = m.Height }); break; }
            case "layer":
                var ops = s.GetProperty("ops").EnumerateArray().ToArray();
                p.Layer(s.GetProperty("key").GetString()!, D(s, "width"), D(s, "height"), child => { foreach (var o in ops) Apply(child, o); }, D(s, "x"), D(s, "y"), s.TryGetProperty("dirty", out var d) && d.GetBoolean());
                break;
            case var t: throw new InvalidOperationException($"unknown paint step {t}");
        }
    }
}

/// <summary>Fixture driver for the painter contract: replays ops onto a recording painter and exposes the recorded op list.</summary>
public sealed class PaintDriver : IFixtureDriver
{
    /// <summary>Handles the <c>paint</c> fixtures.</summary>
    public string Component => "paint";
    private sealed record State(RecordingPainter Painter, List<object?> Queries);

    /// <summary>Creates a recording painter of the given <c>width</c> and <c>height</c>.</summary>
    public object Create(JsonElement setup) => new State(new RecordingPainter(setup.GetProperty("width").GetDouble(), setup.GetProperty("height").GetDouble()), []);

    /// <summary>Replays one op; <c>measure</c> steps append their metrics to the query list.</summary>
    public object Step(object state, JsonElement step) { var s = (State)state; PaintSteps.Apply(s.Painter, step, s.Queries); return s; }

    /// <summary>The recorded ops and the measure answers.</summary>
    public JsonNode Snapshot(object state)
    {
        var s = (State)state;
        return new JsonObject { ["ops"] = JsonNode.Parse(s.Painter.Ops.ToJsonString()), ["queries"] = JsonSerializer.SerializeToNode(s.Queries, Fixtures.Json) };
    }
}
