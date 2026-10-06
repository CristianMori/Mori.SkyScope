// Mori.SkyScope — A painter that records every call as JSON ops with resolved styles, for drawing parity between the two cores.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;

namespace Mori.SkyScope.Core.Paint;

/// <summary>
/// A painter that records what it is asked to draw, as JSON identical to the TS RecordingPainter's ops —
/// so component drawing logic can be compared across the two cores. Text metrics are synthetic: 0.6 × size per char.
/// </summary>
/// <param name="width">Logical width.</param>
/// <param name="height">Logical height.</param>
/// <param name="pixelRatio">Reported device pixel ratio; nothing is scaled.</param>
/// <param name="charWidth">Synthetic glyph width as a fraction of the font size.</param>
public sealed class RecordingPainter(double width, double height, double pixelRatio = 1, double charWidth = 0.6) : IPainter
{
    /// <summary>Recorded operations in call order; layer draws are nested under <c>layerDraw</c> ops.</summary>
    public JsonArray Ops { get; } = [];
    private readonly Dictionary<string, JsonArray> _layers = [];

    /// <summary>Logical width.</summary>
    public double Width { get; } = width;
    /// <summary>Logical height.</summary>
    public double Height { get; } = height;
    /// <summary>Reported device pixel ratio.</summary>
    public double PixelRatio { get; } = pixelRatio;

    /// <summary>Identical rounding in both cores so recorded geometry compares exactly.</summary>
    public static double R3(double x) => Math.Floor(x * 1000 + 0.5) / 1000;

    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();

    /// <summary>A stroke as the JSON object the TS painter records (enum names lower-case, empty dash as null).</summary>
    public static JsonObject Resolve(Stroke s) => new()
    {
        ["color"] = s.Color, ["width"] = s.Width,
        ["dash"] = s.Dash is { Length: > 0 } ? new JsonArray(s.Dash.Select(d => (JsonNode)d).ToArray()) : null,
        ["cap"] = Lower(s.Cap), ["join"] = Lower(s.Join), ["opacity"] = s.Opacity,
    };
    /// <summary>A fill as the JSON object the TS painter records.</summary>
    public static JsonObject Resolve(Fill f) => new() { ["color"] = f.Color, ["opacity"] = f.Opacity };
    /// <summary>A text style as the JSON object the TS painter records.</summary>
    public static JsonObject Resolve(TextStyle t) => new()
    {
        ["color"] = t.Color, ["family"] = t.Family, ["size"] = t.Size, ["weight"] = t.Weight,
        ["align"] = Lower(t.Align), ["baseline"] = Lower(t.Baseline), ["rotation"] = t.Rotation, ["opacity"] = t.Opacity,
    };

    private void Push(JsonObject op) => Ops.Add(op);
    private static JsonObject Op(string name) => new() { ["op"] = name };
    private static JsonArray Pts(ReadOnlySpan<double> points, int offset = 0, int count = -1)
    {
        var n = count < 0 ? (points.Length - offset) / 2 : count;
        var arr = new JsonArray();
        for (var i = 0; i < n; i++) { arr.Add(R3(points[offset + 2 * i])); arr.Add(R3(points[offset + 2 * i + 1])); }
        return arr;
    }

    /// <summary>Records a <c>save</c> op.</summary>
    public void Save() => Push(Op("save"));
    /// <summary>Records a <c>restore</c> op.</summary>
    public void Restore() => Push(Op("restore"));
    /// <summary>Records a <c>translate</c> op (coordinates rounded to 3 decimals).</summary>
    public void Translate(double x, double y) { var o = Op("translate"); o["x"] = R3(x); o["y"] = R3(y); Push(o); }
    /// <summary>Records a <c>scale</c> op.</summary>
    public void Scale(double sx, double sy) { var o = Op("scale"); o["sx"] = R3(sx); o["sy"] = R3(sy); Push(o); }
    /// <summary>Records a <c>rotate</c> op.</summary>
    public void Rotate(double radians) { var o = Op("rotate"); o["radians"] = R3(radians); Push(o); }
    /// <summary>Records a <c>clipRect</c> op.</summary>
    public void ClipRect(double x, double y, double w, double h) { var o = Op("clipRect"); o["x"] = R3(x); o["y"] = R3(y); o["w"] = R3(w); o["h"] = R3(h); Push(o); }
    /// <summary>Records a <c>clear</c> op.</summary>
    public void Clear(string? color = null) { var o = Op("clear"); o["color"] = color; Push(o); }

    /// <summary>Records a <c>line</c> op with the resolved stroke.</summary>
    public void Line(double x1, double y1, double x2, double y2, Stroke stroke)
    {
        var o = Op("line"); o["x1"] = R3(x1); o["y1"] = R3(y1); o["x2"] = R3(x2); o["y2"] = R3(y2); o["stroke"] = Resolve(stroke); Push(o);
    }
    /// <summary>Records a <c>polyline</c> op with the selected points rounded to 3 decimals.</summary>
    public void Polyline(ReadOnlySpan<double> points, Stroke stroke, int offset = 0, int count = -1)
    {
        var o = Op("polyline"); o["points"] = Pts(points, offset, count); o["stroke"] = Resolve(stroke); Push(o);
    }
    /// <summary>Records a <c>polygon</c> op.</summary>
    public void Polygon(ReadOnlySpan<double> points, Fill? fill = null, Stroke? stroke = null)
    {
        var o = Op("polygon"); o["points"] = Pts(points); o["fill"] = fill is null ? null : Resolve(fill); o["stroke"] = stroke is null ? null : Resolve(stroke); Push(o);
    }
    /// <summary>Records a <c>rect</c> op.</summary>
    public void Rect(double x, double y, double w, double h, Fill? fill = null, Stroke? stroke = null, double radius = 0)
    {
        var o = Op("rect"); o["x"] = R3(x); o["y"] = R3(y); o["w"] = R3(w); o["h"] = R3(h); o["radius"] = R3(radius);
        o["fill"] = fill is null ? null : Resolve(fill); o["stroke"] = stroke is null ? null : Resolve(stroke); Push(o);
    }
    /// <summary>Records a <c>circle</c> op.</summary>
    public void Circle(double cx, double cy, double r, Fill? fill = null, Stroke? stroke = null)
    {
        var o = Op("circle"); o["cx"] = R3(cx); o["cy"] = R3(cy); o["r"] = R3(r); o["fill"] = fill is null ? null : Resolve(fill); o["stroke"] = stroke is null ? null : Resolve(stroke); Push(o);
    }
    /// <summary>Records an <c>arc</c> op.</summary>
    public void Arc(double cx, double cy, double r, double startAngle, double endAngle, Stroke stroke)
    {
        var o = Op("arc"); o["cx"] = R3(cx); o["cy"] = R3(cy); o["r"] = R3(r); o["start"] = R3(startAngle); o["end"] = R3(endAngle); o["stroke"] = Resolve(stroke); Push(o);
    }
    /// <summary>Records a <c>sector</c> op.</summary>
    public void Sector(double cx, double cy, double innerRadius, double outerRadius, double startAngle, double endAngle, Fill? fill = null, Stroke? stroke = null)
    {
        var o = Op("sector"); o["cx"] = R3(cx); o["cy"] = R3(cy); o["inner"] = R3(innerRadius); o["outer"] = R3(outerRadius); o["start"] = R3(startAngle); o["end"] = R3(endAngle);
        o["fill"] = fill is null ? null : Resolve(fill); o["stroke"] = stroke is null ? null : Resolve(stroke); Push(o);
    }
    /// <summary>Records a <c>text</c> op with the resolved style.</summary>
    public void Text(string text, double x, double y, TextStyle style)
    {
        var o = Op("text"); o["text"] = text; o["x"] = R3(x); o["y"] = R3(y); o["style"] = Resolve(style); Push(o);
    }
    /// <summary>Synthetic metrics: <c>charWidth × size</c> per character, height = size. Nothing is recorded.</summary>
    public TextMetrics MeasureText(string text, TextStyle style) => new(R3(text.Length * style.Size * charWidth), style.Size);
    /// <summary>FNV-1a over bytes — the raster checksum both cores record for drawing parity.</summary>
    public static uint Fnv1a(byte[] bytes) { uint h = 0x811c9dc5; foreach (var b in bytes) { h ^= b; h *= 0x01000193; } return h; }
    /// <summary>Records an <c>image</c> op; for a <see cref="RasterImage"/> it includes the FNV-1a hash of the pixels.</summary>
    public void Image(IImageHandle image, double x, double y, double w, double h, double opacity = 1)
    {
        var o = Op("image"); o["imageWidth"] = image.Width; o["imageHeight"] = image.Height; o["x"] = R3(x); o["y"] = R3(y); o["w"] = R3(w); o["h"] = R3(h); o["opacity"] = R3(opacity); if (image is RasterImage ri) o["hash"] = Fnv1a(ri.Rgba); Push(o);
    }
    /// <summary>Records the child ops as a <c>layerDraw</c> op the first time or when dirty, then a <c>layer</c> op on every call.</summary>
    public void Layer(string key, double width, double height, Action<IPainter> draw, double x, double y, bool dirty = false)
    {
        if (!_layers.TryGetValue(key, out var cached) || dirty)
        {
            var child = new RecordingPainter(width, height, PixelRatio, charWidth);
            draw(child);
            cached = child.Ops;
            _layers[key] = cached;
            var d = Op("layerDraw"); d["key"] = key; d["width"] = R3(width); d["height"] = R3(height);
            d["ops"] = JsonNode.Parse(cached.ToJsonString()); Push(d);
        }
        var o = Op("layer"); o["key"] = key; o["x"] = R3(x); o["y"] = R3(y); Push(o);
    }
}
