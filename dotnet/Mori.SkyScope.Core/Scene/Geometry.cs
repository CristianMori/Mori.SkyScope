// Mori.SkyScope — 2D geometry shared by scene, charts and gauges: points, rectangles, bounds and the affine matrix.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Scene;

/// <summary>A 2D point or vector. Units depend on the space it is used in: world units or screen pixels.</summary>
public readonly record struct Vec2(double X, double Y);
/// <summary>A 3D point or vector; the 3D scene is right-handed with z up.</summary>
public readonly record struct Vec3(double X, double Y, double Z);
/// <summary>Axis-aligned rectangle given by its minimum corner and its size. Helpers in <see cref="Geometry"/> always produce non-negative <paramref name="W"/> and <paramref name="H"/>.</summary>
/// <param name="X">Left edge (minimum x).</param>
/// <param name="Y">Top edge on screen, bottom edge in a y-up world (minimum y).</param>
/// <param name="W">Width.</param>
/// <param name="H">Height.</param>
public readonly record struct Rect(double X, double Y, double W, double H)
{
    /// <summary>Maximum x edge.</summary>
    public double Right => X + W;
    /// <summary>Maximum y edge.</summary>
    public double Bottom => Y + H;
    /// <summary>Inclusive containment test (points on the edges count as inside).</summary>
    public bool Contains(double x, double y) => x >= X && x <= X + W && y >= Y && y <= Y + H;
}

/// <summary>Affine matrix in DOMMatrix order: x' = A·x + C·y + E, y' = B·x + D·y + F.</summary>
public readonly record struct Mat3(double A, double B, double C, double D, double E, double F)
{
    /// <summary>The identity transform.</summary>
    public static readonly Mat3 Identity = new(1, 0, 0, 1, 0, 0);
    /// <summary>Pure translation by (<paramref name="tx"/>, <paramref name="ty"/>).</summary>
    public static Mat3 Translation(double tx, double ty) => new(1, 0, 0, 1, tx, ty);
    /// <summary>Pure scaling about the origin; a negative <paramref name="sy"/> flips the y axis.</summary>
    public static Mat3 Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);
    /// <summary>Rotation about the origin that turns +x towards +y, which is clockwise on a y-down screen and counter-clockwise in a y-up world.</summary>
    public static Mat3 Rotation(double radians) { double c = Math.Cos(radians), s = Math.Sin(radians); return new(c, s, -s, c, 0, 0); }

    /// <summary><c>m * n</c> applies <c>n</c> first, then <c>m</c> (like DOMMatrix.multiply).</summary>
    public static Mat3 operator *(Mat3 m, Mat3 n) => new(
        m.A * n.A + m.C * n.B, m.B * n.A + m.D * n.B,
        m.A * n.C + m.C * n.D, m.B * n.C + m.D * n.D,
        m.A * n.E + m.C * n.F + m.E, m.B * n.E + m.D * n.F + m.F);

    /// <summary>The inverse transform, or null when the matrix is singular or its determinant is not finite.</summary>
    public Mat3? Invert()
    {
        var det = A * D - B * C;
        if (det == 0 || double.IsInfinity(det) || double.IsNaN(det)) return null;
        double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
        return new Mat3(ia, ib, ic, id, -(ia * E + ic * F), -(ib * E + id * F));
    }

    /// <summary>Transform a point (linear part plus translation).</summary>
    public Vec2 Apply(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);
    /// <summary>Linear part only — for deltas.</summary>
    public Vec2 ApplyLinear(double x, double y) => new(A * x + C * y, B * x + D * y);
}

/// <summary>2D geometry shared by scene, charts and gauges. Mirrors <c>scene/geometry.ts</c>; pinned by <c>spec/fixtures/geometry.json</c>.</summary>
public static class Geometry
{
    /// <summary>Bounding box of interleaved xy points; null for an empty span. A trailing odd value is ignored.</summary>
    public static Rect? RectFromPoints(ReadOnlySpan<double> points)
    {
        var n = points.Length / 2;
        if (n == 0) return null;
        double x0 = double.PositiveInfinity, y0 = double.PositiveInfinity, x1 = double.NegativeInfinity, y1 = double.NegativeInfinity;
        for (var i = 0; i < n; i++)
        {
            double x = points[2 * i], y = points[2 * i + 1];
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
        }
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>Rectangle spanned by two opposite corners given in any order.</summary>
    public static Rect RectNormalize(double x0, double y0, double x1, double y1)
        => new(Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0));

    /// <summary>Smallest rectangle containing both operands; a null operand is ignored, two nulls give null.</summary>
    public static Rect? RectUnion(Rect? a, Rect? b)
    {
        if (a is null) return b; if (b is null) return a;
        var (ra, rb) = (a.Value, b.Value);
        double x0 = Math.Min(ra.X, rb.X), y0 = Math.Min(ra.Y, rb.Y);
        return new Rect(x0, y0, Math.Max(ra.Right, rb.Right) - x0, Math.Max(ra.Bottom, rb.Bottom) - y0);
    }

    /// <summary>Axis-aligned bounding box of a rectangle after transformation.</summary>
    public static Rect TransformRect(Mat3 m, Rect r)
    {
        Span<double> pts = stackalloc double[8];
        var corners = new[] { (r.X, r.Y), (r.Right, r.Y), (r.Right, r.Bottom), (r.X, r.Bottom) };
        for (var i = 0; i < 4; i++) { var p = m.Apply(corners[i].Item1, corners[i].Item2); pts[2 * i] = p.X; pts[2 * i + 1] = p.Y; }
        return RectFromPoints(pts)!.Value;
    }

    /// <summary>Euclidean distance from (<paramref name="px"/>, <paramref name="py"/>) to the closed segment a–b; a degenerate segment measures to its point.</summary>
    public static double DistToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        var len2 = dx * dx + dy * dy;
        var t = len2 == 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / len2;
        t = Math.Max(0, Math.Min(1, t));
        double cx = ax + t * dx - px, cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>Ray casting; points interleaved, polygon implicitly closed.</summary>
    public static bool PointInPolygon(double px, double py, ReadOnlySpan<double> points)
    {
        var n = points.Length / 2;
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = points[2 * i], yi = points[2 * i + 1], xj = points[2 * j], yj = points[2 * j + 1];
            if ((yi > py) != (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
        }
        return inside;
    }
}
