// Mori.SkyScope — Basic 2D scene layers: polylines, point sets and the metric grid.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Scene;

/// <summary>Multiplies a style's opacity by a layer opacity, returning the same instance when nothing changes.</summary>
internal static class StyleOpacity
{
    /// <summary>A stroke with its opacity scaled by <paramref name="o"/>.</summary>
    public static Stroke With(Stroke s, double o) => o == 1 ? s : s with { Opacity = s.Opacity * o };
    /// <summary>A fill with its opacity scaled by <paramref name="o"/>.</summary>
    public static Fill With(Fill f, double o) => o == 1 ? f : f with { Opacity = f.Opacity * o };
}

/// <summary>World-space polyline (robot path, contour, static series).</summary>
public class PolylineLayer(string id, ReadOnlySpan<double> points, Stroke? stroke = null, bool closed = false) : BaseLayer(id)
{
    private double[] _points = points.ToArray();
    private double[] _screen = [];
    /// <summary><c>"polyline"</c>.</summary>
    public override string Kind => "polyline";
    /// <summary>Line style; width in pixels. Setting it does not mark the layer dirty.</summary>
    public Stroke Stroke { get; set; } = stroke ?? new Stroke("#2563eb") { Width = 2 };
    /// <summary>Join the last point back to the first and draw the outline as a polygon.</summary>
    public bool Closed { get; set; } = closed;
    /// <summary>Number of world points.</summary>
    public int PointCount => _points.Length / 2;

    /// <summary>Replace every point (interleaved world xy, copied) and mark the layer dirty.</summary>
    public void SetPoints(ReadOnlySpan<double> pts) { _points = pts.ToArray(); MarkDirty(); }
    /// <summary>Add one world point. Grows the backing array by copy, so prefer <see cref="SetPoints"/> for bulk updates.</summary>
    public virtual void Append(double x, double y) { Array.Resize(ref _points, _points.Length + 2); _points[^2] = x; _points[^1] = y; MarkDirty(); }
    /// <summary>The backing array (interleaved world xy); do not mutate.</summary>
    public double[] RawPoints() => _points;
    /// <summary>Bounding box of the points; null when there are none.</summary>
    public override Rect? Bounds() => Geometry.RectFromPoints(_points);

    private double[] ProjectAll(IProjection proj)
    {
        var n = PointCount;
        if (_screen.Length != n * 2) _screen = new double[n * 2];
        for (var i = 0; i < n; i++) { var p = proj.Project(_points[2 * i], _points[2 * i + 1]); _screen[2 * i] = p.X; _screen[2 * i + 1] = p.Y; }
        return _screen;
    }

    /// <summary>Projects every point and strokes the polyline (or polygon outline when <see cref="Closed"/>).</summary>
    public override void Draw(LayerContext ctx)
    {
        var s = ProjectAll(ctx.Projection);
        if (Closed) ctx.Painter.Polygon(s, null, StyleOpacity.With(Stroke, ctx.Opacity));
        else ctx.Painter.Polyline(s, StyleOpacity.With(Stroke, ctx.Opacity));
    }

    /// <summary>Nearest segment within <paramref name="tolerance"/> pixels. <see cref="HitResult.Index"/> is the index of the segment's first point; <see cref="HitResult.World"/> is the probe position in world units.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        var s = ProjectAll(ctx.Projection);
        var n = PointCount;
        double best = double.PositiveInfinity; var bestIndex = -1;
        var segs = Closed ? n : n - 1;
        for (var i = 0; i < segs; i++)
        {
            var j = (i + 1) % n;
            var d = Geometry.DistToSegment(sx, sy, s[2 * i], s[2 * i + 1], s[2 * j], s[2 * j + 1]);
            if (d < best) { best = d; bestIndex = i; }
        }
        if (bestIndex < 0 || best > tolerance) return null;
        var w = ctx.Projection.Unproject(sx, sy);
        return new HitResult(Id, new Vec3(w.X, w.Y, 0), new Vec2(sx, sy), best) { Index = bestIndex };
    }
}

/// <summary>World-space markers (waypoints, scatter).</summary>
public sealed class PointsLayer(string id, ReadOnlySpan<double> points, double radius = 4, Fill? fill = null, Stroke? stroke = null) : BaseLayer(id)
{
    private double[] _points = points.ToArray();
    /// <summary><c>"points"</c>.</summary>
    public override string Kind => "points";
    /// <summary>Marker radius in screen pixels.</summary>
    public double Radius { get; set; } = radius;
    /// <summary>Marker fill; null draws outlines only.</summary>
    public Fill? Fill { get; set; } = fill ?? new Fill("#dc2626");
    /// <summary>Marker outline; null draws none.</summary>
    public Stroke? Stroke { get; set; } = stroke;
    /// <summary>Number of markers.</summary>
    public int PointCount => _points.Length / 2;
    /// <summary>Replace every marker position (interleaved world xy, copied) and mark the layer dirty.</summary>
    public void SetPoints(ReadOnlySpan<double> pts) { _points = pts.ToArray(); MarkDirty(); }
    /// <summary>Bounding box of the marker centres; null when there are none.</summary>
    public override Rect? Bounds() => Geometry.RectFromPoints(_points);

    /// <summary>Draws one circle of <see cref="Radius"/> pixels per point.</summary>
    public override void Draw(LayerContext ctx)
    {
        var fill = Fill is null ? null : StyleOpacity.With(Fill, ctx.Opacity);
        var stroke = Stroke is null ? null : StyleOpacity.With(Stroke, ctx.Opacity);
        for (var i = 0; i < PointCount; i++) { var p = ctx.Projection.Project(_points[2 * i], _points[2 * i + 1]); ctx.Painter.Circle(p.X, p.Y, Radius, fill, stroke); }
    }

    /// <summary>Nearest marker whose centre lies within <see cref="Radius"/> + <paramref name="tolerance"/> pixels. <see cref="HitResult.Index"/> is the point index and <see cref="HitResult.World"/> the marker's world position.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        double best = double.PositiveInfinity; var bestIndex = -1;
        for (var i = 0; i < PointCount; i++)
        {
            var p = ctx.Projection.Project(_points[2 * i], _points[2 * i + 1]);
            var d = Math.Sqrt((p.X - sx) * (p.X - sx) + (p.Y - sy) * (p.Y - sy));
            if (d < best) { best = d; bestIndex = i; }
        }
        if (bestIndex < 0 || best > Radius + tolerance) return null;
        return new HitResult(Id, new Vec3(_points[2 * bestIndex], _points[2 * bestIndex + 1], 0), new Vec2(sx, sy), best) { Index = bestIndex };
    }
}

/// <summary>World-aligned grid that adapts its spacing to the zoom. Works with any projection.</summary>
public sealed class GridLayer(string id) : BaseLayer(id)
{
    /// <summary><c>"grid"</c>.</summary>
    public override string Kind => "grid";
    /// <summary>World units between lines; null = auto 1-2-5 step for about <see cref="TargetPixels"/> px.</summary>
    public double? Spacing { get; set; }
    /// <summary>Desired on-screen distance between lines, in pixels, when <see cref="Spacing"/> is automatic.</summary>
    public double TargetPixels { get; set; } = 80;
    /// <summary>Minor line style.</summary>
    public Stroke Stroke { get; set; } = new("#e2e8f0");
    /// <summary>Every n-th line, counted from world zero, is drawn with <see cref="MajorStroke"/>.</summary>
    public int MajorEvery { get; set; } = 5;
    /// <summary>Major line style.</summary>
    public Stroke MajorStroke { get; set; } = new("#cbd5e1");

    /// <summary>Visible world AABB from the viewport corners — works for cameras and scale projections alike.</summary>
    public static Rect VisibleBounds(HitContext ctx)
    {
        Span<double> pts = stackalloc double[8];
        var corners = new[] { (0.0, 0.0), (ctx.Width, 0.0), (ctx.Width, ctx.Height), (0.0, ctx.Height) };
        for (var i = 0; i < 4; i++) { var w = ctx.Projection.Unproject(corners[i].Item1, corners[i].Item2); pts[2 * i] = w.X; pts[2 * i + 1] = w.Y; }
        return Geometry.RectFromPoints(pts)!.Value;
    }

    /// <summary>Line spacing per axis in world units: <see cref="Spacing"/> when set, otherwise the 1-2-5 tick step that gives about <see cref="TargetPixels"/> pixels per line over the visible bounds.</summary>
    public (double X, double Y) Step(HitContext ctx)
    {
        if (Spacing is { } s) return (s, s);
        var b = VisibleBounds(ctx);
        int nx = Math.Max(1, (int)Math.Round(ctx.Width / TargetPixels)), ny = Math.Max(1, (int)Math.Round(ctx.Height / TargetPixels));
        return (Ticks.Spec(b.X, b.Right, nx).Step, Ticks.Spec(b.Y, b.Bottom, ny).Step);
    }

    /// <summary>Draws vertical and horizontal lines across the visible bounds; nothing when the step is not positive.</summary>
    public override void Draw(LayerContext ctx)
    {
        var b = VisibleBounds(ctx.Hit);
        var (stx, sty) = Step(ctx.Hit);
        if (!(stx > 0) || !(sty > 0)) return;
        Stroke minor = StyleOpacity.With(Stroke, ctx.Opacity), major = StyleOpacity.With(MajorStroke, ctx.Opacity);
        foreach (var x in Ticks.FromSpec(b.X, b.Right, new TickSpec(stx, stx < 1 ? 1 / stx : 0, 0)))
        {
            Vec2 a = ctx.Projection.Project(x, b.Y), c = ctx.Projection.Project(x, b.Bottom);
            ctx.Painter.Line(a.X, a.Y, c.X, c.Y, (long)Math.Round(x / stx) % MajorEvery == 0 ? major : minor);
        }
        foreach (var y in Ticks.FromSpec(b.Y, b.Bottom, new TickSpec(sty, sty < 1 ? 1 / sty : 0, 0)))
        {
            Vec2 a = ctx.Projection.Project(b.X, y), c = ctx.Projection.Project(b.Right, y);
            ctx.Painter.Line(a.X, a.Y, c.X, c.Y, (long)Math.Round(y / sty) % MajorEvery == 0 ? major : minor);
        }
    }
}
