// Mori.SkyScope — Text drawing and measurement for the SkiaSharp painter.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using SkiaSharp;

namespace Mori.SkyScope.Render.Skia;

public sealed partial class SkiaPainter
{
    private static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

    private void ApplyFont(TextStyle style)
    {
        var family = style.Family.Split(',')[0].Trim().Trim('\'', '"');
        var weight = style.Weight switch
        {
            "bold" => SKFontStyleWeight.Bold,
            "normal" => SKFontStyleWeight.Normal,
            var w when int.TryParse(w, out var n) => (SKFontStyleWeight)n,
            _ => SKFontStyleWeight.Normal,
        };
        var name = family is "system-ui" or "sans-serif" or "" ? null : family;
        _font.Typeface = SKTypeface.FromFamilyName(name, weight, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        _font.Size = (float)style.Size;
        _font.Subpixel = true;
    }

    /// <summary>Draws a string at a point, honouring the family, weight, size, alignment, baseline, rotation and opacity of the style.</summary>
    public void Text(string text, double x, double y, TextStyle style)
    {
        ApplyFont(style);
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = Color(style.Color, style.Opacity);
        _paint.PathEffect?.Dispose(); _paint.PathEffect = null;
        var m = _font.Metrics;
        var dy = style.Baseline switch
        {
            TextBaseline.Top => -m.Ascent,
            TextBaseline.Middle => -(m.Ascent + m.Descent) / 2,
            TextBaseline.Bottom => -m.Descent,
            _ => 0,
        };
        var align = style.Align switch { TextAlign.Center => SKTextAlign.Center, TextAlign.Right => SKTextAlign.Right, _ => SKTextAlign.Left };
        if (style.Rotation != 0)
        {
            _canvas.Save();
            _canvas.Translate((float)x, (float)y);
            _canvas.RotateRadians((float)style.Rotation);
            _canvas.DrawText(text, 0, dy, align, _font, _paint);
            _canvas.Restore();
        }
        else _canvas.DrawText(text, (float)x, (float)(y + dy), align, _font, _paint);
    }

    /// <summary>Measures the advance width and the ascent-plus-descent height of a string in the given style.</summary>
    public TextMetrics MeasureText(string text, TextStyle style)
    {
        ApplyFont(style);
        var m = _font.Metrics;
        return new TextMetrics(_font.MeasureText(text), -m.Ascent + m.Descent);
    }

    /// <summary>Draws an image into the destination rectangle: a <see cref="SkiaImage"/> is sampled linearly, a core <c>RasterImage</c> (heatmap) is uploaded through <see cref="RasterCache"/> and sampled nearest-neighbour. Other handles throw.</summary>
    public void Image(IImageHandle image, double x, double y, double w, double h, double opacity = 1)
    {
        SKImage sk; var sampling = Sampling;
        if (image is SkiaImage si) sk = si.Image;
        else if (image is RasterImage ri) { sk = RasterCache.Get(ri); sampling = new SKSamplingOptions(SKFilterMode.Nearest); }
        else throw new ArgumentException("SkiaPainter needs a SkiaImage or RasterImage", nameof(image));
        _paint.Style = SKPaintStyle.Fill;
        _paint.Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255));
        _paint.PathEffect?.Dispose(); _paint.PathEffect = null;
        _canvas.DrawImage(sk, SKRect.Create((float)x, (float)y, (float)w, (float)h), sampling, _paint);
    }

    /// <summary>Draws a cached offscreen layer at (x, y); the layer is re-rendered through <paramref name="draw"/> only when it is new, resized or marked <paramref name="dirty"/>.</summary>
    public void Layer(string key, double width, double height, Action<IPainter> draw, double x, double y, bool dirty = false)
    {
        if (!_layers.TryGetValue(key, out var entry) || dirty || entry.Width != width || entry.Height != height)
        {
            if (entry is null || entry.Width != width || entry.Height != height)
            {
                if (entry is not null) { entry.Snapshot?.Dispose(); entry.Surface.Dispose(); }
                var pw = Math.Max(1, (int)Math.Round(width * PixelRatio));
                var ph = Math.Max(1, (int)Math.Round(height * PixelRatio));
                entry = new LayerEntry(_surfaceFactory(pw, ph), width, height);
                _layers[key] = entry;
            }
            entry.Snapshot?.Dispose(); entry.Snapshot = null;
            var c = entry.Surface.Canvas;
            c.Clear(SKColors.Transparent);
            c.Save();
            using (var child = new SkiaPainter(c, width, height, PixelRatio, _surfaceFactory)) draw(child);
            c.Restore();
            entry.Snapshot = entry.Surface.Snapshot();
        }
        _canvas.DrawImage(entry.Snapshot!, SKRect.Create((float)x, (float)y, (float)width, (float)height), Sampling);
    }

    /// <summary>Releases the offscreen surface cached under <paramref name="key"/>.</summary>
    public void DropLayer(string key)
    {
        if (_layers.Remove(key, out var e)) { e.Snapshot?.Dispose(); e.Surface.Dispose(); }
    }

    /// <summary>Releases every cached layer surface and the shared paint and font objects.</summary>
    public void Dispose()
    {
        foreach (var e in _layers.Values) { e.Snapshot?.Dispose(); e.Surface.Dispose(); }
        _layers.Clear();
        _paint.PathEffect?.Dispose(); _paint.Dispose(); _font.Dispose();
    }
}
