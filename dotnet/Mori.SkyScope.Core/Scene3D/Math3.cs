// Mori.SkyScope — 3D math shared by the 3D camera, frame tree and layers: vectors, quaternions, column-major matrices, boxes, rays.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>Unit quaternion (x, y, z, w).</summary>
public readonly record struct Quat(double X, double Y, double Z, double W)
{
    /// <summary>No rotation.</summary>
    public static readonly Quat Identity = new(0, 0, 0, 1);
    /// <summary>Unit-length copy; the zero quaternion becomes <see cref="Identity"/>.</summary>
    public Quat Normalized() { var l = Math.Sqrt(X * X + Y * Y + Z * Z + W * W); return l > 0 ? new(X / l, Y / l, Z / l, W / l) : Identity; }
    /// <summary>The inverse rotation (for a unit quaternion).</summary>
    public Quat Conjugate() => new(-X, -Y, -Z, W);
    /// <summary>Rotation of <paramref name="radians"/> about a unit axis.</summary>
    public static Quat FromAxisAngle(Vec3 axis, double radians) { var a = Math3.Normalize(axis); var s = Math.Sin(radians / 2); return new(a.X * s, a.Y * s, a.Z * s, Math.Cos(radians / 2)); }
    /// <summary>ZYX (yaw about z, then pitch about y, then roll about x) — the ROS/REP-103 convention.</summary>
    public static Quat FromEuler(double roll, double pitch, double yaw)
    {
        double cr = Math.Cos(roll / 2), sr = Math.Sin(roll / 2), cp = Math.Cos(pitch / 2), sp = Math.Sin(pitch / 2), cy = Math.Cos(yaw / 2), sy = Math.Sin(yaw / 2);
        return new(sr * cp * cy - cr * sp * sy, cr * sp * cy + sr * cp * sy, cr * cp * sy - sr * sp * cy, cr * cp * cy + sr * sp * sy);
    }
    /// <summary>Inverse of <see cref="FromEuler"/>; pitch clamped to ±π/2.</summary>
    public (double Roll, double Pitch, double Yaw) ToEuler()
    {
        var sinp = 2 * (W * Y - Z * X);
        return (Math.Atan2(2 * (W * X + Y * Z), 1 - 2 * (X * X + Y * Y)), Math.Abs(sinp) >= 1 ? (sinp < 0 ? -Math.PI / 2 : Math.PI / 2) : Math.Asin(sinp), Math.Atan2(2 * (W * Z + X * Y), 1 - 2 * (Y * Y + Z * Z)));
    }
    /// <summary><c>a * b</c> applies <c>b</c> first, then <c>a</c>.</summary>
    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    /// <summary>Rotate a vector by this (unit) quaternion.</summary>
    public Vec3 Rotate(Vec3 v)
    {
        double tx = 2 * (Y * v.Z - Z * v.Y), ty = 2 * (Z * v.X - X * v.Z), tz = 2 * (X * v.Y - Y * v.X);
        return new(v.X + W * tx + (Y * tz - Z * ty), v.Y + W * ty + (Z * tx - X * tz), v.Z + W * tz + (X * ty - Y * tx));
    }
    /// <summary>Spherical interpolation along the shorter arc.</summary>
    public static Quat Slerp(Quat a, Quat b, double t)
    {
        double bx = b.X, by = b.Y, bz = b.Z, bw = b.W;
        var cos = a.X * bx + a.Y * by + a.Z * bz + a.W * bw;
        if (cos < 0) { cos = -cos; bx = -bx; by = -by; bz = -bz; bw = -bw; }
        double ka, kb;
        if (cos > 0.9995) { ka = 1 - t; kb = t; }
        else { var th = Math.Acos(cos); var s = Math.Sin(th); ka = Math.Sin((1 - t) * th) / s; kb = Math.Sin(t * th) / s; }
        return new Quat(ka * a.X + kb * bx, ka * a.Y + kb * by, ka * a.Z + kb * bz, ka * a.W + kb * bw).Normalized();
    }
}

/// <summary>Axis-aligned 3D box; a box whose <paramref name="Max"/> lies below <paramref name="Min"/> on any axis is empty.</summary>
public readonly record struct Box3(Vec3 Min, Vec3 Max)
{
    /// <summary>The box that contains nothing (+∞ min, −∞ max), the identity for unions.</summary>
    public static Box3 Empty => new(new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));
    /// <summary>True when inverted on any axis or when any bound is NaN.</summary>
    public bool IsEmpty => !(Max.X >= Min.X && Max.Y >= Min.Y && Max.Z >= Min.Z);
    /// <summary>Smallest box containing both; a null operand is ignored, two nulls give null.</summary>
    public static Box3? Union(Box3? a, Box3? b)
    {
        if (a is null) return b; if (b is null) return a;
        var (p, q) = (a.Value, b.Value);
        return new Box3(new(Math.Min(p.Min.X, q.Min.X), Math.Min(p.Min.Y, q.Min.Y), Math.Min(p.Min.Z, q.Min.Z)), new(Math.Max(p.Max.X, q.Max.X), Math.Max(p.Max.Y, q.Max.Y), Math.Max(p.Max.Z, q.Max.Z)));
    }
    /// <summary>Bounds of interleaved xyz positions (optionally transformed).</summary>
    public static Box3? FromPositions(ReadOnlySpan<float> p, double[]? m = null)
    {
        var n = p.Length / 3;
        if (n == 0) return null;
        var b = Empty; double x0 = b.Min.X, y0 = b.Min.Y, z0 = b.Min.Z, x1 = b.Max.X, y1 = b.Max.Y, z1 = b.Max.Z;
        for (var i = 0; i < n; i++)
        {
            double x = p[3 * i], y = p[3 * i + 1], z = p[3 * i + 2];
            if (m is not null) { var q = Mat4.Point(m, x, y, z); x = q.X; y = q.Y; z = q.Z; }
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; if (z < z0) z0 = z; if (z > z1) z1 = z;
        }
        return new Box3(new(x0, y0, z0), new(x1, y1, z1));
    }
    /// <summary>Double positions (corner lists) keep full precision, matching the TS side where every array is double.</summary>
    public static Box3? FromPositions(ReadOnlySpan<double> p, double[]? m = null)
    {
        var n = p.Length / 3;
        if (n == 0) return null;
        var b = Empty; double x0 = b.Min.X, y0 = b.Min.Y, z0 = b.Min.Z, x1 = b.Max.X, y1 = b.Max.Y, z1 = b.Max.Z;
        for (var i = 0; i < n; i++)
        {
            double x = p[3 * i], y = p[3 * i + 1], z = p[3 * i + 2];
            if (m is not null) { var q = Mat4.Point(m, x, y, z); x = q.X; y = q.Y; z = q.Z; }
            if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; if (z < z0) z0 = z; if (z > z1) z1 = z;
        }
        return new Box3(new(x0, y0, z0), new(x1, y1, z1));
    }
    /// <summary>Bounding sphere (centre + half diagonal).</summary>
    public (Vec3 Center, double Radius) Sphere() => (Math3.Scale(Math3.Add(Min, Max), 0.5), Math3.Length(Math3.Sub(Max, Min)) / 2);
}

/// <summary>A half-line <c>Origin + t · Dir</c>, t ≥ 0; <paramref name="Dir"/> is expected to be unit length so that t is a world distance.</summary>
public readonly record struct Ray(Vec3 Origin, Vec3 Dir)
{
    /// <summary>The point at parameter <paramref name="t"/>.</summary>
    public Vec3 At(double t) => Math3.Add(Origin, Math3.Scale(Dir, t));
    /// <summary>Distance to the plane <c>n · p = d</c>, or null when parallel or behind the origin.</summary>
    public double? Plane(Vec3 n, double d)
    {
        var denom = Math3.Dot(n, Dir);
        if (Math.Abs(denom) < 1e-12) return null;
        var t = (d - Math3.Dot(n, Origin)) / denom;
        return t >= 0 ? t : null;
    }
    /// <summary>Slab test; entry distance or null.</summary>
    public double? Box(Box3 b)
    {
        double t0 = 0, t1 = double.PositiveInfinity;
        Span<double> o = [Origin.X, Origin.Y, Origin.Z]; Span<double> dd = [Dir.X, Dir.Y, Dir.Z];
        Span<double> mn = [b.Min.X, b.Min.Y, b.Min.Z]; Span<double> mx = [b.Max.X, b.Max.Y, b.Max.Z];
        for (var i = 0; i < 3; i++)
        {
            if (Math.Abs(dd[i]) < 1e-12) { if (o[i] < mn[i] || o[i] > mx[i]) return null; continue; }
            double a = (mn[i] - o[i]) / dd[i], c = (mx[i] - o[i]) / dd[i];
            if (a > c) (a, c) = (c, a);
            if (a > t0) t0 = a; if (c < t1) t1 = c;
            if (t0 > t1) return null;
        }
        return t0;
    }
    /// <summary>Nearest hit on a sphere, or null.</summary>
    public double? Sphere(Vec3 center, double radius)
    {
        var oc = Math3.Sub(Origin, center);
        double b = Math3.Dot(oc, Dir), c = Math3.Dot(oc, oc) - radius * radius;
        var disc = b * b - c;
        if (disc < 0) return null;
        var s = Math.Sqrt(disc);
        var t = -b - s;
        if (t >= 0) return t;
        var t2 = -b + s;
        return t2 >= 0 ? t2 : null;
    }
}

/// <summary>3D vector helpers. Mirrors the <c>v3*</c> functions in <c>scene3d/math3.ts</c>.</summary>
public static class Math3
{
    /// <summary>Component-wise sum.</summary>
    public static Vec3 Add(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    /// <summary>Component-wise difference <c>a − b</c>.</summary>
    public static Vec3 Sub(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    /// <summary>Multiply by a scalar.</summary>
    public static Vec3 Scale(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    /// <summary>Dot product.</summary>
    public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    /// <summary>Right-handed cross product <c>a × b</c>.</summary>
    public static Vec3 Cross(Vec3 a, Vec3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    /// <summary>Euclidean length.</summary>
    public static double Length(Vec3 a) => Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
    /// <summary>Unit-length copy; the zero vector stays zero.</summary>
    public static Vec3 Normalize(Vec3 a) { var l = Length(a); return l > 0 ? Scale(a, 1 / l) : default; }
    /// <summary>Linear interpolation, <paramref name="t"/> = 0 gives <paramref name="a"/>, 1 gives <paramref name="b"/>; not clamped.</summary>
    public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
}

/// <summary>
/// Column-major 4×4 matrices as <c>double[16]</c> (WebGL order): element (row r, column c) is <c>m[c * 4 + r]</c>.
/// Mirrors <c>scene3d/math3.ts</c>; pinned by <c>spec/fixtures/math3.json</c>. Right-handed, z up.
/// </summary>
public static class Mat4
{
    /// <summary>A fresh identity matrix on every access (safe to mutate).</summary>
    public static double[] Identity => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
    /// <summary>Pure translation.</summary>
    public static double[] Translation(double x, double y, double z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
    /// <summary>Pure scaling about the origin.</summary>
    public static double[] Scaling(double x, double y, double z) => [x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1];
    /// <summary>Rotation matrix of a unit quaternion.</summary>
    public static double[] FromQuat(Quat q)
    {
        double x = q.X, y = q.Y, z = q.Z, w = q.W;
        double xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
        return [1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0, 2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0, 2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0, 0, 0, 0, 1];
    }
    /// <summary>Rigid transform: rotate by <paramref name="q"/>, then translate by <paramref name="t"/>.</summary>
    public static double[] FromPose(Vec3 t, Quat q) { var m = FromQuat(q); m[12] = t.X; m[13] = t.Y; m[14] = t.Z; return m; }
    /// <summary><c>Mul(a, b)</c> applies <c>b</c> first, then <c>a</c>.</summary>
    public static double[] Mul(double[] a, double[] b)
    {
        var o = new double[16];
        for (var c = 0; c < 4; c++) for (var r = 0; r < 4; r++)
            o[c * 4 + r] = a[r] * b[c * 4] + a[4 + r] * b[c * 4 + 1] + a[8 + r] * b[c * 4 + 2] + a[12 + r] * b[c * 4 + 3];
        return o;
    }
    /// <summary>The transposed matrix (a new array).</summary>
    public static double[] Transpose(double[] m)
    {
        var o = new double[16];
        for (var c = 0; c < 4; c++) for (var r = 0; r < 4; r++) o[c * 4 + r] = m[r * 4 + c];
        return o;
    }
    /// <summary>General inverse, or null when the determinant is zero or not finite.</summary>
    public static double[]? Invert(double[] m)
    {
        double a00 = m[0], a01 = m[1], a02 = m[2], a03 = m[3], a10 = m[4], a11 = m[5], a12 = m[6], a13 = m[7];
        double a20 = m[8], a21 = m[9], a22 = m[10], a23 = m[11], a30 = m[12], a31 = m[13], a32 = m[14], a33 = m[15];
        double b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
        double b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12, b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
        double b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
        var det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
        if (det == 0 || !double.IsFinite(det)) return null;
        var d = 1 / det;
        return [
            (a11 * b11 - a12 * b10 + a13 * b09) * d, (a02 * b10 - a01 * b11 - a03 * b09) * d, (a31 * b05 - a32 * b04 + a33 * b03) * d, (a22 * b04 - a21 * b05 - a23 * b03) * d,
            (a12 * b08 - a10 * b11 - a13 * b07) * d, (a00 * b11 - a02 * b08 + a03 * b07) * d, (a32 * b02 - a30 * b05 - a33 * b01) * d, (a20 * b05 - a22 * b02 + a23 * b01) * d,
            (a10 * b10 - a11 * b08 + a13 * b06) * d, (a01 * b08 - a00 * b10 - a03 * b06) * d, (a30 * b04 - a31 * b02 + a33 * b00) * d, (a21 * b02 - a20 * b04 - a23 * b00) * d,
            (a11 * b07 - a10 * b09 - a12 * b06) * d, (a00 * b09 - a01 * b07 + a02 * b06) * d, (a31 * b01 - a30 * b03 - a32 * b00) * d, (a20 * b03 - a21 * b01 + a22 * b00) * d,
        ];
    }
    /// <summary>Transform a point (w = 1), dividing by w.</summary>
    public static Vec3 Point(double[] m, double x, double y, double z)
    {
        var w = m[3] * x + m[7] * y + m[11] * z + m[15];
        var iw = w == 0 ? 1 : 1 / w;
        return new((m[0] * x + m[4] * y + m[8] * z + m[12]) * iw, (m[1] * x + m[5] * y + m[9] * z + m[13]) * iw, (m[2] * x + m[6] * y + m[10] * z + m[14]) * iw);
    }
    /// <summary>Transform a direction (w = 0).</summary>
    public static Vec3 Dir(double[] m, double x, double y, double z) => new(m[0] * x + m[4] * y + m[8] * z, m[1] * x + m[5] * y + m[9] * z, m[2] * x + m[6] * y + m[10] * z);
    /// <summary>Right-handed view matrix looking from <paramref name="eye"/> to <paramref name="target"/>.</summary>
    public static double[] LookAt(Vec3 eye, Vec3 target, Vec3 up)
    {
        var f = Math3.Normalize(Math3.Sub(target, eye));
        var s = Math3.Cross(f, up);
        if (Math3.Length(s) < 1e-9) s = Math3.Cross(f, Math.Abs(f.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0));
        s = Math3.Normalize(s);
        var u = Math3.Cross(s, f);
        return [s.X, u.X, -f.X, 0, s.Y, u.Y, -f.Y, 0, s.Z, u.Z, -f.Z, 0, -Math3.Dot(s, eye), -Math3.Dot(u, eye), Math3.Dot(f, eye), 1];
    }
    /// <summary>OpenGL clip space (z in [−1, 1]); <paramref name="fovY"/> in radians.</summary>
    public static double[] Perspective(double fovY, double aspect, double near, double far)
    {
        double f = 1 / Math.Tan(fovY / 2), nf = 1 / (near - far);
        return [f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * nf, -1, 0, 0, 2 * far * near * nf, 0];
    }
    /// <summary>Orthographic projection to OpenGL clip space (z in [−1, 1]) from the given eye-space extents.</summary>
    public static double[] Ortho(double left, double right, double bottom, double top, double near, double far)
    {
        double lr = 1 / (left - right), bt = 1 / (bottom - top), nf = 1 / (near - far);
        return [-2 * lr, 0, 0, 0, 0, -2 * bt, 0, 0, 0, 0, 2 * nf, 0, (left + right) * lr, (top + bottom) * bt, (far + near) * nf, 1];
    }
}
