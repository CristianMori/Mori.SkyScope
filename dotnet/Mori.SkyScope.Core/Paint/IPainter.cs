// Mori.SkyScope — The rendering contract.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Paint;

/// <summary>Shape of stroke ends.</summary>
public enum LineCap { Butt, Round, Square }
/// <summary>Shape of stroke corners.</summary>
public enum LineJoin { Miter, Round, Bevel }
/// <summary>Horizontal anchor of text relative to its x.</summary>
public enum TextAlign { Left, Center, Right }
/// <summary>Vertical anchor of text relative to its y.</summary>
public enum TextBaseline { Top, Middle, Bottom, Alphabetic }

/// <summary>Line style.</summary>
/// <param name="Color">CSS colour.</param>
public sealed record Stroke(string Color)
{
    /// <summary>Line width in logical pixels.</summary>
    public double Width { get; init; } = 1;
    /// <summary>Dash and gap lengths in pixels; null or empty for solid.</summary>
    public double[]? Dash { get; init; }
    /// <summary>Shape of the line ends.</summary>
    public LineCap Cap { get; init; } = LineCap.Butt;
    /// <summary>Shape of the corners.</summary>
    public LineJoin Join { get; init; } = LineJoin.Miter;
    /// <summary>Opacity 0–1, multiplied with the colour's alpha.</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>Fill style.</summary>
/// <param name="Color">CSS colour.</param>
public sealed record Fill(string Color)
{
    /// <summary>Opacity 0–1, multiplied with the colour's alpha.</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>Type settings for <see cref="IPainter.Text"/>.</summary>
/// <param name="Color">CSS colour.</param>
public sealed record TextStyle(string Color)
{
    /// <summary>System UI font stack.</summary>
    public const string DefaultFamily = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
    /// <summary>12 logical pixels.</summary>
    public const double DefaultSize = 12;

    /// <summary>CSS font family list.</summary>
    public string Family { get; init; } = DefaultFamily;
    /// <summary>Font size in logical pixels.</summary>
    public double Size { get; init; } = DefaultSize;
    /// <summary>"normal", "bold" or a numeric weight as text ("600"), matching CSS.</summary>
    public string Weight { get; init; } = "normal";
    /// <summary>Horizontal anchor relative to x.</summary>
    public TextAlign Align { get; init; } = TextAlign.Left;
    /// <summary>Vertical anchor relative to y.</summary>
    public TextBaseline Baseline { get; init; } = TextBaseline.Alphabetic;
    /// <summary>Radians, clockwise, around (x, y).</summary>
    public double Rotation { get; init; }
    /// <summary>Opacity 0–1, multiplied with the colour's alpha.</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>Measured size of a text run in logical pixels.</summary>
public readonly record struct TextMetrics(double Width, double Height);

/// <summary>Opaque platform image (SKImage, …).</summary>
public interface IImageHandle
{
    /// <summary>Logical width in pixels.</summary>
    double Width { get; }
    /// <summary>Logical height in pixels.</summary>
    double Height { get; }
}

/// <summary>A core-produced RGBA raster (heatmaps). Painters upload it to a platform image, re-uploading only when <see cref="Version"/> changes. Mirrors <c>RasterImage</c> in TS.</summary>
/// <param name="width">Width in pixels.</param>
/// <param name="height">Height in pixels.</param>
/// <param name="rgba">Row-major RGBA8 pixels, top row first.</param>
/// <param name="version">Changes whenever the pixels change.</param>
public sealed class RasterImage(int width, int height, byte[] rgba, long version) : IImageHandle
{
    /// <summary>Width in pixels, as a double for <see cref="IImageHandle"/>.</summary>
    public double Width => width;
    /// <summary>Height in pixels, as a double for <see cref="IImageHandle"/>.</summary>
    public double Height => height;
    /// <summary>Width in pixels.</summary>
    public int PixelWidth => width;
    /// <summary>Height in pixels.</summary>
    public int PixelHeight => height;
    /// <summary>Row-major RGBA8 pixels, top row first, 4 bytes per pixel; transparent where alpha is 0.</summary>
    public byte[] Rgba { get; } = rgba;
    /// <summary>Changes whenever the pixels change; painters re-upload only when it differs from the cached one.</summary>
    public long Version { get; } = version;
}

/// <summary>
/// The rendering contract. The core computes geometry and calls these primitives; a renderer
/// (SkiaSharp, SVG, …) implements them. Mirrors <c>paint/painter.ts</c>.
/// Conventions: pixels in logical units (the painter applies <see cref="PixelRatio"/>), y down, angles in
/// radians from +x increasing clockwise on screen. Polylines/polygons take interleaved [x0, y0, x1, y1, …].
/// </summary>
public interface IPainter
{
    /// <summary>Logical width of the surface in pixels.</summary>
    double Width { get; }
    /// <summary>Logical height of the surface in pixels.</summary>
    double Height { get; }
    /// <summary>Device pixels per logical pixel.</summary>
    double PixelRatio { get; }

    /// <summary>Pushes the current transform and clip.</summary>
    void Save();
    /// <summary>Pops the transform and clip saved by the matching <see cref="Save"/>.</summary>
    void Restore();
    /// <summary>Moves the origin by (x, y) logical pixels.</summary>
    void Translate(double x, double y);
    /// <summary>Scales subsequent drawing about the origin.</summary>
    void Scale(double sx, double sy);
    /// <summary>Rotates subsequent drawing about the origin, clockwise on screen.</summary>
    void Rotate(double radians);
    /// <summary>Intersects the clip with a rectangle until the matching <see cref="Restore"/>.</summary>
    void ClipRect(double x, double y, double w, double h);

    /// <summary>Fills the whole surface with a colour; null clears to transparent.</summary>
    void Clear(string? color = null);
    /// <summary>Stroked straight segment.</summary>
    void Line(double x1, double y1, double x2, double y2, Stroke stroke);
    /// <summary><paramref name="count"/> points (-1 = all) starting at element <paramref name="offset"/> of the interleaved span.</summary>
    void Polyline(ReadOnlySpan<double> points, Stroke stroke, int offset = 0, int count = -1);
    /// <summary>Closed polygon from interleaved points, filled and/or stroked.</summary>
    void Polygon(ReadOnlySpan<double> points, Fill? fill = null, Stroke? stroke = null);
    /// <summary>Rectangle with optional rounded corners, filled and/or stroked.</summary>
    void Rect(double x, double y, double w, double h, Fill? fill = null, Stroke? stroke = null, double radius = 0);
    /// <summary>Circle of radius <paramref name="r"/>, filled and/or stroked.</summary>
    void Circle(double cx, double cy, double r, Fill? fill = null, Stroke? stroke = null);
    /// <summary>Stroked arc from <paramref name="startAngle"/> to <paramref name="endAngle"/> (radians from +x, clockwise).</summary>
    void Arc(double cx, double cy, double r, double startAngle, double endAngle, Stroke stroke);
    /// <summary>Annular sector between two radii and two angles (radians, clockwise), filled and/or stroked.</summary>
    void Sector(double cx, double cy, double innerRadius, double outerRadius, double startAngle, double endAngle, Fill? fill = null, Stroke? stroke = null);
    /// <summary>Draws text anchored at (x, y) according to the style's alignment and baseline.</summary>
    void Text(string text, double x, double y, TextStyle style);
    /// <summary>Width and height the text would take in logical pixels.</summary>
    TextMetrics MeasureText(string text, TextStyle style);
    /// <summary>Draws an image scaled into the rectangle.</summary>
    void Image(IImageHandle image, double x, double y, double w, double h, double opacity = 1);
    /// <summary>
    /// Cached offscreen surface: <paramref name="draw"/> runs only the first time or when <paramref name="dirty"/>;
    /// the surface is then composited at (x, y). Static layers (grids, bitmaps, paused series) cost one blit.
    /// </summary>
    void Layer(string key, double width, double height, Action<IPainter> draw, double x, double y, bool dirty = false);
}
