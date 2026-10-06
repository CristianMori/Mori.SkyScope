// Mori.SkyScope — Unit meshes shared by markers: cube (±0.5), sphere (radius 0.5), cylinder (radius 0.5 along z, height 1), cone and an arrow along +x (len…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>
/// Unit meshes shared by markers: cube (±0.5), sphere (radius 0.5), cylinder (radius 0.5 along z, height 1), cone and an
/// arrow along +x (length 1). Flat normals, built the same way as <c>scene3d/primitives.ts</c> so recorded hashes agree.
/// </summary>
public static class Primitives
{
    private static readonly Dictionary<string, Mesh3D> Cache = [];
    private static readonly object Lock = new();

    private static Mesh3D Build(string key, Func<List<double>> tris)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(key, out var m)) return m;
            var t = tris();
            var pos = t.Select(v => (float)v).ToArray(); var n = pos.Length / 9; var nor = new float[pos.Length];
            for (var i = 0; i < n; i++)
            {
                var o = i * 9;
                double ax = pos[o + 3] - pos[o], ay = pos[o + 4] - pos[o + 1], az = pos[o + 5] - pos[o + 2];
                double bx = pos[o + 6] - pos[o], by = pos[o + 7] - pos[o + 1], bz = pos[o + 8] - pos[o + 2];
                double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                var l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l == 0) l = 1; nx /= l; ny /= l; nz /= l;
                for (var k = 0; k < 3; k++) { nor[o + 3 * k] = (float)nx; nor[o + 3 * k + 1] = (float)ny; nor[o + 3 * k + 2] = (float)nz; }
            }
            m = new Mesh3D($"unit:{key}", pos) { Normals = nor };
            Cache[key] = m;
            return m;
        }
    }
    private static void Tri(List<double> o, double[] a, double[] b, double[] c) { o.AddRange([a[0], a[1], a[2], b[0], b[1], b[2], c[0], c[1], c[2]]); }
    private static void Quad(List<double> o, double[] a, double[] b, double[] c, double[] d) { Tri(o, a, b, c); Tri(o, a, c, d); }

    /// <summary>Cube spanning ±0.5 on every axis, 12 triangles. Cached and shared: do not mutate the returned mesh.</summary>
    public static Mesh3D UnitCube() => Build("cube", () =>
    {
        var o = new List<double>(); const double h = 0.5;
        double[] P(double x, double y, double z) => [x * h, y * h, z * h];
        Quad(o, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));
        Quad(o, P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1));
        Quad(o, P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));
        Quad(o, P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1));
        Quad(o, P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));
        Quad(o, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1));
        return o;
    });

    /// <summary>UV sphere of radius 0.5 centred on the origin; cached per (<paramref name="rings"/>, <paramref name="segments"/>) and shared: do not mutate.</summary>
    public static Mesh3D UnitSphere(int rings = 8, int segments = 16) => Build($"sphere:{rings}:{segments}", () =>
    {
        var o = new List<double>(); const double r = 0.5;
        double[] At(int i, int j) { double th = Math.PI * i / rings, ph = 2 * Math.PI * j / segments; return [r * Math.Sin(th) * Math.Cos(ph), r * Math.Sin(th) * Math.Sin(ph), r * Math.Cos(th)]; }
        for (var i = 0; i < rings; i++) for (var j = 0; j < segments; j++)
        {
            double[] a = At(i, j), b = At(i + 1, j), c = At(i + 1, j + 1), d = At(i, j + 1);
            if (i > 0) Tri(o, a, b, d);
            if (i < rings - 1) Tri(o, b, c, d);
        }
        return o;
    });

    /// <summary>Closed cylinder of radius 0.5 along z from −0.5 to 0.5; cached per <paramref name="segments"/> and shared: do not mutate.</summary>
    public static Mesh3D UnitCylinder(int segments = 16) => Build($"cylinder:{segments}", () =>
    {
        var o = new List<double>(); const double r = 0.5;
        double[] Ring(int j, double z) { var ph = 2 * Math.PI * j / segments; return [r * Math.Cos(ph), r * Math.Sin(ph), z]; }
        for (var j = 0; j < segments; j++)
        {
            Quad(o, Ring(j, -0.5), Ring(j + 1, -0.5), Ring(j + 1, 0.5), Ring(j, 0.5));
            Tri(o, [0, 0, 0.5], Ring(j, 0.5), Ring(j + 1, 0.5));
            Tri(o, [0, 0, -0.5], Ring(j + 1, -0.5), Ring(j, -0.5));
        }
        return o;
    });

    /// <summary>Cone with a base of radius 0.5 at z = 0 and its apex at z = 1; cached per <paramref name="segments"/> and shared: do not mutate.</summary>
    public static Mesh3D UnitCone(int segments = 16) => Build($"cone:{segments}", () =>
    {
        var o = new List<double>(); const double r = 0.5;
        double[] Ring(int j) { var ph = 2 * Math.PI * j / segments; return [r * Math.Cos(ph), r * Math.Sin(ph), 0]; }
        for (var j = 0; j < segments; j++) { Tri(o, Ring(j), Ring(j + 1), [0, 0, 1]); Tri(o, [0, 0, 0], Ring(j + 1), Ring(j)); }
        return o;
    });

    /// <summary>Arrow along +x: shaft (radius 0.1, x ∈ [0, 0.7]) and head (base radius 0.25, x ∈ [0.7, 1]).</summary>
    public static Mesh3D UnitArrow(int segments = 12) => Build($"arrow:{segments}", () =>
    {
        var o = new List<double>();
        double[] Ring(int j, double x, double r) { var ph = 2 * Math.PI * j / segments; return [x, r * Math.Cos(ph), r * Math.Sin(ph)]; }
        for (var j = 0; j < segments; j++)
        {
            Quad(o, Ring(j, 0, 0.1), Ring(j, 0.7, 0.1), Ring(j + 1, 0.7, 0.1), Ring(j + 1, 0, 0.1));
            Tri(o, [0, 0, 0], Ring(j, 0, 0.1), Ring(j + 1, 0, 0.1));
            Quad(o, Ring(j, 0.7, 0.1), Ring(j, 0.7, 0.25), Ring(j + 1, 0.7, 0.25), Ring(j + 1, 0.7, 0.1));
            Tri(o, Ring(j, 0.7, 0.25), [1, 0, 0], Ring(j + 1, 0.7, 0.25));
        }
        return o;
    });
}
