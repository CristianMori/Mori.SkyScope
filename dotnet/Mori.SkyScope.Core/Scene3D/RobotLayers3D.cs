// Mori.SkyScope — Robotics 3D layers: paths, poses, markers (including meshes), laser scans, occupancy grid planes, and the mesh registry.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>Loaded mesh resources by URI, shared by marker layers and robot models of one scene (via <c>LayerJson.LayerEnv.Meshes</c>).</summary>
public sealed class MeshRegistry
{
    private readonly Dictionary<string, Mesh3D> _meshes = [];
    /// <summary>Incremented on every registration and clear.</summary>
    public int Version { get; private set; }
    /// <summary>Register (or replace) the mesh for a URI, converting it to a renderer mesh with normals.</summary>
    public Mesh3D Set(string uri, ParsedMesh parsed) { var m = MeshFormats.ToMesh($"mesh:{uri}", parsed); _meshes[uri] = m; Version++; return m; }
    /// <summary>The mesh for a URI, or null when not loaded yet.</summary>
    public Mesh3D? Get(string uri) => _meshes.GetValueOrDefault(uri);
    /// <summary>True when a mesh is registered for the URI.</summary>
    public bool Has(string uri) => _meshes.ContainsKey(uri);
    /// <summary>Registered URIs, sorted ordinally.</summary>
    public List<string> Uris() => _meshes.Keys.Order(StringComparer.Ordinal).ToList();
    /// <summary>Forget every mesh.</summary>
    public void Clear() { if (_meshes.Count > 0) { _meshes.Clear(); Version++; } }
}

/// <summary>An invisible layer whose pushes register mesh resources in the sink's shared <see cref="MeshRegistry"/>.</summary>
public sealed class MeshesLayer(string id, MeshRegistry meshes) : BaseLayer3D(id)
{
    /// <summary><c>"meshes"</c>.</summary>
    public override string Kind => "meshes";
    /// <summary>The registry fed by this layer.</summary>
    public MeshRegistry Meshes { get; } = meshes;
    /// <summary>Register a parsed mesh resource under its URI.</summary>
    public void Register(string uri, ParsedMesh parsed) => Meshes.Set(uri, parsed);
    /// <summary>Draws nothing.</summary>
    public override void Draw(LayerContext ctx) { }
}

internal static class Frame3D
{
    /// <summary>(found, model): not found means the frame is unknown; found with a null matrix means identity.</summary>
    public static (bool Found, double[]? Model) ModelFor(string? frame, FrameTree? frames, string? fixedFrame, double? time)
    {
        if (frame is null || frames is null || fixedFrame is null) return (true, null);
        var m = frames.Lookup(fixedFrame, frame, time);
        return (m is not null, m);
    }
    public static (int Index, double Distance, Vec3 World)? NearestVertex(Camera3D cam, ReadOnlySpan<float> p, int count, double[]? model, double sx, double sy)
    {
        var best = double.PositiveInfinity; var bi = -1; Vec3 bw = default;
        for (var i = 0; i < count; i++)
        {
            double x = p[3 * i], y = p[3 * i + 1], z = p[3 * i + 2];
            if (model is not null) { var w = Mat4.Point(model, x, y, z); x = w.X; y = w.Y; z = w.Z; }
            var q = cam.Project3(x, y, z);
            if (!q.Visible) continue;
            var d = Math.Sqrt((q.X - sx) * (q.X - sx) + (q.Y - sy) * (q.Y - sy));
            if (d < best) { best = d; bi = i; bw = new(x, y, z); }
        }
        return bi < 0 ? null : (bi, best, bw);
    }
}

/// <summary>A polyline in 3D (nav_msgs/Path, a trajectory, a trail); <see cref="Append"/> keeps the newest <see cref="MaxPoints"/>.</summary>
public sealed class Path3DLayer(string id) : BaseLayer3D(id)
{
    /// <summary><c>"path3d"</c>.</summary>
    public override string Kind => "path3d";
    /// <summary>Frame the points are expressed in; null draws them as given.</summary>
    public string? Frame { get; set; }
    /// <summary>Tree used to resolve <see cref="Frame"/>.</summary>
    public FrameTree? Frames { get; set; }
    /// <summary>Frame the scene is drawn in.</summary>
    public string? FixedFrame { get; set; }
    /// <summary>Line colour (hex).</summary>
    public string Color { get; set; } = "#22d3ee";
    /// <summary>Line width in pixels.</summary>
    public double LineWidth { get; set; } = 2;
    /// <summary>Points kept; the oldest are dropped by <see cref="SetPoints"/> and <see cref="Append"/>.</summary>
    public int MaxPoints { get; set; } = 10000;
    /// <summary>The renderer buffer: xyz positions in the path's own frame, drawn as a strip.</summary>
    public Mesh3D Mesh { get; } = new($"path3d:{id}", []);
    /// <summary>Time of the latest update, used for the frame lookup; null uses the latest transform.</summary>
    public double? Stamp { get; set; }
    /// <summary>Number of points.</summary>
    public int PointCount => Mesh.Positions.Length / 3;

    /// <summary>Replace the path with interleaved xyz (the array is kept, not copied; only the newest <see cref="MaxPoints"/> survive).</summary>
    public void SetPoints(float[] positions, double? stamp = null)
    {
        var max = MaxPoints * 3;
        Mesh.Positions = positions.Length > max ? positions[^max..] : positions;
        Mesh.Version++; Stamp = stamp; MarkDirty();
    }
    /// <summary>Add one point, dropping the oldest beyond <see cref="MaxPoints"/>; reallocates the buffer each call.</summary>
    public void Append(double x, double y, double z)
    {
        var p = Mesh.Positions; var n = p.Length;
        if (n >= MaxPoints * 3) { var q = new float[n]; Array.Copy(p, 3, q, 0, n - 3); q[n - 3] = (float)x; q[n - 2] = (float)y; q[n - 1] = (float)z; Mesh.Positions = q; }
        else { var q = new float[n + 3]; Array.Copy(p, q, n); q[n] = (float)x; q[n + 1] = (float)y; q[n + 2] = (float)z; Mesh.Positions = q; }
        Mesh.Version++; MarkDirty();
    }
    /// <summary>Remove every point.</summary>
    public void Clear() { Mesh.Positions = []; Mesh.Version++; MarkDirty(); }
    /// <summary>Model matrix at <paramref name="now"/> (or at <see cref="Stamp"/> when set): not found means the frame is unknown; found with a null matrix means identity.</summary>
    public (bool Found, double[]? Model) Model(double now) => Frame3D.ModelFor(Frame, Frames, FixedFrame, Stamp ?? now);
    /// <summary>Draws the strip; nothing with fewer than two points or an unknown frame.</summary>
    public override void Draw(LayerContext ctx)
    {
        if (PointCount < 2) return;
        var (found, model) = Model(ctx.Now);
        if (!found) return;
        ctx.Painter3D?.Lines(Mesh, new Material3D { Color = Color, Opacity = ctx.Opacity, LineWidth = LineWidth, Model = model }, strip: true);
    }
    /// <summary>Box of the points in the fixed frame; null when the frame is unknown.</summary>
    public override Box3? Bounds3() { var (found, model) = Model(Stamp ?? 0); return found ? Box3.FromPositions(Mesh.Positions, model) : null; }
    /// <summary>Nearest visible vertex within <paramref name="tolerance"/> + line width pixels, under a <see cref="Camera3D"/> only; <see cref="HitResult.Index"/> is the point index.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        if (ctx.Projection is not Camera3D cam) return null;
        var (found, model) = Model(ctx.Now);
        if (!found) return null;
        var h = Frame3D.NearestVertex(cam, Mesh.Positions, PointCount, model, sx, sy);
        if (h is null || h.Value.Distance > tolerance + LineWidth) return null;
        return new HitResult(Id, h.Value.World, new Vec2(sx, sy), h.Value.Distance) { Index = h.Value.Index };
    }
}

/// <summary>A rigid pose: rotate by <paramref name="Q"/>, then translate by <paramref name="T"/> (world units). <c>Identity</c> is the origin with no rotation.</summary>
public readonly record struct Pose3D(Vec3 T, Quat Q) { public static readonly Pose3D Identity = new(default, Quat.Identity); }

/// <summary>A pose (geometry_msgs/PoseStamped, an odometry estimate): an arrow along its +x, an optional axes triad and label.</summary>
public sealed class Pose3DLayer(string id) : BaseLayer3D(id)
{
    /// <summary><c>"pose3d"</c>.</summary>
    public override string Kind => "pose3d";
    /// <summary>Frame the pose is expressed in; null draws it as given.</summary>
    public string? Frame { get; set; }
    /// <summary>Tree used to resolve <see cref="Frame"/>.</summary>
    public FrameTree? Frames { get; set; }
    /// <summary>Frame the scene is drawn in.</summary>
    public string? FixedFrame { get; set; }
    /// <summary>Current pose in <see cref="Frame"/>.</summary>
    public Pose3D Pose { get; private set; } = Pose3D.Identity;
    /// <summary>Arrow and label colour (hex).</summary>
    public string Color { get; set; } = "#f59e0b";
    /// <summary>Arrow length in world units; the triad is drawn at 60 % of it.</summary>
    public double AxisLength { get; set; } = 0.5;
    /// <summary>Text drawn next to the pose on the 2D painter; null draws none.</summary>
    public string? Label { get; set; }
    /// <summary>Draw the RGB triad (always on top) in addition to the arrow.</summary>
    public bool ShowAxes { get; set; } = true;
    /// <summary>Time of the latest pose, used for the frame lookup; null uses the latest transform.</summary>
    public double? Stamp { get; set; }
    private readonly Mesh3D _axes = new($"pose3d:{id}:axes", [0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1]) { Colors = [220, 38, 38, 255, 220, 38, 38, 255, 22, 163, 74, 255, 22, 163, 74, 255, 37, 99, 235, 255, 37, 99, 235, 255] };

    /// <summary>Replace the pose and its stamp.</summary>
    public void SetPose(Pose3D pose, double? stamp = null) { Pose = pose; Stamp = stamp; MarkDirty(); }
    /// <summary>Pose → fixed frame, or null when the frame is unknown.</summary>
    public double[]? Model(double now)
    {
        var (found, frame) = Frame3D.ModelFor(Frame, Frames, FixedFrame, Stamp ?? now);
        if (!found) return null;
        var local = Mat4.FromPose(Pose.T, Pose.Q);
        return frame is null ? local : Mat4.Mul(frame, local);
    }
    /// <summary>Draws the lit arrow, the triad when enabled, and the label on the 2D painter; nothing when the frame is unknown.</summary>
    public override void Draw(LayerContext ctx)
    {
        var m = Model(ctx.Now);
        if (m is null || ctx.Painter3D is null) return;
        var L = AxisLength;
        ctx.Painter3D.Triangles(Primitives.UnitArrow(), new Material3D { Color = Color, Opacity = ctx.Opacity, Lit = true, Model = Mat4.Mul(m, Mat4.Scaling(L, L, L)) });
        if (ShowAxes) ctx.Painter3D.Lines(_axes, new Material3D { Opacity = ctx.Opacity, LineWidth = 2, DepthTest = false, Model = Mat4.Mul(m, Mat4.Scaling(L * 0.6, L * 0.6, L * 0.6)) });
        if (Label is not null && ctx.Projection is Camera3D cam)
        {
            var o = Mat4.Point(m, 0, 0, 0); var s = cam.Project3(o.X, o.Y, o.Z);
            if (s.Visible) ctx.Painter.Text(Label, s.X + 8, s.Y - 8, new TextStyle(Color) { Size = 11, Align = TextAlign.Left, Baseline = TextBaseline.Bottom, Opacity = ctx.Opacity });
        }
    }
    /// <summary>Box of the origin and the three axis tips in the fixed frame; null when the frame is unknown.</summary>
    public override Box3? Bounds3() { var m = Model(Stamp ?? 0); if (m is null) return null; var L = AxisLength; return Box3.FromPositions([0, 0, 0, L, 0, 0, 0, L, 0, 0, 0, L], m); }
    /// <summary>Hit when the projected origin lies within <paramref name="tolerance"/> + 6 pixels, under a <see cref="Camera3D"/> only; <see cref="HitResult.Data"/> is the label.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        if (ctx.Projection is not Camera3D cam) return null;
        var m = Model(ctx.Now);
        if (m is null) return null;
        var o = Mat4.Point(m, 0, 0, 0); var s = cam.Project3(o.X, o.Y, o.Z);
        if (!s.Visible) return null;
        var d = Math.Sqrt((s.X - sx) * (s.X - sx) + (s.Y - sy) * (s.Y - sy));
        return d <= tolerance + 6 ? new HitResult(Id, o, new Vec2(sx, sy), d) { Data = Label } : null;
    }
}

/// <summary>Marker shapes after <c>visualization_msgs/Marker</c>: unit solids scaled by <see cref="Marker.Scale"/> (<c>Cube</c>, <c>Sphere</c>, <c>Cylinder</c>, <c>Arrow</c>), vertex lists (<c>LineList</c> pairs, <c>LineStrip</c>, <c>Points</c>), a screen-space <c>Text</c> label, or a registered <c>Mesh</c> resource.</summary>
public enum MarkerType { Cube, Sphere, Cylinder, Arrow, LineList, LineStrip, Points, Text, Mesh }

/// <summary>One marker, after visualization_msgs/Marker. Mirrors the TS <c>Marker</c> shape.</summary>
public sealed record Marker(string Id, MarkerType Type)
{
    /// <summary>Frame of the pose; the layer's frame when null.</summary>
    public string? Frame { get; init; }
    /// <summary>Translation in <see cref="Frame"/>, world units.</summary>
    public Vec3 Position { get; init; }
    /// <summary>Rotation in <see cref="Frame"/>.</summary>
    public Quat Orientation { get; init; } = Quat.Identity;
    /// <summary>cube/sphere/cylinder: size; arrow: [length, width, height]; lines: [width]; points: [size]; text: [height px].</summary>
    public Vec3 Scale { get; init; } = new(1, 1, 1);
    /// <summary>Hex colour; multiplied with per-vertex <see cref="Colors"/> when present.</summary>
    public string Color { get; init; } = "#ffffff";
    /// <summary>0–1, multiplied with the layer opacity.</summary>
    public double Opacity { get; init; } = 1;
    /// <summary>Interleaved xyz vertices for line and point markers, relative to the pose.</summary>
    public float[]? Points { get; init; }
    /// <summary>RGBA per vertex for line and point markers.</summary>
    public byte[]? Colors { get; init; }
    /// <summary>text: the label; drawn centred on the projected position with the 2D painter.</summary>
    public string? Text { get; init; }
    /// <summary>mesh: URI of a resource registered in the layer's <see cref="MeshRegistry"/> (drawn once it is loaded).</summary>
    public string? MeshResource { get; init; }
    /// <summary>Seconds after insertion before the marker expires; 0 keeps it forever.</summary>
    public double Lifetime { get; init; }
    /// <summary>Time used for the frame lookup; null uses the latest transform.</summary>
    public double? Stamp { get; init; }

    /// <summary>The JSON type name (<c>cube</c>, <c>sphere</c>, <c>cylinder</c>, <c>arrow</c>, <c>lineList</c>, <c>lineStrip</c>, <c>points</c>, <c>text</c>, <c>mesh</c>) → <see cref="MarkerType"/>; throws <see cref="ArgumentException"/> otherwise.</summary>
    public static MarkerType ParseType(string s) => s switch
    {
        "cube" => MarkerType.Cube, "sphere" => MarkerType.Sphere, "cylinder" => MarkerType.Cylinder, "arrow" => MarkerType.Arrow,
        "lineList" => MarkerType.LineList, "lineStrip" => MarkerType.LineStrip, "points" => MarkerType.Points, "text" => MarkerType.Text, "mesh" => MarkerType.Mesh,
        _ => throw new ArgumentException($"unknown marker type '{s}'"),
    };
}

/// <summary>A visualization_msgs/MarkerArray-style layer: upsert/remove markers by id; each has its own frame and lifetime.</summary>
public sealed class MarkerLayer(string id) : BaseLayer3D(id)
{
    /// <summary><c>"markers"</c>.</summary>
    public override string Kind => "markers";
    /// <summary>Tree used to resolve marker frames; null draws markers as given.</summary>
    public FrameTree? Frames { get; set; }
    /// <summary>Frame the scene is drawn in.</summary>
    public string? FixedFrame { get; set; }
    /// <summary>Default frame for markers that name none.</summary>
    public string? Frame { get; set; }
    /// <summary>Text marker size in pixels when the marker's scale x is 0 or 1.</summary>
    public double FontSize { get; set; } = 12;
    /// <summary>Where <see cref="MarkerType.Mesh"/> resources are looked up; unresolved meshes are skipped until loaded.</summary>
    public MeshRegistry Meshes { get; set; } = new();
    private sealed class Entry(Marker marker, Mesh3D? mesh, double added) { public Marker Marker = marker; public Mesh3D? Mesh = mesh; public double Added = added; }
    private readonly Dictionary<string, Entry> _entries = [];
    private readonly List<string> _order = [];
    private int _seq;

    /// <summary>Number of live markers.</summary>
    public int Count => _entries.Count;
    /// <summary>Marker ids in insertion (draw) order.</summary>
    public IReadOnlyList<string> Ids => _order;
    /// <summary>The marker with this id, or null.</summary>
    public Marker? Get(string id) => _entries.TryGetValue(id, out var e) ? e.Marker : null;
    /// <summary>Insert or replace by id, keeping the original draw order; <paramref name="now"/> (seconds) starts the lifetime. Vertex meshes of line/point markers are updated in place.</summary>
    public void Upsert(Marker m, double now = 0)
    {
        _entries.TryGetValue(m.Id, out var prev);
        var mesh = prev?.Mesh;
        if (m.Type is MarkerType.LineList or MarkerType.LineStrip or MarkerType.Points)
        {
            var pos = m.Points ?? [];
            if (mesh is null) mesh = new Mesh3D($"marker:{Id}:{m.Id}:{++_seq}", pos) { Colors = m.Colors };
            else { mesh.Positions = pos; mesh.Colors = m.Colors; mesh.Version++; }
        }
        else mesh = null;
        if (prev is null) _order.Add(m.Id);
        _entries[m.Id] = new Entry(m, mesh, now);
        MarkDirty();
    }
    /// <summary>Remove the marker with this id; false when there is none.</summary>
    public bool Remove(string id) { var ok = _entries.Remove(id); if (ok) { _order.Remove(id); MarkDirty(); } return ok; }
    /// <summary>Remove every marker.</summary>
    public void Clear() { _entries.Clear(); _order.Clear(); MarkDirty(); }
    /// <summary>Replace the whole set.</summary>
    public void SetMarkers(IEnumerable<Marker> markers, double now = 0) { Clear(); foreach (var m in markers) Upsert(m, now); }
    /// <summary>Remove markers whose lifetime has elapsed at <paramref name="now"/>; called by <see cref="Draw"/>.</summary>
    /// <returns>How many were removed.</returns>
    public int Expire(double now)
    {
        var gone = _entries.Where(kv => kv.Value.Marker.Lifetime > 0 && now - kv.Value.Added > kv.Value.Marker.Lifetime).Select(kv => kv.Key).ToList();
        foreach (var g in gone) Remove(g);
        return gone.Count;
    }
    /// <summary>Pose matrix of a marker in the fixed frame at <paramref name="now"/> (or its stamp), excluding scale; null when its frame is unknown.</summary>
    public double[]? ModelOf(Marker m, double now)
    {
        var frame = m.Frame ?? Frame;
        double[]? b = null;
        if (frame is not null && Frames is not null && FixedFrame is not null) { b = Frames.Lookup(FixedFrame, frame, m.Stamp ?? now); if (b is null) return null; }
        var local = Mat4.FromPose(m.Position, m.Orientation);
        return b is null ? local : Mat4.Mul(b, local);
    }
    private static double[] ShapeModel(Marker m, double[] pose) => Mat4.Mul(pose, Mat4.Scaling(m.Scale.X, m.Scale.Y, m.Scale.Z));
    /// <summary>Expires old markers, then draws each in order: solids lit, lines and points through their meshes, text on the 2D painter.</summary>
    public override void Draw(LayerContext ctx)
    {
        Expire(ctx.Now);
        var p3 = ctx.Painter3D;
        if (p3 is null) return;
        foreach (var key in _order)
        {
            var e = _entries[key]; var m = e.Marker; var pose = ModelOf(m, ctx.Now);
            if (pose is null) continue;
            var opacity = m.Opacity * ctx.Opacity; var s = m.Scale;
            switch (m.Type)
            {
                case MarkerType.Cube: p3.Triangles(Primitives.UnitCube(), new Material3D { Color = m.Color, Opacity = opacity, Lit = true, Model = ShapeModel(m, pose) }); break;
                case MarkerType.Sphere: p3.Triangles(Primitives.UnitSphere(), new Material3D { Color = m.Color, Opacity = opacity, Lit = true, Model = ShapeModel(m, pose) }); break;
                case MarkerType.Cylinder: p3.Triangles(Primitives.UnitCylinder(), new Material3D { Color = m.Color, Opacity = opacity, Lit = true, Model = ShapeModel(m, pose) }); break;
                case MarkerType.Arrow: p3.Triangles(Primitives.UnitArrow(), new Material3D { Color = m.Color, Opacity = opacity, Lit = true, Model = Mat4.Mul(pose, Mat4.Scaling(s.X, s.Y * 2, s.Z * 2)) }); break;
                case MarkerType.Mesh: if (m.MeshResource is not null && Meshes.Get(m.MeshResource) is { } mesh) p3.Triangles(mesh, new Material3D { Color = m.Color, Opacity = opacity, Lit = true, Model = ShapeModel(m, pose) }); break;
                case MarkerType.LineList: if (e.Mesh is not null) p3.Lines(e.Mesh, new Material3D { Color = m.Color, Opacity = opacity, LineWidth = s.X, Model = pose }, strip: false); break;
                case MarkerType.LineStrip: if (e.Mesh is not null) p3.Lines(e.Mesh, new Material3D { Color = m.Color, Opacity = opacity, LineWidth = s.X, Model = pose }, strip: true); break;
                case MarkerType.Points: if (e.Mesh is not null) p3.Points(e.Mesh, new Material3D { Color = m.Color, Opacity = opacity, PointSize = s.X, Model = pose }); break;
                case MarkerType.Text:
                    if (m.Text is null || ctx.Projection is not Camera3D cam) break;
                    var o = Mat4.Point(pose, 0, 0, 0); var sc = cam.Project3(o.X, o.Y, o.Z);
                    if (sc.Visible) ctx.Painter.Text(m.Text, sc.X, sc.Y, new TextStyle(m.Color) { Size = s.X > 0 && s.X != 1 ? s.X : FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Middle, Opacity = opacity });
                    break;
            }
        }
    }
    private (Vec3 Center, double Radius) SphereOf(Marker m, double[] pose)
    {
        var s = m.Scale;
        if (m.Type == MarkerType.Mesh)
        {
            var mesh = m.MeshResource is null ? null : Meshes.Get(m.MeshResource);
            var b = mesh is null ? null : Box3.FromPositions(mesh.Positions, ShapeModel(m, pose));
            return b is { } bb ? bb.Sphere() : (Mat4.Point(pose, 0, 0, 0), 0);
        }
        var c = Mat4.Point(pose, m.Type == MarkerType.Arrow ? s.X / 2 : 0, 0, 0);
        var r = m.Type == MarkerType.Arrow ? Math.Max(s.X, Math.Max(s.Y * 2, s.Z * 2)) / 2 : Math.Sqrt(s.X * s.X + s.Y * s.Y + s.Z * s.Z) / 2;
        return (c, r);
    }
    /// <summary>Union of the markers' boxes in the fixed frame (solids via their bounding sphere); markers with unknown frames are skipped.</summary>
    public override Box3? Bounds3()
    {
        Box3? b = null;
        foreach (var key in _order)
        {
            var e = _entries[key]; var m = e.Marker; var pose = ModelOf(m, m.Stamp ?? 0);
            if (pose is null) continue;
            if (e.Mesh is not null) b = Box3.Union(b, Box3.FromPositions(e.Mesh.Positions, pose));
            else if (m.Type == MarkerType.Mesh) { if (m.MeshResource is not null && Meshes.Get(m.MeshResource) is { } mesh) b = Box3.Union(b, Box3.FromPositions(mesh.Positions, ShapeModel(m, pose))); }
            else if (m.Type == MarkerType.Text) b = Box3.Union(b, Box3.FromPositions([0f, 0f, 0f], pose));
            else { var (c, r) = SphereOf(m, pose); b = Box3.Union(b, new Box3(new(c.X - r, c.Y - r, c.Z - r), new(c.X + r, c.Y + r, c.Z + r))); }
        }
        return b;
    }
    /// <summary>Under a <see cref="Camera3D"/>: solids by ray–sphere (nearest wins, distance 0), line/point markers and text by screen distance within <paramref name="tolerance"/>. <see cref="HitResult.Data"/> is the marker id.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        if (ctx.Projection is not Camera3D cam) return null;
        var ray = cam.Ray(sx, sy);
        HitResult? best = null; var bestT = double.PositiveInfinity;
        foreach (var key in _order)
        {
            var e = _entries[key]; var m = e.Marker; var pose = ModelOf(m, ctx.Now);
            if (pose is null) continue;
            if (e.Mesh is not null)
            {
                var h = Frame3D.NearestVertex(cam, e.Mesh.Positions, e.Mesh.Positions.Length / 3, pose, sx, sy);
                if (h is { } hh && hh.Distance <= tolerance + m.Scale.X / 2 && (best is null || hh.Distance < best.Distance)) best = new HitResult(Id, hh.World, new Vec2(sx, sy), hh.Distance) { Index = hh.Index, Data = key };
            }
            else if (m.Type == MarkerType.Text)
            {
                var o = Mat4.Point(pose, 0, 0, 0); var sc = cam.Project3(o.X, o.Y, o.Z);
                var d = Math.Sqrt((sc.X - sx) * (sc.X - sx) + (sc.Y - sy) * (sc.Y - sy));
                if (sc.Visible && d <= tolerance + 8 && (best is null || d < best.Distance)) best = new HitResult(Id, o, new Vec2(sx, sy), d) { Data = key };
            }
            else
            {
                var (c, r) = SphereOf(m, pose); var t = ray.Sphere(c, r);
                if (t is { } tt && tt < bestT) { bestT = tt; best = new HitResult(Id, ray.At(tt), new Vec2(sx, sy), 0) { Data = key }; }
            }
        }
        return best;
    }
}

/// <summary>A sensor_msgs/LaserScan drawn in 3D: the scan's points on the z = 0 plane of its own frame.</summary>
public sealed class LaserScan3DLayer : BaseLayer3D
{
    /// <summary><c>"laserScan3d"</c>.</summary>
    public override string Kind => "laserScan3d";
    /// <summary>The cloud that holds and draws the scan points; set its frame and colours here.</summary>
    public PointCloud3DLayer Cloud { get; }
    /// <summary>Creates the layer with a red, 3-pixel inner cloud.</summary>
    public LaserScan3DLayer(string id) : base(id) { Cloud = new PointCloud3DLayer(id) { Color = "#f87171", PointSize = 3 }; }
    /// <summary>Convert the scan to xy points at z = 0 in the sensor frame and load them into <see cref="Cloud"/>.</summary>
    public void SetScan(LaserScan scan, double? stamp = null)
    {
        var xy = RobotGeometry.LaserScanToPoints(scan); var n = xy.Length / 2; var pos = new float[n * 3];
        for (var i = 0; i < n; i++) { pos[3 * i] = (float)xy[2 * i]; pos[3 * i + 1] = (float)xy[2 * i + 1]; pos[3 * i + 2] = 0; }
        Cloud.SetPoints(pos, null, null, stamp);
        MarkDirty();
    }
    /// <summary>Delegates to the inner cloud after syncing opacity.</summary>
    public override void Draw(LayerContext ctx) { Cloud.Opacity = Opacity; Cloud.Draw(ctx); }
    /// <summary>The inner cloud's bounds.</summary>
    public override Box3? Bounds3() => Cloud.Bounds3();
    /// <summary>The inner cloud's hit, reported under this layer's id.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance) => Cloud.HitTest(sx, sy, ctx, tolerance) is { } h ? h with { LayerId = Id } : null;
}

/// <summary>A 2D occupancy grid drawn as a textured plane at height <see cref="Z"/> in its frame.</summary>
public sealed class OccupancyGridPlaneLayer(string id, int width, int height, double resolution) : BaseLayer3D(id)
{
    /// <summary><c>"occupancyGrid3d"</c>.</summary>
    public override string Kind => "occupancyGrid3d";
    /// <summary>The 2D grid that owns the cells, colours and raster; feed data and origin through it.</summary>
    public OccupancyGridLayer Grid { get; } = new($"{id}:grid", width, height, resolution);
    /// <summary>Frame the grid is expressed in; null draws it as given.</summary>
    public string? Frame { get; set; }
    /// <summary>Tree used to resolve <see cref="Frame"/>.</summary>
    public FrameTree? Frames { get; set; }
    /// <summary>Frame the scene is drawn in.</summary>
    public string? FixedFrame { get; set; }
    /// <summary>Height of the plane in its frame, world units.</summary>
    public double Z { get; set; }
    /// <summary>Time used for the frame lookup; null uses the latest transform.</summary>
    public double? Stamp { get; set; }

    /// <summary>World corners (in the grid's frame): bottom-left, bottom-right, top-right, top-left of the raster.</summary>
    public double[] Corners()
    {
        var m = RobotGeometry.RasterToWorld(Grid.Placement(), Grid.Height); double w = Grid.Width, h = Grid.Height;
        var o = new List<double>();
        foreach (var (px, py) in new[] { (0d, h), (w, h), (w, 0d), (0d, 0d) }) { var p = m.Apply(px, py); o.AddRange([p.X, p.Y, Z]); }
        return o.ToArray();
    }
    /// <summary>Model matrix at <paramref name="now"/> (or at <see cref="Stamp"/>): not found means the frame is unknown; found with a null matrix means identity.</summary>
    public (bool Found, double[]? Model) Model(double now) => Frame3D.ModelFor(Frame, Frames, FixedFrame, Stamp ?? now);
    /// <summary>Draws the grid raster as a textured quad; nothing when the frame is unknown.</summary>
    public override void Draw(LayerContext ctx)
    {
        var (found, model) = Model(ctx.Now);
        if (!found) return;
        ctx.Painter3D?.Image(Grid.Image(), Corners(), new Material3D { Opacity = ctx.Opacity, Model = model });
    }
    /// <summary>Box of the four corners in the fixed frame; null when the frame is unknown.</summary>
    public override Box3? Bounds3() { var (found, model) = Model(Stamp ?? 0); return found ? Box3.FromPositions(Corners(), model) : null; }
    /// <summary>Ray–plane intersection with the quad under a <see cref="Camera3D"/>: <see cref="HitResult.Index"/> = <c>row × width + col</c>, <see cref="HitResult.Data"/> = the cell value (double). Tolerance is ignored.</summary>
    public override HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance)
    {
        if (ctx.Projection is not Camera3D cam) return null;
        var (found, model) = Model(ctx.Now);
        if (!found) return null;
        var ray = cam.Ray(sx, sy); var c = Corners(); var M = model ?? Mat4.Identity;
        Vec3 p0 = Mat4.Point(M, c[0], c[1], c[2]), p1 = Mat4.Point(M, c[3], c[4], c[5]), p3 = Mat4.Point(M, c[9], c[10], c[11]);
        Vec3 u = Math3.Sub(p1, p0), v = Math3.Sub(p3, p0);
        var n = Math3.Cross(u, v);
        var denom = Math3.Dot(n, ray.Dir);
        if (Math.Abs(denom) < 1e-12) return null;
        var t = Math3.Dot(n, Math3.Sub(p0, ray.Origin)) / denom;
        if (t < 0) return null;
        var w = ray.At(t);
        var d = Math3.Sub(w, p0);
        double a = Math3.Dot(d, u) / Math3.Dot(u, u), b = Math3.Dot(d, v) / Math3.Dot(v, v);
        if (a < 0 || a > 1 || b < 0 || b > 1) return null;
        int col = Math.Min(Grid.Width - 1, (int)Math.Floor(a * Grid.Width)), row = Math.Min(Grid.Height - 1, (int)Math.Floor((1 - b) * Grid.Height));
        return new HitResult(Id, w, new Vec2(sx, sy), 0) { Index = row * Grid.Width + col, Data = (double)Grid.Get(row, col) };
    }
}
