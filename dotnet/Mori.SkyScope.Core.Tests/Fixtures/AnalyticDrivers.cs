// Mori.SkyScope — Fixture drivers for the analytic charts.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture drivers for the analytic charts. Mirrors <c>fixtures/analytic-driver.ts</c>; every query answer is rounded to 9 decimals.</summary>
internal static class Analytic
{
    /// <summary>Rounds every value to 9 decimals.</summary>
    public static double[] Arr9(IEnumerable<double> a) => a.Select(Round.R9).ToArray();
    /// <summary>A chart hit as <c>{seriesId, index}</c>, or null.</summary>
    public static object? Hit(ChartHit? h) => h is null ? null : new { seriesId = h.SeriesId, index = h.Index };
    /// <summary>Legend items as JSON, with the swatch shape spelled out.</summary>
    public static object Legend(IEnumerable<LegendItem> items) => items.Select(i => new { id = i.Id, name = i.Name, color = i.Color, hidden = i.Hidden, shape = i.Shape switch { LegendShape.Line => "line", LegendShape.Box => "box", _ => "circle" } }).ToList();
    /// <summary>Draws the chart into a recording painter and returns the recorded ops.</summary>
    public static object Draw(IDrawable g, double w, double h) { var p = new RecordingPainter(w, h); g.Draw(p, w, h); return p.Ops; }
    /// <summary>A (low, high) tuple as a rounded two-element array.</summary>
    public static double[] Pair((double, double) d) => [Round.R9(d.Item1), Round.R9(d.Item2)];
    /// <summary>The <c>options</c> object of a setup, or an empty object.</summary>
    public static JsonElement Options(JsonElement setup) => setup.TryGetProperty("options", out var o) ? o : JsonDocument.Parse("{}").RootElement;
    /// <summary>A required numeric property.</summary>
    public static double N(JsonElement e, string n) => e.GetProperty(n).GetDouble();
    /// <summary>A required numeric array property.</summary>
    public static double[] Pt(JsonElement e, string n) => ChartJson.Doubles(e.GetProperty(n));
    /// <summary>The query list wrapped as a <c>{queries}</c> snapshot.</summary>
    public static JsonNode Snap(List<object?> q) => JsonSerializer.SerializeToNode(new { queries = q }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the cartesian chart (lines, areas, bars, stacks, histograms): toggles, hover, click and zoom, then domain, layout, geometry, hit, tooltip, legend, tick and draw queries.</summary>
public sealed class CartesianDriver : IFixtureDriver
{
    /// <summary>Handles the <c>cartesian</c> fixtures.</summary>
    public string Component => "cartesian";
    private sealed record State(CartesianChart G, double W, double H, List<object?> Queries);

    /// <summary>Builds the chart from the <c>options</c> and remembers the size.</summary>
    public object Create(JsonElement setup) => new State(new CartesianChart(ChartJson.Cartesian(Analytic.Options(setup))), Analytic.N(setup, "width"), Analytic.N(setup, "height"), []);

    /// <summary>Applies toggle, hover, leave, click (recording the hit), zoomBox, wheel and resetZoom, and answers xDomain, yDomain, layout, bars, points, area, stacked, hit, hover, tooltip, legend, xTicks, barSlot, histogram and draw queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (g, w, h, q) = ((State)state); var l = g.Layout(w, h);
        switch (step.GetProperty("type").GetString())
        {
            case "toggle": g.ToggleSeries(step.GetProperty("id").GetString()!); break;
            case "hover": g.PointerMove(Analytic.N(step, "x"), Analytic.N(step, "y"), w, h); break;
            case "leave": g.PointerLeave(); break;
            case "click": q.Add(g.Click(Analytic.N(step, "x"), Analytic.N(step, "y"), w, h)); break;
            case "zoomBox": g.ZoomTo(Analytic.N(step, "x0"), Analytic.N(step, "y0"), Analytic.N(step, "x1"), Analytic.N(step, "y1"), l); break;
            case "wheel": g.WheelZoom(Analytic.N(step, "x"), Analytic.N(step, "y"), Analytic.N(step, "dy"), l); break;
            case "resetZoom": g.ResetZoom(); break;
            case "query":
                if (step.TryGetProperty("xDomain", out _)) q.Add(Analytic.Pair(g.XDomain()));
                else if (step.TryGetProperty("yDomain", out var yd)) q.Add(Analytic.Pair(g.YDomain(yd.GetString() == "y2" ? YAxisId.Y2 : YAxisId.Y)));
                else if (step.TryGetProperty("layout", out _)) q.Add(new { plot = Round.Rect(l.Plot), yAxis = Round.Rect(l.YAxis), y2Axis = Round.Rect(l.Y2Axis), xAxis = Round.Rect(l.XAxis), title = Round.Rect(l.Title), legend = Round.Rect(l.Legend) });
                else if (step.TryGetProperty("bars", out _)) q.Add(g.Bars(l).Select(b => new { seriesId = b.SeriesId, index = b.Index, x = Round.R9(b.X), y = Round.R9(b.Y), w = Round.R9(b.W), h = Round.R9(b.H) }).ToList());
                else if (step.TryGetProperty("points", out var pid)) q.Add(Analytic.Arr9(g.PixelPoints(g.Series(pid.GetString()!)!, l)));
                else if (step.TryGetProperty("area", out var aid)) q.Add(Analytic.Arr9(g.AreaPolygon(g.Series(aid.GetString()!)!, l)));
                else if (step.TryGetProperty("stacked", out var stk)) { var arr = stk.EnumerateArray().ToArray(); var (b, t) = g.StackedValue(g.Series(arr[0].GetString()!)!, arr[1].GetInt32()); q.Add(new[] { Round.R9(b), Round.R9(t) }); }
                else if (step.TryGetProperty("hit", out var hit)) { var xy = ChartJson.Doubles(hit); q.Add(Analytic.Hit(g.HitTest(xy[0], xy[1], l))); }
                else if (step.TryGetProperty("hover", out _)) q.Add(Analytic.Hit(g.Hover));
                else if (step.TryGetProperty("tooltip", out _)) { var t = g.Hover is null ? null : g.TooltipFor(g.Hover, l); q.Add(t is null ? null : new { x = Round.R9(t.X), y = Round.R9(t.Y), title = t.Title, lines = t.Lines.Select(ln => new { name = ln.Name, value = ln.Value, color = ln.Color }).ToList() }); }
                else if (step.TryGetProperty("legend", out _)) q.Add(Analytic.Legend(g.LegendItems()));
                else if (step.TryGetProperty("xTicks", out _)) q.Add(g.XTicks(l).Select(v => new { v = Round.R9(v), label = g.FormatX(v, l) }).ToList());
                else if (step.TryGetProperty("barSlot", out _)) q.Add(new { slot = Round.R9(g.BarSlot()), width = Round.R9(g.BarWidth()), groups = g.BarGroups() });
                else if (step.TryGetProperty("histogram", out var hs))
                {
                    var o = hs.TryGetProperty("options", out var ho) ? ho : JsonDocument.Parse("{}").RootElement;
                    var r = Histogram.Compute(ChartJson.Doubles(hs.GetProperty("values")), new HistogramOptions { Bins = (int?)ChartJson.Num(o, "bins"), BinWidth = ChartJson.Num(o, "binWidth"), Min = ChartJson.Num(o, "min"), Max = ChartJson.Num(o, "max"), Density = ChartJson.Bool(o, "density") ?? false });
                    q.Add(new { edges = Analytic.Arr9(r.Edges), counts = Analytic.Arr9(r.Counts), centers = Analytic.Arr9(r.Centers), binWidth = Round.R9(r.BinWidth), total = r.Total });
                }
                else if (step.TryGetProperty("draw", out _)) q.Add(Analytic.Draw(g, w, h));
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => Analytic.Snap(((State)state).Queries);
}

/// <summary>Fixture driver for the pie and donut chart: slice toggles, hover and click, then slice geometry, layout, hit, label, total, legend and draw queries.</summary>
public sealed class PieDriver : IFixtureDriver
{
    /// <summary>Handles the <c>pie</c> fixtures.</summary>
    public string Component => "pie";
    private sealed record State(PieChart G, double W, double H, List<object?> Queries);

    /// <summary>Builds the chart from the <c>options</c> and remembers the size.</summary>
    public object Create(JsonElement setup) => new State(new PieChart(ChartJson.Pie(Analytic.Options(setup))), Analytic.N(setup, "width"), Analytic.N(setup, "height"), []);

    /// <summary>Applies toggle, hover, leave and click (recording the hit), and answers slices, layout, hit, hover, labels, total, legend and draw queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (g, w, h, q) = ((State)state); var l = g.Layout(w, h);
        switch (step.GetProperty("type").GetString())
        {
            case "toggle": g.ToggleSlice(step.GetProperty("id").GetString()!); break;
            case "hover": g.PointerMove(Analytic.N(step, "x"), Analytic.N(step, "y"), w, h); break;
            case "leave": g.PointerLeave(); break;
            case "click": q.Add(g.Click(Analytic.N(step, "x"), Analytic.N(step, "y"), w, h)); break;
            case "query":
                if (step.TryGetProperty("slices", out _)) q.Add(g.Slices().Select(s => new { id = s.Id, value = Round.R9(s.Value), frac = Round.R9(s.Frac), start = Round.R9(s.Start), end = Round.R9(s.End), mid = Round.R9(s.Mid) }).ToList());
                else if (step.TryGetProperty("layout", out _)) q.Add(new { cx = Round.R9(l.Cx), cy = Round.R9(l.Cy), r = Round.R9(l.R), inner = Round.R9(l.Inner), plot = Round.Rect(l.Plot), title = Round.Rect(l.Title), legend = Round.Rect(l.Legend) });
                else if (step.TryGetProperty("hit", out var hit)) { var xy = ChartJson.Doubles(hit); q.Add(g.HitTest(xy[0], xy[1], l)); }
                else if (step.TryGetProperty("hover", out _)) q.Add(g.Hover);
                else if (step.TryGetProperty("labels", out _)) q.Add(g.Slices().Select(g.LabelFor).ToList());
                else if (step.TryGetProperty("total", out _)) q.Add(Round.R9(g.Total()));
                else if (step.TryGetProperty("legend", out _)) q.Add(Analytic.Legend(g.LegendItems()));
                else if (step.TryGetProperty("draw", out _)) q.Add(Analytic.Draw(g, w, h));
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => Analytic.Snap(((State)state).Queries);
}

/// <summary>Fixture driver for the polar chart: series toggles and hover, then radial domain, layout, points, grid angles, hit, legend and draw queries.</summary>
public sealed class PolarDriver : IFixtureDriver
{
    /// <summary>Handles the <c>polar</c> fixtures.</summary>
    public string Component => "polar";
    private sealed record State(PolarChart G, double W, double H, List<object?> Queries);

    /// <summary>Builds the chart from the <c>options</c> and remembers the size.</summary>
    public object Create(JsonElement setup) => new State(new PolarChart(ChartJson.Polar(Analytic.Options(setup))), Analytic.N(setup, "width"), Analytic.N(setup, "height"), []);

    /// <summary>Applies toggle, hover and leave, and answers rDomain, layout, points, gridAngles, hit, hover, legend and draw queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (g, w, h, q) = ((State)state); var l = g.Layout(w, h);
        switch (step.GetProperty("type").GetString())
        {
            case "toggle": g.ToggleSeries(step.GetProperty("id").GetString()!); break;
            case "hover": g.PointerMove(Analytic.N(step, "x"), Analytic.N(step, "y"), w, h); break;
            case "leave": g.PointerLeave(); break;
            case "query":
                if (step.TryGetProperty("rDomain", out _)) q.Add(Analytic.Pair(g.RDomain()));
                else if (step.TryGetProperty("layout", out _)) q.Add(new { cx = Round.R9(l.Cx), cy = Round.R9(l.Cy), r = Round.R9(l.R), plot = Round.Rect(l.Plot), title = Round.Rect(l.Title), legend = Round.Rect(l.Legend) });
                else if (step.TryGetProperty("points", out var pid)) q.Add(Analytic.Arr9(g.PixelPoints(g.Series(pid.GetString()!)!, l)));
                else if (step.TryGetProperty("gridAngles", out _)) q.Add(g.GridAngles().Select(a => new { a = Round.R9(a), label = g.AngleLabel(a) }).ToList());
                else if (step.TryGetProperty("hit", out var hit)) { var xy = ChartJson.Doubles(hit); q.Add(Analytic.Hit(g.HitTest(xy[0], xy[1], l))); }
                else if (step.TryGetProperty("hover", out _)) q.Add(Analytic.Hit(g.Hover));
                else if (step.TryGetProperty("legend", out _)) q.Add(Analytic.Legend(g.LegendItems()));
                else if (step.TryGetProperty("draw", out _)) q.Add(Analytic.Draw(g, w, h));
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => Analytic.Snap(((State)state).Queries);
}

/// <summary>Fixture driver for the heatmap: column pushes and cell writes, then range, colour, cell, image hash and pixel, layout, extent, colormap and draw queries.</summary>
public sealed class HeatmapDriver : IFixtureDriver
{
    /// <summary>Handles the <c>heatmap</c> fixtures.</summary>
    public string Component => "heatmap";
    private sealed record State(Heatmap G, double W, double H, List<object?> Queries);

    /// <summary>Builds the heatmap from the <c>options</c> and the optional initial <c>values</c>, remembering the size.</summary>
    public object Create(JsonElement setup)
    {
        var values = setup.TryGetProperty("values", out var v) ? ChartJson.Doubles(v) : [];
        return new State(new Heatmap(ChartJson.Heatmap(Analytic.Options(setup)), values), Analytic.N(setup, "width"), Analytic.N(setup, "height"), []);
    }

    /// <summary>Applies push, set, setValues, hover and leave, and answers range, color, get, hash, pixel, layout, cell, extent, colormap and draw queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (g, w, h, q) = ((State)state); var l = g.Layout(w, h);
        switch (step.GetProperty("type").GetString())
        {
            case "push": g.PushColumn(Analytic.Pt(step, "column")); break;
            case "set": g.Set((int)Analytic.N(step, "row"), (int)Analytic.N(step, "col"), Analytic.N(step, "value")); break;
            case "setValues": g.SetValues(Analytic.Pt(step, "values")); break;
            case "hover": g.PointerMove(Analytic.N(step, "x"), Analytic.N(step, "y"), w, h); break;
            case "leave": g.PointerLeave(); break;
            case "query":
                if (step.TryGetProperty("range", out _)) q.Add(Analytic.Pair(g.Range()));
                else if (step.TryGetProperty("color", out var cv)) q.Add(g.ColorAt(cv.GetDouble()));
                else if (step.TryGetProperty("get", out var rc)) { var a = ChartJson.Doubles(rc); var v = g.Get((int)a[0], (int)a[1]); q.Add(double.IsNaN(v) ? null : Round.R9(v)); }
                else if (step.TryGetProperty("hash", out _)) { var img = g.Image(); q.Add(new { width = img.PixelWidth, height = img.PixelHeight, hash = RecordingPainter.Fnv1a(img.Rgba) }); }
                else if (step.TryGetProperty("pixel", out var px)) { var a = ChartJson.Doubles(px); var img = g.Image(); var o = ((int)a[1] * img.PixelWidth + (int)a[0]) * 4; q.Add(new int[] { img.Rgba[o], img.Rgba[o + 1], img.Rgba[o + 2], img.Rgba[o + 3] }); }
                else if (step.TryGetProperty("layout", out _)) q.Add(new { plot = Round.Rect(l.Plot), yAxis = Round.Rect(l.YAxis), xAxis = Round.Rect(l.XAxis), colorbar = Round.Rect(l.Colorbar), title = Round.Rect(l.Title) });
                else if (step.TryGetProperty("cell", out var cell)) { var a = ChartJson.Doubles(cell); var c = g.CellAt(a[0], a[1], l); q.Add(c is null ? null : new { col = c.Col, row = c.Row, value = Round.R9(c.Value) }); }
                else if (step.TryGetProperty("extent", out _)) q.Add(new { xMin = Round.R9(g.Config.XMin), xMax = Round.R9(g.Config.XMax) });
                else if (step.TryGetProperty("colormap", out var cm))
                {
                    var map = cm.GetProperty("map"); var stops = map.ValueKind == JsonValueKind.Array ? map.EnumerateArray().Select(x => x.GetString()!).ToArray() : Colormaps.Stops(map.GetString()!);
                    q.Add(Colormaps.Hex(stops, cm.GetProperty("t").GetDouble()));
                }
                else if (step.TryGetProperty("draw", out _)) q.Add(Analytic.Draw(g, w, h));
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => Analytic.Snap(((State)state).Queries);
}
