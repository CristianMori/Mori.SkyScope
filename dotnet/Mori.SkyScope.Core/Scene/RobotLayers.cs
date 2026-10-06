// Mori.SkyScope — Robotics layers for the SceneView: georeferenced bitmaps, occupancy grids, point clouds (with a LaserScan converter), poses with footprin…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Scene;

/// <summary>
/// Robotics layers for the SceneView: georeferenced bitmaps, occupancy grids, point clouds (with a LaserScan converter),
/// poses with footprints, shapes and trails. World space is y-up with yaw counter-clockwise (ROS).
/// Mirrors <c>scene/robot-layers.ts</c>; pinned by <c>spec/fixtures/robot-layers.json</c>.
/// </summary>
/// <param name="X">World x in world units.</param>
/// <param name="Y">World y in world units.</param>
/// <param name="Yaw">Heading in radians, counter-clockwise from +x.</param>
public readonly record struct Pose2D(double X, double Y, double Yaw);

/// <summary>Where a raster sits in the world (ROS map convention: the origin is the world position of the bottom-left pixel corner).</summary>
/// <param name="OriginX">World x of the bottom-left pixel corner.</param>
/// <param name="OriginY">World y of the bottom-left pixel corner.</param>
/// <param name="Resolution">World units per pixel.</param>
/// <param name="Rotation">Rotation about the origin in radians, counter-clockwise.</param>
public readonly record struct RasterPlacement(double OriginX, double OriginY, double Resolution, double Rotation);

/// <summary>A planar range scan after <c>sensor_msgs/LaserScan</c>: beam <c>i</c> points at <c>AngleMin + i · AngleIncrement</c> radians in the sensor frame.</summary>
/// <param name="AngleMin">Angle of the first beam, radians.</param>
/// <param name="AngleIncrement">Angle between consecutive beams, radians.</param>
/// <param name="Ranges">One range per beam in world units; non-finite values are invalid readings.</param>
/// <param name="RangeMin">Readings below this are discarded.</param>
/// <param name="RangeMax">Readings above this are discarded.</param>
public sealed record LaserScan(double AngleMin, double AngleIncrement, double[] Ranges, double RangeMin = 0, double RangeMax = double.PositiveInfinity);

/// <summary>Pure helpers for 2D poses, raster placement, projections and laser scans.</summary>
public static class RobotGeometry
{
    /// <summary>Body frame → world: T(x, y) · R(yaw).</summary>
    public static Mat3 PoseMatrix(Pose2D p) => Mat3.Translation(p.X, p.Y) * Mat3.Rotation(p.Yaw);

    /// <summary>Pixel space (x right, y down, origin top-left) → world.</summary>
    public static Mat3 RasterToWorld(RasterPlacement p, double heightPx) =>
        Mat3.Translation(p.OriginX, p.OriginY) * Mat3.Rotation(p.Rotation) * Mat3.Scaling(p.Resolution, -p.Resolution) * Mat3.Translation(0, -heightPx);

    /// <summary>The projection as an affine matrix, sampled from three points (exact for cameras and axis scales).</summary>
    public static Mat3 ProjectionMatrix(IProjection proj)
    {
        var o = proj.Project(0, 0); var px = proj.Project(1, 0); var py = proj.Project(0, 1);
        return new Mat3(px.X - o.X, px.Y - o.Y, py.X - o.X, py.Y - o.Y, o.X, o.Y);
    }

    /// <summary>Split a conformal affine into translate → rotate → scale for painters without a general transform.</summary>
    public static (double Tx, double Ty, double Rotation, double Sx, double Sy) DecomposeConformal(Mat3 m)
    {
        var sx = Math.Sqrt(m.A * m.A + m.B * m.B);
        return (m.E, m.F, Math.Atan2(m.B, m.A), sx, sx == 0 ? 0 : (m.A * m.D - m.B * m.C) / sx);
    }

    /// <summary>Draw an image through its pixel → world matrix with the context's painter, using translate / rotate / scale (exact for conformal projections such as cameras and equal scales).</summary>
    public static void DrawPlacedImage(LayerContext ctx, IImageHandle image, Mat3 pixelToWorld, double opacity)
    {
        var d = DecomposeConformal(ProjectionMatrix(ctx.Projection) * pixelToWorld);
        var p = ctx.Painter;
        p.Save();
        p.Translate(d.Tx, d.Ty); p.Rotate(d.Rotation); p.Scale(d.Sx, d.Sy);
        p.Image(image, 0, 0, image.Width, image.Height, opacity);
        p.Restore();
    }

    /// <summary><c>sensor_msgs/LaserScan</c> → interleaved world xy through <paramref name="pose"/>; out-of-range and non-finite readings are dropped.</summary>
    public static double[] LaserScanToPoints(LaserScan scan, Pose2D pose = default)
    {
        var m = PoseMatrix(pose); var o = new List<double>();
        for (var i = 0; i < scan.Ranges.Length; i++)
        {
            var r = scan.Ranges[i];
            if (!double.IsFinite(r) || r < scan.RangeMin || r > scan.RangeMax) continue;
            var a = scan.AngleMin + i * scan.AngleIncrement; var p = m.Apply(r * Math.Cos(a), r * Math.Sin(a));
            o.Add(p.X); o.Add(p.Y);
        }
        return o.ToArray();
    }

    internal static string HexOf(byte[] lut, int k) => $"#{lut[k * 3]:x2}{lut[k * 3 + 1]:x2}{lut[k * 3 + 2]:x2}";
}

/// <summary>A georeferenced bitmap (floor plan, satellite tile, camera frame).</summary>
public sealed class BitmapLayer(string id, IImageHandle image, RasterPlacement placement) : BaseLayer(id)
{
    /// <summary><c>"bitmap"</c>.</summary>
    public override string Kind => "bitmap";
    /// <summary>The bitmap being placed.</summary>
    public IImageHandle Image { get; private set; } = image;
    /// <summary>Where the bitmap sits in the world.</summary>
    public RasterPlacement Placement { get; private set; } = placement;
    /// <summary>Swap the bitmap (same placement) and mark dirty.</summary>
    public void SetImage(IImageHandle img) { Image = img; MarkDirty(); }
    /// <summary>Move the bitmap and mark dirty.</summary>
    public void SetPlacement(RasterPlacement p) { Placement = p; MarkDirty(); }
    /// <summary>Pixel space (origin top-left, y down) → world.</summary>
    public Mat3 PixelToWorld() => RobotGeometry.RasterToWorld(Placement, Image.Height);
    /// <summary>World box of the placed image.</summary>
    public override Rect? Bounds() => Geometry.TransformRect(PixelToWorld(), new Rect(0, 0, Image.Width, Image.Height));
    /// <summary>Draws the bitmap through <see cref="RobotGeometry.DrawPlacedImage"/>.</summary>
    public override void Draw(LayerContext ctx) => RobotGeometry.DrawPlacedImage(ctx, Image, PixelToWorld(), ctx.Opacity);
    /// <summary>Pixel under the probe, as <c>index = row × width + col</c>.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        if (PixelToWorld().Invert() is not { } inv) return null;
        var w = ctx.Projection.Unproject(sx, sy); var px = inv.Apply(w.X, w.Y);
        int col = (int)Math.Floor(px.X), row = (int)Math.Floor(px.Y);
        if (col < 0 || row < 0 || col >= Image.Width || row >= Image.Height) return null;
        return new HitResult(Id, new Vec3(w.X, w.Y, 0), new Vec2(sx, sy), 0) { Index = row * (int)Image.Width + col };
    }
}

/// <summary>ROS-style occupancy grid rendered as a raster; cells are edited in place and re-uploaded on the next draw.</summary>
public sealed class OccupancyGridLayer(string id, int width, int height, double resolution) : BaseLayer(id)
{
    /// <summary><c>"occupancyGrid"</c>.</summary>
    public override string Kind => "occupancyGrid";
    /// <summary>Columns.</summary>
    public int Width { get; } = width;
    /// <summary>Rows.</summary>
    public int Height { get; } = height;
    /// <summary>World units per cell.</summary>
    public double Resolution { get; } = resolution;
    /// <summary>World pose of the bottom-left cell corner (ROS map convention); yaw rotates the grid.</summary>
    public Pose2D Origin { get; private set; }
    /// <summary>Row-major, row 0 at the origin: −1 unknown, 0 free … 100 occupied.</summary>
    public sbyte[] Data { get; } = Enumerable.Repeat((sbyte)-1, width * height).ToArray();
    /// <summary>Colour of value 0 (hex).</summary>
    public string FreeColor { get; set; } = "#ffffff";
    /// <summary>Colour of value 100 (hex); values in between interpolate.</summary>
    public string OccupiedColor { get; set; } = "#0f172a";
    /// <summary>Colour of negative (unknown) cells (hex).</summary>
    public string UnknownColor { get; set; } = "#cbd5e1";
    /// <summary>Alpha of unknown cells, 0–1.</summary>
    public double UnknownOpacity { get; set; } = 1;
    private long _version;
    private RasterImage? _raster;

    /// <summary>Copy up to <c>Width × Height</c> values into <see cref="Data"/> (truncated to sbyte) and mark dirty.</summary>
    public void SetData(ReadOnlySpan<double> data) { var n = Math.Min(data.Length, Data.Length); for (var i = 0; i < n; i++) Data[i] = (sbyte)data[i]; _version++; MarkDirty(); }
    /// <summary>Write one cell (row 0 at the origin) and mark dirty; no bounds check.</summary>
    public void Set(int row, int col, int v) { Data[row * Width + col] = (sbyte)v; _version++; MarkDirty(); }
    /// <summary>Cell value, or −1 (unknown) outside the grid.</summary>
    public int Get(int row, int col) => row < 0 || col < 0 || row >= Height || col >= Width ? -1 : Data[row * Width + col];
    /// <summary>Move the grid and mark dirty.</summary>
    public void SetOrigin(Pose2D p) { Origin = p; MarkDirty(); }
    /// <summary>The grid's placement as a raster.</summary>
    public RasterPlacement Placement() => new(Origin.X, Origin.Y, Resolution, Origin.Yaw);
    /// <summary>Raster pixel space (origin top-left, y down) → world.</summary>
    public Mat3 PixelToWorld() => RobotGeometry.RasterToWorld(Placement(), Height);
    /// <summary>World → (row, col) or null outside the grid.</summary>
    public (int Row, int Col)? CellAt(double wx, double wy)
    {
        if (PixelToWorld().Invert() is not { } inv) return null;
        var px = inv.Apply(wx, wy); int col = (int)Math.Floor(px.X), row = Height - 1 - (int)Math.Floor(px.Y);
        return col < 0 || row < 0 || col >= Width || row >= Height ? null : (row, col);
    }
    /// <summary>RGBA raster (row 0 = top = last data row). Free → occupied interpolates the two colours.</summary>
    public RasterImage Image()
    {
        if (_raster is not null && _raster.Version == _version) return _raster;
        var free = Colormaps.ParseHex(FreeColor); var occ = Colormaps.ParseHex(OccupiedColor); var unk = Colormaps.ParseHex(UnknownColor);
        var rgba = new byte[Width * Height * 4]; var ua = (byte)Math.Floor(UnknownOpacity * 255 + 0.5);
        for (var row = 0; row < Height; row++)
        {
            var py = Height - 1 - row;
            for (var col = 0; col < Width; col++)
            {
                int v = Data[row * Width + col], o = (py * Width + col) * 4;
                if (v < 0) { rgba[o] = (byte)unk.R; rgba[o + 1] = (byte)unk.G; rgba[o + 2] = (byte)unk.B; rgba[o + 3] = ua; continue; }
                var t = Math.Min(100, v) / 100.0;
                rgba[o] = (byte)Math.Floor(free.R + (occ.R - free.R) * t + 0.5); rgba[o + 1] = (byte)Math.Floor(free.G + (occ.G - free.G) * t + 0.5); rgba[o + 2] = (byte)Math.Floor(free.B + (occ.B - free.B) * t + 0.5); rgba[o + 3] = 255;
            }
        }
        return _raster = new RasterImage(Width, Height, rgba, _version);
    }
    /// <summary>World box of the grid.</summary>
    public override Rect? Bounds() => Geometry.TransformRect(PixelToWorld(), new Rect(0, 0, Width, Height));
    /// <summary>Draws the rasterized grid through <see cref="RobotGeometry.DrawPlacedImage"/>.</summary>
    public override void Draw(LayerContext ctx) => RobotGeometry.DrawPlacedImage(ctx, Image(), PixelToWorld(), ctx.Opacity);
    /// <summary>The cell under the probe: <see cref="HitResult.Index"/> = <c>row × Width + col</c>, <see cref="HitResult.Data"/> = the cell value (int). Null outside the grid; tolerance is ignored.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        var w = ctx.Projection.Unproject(sx, sy);
        if (CellAt(w.X, w.Y) is not { } c) return null;
        return new HitResult(Id, new Vec3(w.X, w.Y, 0), new Vec2(sx, sy), 0) { Index = c.Row * Width + c.Col, Data = Get(c.Row, c.Col) };
    }
}

/// <summary>Lidar / point-cloud layer: one square per point, optional intensity colouring.</summary>
public sealed class PointCloudLayer(string id) : BaseLayer(id)
{
    private double[] _points = [];
    private double[]? _intensities;
    /// <summary><c>"pointCloud"</c>.</summary>
    public override string Kind => "pointCloud";
    /// <summary>Named colormap for intensity colouring (see <c>Colormaps.Stops</c>); ignored when <see cref="ColormapStops"/> is set.</summary>
    public string Colormap { get; set; } = "turbo";
    /// <summary>Explicit hex colour stops overriding <see cref="Colormap"/>.</summary>
    public string[]? ColormapStops { get; set; }
    /// <summary>Intensity values mapped to the colormap ends; the data min/max when null.</summary>
    public (double Lo, double Hi)? IntensityRange { get; set; }
    /// <summary>Flat colour (hex) used when no intensities are present.</summary>
    public string Color { get; set; } = "#dc2626";
    /// <summary>Screen pixels.</summary>
    public double PointSize { get; set; } = 2;
    /// <summary>Number of points.</summary>
    public int PointCount => _points.Length / 2;

    /// <summary>Replace the cloud: interleaved world xy plus an optional intensity per point (an empty span clears intensities). Both are copied.</summary>
    public void SetPoints(ReadOnlySpan<double> points, ReadOnlySpan<double> intensities = default)
    {
        _points = points.ToArray(); _intensities = intensities.Length > 0 ? intensities.ToArray() : null; MarkDirty();
    }
    /// <summary>Replace the cloud with a laser scan transformed through the sensor <paramref name="pose"/>; intensities are cleared.</summary>
    public void SetScan(LaserScan scan, Pose2D pose = default) => SetPoints(RobotGeometry.LaserScanToPoints(scan, pose));
    /// <summary>Bounding box of the points; null when there are none.</summary>
    public override Rect? Bounds() => Geometry.RectFromPoints(_points);
    private string[] Stops() => ColormapStops ?? Colormaps.Stops(Colormap);
    private (double, double) DataRange()
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var v in _intensities!) { if (v < lo) lo = v; if (v > hi) hi = v; }
        return double.IsFinite(lo) && hi > lo ? (lo, hi) : (0, 1);
    }
    private static int Bucket(double v, double lo, double span) { var k = (int)Math.Floor((v - lo) / span * 255 + 0.5); return k < 0 ? 0 : k > 255 ? 255 : k; }
    /// <summary>Colour of point <paramref name="i"/> (hex).</summary>
    public string ColorOf(int i)
    {
        if (_intensities is null || i >= _intensities.Length) return Color;
        var (lo, hi) = IntensityRange ?? DataRange();
        return RobotGeometry.HexOf(Colormaps.Lut(Stops(), 256), Bucket(_intensities[i], lo, Math.Max(1e-12, hi - lo)));
    }
    /// <summary>Draws one <see cref="PointSize"/>-pixel square per point, coloured through the colormap when intensities are present.</summary>
    public override void Draw(LayerContext ctx)
    {
        double s = PointSize, h = s / 2; var p = ctx.Painter;
        var plain = new Fill(Color) { Opacity = ctx.Opacity };
        byte[]? lut = null; double lo = 0, span = 1;
        if (_intensities is not null) { lut = Colormaps.Lut(Stops(), 256); var (a, b) = IntensityRange ?? DataRange(); lo = a; span = Math.Max(1e-12, b - a); }
        for (var i = 0; i < PointCount; i++)
        {
            var q = ctx.Projection.Project(_points[2 * i], _points[2 * i + 1]);
            var fill = plain;
            if (lut is not null && i < _intensities!.Length) fill = new Fill(RobotGeometry.HexOf(lut, Bucket(_intensities[i], lo, span))) { Opacity = ctx.Opacity };
            p.Rect(q.X - h, q.Y - h, s, s, fill);
        }
    }
    /// <summary>Nearest point within <paramref name="tolerance"/> + half the point size. <see cref="HitResult.Index"/> is the point index, <see cref="HitResult.Data"/> its intensity (double) when present.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        var best = double.PositiveInfinity; var bestIndex = -1;
        for (var i = 0; i < PointCount; i++)
        {
            var q = ctx.Projection.Project(_points[2 * i], _points[2 * i + 1]);
            var d = Math.Sqrt((q.X - sx) * (q.X - sx) + (q.Y - sy) * (q.Y - sy));
            if (d < best) { best = d; bestIndex = i; }
        }
        if (bestIndex < 0 || best > tolerance + PointSize / 2) return null;
        return new HitResult(Id, new Vec3(_points[2 * bestIndex], _points[2 * bestIndex + 1], 0), new Vec2(sx, sy), best) { Index = bestIndex, Data = _intensities is not null && bestIndex < _intensities.Length ? _intensities[bestIndex] : null };
    }
}

/// <summary>A robot (or any oriented thing): heading arrow, optional footprint and label.</summary>
public sealed class PoseLayer : BaseLayer
{
    /// <summary><c>"pose"</c>.</summary>
    public override string Kind => "pose";
    /// <summary>Current pose, world units and radians.</summary>
    public Pose2D Pose { get; private set; }
    /// <summary>Outline in the body frame (interleaved xy, world units, implicitly closed); null draws no footprint.</summary>
    public double[]? Footprint { get; set; }
    /// <summary>Arrow, footprint and label colour (hex).</summary>
    public string Color { get; set; } = "#2563eb";
    /// <summary>Footprint fill alpha, 0–1 (the outline is drawn opaque).</summary>
    public double FillOpacity { get; set; } = 0.25;
    /// <summary>Arrow size in screen pixels.</summary>
    public double ArrowSize { get; set; } = 14;
    /// <summary>Text drawn above the arrow; null draws none.</summary>
    public string? Label { get; set; }
    /// <summary>Label font family; the painter default when null.</summary>
    public string? FontFamily { get; set; }
    /// <summary>Label font size in pixels.</summary>
    public double FontSize { get; set; } = 11;

    /// <summary>Creates the layer at <paramref name="pose"/>. Not cacheable, since a pose changes every update.</summary>
    public PoseLayer(string id, Pose2D pose) : base(id) { Pose = pose; Cacheable = false; }
    /// <summary>Move the pose and mark dirty.</summary>
    public void SetPose(Pose2D p) { Pose = p; MarkDirty(); }
    /// <summary>Footprint in world coordinates (interleaved).</summary>
    public double[]? WorldFootprint()
    {
        if (Footprint is null) return null;
        var m = RobotGeometry.PoseMatrix(Pose); var o = new double[Footprint.Length];
        for (var i = 0; i < Footprint.Length; i += 2) { var p = m.Apply(Footprint[i], Footprint[i + 1]); o[i] = p.X; o[i + 1] = p.Y; }
        return o;
    }
    /// <summary>Box of the world footprint, or a zero-size box at the position when there is none.</summary>
    public override Rect? Bounds() => WorldFootprint() is { } f ? Geometry.RectFromPoints(f) : new Rect(Pose.X, Pose.Y, 0, 0);
    /// <summary>Screen-space arrow polygon: tip, right wing, notch, left wing.</summary>
    public double[] Arrow(HitContext ctx)
    {
        var c = ctx.Projection.Project(Pose.X, Pose.Y); var f = ctx.Projection.Project(Pose.X + Math.Cos(Pose.Yaw), Pose.Y + Math.Sin(Pose.Yaw));
        var len = Math.Sqrt((f.X - c.X) * (f.X - c.X) + (f.Y - c.Y) * (f.Y - c.Y)); if (len == 0) len = 1;
        double ux = (f.X - c.X) / len, uy = (f.Y - c.Y) / len, s = ArrowSize;
        double tipX = c.X + ux * s, tipY = c.Y + uy * s, backX = c.X - ux * s * 0.5, backY = c.Y - uy * s * 0.5;
        return [tipX, tipY, backX - uy * s * 0.45, backY + ux * s * 0.45, c.X - ux * s * 0.2, c.Y - uy * s * 0.2, backX + uy * s * 0.45, backY - ux * s * 0.45];
    }
    /// <summary>Draws the footprint, the arrow and the label.</summary>
    public override void Draw(LayerContext ctx)
    {
        var p = ctx.Painter; var stroke = new Stroke(Color) { Width = 1.5, Opacity = ctx.Opacity, Join = LineJoin.Round };
        if (WorldFootprint() is { } wf)
        {
            var s = new double[wf.Length];
            for (var i = 0; i < wf.Length; i += 2) { var q = ctx.Projection.Project(wf[i], wf[i + 1]); s[i] = q.X; s[i + 1] = q.Y; }
            p.Polygon(s, new Fill(Color) { Opacity = FillOpacity * ctx.Opacity }, stroke);
        }
        p.Polygon(Arrow(ctx.Hit), new Fill(Color) { Opacity = ctx.Opacity }, new Stroke("#ffffff") { Opacity = ctx.Opacity });
        if (Label is not null)
        {
            var c = ctx.Projection.Project(Pose.X, Pose.Y);
            var t = new TextStyle(Color) { Size = FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Bottom, Opacity = ctx.Opacity };
            if (FontFamily is not null) t = t with { Family = FontFamily };
            p.Text(Label, c.X, c.Y - ArrowSize - 3, t);
        }
    }
    /// <summary>Hit when the probe is within <see cref="ArrowSize"/> + <paramref name="tolerance"/> pixels of the position; <see cref="HitResult.Data"/> is the <see cref="Pose2D"/>.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        var c = ctx.Projection.Project(Pose.X, Pose.Y); var d = Math.Sqrt((c.X - sx) * (c.X - sx) + (c.Y - sy) * (c.Y - sy));
        if (d > tolerance + ArrowSize) return null;
        return new HitResult(Id, new Vec3(Pose.X, Pose.Y, 0), new Vec2(sx, sy), d) { Data = Pose };
    }
}

/// <summary>Annotations in world units: zones, goals, no-go areas, labels.</summary>
public abstract record Shape(string Id)
{
    /// <summary>Text drawn at the shape's centre; null draws none.</summary>
    public string? Label { get; init; }
    /// <summary>A circle of world radius <paramref name="R"/> centred on (<paramref name="X"/>, <paramref name="Y"/>). With neither fill nor stroke a default outline is drawn.</summary>
    public sealed record Circle(string Id, double X, double Y, double R, Fill? Fill = null, Stroke? Stroke = null) : Shape(Id);
    /// <summary>An axis-aligned world rectangle from its minimum corner. With neither fill nor stroke a default outline is drawn.</summary>
    public sealed record Box(string Id, double X, double Y, double W, double H, Fill? Fill = null, Stroke? Stroke = null) : Shape(Id);
    /// <summary>A closed polygon; <paramref name="Points"/> is interleaved world xy, the last point joins the first.</summary>
    public sealed record Polygon(string Id, double[] Points, Fill? Fill = null, Stroke? Stroke = null) : Shape(Id);
    /// <summary>An open polyline; <paramref name="Points"/> is interleaved world xy.</summary>
    public sealed record Line(string Id, double[] Points, Stroke? Stroke = null) : Shape(Id);
    /// <summary>A text label anchored at a world position; <paramref name="Style"/> null uses the layer's label style.</summary>
    public sealed record Text(string Id, double X, double Y, string Value, TextStyle? Style = null) : Shape(Id);
}

/// <summary>Draws a list of <see cref="Shape"/> annotations in list order (last on top); hit testing runs top-down.</summary>
public sealed class ShapeLayer(string id, IEnumerable<Shape>? shapes = null) : BaseLayer(id)
{
    /// <summary><c>"shapes"</c>.</summary>
    public override string Kind => "shapes";
    /// <summary>The shapes in draw order. Prefer <see cref="SetShapes"/>, <see cref="Upsert"/> and <see cref="RemoveShape"/>, which mark the layer dirty.</summary>
    public List<Shape> Shapes { get; private set; } = shapes?.ToList() ?? [];
    /// <summary>Label font family; the painter default when null.</summary>
    public string? FontFamily { get; set; }
    /// <summary>Label font size in pixels (also the hit radius of text shapes).</summary>
    public double FontSize { get; set; } = 11;
    private static readonly Stroke DefaultStroke = new("#2563eb") { Width = 1.5 };

    /// <summary>Replace every shape and mark dirty.</summary>
    public void SetShapes(IEnumerable<Shape> s) { Shapes = s.ToList(); MarkDirty(); }
    /// <summary>Replace the shape with the same id in place, or append it; marks dirty.</summary>
    public void Upsert(Shape s) { var i = Shapes.FindIndex(x => x.Id == s.Id); if (i < 0) Shapes.Add(s); else Shapes[i] = s; MarkDirty(); }
    /// <summary>Remove the shape with this id; false when there is none.</summary>
    public bool RemoveShape(string shapeId) { var i = Shapes.FindIndex(x => x.Id == shapeId); if (i < 0) return false; Shapes.RemoveAt(i); MarkDirty(); return true; }
    /// <summary>World outline of a shape (circles are approximated by their bounding square for bounds).</summary>
    public static double[] Outline(Shape s) => s switch
    {
        Shape.Circle c => [c.X - c.R, c.Y - c.R, c.X + c.R, c.Y - c.R, c.X + c.R, c.Y + c.R, c.X - c.R, c.Y + c.R],
        Shape.Box b => [b.X, b.Y, b.X + b.W, b.Y, b.X + b.W, b.Y + b.H, b.X, b.Y + b.H],
        Shape.Polygon p => p.Points,
        Shape.Line l => l.Points,
        Shape.Text t => [t.X, t.Y],
        _ => [],
    };
    /// <summary>Box of every shape's outline; null when there are no shapes.</summary>
    public override Rect? Bounds() { var pts = new List<double>(); foreach (var s in Shapes) pts.AddRange(Outline(s)); return Geometry.RectFromPoints(pts.ToArray()); }
    private static Vec2 Center(Shape s)
    {
        switch (s)
        {
            case Shape.Circle c: return new Vec2(c.X, c.Y);
            case Shape.Text t: return new Vec2(t.X, t.Y);
            case Shape.Box b: return new Vec2(b.X + b.W / 2, b.Y + b.H / 2);
            default: { var r = Geometry.RectFromPoints(Outline(s)) ?? new Rect(0, 0, 0, 0); return new Vec2(r.X + r.W / 2, r.Y + r.H / 2); }
        }
    }
    /// <summary>Draws every shape and its label.</summary>
    public override void Draw(LayerContext ctx)
    {
        var p = ctx.Painter; var o = ctx.Opacity;
        Fill? Fo(Fill? f) => f is null ? null : f with { Opacity = f.Opacity * o };
        Stroke? So(Stroke? s) => s is null ? null : s with { Opacity = s.Opacity * o };
        var labelStyle = new TextStyle("#0f172a") { Size = FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Middle, Opacity = o };
        if (FontFamily is not null) labelStyle = labelStyle with { Family = FontFamily };
        foreach (var s in Shapes)
        {
            switch (s)
            {
                case Shape.Circle c:
                    {
                        var ctr = ctx.Projection.Project(c.X, c.Y); var e = ctx.Projection.Project(c.X + c.R, c.Y);
                        p.Circle(ctr.X, ctr.Y, Math.Sqrt((e.X - ctr.X) * (e.X - ctr.X) + (e.Y - ctr.Y) * (e.Y - ctr.Y)), Fo(c.Fill), So(c.Stroke ?? (c.Fill is null ? DefaultStroke : null)));
                        break;
                    }
                case Shape.Text t:
                    {
                        var ctr = ctx.Projection.Project(t.X, t.Y);
                        var st = t.Style ?? labelStyle with { Opacity = 1 };
                        p.Text(t.Value, ctr.X, ctr.Y, st with { Opacity = st.Opacity * o });
                        continue;
                    }
                default:
                    {
                        var pts = Outline(s); var scr = new double[pts.Length];
                        for (var i = 0; i < pts.Length; i += 2) { var q = ctx.Projection.Project(pts[i], pts[i + 1]); scr[i] = q.X; scr[i + 1] = q.Y; }
                        if (s is Shape.Line ln) p.Polyline(scr, So(ln.Stroke) ?? DefaultStroke with { Opacity = o });
                        else { var (fill, stroke) = s switch { Shape.Box b => (b.Fill, b.Stroke), Shape.Polygon pg => (pg.Fill, pg.Stroke), _ => (null, null) }; p.Polygon(scr, Fo(fill), So(stroke ?? (fill is null ? DefaultStroke : null))); }
                        break;
                    }
            }
            if (s.Label is not null) { var ctr = Center(s); var q = ctx.Projection.Project(ctr.X, ctr.Y); p.Text(s.Label, q.X, q.Y, labelStyle); }
        }
    }
    /// <summary>Topmost shape under the probe: circles and text by screen distance, lines by segment distance, boxes and polygons by containment. <see cref="HitResult.Index"/> is the list index, <see cref="HitResult.Data"/> the shape id.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        var w = ctx.Projection.Unproject(sx, sy);
        for (var i = Shapes.Count - 1; i >= 0; i--)
        {
            var s = Shapes[i]; var hit = false; double dist = 0;
            switch (s)
            {
                case Shape.Circle c: { var ctr = ctx.Projection.Project(c.X, c.Y); var e = ctx.Projection.Project(c.X + c.R, c.Y); dist = Math.Sqrt((ctr.X - sx) * (ctr.X - sx) + (ctr.Y - sy) * (ctr.Y - sy)); hit = dist <= Math.Sqrt((e.X - ctr.X) * (e.X - ctr.X) + (e.Y - ctr.Y) * (e.Y - ctr.Y)) + tolerance; break; }
                case Shape.Text t: { var ctr = ctx.Projection.Project(t.X, t.Y); dist = Math.Sqrt((ctr.X - sx) * (ctr.X - sx) + (ctr.Y - sy) * (ctr.Y - sy)); hit = dist <= tolerance + FontSize; break; }
                case Shape.Line l:
                    {
                        var pts = l.Points; dist = double.PositiveInfinity;
                        for (var k = 0; k + 3 < pts.Length; k += 2) { var a = ctx.Projection.Project(pts[k], pts[k + 1]); var b = ctx.Projection.Project(pts[k + 2], pts[k + 3]); dist = Math.Min(dist, Geometry.DistToSegment(sx, sy, a.X, a.Y, b.X, b.Y)); }
                        hit = dist <= tolerance; break;
                    }
                default: hit = Geometry.PointInPolygon(w.X, w.Y, Outline(s)); break;
            }
            if (hit) return new HitResult(Id, new Vec3(w.X, w.Y, 0), new Vec2(sx, sy), dist) { Index = i, Data = s.Id };
        }
        return null;
    }
}

/// <summary>A polyline that keeps only the last <see cref="MaxPoints"/> (robot trail).</summary>
public sealed class TrailLayer(string id, int maxPoints = 2000, Stroke? stroke = null) : PolylineLayer(id, [], stroke ?? new Stroke("#16a34a") { Width = 2 })
{
    /// <summary>Points kept; older ones are dropped on <see cref="Append"/>.</summary>
    public int MaxPoints { get; set; } = maxPoints;
    /// <summary>Add a point and trim the oldest beyond <see cref="MaxPoints"/>.</summary>
    public override void Append(double x, double y)
    {
        base.Append(x, y);
        if (PointCount > MaxPoints) { var all = RawPoints(); SetPoints(all.AsSpan(all.Length - MaxPoints * 2)); }
    }
}
