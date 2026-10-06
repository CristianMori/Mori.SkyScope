// Mori.SkyScope — Shared JSON ↔ 3D value helpers for the scene3d drivers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Shared JSON ↔ 3D value helpers for the scene3d drivers. Mirrors <c>fixtures/scene3d-drivers.ts</c>.</summary>
internal static class J3
{
    /// <summary>Parses a JSON number array.</summary>
    public static double[] Nums(JsonElement e) => ChartJson.Doubles(e);
    /// <summary>Parses <c>[x, y, z]</c>.</summary>
    public static Vec3 Vec(JsonElement e) { var n = Nums(e); return new(n[0], n[1], n[2]); }
    /// <summary>Parses <c>[x, y, z, w]</c>.</summary>
    public static Quat Quat(JsonElement e) { var n = Nums(e); return new(n[0], n[1], n[2], n[3]); }
    /// <summary>Parses a <c>{min, max}</c> box.</summary>
    public static Box3 Box(JsonElement e) => new(Vec(e.GetProperty("min")), Vec(e.GetProperty("max")));
    /// <summary>Parses <c>{origin, dir}</c>, normalising the direction.</summary>
    public static Ray Ray(JsonElement e) => new(Vec(e.GetProperty("origin")), Math3.Normalize(Vec(e.GetProperty("dir"))));
    /// <summary>A number, or null for any other JSON kind.</summary>
    public static double? Opt(JsonElement e) => e.ValueKind == JsonValueKind.Number ? e.GetDouble() : null;
    /// <summary>A 3D vector as a rounded <c>{x, y, z}</c> object.</summary>
    public static object RV(Vec3 v) => new { x = Round.R9(v.X), y = Round.R9(v.Y), z = Round.R9(v.Z) };
    /// <summary>A quaternion as a rounded <c>{x, y, z, w}</c> object.</summary>
    public static object RQ(Quat q) => new { x = Round.R9(q.X), y = Round.R9(q.Y), z = Round.R9(q.Z), w = Round.R9(q.W) };
    /// <summary>A matrix rounded element-wise, or null.</summary>
    public static double[]? RM(double[]? m) => m?.Select(Round.R9).ToArray();
    /// <summary>A box as rounded <c>min</c>/<c>max</c> objects, or null.</summary>
    public static object? RBox(Box3? b) => b is { } v ? new { min = RV(v.Min), max = RV(v.Max) } : null;

    /// <summary>Builds a 4x4 matrix from a literal 16-element array or from a constructor object: translation, scaling, quat, euler, pose, mul, invert, transpose, lookAt, perspective or ortho.</summary>
    public static double[] Mat(JsonElement s)
    {
        if (s.ValueKind == JsonValueKind.Array) return Nums(s);
        if (s.TryGetProperty("translation", out var t)) { var n = Nums(t); return Mat4.Translation(n[0], n[1], n[2]); }
        if (s.TryGetProperty("scaling", out var sc)) { var n = Nums(sc); return Mat4.Scaling(n[0], n[1], n[2]); }
        if (s.TryGetProperty("quat", out var q)) return Mat4.FromQuat(Quat(q));
        if (s.TryGetProperty("euler", out var eu)) { var n = Nums(eu); return Mat4.FromQuat(Scene3D.Quat.FromEuler(n[0], n[1], n[2])); }
        if (s.TryGetProperty("pose", out var p)) { var a = p.EnumerateArray().ToArray(); return Mat4.FromPose(Vec(a[0]), Quat(a[1])); }
        if (s.TryGetProperty("mul", out var m)) { var a = m.EnumerateArray().ToArray(); return Mat4.Mul(Mat(a[0]), Mat(a[1])); }
        if (s.TryGetProperty("invert", out var inv)) return Mat4.Invert(Mat(inv)) ?? Mat4.Identity;
        if (s.TryGetProperty("transpose", out var tr)) return Mat4.Transpose(Mat(tr));
        if (s.TryGetProperty("lookAt", out var la)) { var a = la.EnumerateArray().ToArray(); return Mat4.LookAt(Vec(a[0]), Vec(a[1]), Vec(a[2])); }
        if (s.TryGetProperty("perspective", out var pe)) { var n = Nums(pe); return Mat4.Perspective(n[0], n[1], n[2], n[3]); }
        if (s.TryGetProperty("ortho", out var or)) { var n = Nums(or); return Mat4.Ortho(n[0], n[1], n[2], n[3], n[4], n[5]); }
        throw new InvalidOperationException($"unknown matrix {s}");
    }

    /// <summary>Builds a <c>Camera3D</c> from JSON with the library defaults; the viewport may be overridden.</summary>
    public static Camera3D Camera(JsonElement setup, double? width = null, double? height = null)
    {
        double? N(string n) => ChartJson.Num(setup, n);
        return new Camera3D(width ?? N("width") ?? 1, height ?? N("height") ?? 1, setup.TryGetProperty("target", out var t) && t.ValueKind == JsonValueKind.Array ? Vec(t) : null,
            N("distance") ?? 10, N("yaw") ?? Math.PI, N("pitch") ?? 0.6, N("fov") ?? Math.PI / 4, N("near") ?? 0.05, N("far") ?? 5000, ChartJson.Bool(setup, "ortho") ?? false, N("minDistance") ?? 1e-3, N("maxDistance") ?? 1e6);
    }
    /// <summary>Applies a camera command step; returns false when the step type is not a camera command.</summary>
    public static bool CameraStep(Camera3D c, JsonElement step)
    {
        double D(string n) => step.GetProperty(n).GetDouble();
        switch (step.GetProperty("type").GetString())
        {
            case "setViewport": c.SetViewport(D("width"), D("height")); return true;
            case "setTarget": { var v = Vec(step.GetProperty("target")); c.SetTarget(v.X, v.Y, v.Z); return true; }
            case "setDistance": c.SetDistance(D("distance")); return true;
            case "setOrbit": c.SetOrbit(D("yaw"), D("pitch")); return true;
            case "setFov": c.SetFov(D("fov")); return true;
            case "setOrtho": c.SetOrtho(ChartJson.Bool(step, "ortho") ?? false); return true;
            case "orbit": c.Orbit(D("dyaw"), D("dpitch")); return true;
            case "pan": c.Pan(D("dx"), D("dy")); return true;
            case "dolly": c.Dolly(D("factor")); return true;
            case "dollyAt": c.DollyAt(D("x"), D("y"), D("factor")); return true;
            case "fitSphere": c.FitSphere(Vec(step.GetProperty("center")), D("radius")); return true;
            case "fitBox": c.FitBox(Box(step.GetProperty("box"))); return true;
            default: return false;
        }
    }
    /// <summary>Answers one camera query: 3D or 2D projection and unprojection, ray, ground point, eye, view and projection matrices, world-per-pixel or state.</summary>
    public static object? CameraQuery(Camera3D c, JsonElement step)
    {
        if (step.TryGetProperty("project3", out var p3)) { var n = Nums(p3); var p = c.Project3(n[0], n[1], n[2]); return new { x = Round.R9(p.X), y = Round.R9(p.Y), depth = Round.R9(p.Depth), visible = p.Visible }; }
        if (step.TryGetProperty("unproject3", out var u3)) { var n = Nums(u3); return RV(c.Unproject3(n[0], n[1], n[2])); }
        if (step.TryGetProperty("ray", out var ry)) { var n = Nums(ry); var r = c.Ray(n[0], n[1]); return new { origin = RV(r.Origin), dir = RV(r.Dir) }; }
        if (step.TryGetProperty("groundPoint", out var gp)) { var n = Nums(gp); return c.GroundPoint(n[0], n[1]) is { } g ? RV(g) : null; }
        if (step.TryGetProperty("project", out var pr)) { var n = Nums(pr); return Round.Vec(c.Project(n[0], n[1])); }
        if (step.TryGetProperty("unproject", out var up)) { var n = Nums(up); return Round.Vec(c.Unproject(n[0], n[1])); }
        if (step.TryGetProperty("eye", out _)) return RV(c.Eye());
        if (step.TryGetProperty("view", out _)) return RM(c.View());
        if (step.TryGetProperty("proj", out _)) return RM(c.Projection());
        if (step.TryGetProperty("worldPerPixel", out _)) return Round.R9(c.WorldPerPixel());
        if (step.TryGetProperty("state", out _)) return new { target = RV(c.Target), distance = Round.R9(c.Distance), yaw = Round.R9(c.Yaw), pitch = Round.R9(c.Pitch), ortho = c.Ortho, version = c.Version };
        throw new InvalidOperationException($"unknown query {step}");
    }

    /// <summary>Fixture JSON for typed arrays: <c>{"$f32":[…]}</c> … at the top level of a payload.</summary>
    public static object? PayloadFromJson(JsonElement p)
    {
        if (p.ValueKind != JsonValueKind.Object) return p;
        var d = new Dictionary<string, object?>();
        foreach (var prop in p.EnumerateObject())
        {
            var v = prop.Value;
            if (v.ValueKind == JsonValueKind.Object && v.EnumerateObject().Count() == 1)
            {
                var tag = v.EnumerateObject().First();
                if (tag.Name.StartsWith('$'))
                {
                    var n = Nums(tag.Value);
                    d[prop.Name] = tag.Name switch
                    {
                        "$f32" => n.Select(x => (float)x).ToArray(), "$f64" => n, "$u8" => n.Select(x => (byte)x).ToArray(), "$u16" => n.Select(x => (ushort)x).ToArray(),
                        "$i16" => n.Select(x => (short)x).ToArray(), "$u32" => n.Select(x => (uint)x).ToArray(), "$i32" => n.Select(x => (int)x).ToArray(), _ => (object)v.Clone(),
                    };
                    continue;
                }
            }
            d[prop.Name] = v.Clone();
        }
        return d;
    }
    /// <summary>The inverse of <see cref="PayloadFromJson"/>: typed arrays back to tagged objects, with float values rounded.</summary>
    public static object? PayloadToJson(IReadOnlyDictionary<string, object?> p)
    {
        var o = new Dictionary<string, object?>();
        foreach (var (k, v) in p)
        {
            // by element type: the CLR lets short[] match a ushort[] pattern (and int[] a uint[] one)
            o[k] = v is Array arr ? Type.GetTypeCode(arr.GetType().GetElementType()) switch
            {
                TypeCode.Single => new Dictionary<string, object?> { ["$f32"] = ((float[])arr).Select(x => Round.R9(x)).ToArray() },
                TypeCode.Double => new Dictionary<string, object?> { ["$f64"] = ((double[])arr).Select(Round.R9).ToArray() },
                TypeCode.Byte => new Dictionary<string, object?> { ["$u8"] = ((byte[])arr).Select(x => (int)x).ToArray() },
                TypeCode.UInt16 => new Dictionary<string, object?> { ["$u16"] = ((ushort[])arr).Select(x => (int)x).ToArray() },
                TypeCode.Int16 => new Dictionary<string, object?> { ["$i16"] = ((short[])arr).Select(x => (int)x).ToArray() },
                TypeCode.UInt32 => new Dictionary<string, object?> { ["$u32"] = ((uint[])arr).Select(x => (long)x).ToArray() },
                TypeCode.Int32 => new Dictionary<string, object?> { ["$i32"] = (int[])arr },
                _ => v,
            } : v;
        }
        return o;
    }
}

/// <summary>Fixture driver for the 3D math primitives: quaternions, 4x4 matrices, boxes and ray intersections.</summary>
public sealed class Math3Driver : IFixtureDriver
{
    /// <summary>Handles the <c>math3</c> fixtures.</summary>
    public string Component => "math3";
    /// <summary>No setup; the state is the query list.</summary>
    public object Create(JsonElement setup) => new List<object?>();
    /// <summary>Evaluates one query against <c>Quat</c>, <c>Mat4</c>, <c>Box3</c> or <c>Ray</c>.</summary>
    public object Step(object state, JsonElement s)
    {
        var q = (List<object?>)state;
        if (s.TryGetProperty("quatFromEuler", out var qe)) { var n = J3.Nums(qe); q.Add(J3.RQ(Quat.FromEuler(n[0], n[1], n[2]))); }
        else if (s.TryGetProperty("quatToEuler", out var qt)) { var (r, p, y) = J3.Quat(qt).ToEuler(); q.Add(new { roll = Round.R9(r), pitch = Round.R9(p), yaw = Round.R9(y) }); }
        else if (s.TryGetProperty("quatFromAxisAngle", out var qa)) { var a = qa.EnumerateArray().ToArray(); q.Add(J3.RQ(Quat.FromAxisAngle(J3.Vec(a[0]), a[1].GetDouble()))); }
        else if (s.TryGetProperty("quatMul", out var qm)) { var a = qm.EnumerateArray().ToArray(); q.Add(J3.RQ(J3.Quat(a[0]) * J3.Quat(a[1]))); }
        else if (s.TryGetProperty("quatRotate", out var qr)) { var a = qr.EnumerateArray().ToArray(); q.Add(J3.RV(J3.Quat(a[0]).Rotate(J3.Vec(a[1])))); }
        else if (s.TryGetProperty("quatSlerp", out var qs)) { var a = qs.EnumerateArray().ToArray(); q.Add(J3.RQ(Quat.Slerp(J3.Quat(a[0]), J3.Quat(a[1]), a[2].GetDouble()))); }
        else if (s.TryGetProperty("mat4", out var m4)) q.Add(J3.RM(J3.Mat(m4)));
        else if (s.TryGetProperty("invert", out var inv)) q.Add(J3.RM(Mat4.Invert(J3.Mat(inv))));
        else if (s.TryGetProperty("point", out var pt)) { var a = pt.EnumerateArray().ToArray(); q.Add(J3.RV(Mat4.Point(J3.Mat(a[0]), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()))); }
        else if (s.TryGetProperty("dir", out var dr)) { var a = dr.EnumerateArray().ToArray(); q.Add(J3.RV(Mat4.Dir(J3.Mat(a[0]), a[1].GetDouble(), a[2].GetDouble(), a[3].GetDouble()))); }
        else if (s.TryGetProperty("box3FromPositions", out var bp)) { var a = bp.EnumerateArray().ToArray(); q.Add(J3.RBox(Box3.FromPositions(J3.Nums(a[0]), a[1].ValueKind == JsonValueKind.Null ? null : J3.Mat(a[1])))); }
        else if (s.TryGetProperty("box3Sphere", out var bs)) { var (c, r) = J3.Box(bs).Sphere(); q.Add(new { center = J3.RV(c), radius = Round.R9(r) }); }
        else if (s.TryGetProperty("box3Union", out var bu)) { var a = bu.EnumerateArray().ToArray(); q.Add(J3.RBox(Box3.Union(a[0].ValueKind == JsonValueKind.Null ? null : J3.Box(a[0]), a[1].ValueKind == JsonValueKind.Null ? null : J3.Box(a[1])))); }
        else if (s.TryGetProperty("rayPlane", out var rp)) { var a = rp.EnumerateArray().ToArray(); var t = J3.Ray(a[0]).Plane(J3.Vec(a[1]), a[2].GetDouble()); q.Add(t is { } v ? Round.R9(v) : null); }
        else if (s.TryGetProperty("rayBox", out var rb)) { var a = rb.EnumerateArray().ToArray(); var t = J3.Ray(a[0]).Box(J3.Box(a[1])); q.Add(t is { } v ? Round.R9(v) : null); }
        else if (s.TryGetProperty("raySphere", out var rs)) { var a = rs.EnumerateArray().ToArray(); var t = J3.Ray(a[0]).Sphere(J3.Vec(a[1]), a[2].GetDouble()); q.Add(t is { } v ? Round.R9(v) : null); }
        else throw new InvalidOperationException($"unknown query {s}");
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = (List<object?>)state }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the orbit camera: commands through <see cref="J3.CameraStep"/>, queries through <see cref="J3.CameraQuery"/>.</summary>
public sealed class Camera3DDriver : IFixtureDriver
{
    /// <summary>Handles the <c>camera3d</c> fixtures.</summary>
    public string Component => "camera3d";
    private sealed record State(Camera3D Camera, List<object?> Queries);
    /// <summary>Creates the camera from the setup object.</summary>
    public object Create(JsonElement setup) => new State(J3.Camera(setup), []);
    /// <summary>Applies a camera command or records a query answer.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        if (step.GetProperty("type").GetString() == "query") s.Queries.Add(J3.CameraQuery(s.Camera, step));
        else if (!J3.CameraStep(s.Camera, step)) throw new InvalidOperationException($"unknown step {step}");
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the TF frame tree: stamped transforms in, lookups, interpolated transforms and graph queries out.</summary>
public sealed class FrameTreeDriver : IFixtureDriver
{
    /// <summary>Handles the <c>frameTree</c> fixtures.</summary>
    public string Component => "frameTree";
    private sealed record State(FrameTree Tree, List<object?> Queries);
    /// <summary>Creates an empty tree keeping <c>maxSamples</c> of history per frame.</summary>
    public object Create(JsonElement setup) => new State(new FrameTree((int)(ChartJson.Num(setup, "maxSamples") ?? 200)), []);
    private static double? Time(JsonElement[] a, int i) => a.Length > i ? J3.Opt(a[i]) : null;
    /// <summary>Applies set, remove and clear, and answers lookup, point, transformAt, frameIds, roots, parent, canTransform and version queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (tree, q) = ((State)state);
        switch (step.GetProperty("type").GetString())
        {
            case "set": tree.Set(step.GetProperty("child").GetString()!, step.GetProperty("parent").GetString()!, new Transform3(J3.Vec(step.GetProperty("t")), J3.Quat(step.GetProperty("q"))), ChartJson.Num(step, "time")); break;
            case "remove": tree.Remove(step.GetProperty("frame").GetString()!); break;
            case "clear": tree.Clear(); break;
            case "query":
                if (step.TryGetProperty("lookup", out var lk)) { var a = lk.EnumerateArray().ToArray(); q.Add(J3.RM(tree.Lookup(a[0].GetString()!, a[1].GetString()!, Time(a, 2)))); }
                else if (step.TryGetProperty("point", out var pt)) { var a = pt.EnumerateArray().ToArray(); var m = tree.Lookup(a[0].GetString()!, a[1].GetString()!, Time(a, 2)); var v = J3.Vec(a[3]); q.Add(m is null ? null : J3.RV(Mat4.Point(m, v.X, v.Y, v.Z))); }
                else if (step.TryGetProperty("transformAt", out var ta)) { var a = ta.EnumerateArray().ToArray(); var tf = tree.TransformAt(a[0].GetString()!, Time(a, 1)); q.Add(tf is { } t ? new { t = J3.RV(t.T), q = J3.RQ(t.Q) } : null); }
                else if (step.TryGetProperty("frameIds", out _)) q.Add(tree.FrameIds());
                else if (step.TryGetProperty("roots", out _)) q.Add(tree.Roots());
                else if (step.TryGetProperty("parent", out var pa)) q.Add(tree.Parent(pa.GetString()!));
                else if (step.TryGetProperty("canTransform", out var ct)) { var a = ct.EnumerateArray().ToArray(); q.Add(tree.CanTransform(a[0].GetString()!, a[1].GetString()!, Time(a, 2))); }
                else if (step.TryGetProperty("version", out _)) q.Add(tree.Version);
                else throw new InvalidOperationException($"unknown query {step}");
                break;
            default: throw new InvalidOperationException($"unknown step {step}");
        }
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the binary SkyScopeLayer message: encode, decode, binary detection and base64 of the last encoding.</summary>
public sealed class LayerMessageDriver : IFixtureDriver
{
    /// <summary>Handles the <c>layerMessage</c> fixtures.</summary>
    public string Component => "layerMessage";
    private sealed class State { public List<object?> Queries = []; public byte[]? Last; }
    /// <summary>No setup; starts with an empty query list and no last encoding.</summary>
    public object Create(JsonElement setup) => new State();
    /// <summary>Encodes a payload (recording length, hash and the decoded round trip), decodes base64 bytes (recording errors as messages), tests <c>hasBinary</c>, or returns the last encoding as base64.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var q = s.Queries;
        if (step.TryGetProperty("encode", out var e))
        {
            var bytes = LayerMessage.Encode(e.GetProperty("id").GetString()!, (IReadOnlyDictionary<string, object?>)J3.PayloadFromJson(e.GetProperty("payload"))!);
            s.Last = bytes;
            var (id, payload) = LayerMessage.Decode(bytes);
            q.Add(new { length = bytes.Length, hash = RecordingPainter.Fnv1a(bytes), roundTrip = new { id, payload = J3.PayloadToJson(payload) } });
        }
        else if (step.TryGetProperty("decode", out var d))
        {
            try { var (id, payload) = LayerMessage.Decode(Convert.FromBase64String(d.GetString()!)); q.Add(new { id, payload = J3.PayloadToJson(payload) }); }
            catch (InvalidDataException ex) { q.Add(new { error = ex.Message }); }
        }
        else if (step.TryGetProperty("hasBinary", out var hb)) q.Add(LayerMessage.HasBinary(J3.PayloadFromJson(hb)));
        else if (step.TryGetProperty("base64", out _)) q.Add(s.Last is null ? null : Convert.ToBase64String(s.Last));
        else throw new InvalidOperationException($"unknown query {step}");
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Layers created through the JSON contract, drawn with a RecordingPainter3D under a Camera3D.</summary>
public sealed class Scene3DDriver : IFixtureDriver
{
    /// <summary>Handles the <c>scene3d</c> fixtures.</summary>
    public string Component => "scene3d";
    private sealed record State(Scene.Scene Scene, SceneLayerSink Sink, Camera3D Camera, double Width, double Height, List<object?> Queries);
    private static FrameTransformMessage Tf(JsonElement t) => new(t.GetProperty("child").GetString()!, t.GetProperty("parent").GetString()!, ChartJson.Doubles(t, "t") ?? [0, 0, 0], ChartJson.Doubles(t, "q") ?? [0, 0, 0, 1], ChartJson.Num(t, "time"));
    private static object Transforms(JsonElement arr) => new Dictionary<string, object?> { ["transforms"] = arr.EnumerateArray().Select(t => new { child = t.GetProperty("child").GetString(), parent = t.GetProperty("parent").GetString(), t = ChartJson.Doubles(t, "t"), q = ChartJson.Doubles(t, "q"), time = ChartJson.Num(t, "time") }).ToList() };

    /// <summary>Builds a scene, a layer sink with the fixed frame, the camera, and the declared layers and transforms.</summary>
    public object Create(JsonElement setup)
    {
        double width = ChartJson.Num(setup, "width") ?? 800, height = ChartJson.Num(setup, "height") ?? 600;
        var camera = J3.Camera(setup.TryGetProperty("camera", out var c) ? c : JsonDocument.Parse("{}").RootElement, width, height);
        var scene = new Scene.Scene();
        var sink = new SceneLayerSink(scene, env: new LayerJson.LayerEnv(FixedFrame: ChartJson.Str(setup, "fixedFrame")));
        if (setup.TryGetProperty("layers", out var layers)) foreach (var l in layers.EnumerateArray()) sink.DeclareLayer(l.GetProperty("id").GetString()!, l.GetProperty("kind").GetString()!, l.TryGetProperty("meta", out var m) ? LayerJson.ToMeta(m.Clone()) : null);
        if (setup.TryGetProperty("transforms", out var tfs)) { sink.DeclareLayer("tf", "frames"); sink.Push("tf", Transforms(tfs)); }
        return new State(scene, sink, camera, width, height, []);
    }
    /// <summary>Declares and pushes layers, pushes transforms, resets, drives the camera, fits to the scene bounds, draws (recording the 3D ops and the 2D op count) and answers hit-test, bounds, order, frame-id, unknown-layer and camera queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var (scene, sink, camera, _, _, q) = s;
        switch (step.GetProperty("type").GetString())
        {
            case "declare": sink.DeclareLayer(step.GetProperty("id").GetString()!, step.GetProperty("kind").GetString()!, step.TryGetProperty("meta", out var m) ? LayerJson.ToMeta(m.Clone()) : null); break;
            case "push": sink.Push(step.GetProperty("id").GetString()!, J3.PayloadFromJson(step.GetProperty("payload"))); break;
            case "transforms": sink.Push("tf", Transforms(step.GetProperty("transforms"))); break;
            case "reset": sink.Reset(); break;
            case "camera": if (!J3.CameraStep(camera, step.GetProperty("op"))) throw new InvalidOperationException("unknown camera op"); break;
            case "fit": if (scene.Bounds3() is { } b) camera.FitBox(b); break;
            case "draw":
                {
                    var p2 = new RecordingPainter(s.Width, s.Height); var p3 = new RecordingPainter3D(s.Width, s.Height);
                    p3.Begin(camera.View(), camera.Projection(), ChartJson.Str(step, "clear"));
                    scene.Draw(p2, camera, ChartJson.Num(step, "now") ?? 0, p3);
                    p3.End();
                    q.Add(new { ops3d = p3.Ops, ops2d = p2.Ops.Count });
                    break;
                }
            case "query":
                if (step.TryGetProperty("hitTest", out var h))
                {
                    var r = scene.HitTest(h.GetProperty("x").GetDouble(), h.GetProperty("y").GetDouble(), camera, s.Width, s.Height, ChartJson.Num(h, "tolerance") ?? 6, ChartJson.Num(h, "now") ?? 0);
                    q.Add(r is null ? null : new { layerId = r.LayerId, world = J3.RV(r.World), distance = Round.R9(r.Distance), index = r.Index, data = r.Data is double dd ? Round.R9(dd) : r.Data });
                }
                else if (step.TryGetProperty("bounds3", out _)) q.Add(J3.RBox(scene.Bounds3()));
                else if (step.TryGetProperty("order", out _)) q.Add(scene.Order());
                else if (step.TryGetProperty("frameIds", out _)) q.Add(sink.Frames.FrameIds());
                else if (step.TryGetProperty("unknown", out _)) q.Add(sink.Unknown.Order(StringComparer.Ordinal).ToList());
                else q.Add(J3.CameraQuery(camera, step));
                break;
            default: throw new InvalidOperationException($"unknown step {step}");
        }
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>The golden SkyScopeLayer written by the TypeScript core must decode here and re-encode byte for byte.</summary>
public sealed class LayerGoldenTests
{
    /// <summary>The length and hash of the golden cloud match the sidecar, it decodes with a float32 positions array, re-encodes identically and is not mistaken for a frame.</summary>
    [Fact]
    public void GoldenCloudRoundTrips()
    {
        var dir = Path.Combine(Fixtures.Directory, "..", "frames");
        var bytes = File.ReadAllBytes(Path.Combine(dir, "layer-cloud.bin"));
        using var side = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "layer-cloud.json")));
        Assert.Equal(side.RootElement.GetProperty("length").GetInt32(), bytes.Length);
        Assert.Equal(side.RootElement.GetProperty("hash").GetUInt32(), RecordingPainter.Fnv1a(bytes));
        Assert.True(LayerMessage.IsLayerMessage(bytes));
        var (id, payload) = LayerMessage.Decode(bytes);
        Assert.Equal(side.RootElement.GetProperty("id").GetString(), id);
        Assert.IsType<float[]>(payload["positions"]);
        var again = LayerMessage.Encode(id, (IReadOnlyDictionary<string, object?>)J3.PayloadFromJson(side.RootElement.GetProperty("payload"))!);
        Assert.Equal(bytes, again);
        Assert.ThrowsAny<Exception>(() => FrameCodec.Decode(bytes));
    }
}

/// <summary>Scene3DController fed with InputEvents; HUD ops recorded with a RecordingPainter. Mirrors the TS <c>scene3dControllerDriver</c>.</summary>
public sealed class Scene3DControllerDriver : IFixtureDriver
{
    /// <summary>Handles the <c>scene3dController</c> fixtures.</summary>
    public string Component => "scene3dController";
    private sealed record State(Scene3DController C, double Width, double Height, List<object?> Queries);
    private static Scene3DTool Tool(string? s) => s switch { "measure" => Scene3DTool.Measure, "select" => Scene3DTool.Select, _ => Scene3DTool.Orbit };
    private static Modifiers Mods(JsonElement step) => step.TryGetProperty("modifiers", out var m) && m.ValueKind == JsonValueKind.Object ? new Modifiers(ChartJson.Bool(m, "shift") ?? false, ChartJson.Bool(m, "ctrl") ?? false, ChartJson.Bool(m, "alt") ?? false) : default;
    private static InputEvent Event(JsonElement s)
    {
        double D(string n) => s.GetProperty(n).GetDouble();
        return s.GetProperty("type").GetString() switch
        {
            "pointerdown" => new InputEvent.PointerDown(D("x"), D("y"), (int)D("button"), Mods(s)),
            "pointermove" => new InputEvent.PointerMove(D("x"), D("y"), Mods(s)),
            "pointerup" => new InputEvent.PointerUp(D("x"), D("y"), (int)D("button"), Mods(s)),
            "pointercancel" => new InputEvent.PointerCancel(),
            "wheel" => new InputEvent.Wheel(D("x"), D("y"), D("deltaY"), Mods(s)),
            "dblclick" => new InputEvent.DoubleClick(D("x"), D("y")),
            "keydown" => new InputEvent.KeyDown(s.GetProperty("key").GetString()!),
            "keyup" => new InputEvent.KeyUp(s.GetProperty("key").GetString()!),
            var t => throw new InvalidOperationException($"unknown step {t}"),
        };
    }
    /// <summary>Builds the controller with camera, tool, fixed frame and unit, then declares the setup layers and transforms through a sink on its scene.</summary>
    public object Create(JsonElement setup)
    {
        double width = ChartJson.Num(setup, "width") ?? 800, height = ChartJson.Num(setup, "height") ?? 600;
        var camera = J3.Camera(setup.TryGetProperty("camera", out var cj) ? cj : JsonDocument.Parse("{}").RootElement, width, height);
        var c = new Scene3DController(camera, Tool(ChartJson.Str(setup, "tool")), fixedFrame: ChartJson.Str(setup, "fixedFrame") ?? "map");
        c.Unit = ChartJson.Str(setup, "unit") ?? c.Unit;
        var sink = new SceneLayerSink(c.Scene, env: new LayerJson.LayerEnv(c.Frames, c.FixedFrame));
        if (setup.TryGetProperty("layers", out var layers)) foreach (var l in layers.EnumerateArray()) sink.DeclareLayer(l.GetProperty("id").GetString()!, l.GetProperty("kind").GetString()!, l.TryGetProperty("meta", out var m) ? LayerJson.ToMeta(m.Clone()) : null);
        if (setup.TryGetProperty("transforms", out var tfs)) { sink.DeclareLayer("tf", "frames"); sink.Push("tf", new Dictionary<string, object?> { ["transforms"] = tfs.EnumerateArray().Select(t => new { child = t.GetProperty("child").GetString(), parent = t.GetProperty("parent").GetString(), t = ChartJson.Doubles(t, "t"), q = ChartJson.Doubles(t, "q"), time = ChartJson.Num(t, "time") }).ToList() }); }
        return new State(c, width, height, []);
    }
    /// <summary>Sets the tool or the time, pushes a payload, draws (recording the 3D op count and the HUD ops), answers measure, selection, hover, cursor, tool and camera queries, or feeds any other step as an input event.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var c = s.C; var q = s.Queries;
        switch (step.GetProperty("type").GetString())
        {
            case "setTool": c.SetTool(Tool(ChartJson.Str(step, "tool"))); break;
            case "now": c.Now = step.GetProperty("now").GetDouble(); break;
            case "push": if (c.Scene.Get(step.GetProperty("id").GetString()!) is { } l) LayerJson.ApplyPayload(l, J3.PayloadFromJson(step.GetProperty("payload"))); break;
            case "draw":
                {
                    var p2 = new RecordingPainter(s.Width, s.Height); var p3 = new RecordingPainter3D(s.Width, s.Height);
                    c.Draw3D(p3, s.Width, s.Height, p2);
                    var before = p2.Ops.Count;
                    c.DrawHud(p2, s.Width, s.Height);
                    var hud = new JsonArray(); for (var i = before; i < p2.Ops.Count; i++) hud.Add(p2.Ops[i]!.DeepClone());
                    q.Add(new { ops3d = p3.Ops.Count, hud });
                    break;
                }
            case "query":
                {
                    static object? Hit(HitResult? h) => h is null ? null : new { layerId = h.LayerId, world = J3.RV(h.World), index = h.Index };
                    if (step.TryGetProperty("state", out _)) q.Add(J3.CameraQuery(c.Camera, step));
                    else if (step.TryGetProperty("measure", out _)) { var d = c.MeasureDistance(); q.Add(new { points = c.Measure.Select(J3.RV).ToList(), distance = d is { } dd ? Round.R9(dd) : (double?)null }); }
                    else if (step.TryGetProperty("selection", out _)) q.Add(Hit(c.Selection));
                    else if (step.TryGetProperty("hover", out _)) q.Add(Hit(c.Hover));
                    else if (step.TryGetProperty("cursor", out _)) q.Add(c.Cursor is { } cu ? J3.RV(cu) : null);
                    else if (step.TryGetProperty("tool", out _)) q.Add(c.ActiveTool switch { Scene3DTool.Measure => "measure", Scene3DTool.Select => "select", _ => "orbit" });
                    else q.Add(J3.CameraQuery(c.Camera, step));
                    break;
                }
            default: q.Add(c.Handle(Event(step))); break;
        }
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Mesh resource parsers: base64 STL/GLB in, counts, bounds and buffer hashes out. Mirrors the TS <c>meshFormatsDriver</c>.</summary>
public sealed class MeshFormatsDriver : IFixtureDriver
{
    /// <summary>Handles the <c>meshFormats</c> fixtures.</summary>
    public string Component => "meshFormats";
    /// <summary>No setup; the state is the query list.</summary>
    public object Create(JsonElement setup) => new List<object?>();
    /// <summary>Parses one base64 resource and records vertex and triangle counts, indexing, bounds and buffer hashes, or the parse error message.</summary>
    public object Step(object state, JsonElement step)
    {
        var q = (List<object?>)state;
        if (!step.TryGetProperty("parse", out var pe)) throw new InvalidOperationException($"unknown query {step}");
        try
        {
            var p = MeshFormats.ParseResource(Convert.FromBase64String(pe.GetProperty("data").GetString()!), ChartJson.Str(pe, "name") ?? "");
            var mesh = MeshFormats.ToMesh("fixture", p); var h = mesh.Hash();
            q.Add(new { vertices = p.Positions.Length / 3, triangles = (p.Indices is null ? p.Positions.Length / 3 : p.Indices.Length) / 3, indexed = p.Indices is not null, bounds = J3.RBox(Box3.FromPositions(p.Positions)), hash = new { positions = h.Positions, colors = h.Colors, normals = h.Normals, indices = h.Indices } });
        }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException) { q.Add(new { error = e.Message }); }
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = (List<object?>)state }, Fixtures.Json)!;
}

/// <summary>URDF: links/joints, markers per visual and joint transforms. Mirrors the TS <c>urdfDriver</c>.</summary>
public sealed class UrdfDriver : IFixtureDriver
{
    /// <summary>Handles the <c>urdf</c> fixtures.</summary>
    public string Component => "urdf";
    private sealed record State(RobotModel Model, List<object?> Queries);
    /// <summary>Parses the <c>xml</c> robot description.</summary>
    public object Create(JsonElement setup) => new State(Urdf.Parse(setup.GetProperty("xml").GetString()!), []);
    private static Dictionary<string, double> Positions(JsonElement e) => e.TryGetProperty("positions", out var p) && p.ValueKind == JsonValueKind.Object ? p.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.GetDouble()) : [];
    private static string TypeName(MarkerType t) => t switch { MarkerType.Cube => "cube", MarkerType.Sphere => "sphere", MarkerType.Cylinder => "cylinder", MarkerType.Arrow => "arrow", MarkerType.LineList => "lineList", MarkerType.LineStrip => "lineStrip", MarkerType.Points => "points", MarkerType.Text => "text", _ => "mesh" };
    /// <summary>Answers summary (links, joints, mesh URIs), markers, transforms for given joint positions, and point (a link-local point expressed in the root frame) queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (model, q) = (State)state;
        if (step.TryGetProperty("summary", out _)) q.Add(new { name = model.Name, root = Urdf.RootLink(model), links = model.Links.Select(l => new { name = l.Name, visuals = l.Visuals.Count }).ToList(), joints = model.Joints.Select(j => new { name = j.Name, type = j.Type, parent = j.Parent, child = j.Child, axis = j.Axis, lower = j.Lower, upper = j.Upper }).ToList(), meshes = Urdf.MeshUris(model) });
        else if (step.TryGetProperty("markers", out var mo)) q.Add(Urdf.Markers(model, ChartJson.Str(mo, "prefix") ?? "", ChartJson.Str(mo, "defaultColor") ?? "#9ca3af").Select(m => new { id = m.Id, type = TypeName(m.Type), frame = m.Frame, position = new[] { Round.R9(m.Position.X), Round.R9(m.Position.Y), Round.R9(m.Position.Z) }, orientation = new[] { Round.R9(m.Orientation.X), Round.R9(m.Orientation.Y), Round.R9(m.Orientation.Z), Round.R9(m.Orientation.W) }, scale = new[] { Round.R9(m.Scale.X), Round.R9(m.Scale.Y), Round.R9(m.Scale.Z) }, color = m.Color, opacity = Round.R9(m.Opacity), meshResource = m.MeshResource }).ToList());
        else if (step.TryGetProperty("transforms", out var to)) q.Add(Urdf.Transforms(model, Positions(to), ChartJson.Str(to, "prefix") ?? "", ChartJson.Num(to, "time")).Select(t => new { child = t.Child, parent = t.Parent, t = t.T.Select(Round.R9).ToArray(), q = t.Q.Select(Round.R9).ToArray(), time = t.Time }).ToList());
        else if (step.TryGetProperty("point", out var po))
        {
            var tree = new FrameTree();
            foreach (var t in Urdf.Transforms(model, Positions(po))) tree.Set(t.Child, t.Parent, new Transform3(new Vec3(t.T[0], t.T[1], t.T[2]), new Quat(t.Q[0], t.Q[1], t.Q[2], t.Q[3])));
            var p = ChartJson.Doubles(po, "p")!; var m = tree.Lookup(Urdf.RootLink(model) ?? "", po.GetProperty("link").GetString()!);
            q.Add(m is null ? null : J3.RV(Mat4.Point(m, p[0], p[1], p[2])));
        }
        else throw new InvalidOperationException($"unknown query {step}");
        return state;
    }
    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
