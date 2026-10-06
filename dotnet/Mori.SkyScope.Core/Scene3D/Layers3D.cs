// Mori.SkyScope — Base 3D layers: the layer contract, grid, axes, point clouds and frame markers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>A layer that lives in 3D: draws through <see cref="LayerContext.Painter3D"/>, reports 3D bounds for fit-all.</summary>
/// <remarks><c>Bounds3()</c> returns the world box in the fixed frame, or null when the layer is empty or its frame is unknown.</remarks>
public interface ILayer3D : ILayer { Box3? Bounds3(); }

/// <summary>Scene helpers for 3D layers.</summary>
public static class Scene3DExtensions
{
    /// <summary>Union of visible 3D layers' bounds (for fit-all).</summary>
    public static Box3? Bounds3(this Scene.Scene scene)
    {
        Box3? b = null;
        foreach (var l in scene.Layers) if (l.Visible && l is ILayer3D l3) b = Box3.Union(b, l3.Bounds3());
        return b;
    }
}

/// <summary>Base of 3D layers: never cached, drawn only under a 3D projection, no bounds unless overridden.</summary>
public abstract class BaseLayer3D : BaseLayer, ILayer3D
{
    /// <summary>Creates the layer with caching off.</summary>
    protected BaseLayer3D(string id) : base(id) { Cacheable = false; }
    /// <summary>3D only.</summary>
    public override IReadOnlyList<CameraKind> CameraKinds => [CameraKind.ThreeD];
    /// <summary>No bounds unless overridden.</summary>
    public virtual Box3? Bounds3() => null;
}

/// <summary>A metric ground grid on the plane z = <see cref="Z"/>. Excluded from fit-all bounds.</summary>
public sealed class Grid3DLayer(string id) : BaseLayer3D(id)
{
    /// <summary><c>"grid3d"</c>.</summary>
    public override string Kind => "grid3d";
    /// <summary>Half extent in world units.</summary>
    public double Size { get; set; } = 10;
    /// <summary>Distance between lines in world units.</summary>
    public double Spacing { get; set; } = 1;
    /// <summary>Every n-th line, counted from the origin, uses <see cref="MajorColor"/>; 0 disables major lines.</summary>
    public int MajorEvery { get; set; } = 5;
    /// <summary>Minor line colour (hex).</summary>
    public string Color { get; set; } = "#3f4a5a";
    /// <summary>Major line colour (hex).</summary>
    public string MajorColor { get; set; } = "#6b7a90";
    /// <summary>Height of the grid plane in world units.</summary>
    public double Z { get; set; }
    private Mesh3D? _mesh; private string _built = "";

    private Mesh3D Build()
    {
        var sig = $"{Size}|{Spacing}|{MajorEvery}|{Color}|{MajorColor}|{Z}";
        if (_mesh is not null && _built == sig) return _mesh;
        var n = Math.Max(0, (int)Math.Round(Size / Spacing)); var s = n * Spacing;
        var pos = new List<float>(); var col = new List<byte>();
        var minor = Colormaps.ParseHex(Color); var major = Colormaps.ParseHex(MajorColor);
        for (var i = -n; i <= n; i++)
        {
            var v = i * Spacing; var c = MajorEvery > 0 && i % MajorEvery == 0 ? major : minor;
            pos.AddRange([(float)v, (float)-s, (float)Z, (float)v, (float)s, (float)Z, (float)-s, (float)v, (float)Z, (float)s, (float)v, (float)Z]);
            for (var k = 0; k < 4; k++) col.AddRange([(byte)c.R, (byte)c.G, (byte)c.B, 255]);
        }
        _mesh = new Mesh3D($"grid3d:{Id}", pos.ToArray()) { Colors = col.ToArray() };
        _built = sig;
        return _mesh;
    }
    /// <summary>Draws the cached line mesh, rebuilt only when a property changed.</summary>
    public override void Draw(LayerContext ctx) => ctx.Painter3D?.Lines(Build(), new Material3D { Opacity = ctx.Opacity, LineWidth = 1 });
}

/// <summary>An RGB triad (x red, y green, z blue) at every frame of a <see cref="FrameTree"/>, looked up at <c>ctx.Now</c>.</summary>
public sealed class AxesLayer(string id, FrameTree frames, string fixedFrame) : BaseLayer3D(id)
{
    /// <summary><c>"axes"</c>.</summary>
    public override string Kind => "axes";
    /// <summary>The tree the triads are looked up in.</summary>
    public FrameTree Frames { get; } = frames;
    /// <summary>Frame the triads are expressed in for drawing.</summary>
    public string FixedFrame { get; set; } = fixedFrame;
    /// <summary>Frames to draw; every known frame when null.</summary>
    public IReadOnlyList<string>? Ids { get; set; }
    /// <summary>Axis length in world units.</summary>
    public double Length { get; set; } = 0.5;
    /// <summary>Line width in pixels; drawn without depth test so triads stay visible.</summary>
    public double LineWidth { get; set; } = 2;
    /// <summary>Frames drawn in the last build.</summary>
    public List<string> Drawn { get; } = [];
    private readonly Mesh3D _mesh = new($"axes:{id}", []) { Colors = [] };
    private (int Version, double Now, string Ids, string Fixed, double Length) _builtFor = (-1, double.NaN, "", "", 0);

    private Mesh3D Build(double now)
    {
        var idsKey = Ids is null ? "*" : string.Join(",", Ids);
        var b = _builtFor;
        if (b.Version == Frames.Version && b.Now.Equals(now) && b.Ids == idsKey && b.Fixed == FixedFrame && b.Length == Length) return _mesh;
        var ids = Ids ?? Frames.FrameIds();
        var pos = new List<float>(); var col = new List<byte>(); var L = Length;
        Drawn.Clear();
        foreach (var f in ids)
        {
            var m = Frames.Lookup(FixedFrame, f, now);
            if (m is null) continue;
            Drawn.Add(f);
            Vec3 o = Mat4.Point(m, 0, 0, 0), x = Mat4.Point(m, L, 0, 0), y = Mat4.Point(m, 0, L, 0), z = Mat4.Point(m, 0, 0, L);
            foreach (var p in new[] { o, x, o, y, o, z }) pos.AddRange([(float)p.X, (float)p.Y, (float)p.Z]);
            col.AddRange([220, 38, 38, 255, 220, 38, 38, 255, 22, 163, 74, 255, 22, 163, 74, 255, 37, 99, 235, 255, 37, 99, 235, 255]);
        }
        _mesh.Positions = pos.ToArray(); _mesh.Colors = col.ToArray(); _mesh.Version++;
        _builtFor = (Frames.Version, now, idsKey, FixedFrame, L);
        return _mesh;
    }
    /// <summary>Rebuilds the triads when the tree, time or settings changed, then draws them on top of everything.</summary>
    public override void Draw(LayerContext ctx) { var m = Build(ctx.Now); if (m.Positions.Length > 0) ctx.Painter3D?.Lines(m, new Material3D { Opacity = ctx.Opacity, LineWidth = LineWidth, DepthTest = false }); }
    /// <summary>Box of the triads drawn last; null before the first draw.</summary>
    public override Box3? Bounds3() => _mesh.Positions.Length > 0 ? Box3.FromPositions(_mesh.Positions) : null;
}

/// <summary>A 3D point cloud: xyz positions, coloured flat, by intensity through a colormap, or by RGBA bytes.</summary>
public sealed class PointCloud3DLayer(string id) : BaseLayer3D(id)
{
    /// <summary><c>"pointCloud3d"</c>.</summary>
    public override string Kind => "pointCloud3d";
    /// <summary>Frame the points are expressed in; drawn through <see cref="Frames"/> into <see cref="FixedFrame"/> when both are set.</summary>
    public string? Frame { get; set; }
    /// <summary>Tree used to resolve <see cref="Frame"/>; null draws the points as given.</summary>
    public FrameTree? Frames { get; set; }
    /// <summary>Frame the scene is drawn in; null draws the points as given.</summary>
    public string? FixedFrame { get; set; }
    /// <summary>Named colormap for intensities; ignored when <see cref="ColormapStops"/> is set.</summary>
    public string Colormap { get; set; } = "turbo";
    /// <summary>Explicit hex colour stops overriding <see cref="Colormap"/>.</summary>
    public string[]? ColormapStops { get; set; }
    /// <summary>Intensity values mapped to the colormap ends; the data min/max when null. Applied when points are set, not retroactively.</summary>
    public (double Lo, double Hi)? IntensityRange { get; set; }
    /// <summary>Flat colour (hex) when the mesh has no per-vertex colours.</summary>
    public string Color { get; set; } = "#ffffff";
    /// <summary>Point diameter in pixels.</summary>
    public double PointSize { get; set; } = 2;
    /// <summary>The renderer buffer: xyz positions and optional RGBA colours, in the cloud's own frame.</summary>
    public Mesh3D Mesh { get; } = new($"pointCloud3d:{id}", []);
    /// <summary>Time of the latest cloud (for frame lookup); null uses the latest transform.</summary>
    public double? Stamp { get; set; }
    private float[]? _intensities;
    /// <summary>Number of points.</summary>
    public int PointCount => Mesh.Positions.Length / 3;

    /// <summary>Replace the cloud. <paramref name="colors"/> (RGBA per point) wins over <paramref name="intensities"/>.</summary>
    public void SetPoints(float[] positions, float[]? intensities = null, byte[]? colors = null, double? stamp = null)
    {
        Mesh.Positions = positions; _intensities = intensities;
        Mesh.Colors = colors ?? (intensities is null ? null : Colorize(intensities));
        Mesh.Count = null; Mesh.Version++; Stamp = stamp; MarkDirty();
    }
    private byte[] Colorize(float[] v)
    {
        var lut = Colormaps.Lut(ColormapStops ?? Colormaps.Stops(Colormap), 256);
        double lo, hi;
        if (IntensityRange is { } r) (lo, hi) = r;
        else { lo = double.PositiveInfinity; hi = double.NegativeInfinity; foreach (var x in v) { if (x < lo) lo = x; if (x > hi) hi = x; } if (!(double.IsFinite(lo) && hi > lo)) { lo = 0; hi = 1; } }
        var span = Math.Max(1e-12, hi - lo); var o = new byte[v.Length * 4];
        for (var i = 0; i < v.Length; i++)
        {
            var k = (int)Math.Floor((v[i] - lo) / span * 255 + 0.5);
            if (k < 0) k = 0; else if (k > 255) k = 255;
            o[4 * i] = lut[k * 3]; o[4 * i + 1] = lut[k * 3 + 1]; o[4 * i + 2] = lut[k * 3 + 2]; o[4 * i + 3] = 255;
        }
        return o;
    }
    /// <summary>Model matrix at <paramref name="now"/>: (found, matrix). Not found means the frame is unknown; a null matrix with found means identity.</summary>
    public (bool Found, double[]? Model) Model(double now)
    {
        if (Frame is null || Frames is null || FixedFrame is null) return (true, null);
        var m = Frames.Lookup(FixedFrame, Frame, Stamp ?? now);
        return (m is not null, m);
    }
    /// <summary>Draws the points through the frame transform; nothing when empty or the frame is unknown.</summary>
    public override void Draw(LayerContext ctx)
    {
        if (PointCount == 0) return;
        var (found, model) = Model(ctx.Now);
        if (!found) return;
        ctx.Painter3D?.Points(Mesh, new Material3D { Color = Mesh.Colors is null ? Color : "#ffffff", Opacity = ctx.Opacity, PointSize = PointSize, Model = model });
    }
    /// <summary>Box of the points in the fixed frame at the cloud's stamp; null when the frame is unknown.</summary>
    public override Box3? Bounds3() { var (found, model) = Model(Stamp ?? 0); return found ? Box3.FromPositions(Mesh.Positions, model) : null; }
    /// <summary>Nearest visible point within <paramref name="tolerance"/> + half the point size, under a <see cref="Camera3D"/> only. <see cref="HitResult.Index"/> is the point index, <see cref="HitResult.Data"/> its intensity (double) when present.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        if (ctx.Projection is not Camera3D cam) return null;
        var p = Mesh.Positions; var (found, model) = Model(ctx.Now);
        if (!found) return null;
        var best = double.PositiveInfinity; var bestIndex = -1; Vec3 bw = default;
        for (var i = 0; i < PointCount; i++)
        {
            double x = p[3 * i], y = p[3 * i + 1], z = p[3 * i + 2];
            if (model is not null) { var w = Mat4.Point(model, x, y, z); x = w.X; y = w.Y; z = w.Z; }
            var q = cam.Project3(x, y, z);
            if (!q.Visible) continue;
            var d = Math.Sqrt((q.X - sx) * (q.X - sx) + (q.Y - sy) * (q.Y - sy));
            if (d < best) { best = d; bestIndex = i; bw = new(x, y, z); }
        }
        if (bestIndex < 0 || best > tolerance + PointSize / 2) return null;
        return new HitResult(Id, bw, new Vec2(sx, sy), best) { Index = bestIndex, Data = _intensities is not null && bestIndex < _intensities.Length ? (double)_intensities[bestIndex] : null };
    }
}

/// <summary>One transform of a <c>frames</c> layer push.</summary>
/// <param name="Child">Child frame id.</param>
/// <param name="Parent">Parent frame id.</param>
/// <param name="T">Translation [x, y, z] of the child in the parent, world units.</param>
/// <param name="Q">Rotation quaternion [x, y, z, w] of the child in the parent.</param>
/// <param name="Time">Sample time in seconds; null declares a static transform.</param>
public sealed record FrameTransformMessage(string Child, string Parent, double[] T, double[] Q, double? Time = null);

/// <summary>An invisible layer whose pushes feed the scene's <see cref="FrameTree"/>.</summary>
public sealed class FramesLayer(string id, FrameTree frames) : BaseLayer3D(id)
{
    /// <summary><c>"frames"</c>.</summary>
    public override string Kind => "frames";
    /// <summary>The tree being fed.</summary>
    public FrameTree Frames { get; } = frames;
    /// <summary>Record every transform in the tree, in order.</summary>
    public void Apply(IEnumerable<FrameTransformMessage> msgs)
    {
        foreach (var m in msgs) Frames.Set(m.Child, m.Parent, new Transform3(new Vec3(m.T[0], m.T[1], m.T[2]), new Quat(m.Q[0], m.Q[1], m.Q[2], m.Q[3])), m.Time);
    }
    /// <summary>Draws nothing.</summary>
    public override void Draw(LayerContext ctx) { }
}
