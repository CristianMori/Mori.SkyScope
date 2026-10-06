// Mori.SkyScope — SkiaSharp implementation of the painter contract with layer surfaces and clipping.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using SkiaSharp;

namespace Mori.SkyScope.Render.Skia;

/// <summary>Wraps an <see cref="SKImage"/> as an <see cref="IImageHandle"/>.</summary>
public sealed class SkiaImage(SKImage image) : IImageHandle, IDisposable
{
    /// <summary>The wrapped Skia image.</summary>
    public SKImage Image { get; } = image;
    /// <summary>Image width in pixels.</summary>
    public double Width => Image.Width;
    /// <summary>Image height in pixels.</summary>
    public double Height => Image.Height;
    /// <summary>Disposes the wrapped Skia image.</summary>
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// <see cref="IPainter"/> over an <see cref="SKCanvas"/>. One instance per surface; layers are cached as
/// offscreen <see cref="SKSurface"/>s created by <c>surfaceFactory</c> (raster by default; pass a GPU
/// factory bound to your GRContext for hardware layers).
/// </summary>
public sealed partial class SkiaPainter : IPainter, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private readonly SKFont _font = new();
    private readonly Func<int, int, SKSurface> _surfaceFactory;
    private readonly Dictionary<string, LayerEntry> _layers = [];
    private sealed record LayerEntry(SKSurface Surface, double Width, double Height) { public SKImage? Snapshot { get; set; } }

    /// <summary>Creates a painter over <paramref name="canvas"/> for a surface of the given logical size; the canvas is scaled by <paramref name="pixelRatio"/> once so callers draw in device-independent units.</summary>
    /// <param name="surfaceFactory">Creates the offscreen surface for a layer from its pixel width and height; defaults to a raster surface in the platform colour type.</param>
    public SkiaPainter(SKCanvas canvas, double width, double height, double pixelRatio = 1, Func<int, int, SKSurface>? surfaceFactory = null)
    {
        _canvas = canvas; Width = width; Height = height; PixelRatio = pixelRatio;
        _surfaceFactory = surfaceFactory ?? ((w, h) => SKSurface.Create(new SKImageInfo(w, h, SKImageInfo.PlatformColorType, SKAlphaType.Premul)));
        if (pixelRatio != 1) _canvas.Scale((float)pixelRatio);
    }

    /// <summary>Logical width of the target surface in device-independent units.</summary>
    public double Width { get; }
    /// <summary>Logical height of the target surface in device-independent units.</summary>
    public double Height { get; }
    /// <summary>Device pixels per logical unit; layer surfaces are allocated at this scale.</summary>
    public double PixelRatio { get; }

    private static SKColor Color(string css, double opacity)
    {
        var c = CssColor.TryParse(css, out var p) ? p : CssColor.Black;
        return new SKColor(c.R, c.G, c.B, (byte)Math.Round(Math.Clamp(c.A * opacity, 0, 1) * 255));
    }

    private SKPaint StrokePaint(Stroke s)
    {
        _paint.Style = SKPaintStyle.Stroke;
        _paint.Color = Color(s.Color, s.Opacity);
        _paint.StrokeWidth = (float)s.Width;
        _paint.StrokeCap = s.Cap switch { LineCap.Round => SKStrokeCap.Round, LineCap.Square => SKStrokeCap.Square, _ => SKStrokeCap.Butt };
        _paint.StrokeJoin = s.Join switch { LineJoin.Round => SKStrokeJoin.Round, LineJoin.Bevel => SKStrokeJoin.Bevel, _ => SKStrokeJoin.Miter };
        _paint.PathEffect?.Dispose();
        _paint.PathEffect = s.Dash is { Length: > 0 } ? SKPathEffect.CreateDash(s.Dash.Select(d => (float)d).ToArray(), 0) : null;
        return _paint;
    }

    private SKPaint FillPaint(Fill f)
    {
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Color(f.Color, f.Opacity);
        _paint.PathEffect?.Dispose(); _paint.PathEffect = null;
        return _paint;
    }

    /// <summary>Pushes the current transform and clip state.</summary>
    public void Save() => _canvas.Save();
    /// <summary>Pops the state pushed by the matching <see cref="Save"/>.</summary>
    public void Restore() => _canvas.Restore();
    /// <summary>Moves the origin by the given logical offsets.</summary>
    public void Translate(double x, double y) => _canvas.Translate((float)x, (float)y);
    /// <summary>Scales subsequent drawing by the given factors.</summary>
    public void Scale(double sx, double sy) => _canvas.Scale((float)sx, (float)sy);
    /// <summary>Rotates subsequent drawing about the current origin.</summary>
    public void Rotate(double radians) => _canvas.RotateRadians((float)radians);
    /// <summary>Intersects the current clip with the given rectangle, with anti-aliased edges.</summary>
    public void ClipRect(double x, double y, double w, double h) => _canvas.ClipRect(SKRect.Create((float)x, (float)y, (float)w, (float)h), antialias: true);
    /// <summary>Fills the whole canvas with a CSS colour, or with transparent when none is given.</summary>
    public void Clear(string? color = null) => _canvas.Clear(color is null ? SKColors.Transparent : Color(color, 1));

    /// <summary>Strokes a straight segment between two points.</summary>
    public void Line(double x1, double y1, double x2, double y2, Stroke stroke)
        => _canvas.DrawLine((float)x1, (float)y1, (float)x2, (float)y2, StrokePaint(stroke));

    private static SKPath Path(ReadOnlySpan<double> points, int offset, int count, bool close)
    {
        var b = new SKPathBuilder();
        if (count < 1) return b.Detach();
        b.MoveTo((float)points[offset], (float)points[offset + 1]);
        for (var i = 1; i < count; i++) b.LineTo((float)points[offset + 2 * i], (float)points[offset + 2 * i + 1]);
        if (close) b.Close();
        return b.Detach();
    }

    /// <summary>Strokes an open path through interleaved x/y <paramref name="points"/>, optionally only <paramref name="count"/> points from <paramref name="offset"/>; fewer than two points draws nothing.</summary>
    public void Polyline(ReadOnlySpan<double> points, Stroke stroke, int offset = 0, int count = -1)
    {
        var n = count < 0 ? (points.Length - offset) / 2 : count;
        if (n < 2) return;
        using var path = Path(points, offset, n, close: false);
        _canvas.DrawPath(path, StrokePaint(stroke));
    }

    /// <summary>Fills and/or strokes a closed path through interleaved x/y points.</summary>
    public void Polygon(ReadOnlySpan<double> points, Fill? fill = null, Stroke? stroke = null)
    {
        var n = points.Length / 2;
        if (n < 2) return;
        using var path = Path(points, 0, n, close: true);
        if (fill is not null) _canvas.DrawPath(path, FillPaint(fill));
        if (stroke is not null) _canvas.DrawPath(path, StrokePaint(stroke));
    }

    /// <summary>Fills and/or strokes a rectangle, with rounded corners when <paramref name="radius"/> is positive.</summary>
    public void Rect(double x, double y, double w, double h, Fill? fill = null, Stroke? stroke = null, double radius = 0)
    {
        var rect = SKRect.Create((float)x, (float)y, (float)w, (float)h);
        var r = (float)radius;
        if (fill is not null) { if (r > 0) _canvas.DrawRoundRect(rect, r, r, FillPaint(fill)); else _canvas.DrawRect(rect, FillPaint(fill)); }
        if (stroke is not null) { if (r > 0) _canvas.DrawRoundRect(rect, r, r, StrokePaint(stroke)); else _canvas.DrawRect(rect, StrokePaint(stroke)); }
    }

    /// <summary>Fills and/or strokes a circle.</summary>
    public void Circle(double cx, double cy, double r, Fill? fill = null, Stroke? stroke = null)
    {
        if (fill is not null) _canvas.DrawCircle((float)cx, (float)cy, (float)r, FillPaint(fill));
        if (stroke is not null) _canvas.DrawCircle((float)cx, (float)cy, (float)r, StrokePaint(stroke));
    }

    private static float Deg(double rad) => (float)(rad * 180 / Math.PI);

    /// <summary>Strokes a circular arc from <paramref name="startAngle"/> to <paramref name="endAngle"/> (radians).</summary>
    public void Arc(double cx, double cy, double r, double startAngle, double endAngle, Stroke stroke)
    {
        var b = new SKPathBuilder();
        b.AddArc(SKRect.Create((float)(cx - r), (float)(cy - r), (float)(2 * r), (float)(2 * r)), Deg(startAngle), Deg(endAngle - startAngle));
        using var path = b.Detach();
        _canvas.DrawPath(path, StrokePaint(stroke));
    }

    /// <summary>Fills and/or strokes an annular sector between two radii and two angles (radians); a zero inner radius gives a pie wedge.</summary>
    public void Sector(double cx, double cy, double innerRadius, double outerRadius, double startAngle, double endAngle, Fill? fill = null, Stroke? stroke = null)
    {
        var b = new SKPathBuilder();
        var sweep = Deg(endAngle - startAngle);
        var outer = SKRect.Create((float)(cx - outerRadius), (float)(cy - outerRadius), (float)(2 * outerRadius), (float)(2 * outerRadius));
        b.ArcTo(outer, Deg(startAngle), sweep, forceMoveTo: true);
        if (innerRadius > 0)
        {
            var inner = SKRect.Create((float)(cx - innerRadius), (float)(cy - innerRadius), (float)(2 * innerRadius), (float)(2 * innerRadius));
            b.ArcTo(inner, Deg(endAngle), -sweep, forceMoveTo: false);
        }
        else b.LineTo((float)cx, (float)cy);
        b.Close();
        using var path = b.Detach();
        if (fill is not null) _canvas.DrawPath(path, FillPaint(fill));
        if (stroke is not null) _canvas.DrawPath(path, StrokePaint(stroke));
    }
}
