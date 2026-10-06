// Mori.SkyScope — Layers described as JSON: the shape a layer sink receives from a source plugin, with the scene and fan-out sinks.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Scene3D;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Scene;

/// <summary>
/// Layers described as JSON — the shape an <see cref="ILayerSink"/> receives from a source plugin (ROS 2, MQTT, a relay).
/// <see cref="CreateLayer"/> builds one from <c>DeclareLayer(id, kind, meta)</c>; <see cref="ApplyPayload"/> feeds
/// <c>Push(id, payload)</c>. Mirrors <c>scene/layer-json.ts</c>.
/// </summary>
public static class LayerJson
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Anonymous object / record → the meta dictionary an <see cref="ILayerSink"/> takes (values are JsonElements).</summary>
    public static IReadOnlyDictionary<string, object?> ToMeta(object meta)
    {
        var el = meta is JsonElement e ? e : JsonSerializer.SerializeToElement(meta, Json);
        var d = new Dictionary<string, object?>();
        foreach (var p in el.EnumerateObject()) d[p.Name] = p.Value;
        return d;
    }

    /// <summary>Any payload object → JsonElement (JsonElements pass through).</summary>
    public static JsonElement ToElement(object? payload) => payload is JsonElement e ? e : JsonSerializer.SerializeToElement(payload, Json);

    private static JsonElement MetaElement(IReadOnlyDictionary<string, object?>? meta)
    {
        if (meta is null) return JsonDocument.Parse("{}").RootElement;
        var d = new Dictionary<string, object?>();
        foreach (var (k, v) in meta) d[k] = v;
        return JsonSerializer.SerializeToElement(d, Json);
    }

    /// <summary>What a sink shares between the layers it creates: the frame tree and the frame the scene is drawn in.</summary>
    /// <param name="Frames">Frame tree 3D layers resolve their frames through; a fresh one per layer when null.</param>
    /// <param name="FixedFrame">Frame the scene is drawn in when the layer meta names none; <c>"map"</c> when null.</param>
    /// <param name="Meshes">Mesh resources shared by marker and mesh layers; a fresh registry per layer when null.</param>
    public sealed record LayerEnv(FrameTree? Frames = null, string? FixedFrame = null, MeshRegistry? Meshes = null);

    /// <summary>Build a layer of <paramref name="kind"/> from its JSON meta (keys mirror the TS layer-json shapes). Returns null for an unknown kind, or for a <c>bitmap</c> whose meta carries no <see cref="IImageHandle"/> under <c>image</c>.</summary>
    public static ILayer? CreateLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null, LayerEnv? env = null)
    {
        var m = MetaElement(meta);
        var frames = env?.Frames ?? new FrameTree(); var fixedFrame = ChartJson.Str(m, "fixedFrame") ?? env?.FixedFrame ?? "map"; var meshes = env?.Meshes ?? new MeshRegistry();
        switch (kind)
        {
            case "grid3d":
                {
                    var g = new Grid3DLayer(id);
                    g.Size = ChartJson.Num(m, "size") ?? g.Size; g.Spacing = ChartJson.Num(m, "spacing") ?? g.Spacing; g.MajorEvery = (int)(ChartJson.Num(m, "majorEvery") ?? g.MajorEvery);
                    g.Color = ChartJson.Str(m, "color") ?? g.Color; g.MajorColor = ChartJson.Str(m, "majorColor") ?? g.MajorColor; g.Z = ChartJson.Num(m, "z") ?? g.Z;
                    return g;
                }
            case "axes":
                {
                    var a = new AxesLayer(id, frames, fixedFrame) { Ids = ChartJson.Strings(m, "ids") };
                    a.Length = ChartJson.Num(m, "length") ?? a.Length; a.LineWidth = ChartJson.Num(m, "lineWidth") ?? a.LineWidth;
                    return a;
                }
            case "pointCloud3d":
                {
                    var l = new PointCloud3DLayer(id) { Frame = ChartJson.Str(m, "frame"), Frames = frames, FixedFrame = fixedFrame };
                    l.Color = ChartJson.Str(m, "color") ?? l.Color; l.PointSize = ChartJson.Num(m, "pointSize") ?? l.PointSize;
                    if (m.TryGetProperty("colormap", out var cm3)) { if (cm3.ValueKind == JsonValueKind.Array) l.ColormapStops = cm3.EnumerateArray().Select(x => x.GetString()!).ToArray(); else l.Colormap = cm3.GetString()!; }
                    if (ChartJson.Doubles(m, "intensityRange") is { Length: 2 } ir3) l.IntensityRange = (ir3[0], ir3[1]);
                    return l;
                }
            case "frames":
                {
                    var l = new FramesLayer(id, frames);
                    if (m.TryGetProperty("transforms", out var tfm) && tfm.ValueKind == JsonValueKind.Array) l.Apply(tfm.EnumerateArray().Select(t => new FrameTransformMessage(t.GetProperty("child").GetString()!, t.GetProperty("parent").GetString()!, ChartJson.Doubles(t, "t") ?? [0, 0, 0], ChartJson.Doubles(t, "q") ?? [0, 0, 0, 1], ChartJson.Num(t, "time"))));
                    return l;
                }
            case "path3d":
                {
                    var l = new Path3DLayer(id) { Frame = ChartJson.Str(m, "frame"), Frames = frames, FixedFrame = fixedFrame };
                    l.Color = ChartJson.Str(m, "color") ?? l.Color; l.LineWidth = ChartJson.Num(m, "lineWidth") ?? l.LineWidth; l.MaxPoints = (int)(ChartJson.Num(m, "maxPoints") ?? l.MaxPoints);
                    return l;
                }
            case "pose3d":
                {
                    var l = new Pose3DLayer(id) { Frame = ChartJson.Str(m, "frame"), Frames = frames, FixedFrame = fixedFrame, Label = ChartJson.Str(m, "label") };
                    l.Color = ChartJson.Str(m, "color") ?? l.Color; l.AxisLength = ChartJson.Num(m, "axisLength") ?? l.AxisLength; l.ShowAxes = ChartJson.Bool(m, "showAxes") ?? l.ShowAxes;
                    if (m.TryGetProperty("pose", out var po)) l.SetPose(Pose3(po));
                    return l;
                }
            case "meshes": return new MeshesLayer(id, meshes);
            case "markers":
                {
                    var l = new MarkerLayer(id) { Frame = ChartJson.Str(m, "frame"), Frames = frames, FixedFrame = fixedFrame, Meshes = meshes }; l.FontSize = ChartJson.Num(m, "fontSize") ?? l.FontSize;
                    if (m.TryGetProperty("markers", out var mks) && mks.ValueKind == JsonValueKind.Array) l.SetMarkers(mks.EnumerateArray().Select(MarkerFrom));
                    return l;
                }
            case "laserScan3d":
                {
                    var l = new LaserScan3DLayer(id); var c = l.Cloud; c.Frame = ChartJson.Str(m, "frame"); c.Frames = frames; c.FixedFrame = fixedFrame;
                    c.Color = ChartJson.Str(m, "color") ?? c.Color; c.PointSize = ChartJson.Num(m, "pointSize") ?? c.PointSize;
                    return l;
                }
            case "occupancyGrid3d":
                {
                    var l = new OccupancyGridPlaneLayer(id, (int)(ChartJson.Num(m, "width") ?? 1), (int)(ChartJson.Num(m, "height") ?? 1), ChartJson.Num(m, "resolution") ?? 1) { Frame = ChartJson.Str(m, "frame"), Frames = frames, FixedFrame = fixedFrame, Z = ChartJson.Num(m, "z") ?? 0 };
                    var g = l.Grid;
                    if (m.TryGetProperty("origin", out var o3)) g.SetOrigin(Pose(o3));
                    if (ChartJson.Doubles(m, "data") is { } data3) g.SetData(data3);
                    g.FreeColor = ChartJson.Str(m, "freeColor") ?? g.FreeColor; g.OccupiedColor = ChartJson.Str(m, "occupiedColor") ?? g.OccupiedColor; g.UnknownColor = ChartJson.Str(m, "unknownColor") ?? g.UnknownColor; g.UnknownOpacity = ChartJson.Num(m, "unknownOpacity") ?? g.UnknownOpacity;
                    return l;
                }
            case "grid": { var g = new GridLayer(id) { Spacing = ChartJson.Num(m, "spacing") }; g.TargetPixels = ChartJson.Num(m, "targetPixels") ?? g.TargetPixels; return g; }
            case "polyline": return new PolylineLayer(id, ChartJson.Doubles(m, "points") ?? [], Stroke(m, "stroke"), ChartJson.Bool(m, "closed") ?? false);
            case "points": return new PointsLayer(id, ChartJson.Doubles(m, "points") ?? [], ChartJson.Num(m, "radius") ?? 4, Fill(m, "fill"), Stroke(m, "stroke"));
            case "bitmap": return meta?.GetValueOrDefault("image") is IImageHandle img && m.TryGetProperty("placement", out var pl) ? new BitmapLayer(id, img, Placement(pl)) : null;
            case "occupancyGrid":
                {
                    var l = new OccupancyGridLayer(id, (int)(ChartJson.Num(m, "width") ?? 1), (int)(ChartJson.Num(m, "height") ?? 1), ChartJson.Num(m, "resolution") ?? 1);
                    if (m.TryGetProperty("origin", out var o)) l.SetOrigin(Pose(o));
                    if (ChartJson.Doubles(m, "data") is { } data) l.SetData(data);
                    l.FreeColor = ChartJson.Str(m, "freeColor") ?? l.FreeColor; l.OccupiedColor = ChartJson.Str(m, "occupiedColor") ?? l.OccupiedColor; l.UnknownColor = ChartJson.Str(m, "unknownColor") ?? l.UnknownColor; l.UnknownOpacity = ChartJson.Num(m, "unknownOpacity") ?? l.UnknownOpacity;
                    return l;
                }
            case "pointCloud":
                {
                    var l = new PointCloudLayer(id) { Color = ChartJson.Str(m, "color") ?? "#dc2626", PointSize = ChartJson.Num(m, "pointSize") ?? 2 };
                    if (m.TryGetProperty("colormap", out var cm)) { if (cm.ValueKind == JsonValueKind.Array) l.ColormapStops = cm.EnumerateArray().Select(x => x.GetString()!).ToArray(); else l.Colormap = cm.GetString()!; }
                    if (ChartJson.Doubles(m, "intensityRange") is { Length: 2 } ir) l.IntensityRange = (ir[0], ir[1]);
                    if (ChartJson.Doubles(m, "points") is { } pts) l.SetPoints(pts, ChartJson.Doubles(m, "intensities") ?? []);
                    return l;
                }
            case "pose":
                {
                    var l = new PoseLayer(id, m.TryGetProperty("pose", out var p) ? Pose(p) : default) { Footprint = ChartJson.Doubles(m, "footprint"), Label = ChartJson.Str(m, "label") };
                    l.Color = ChartJson.Str(m, "color") ?? l.Color; l.ArrowSize = ChartJson.Num(m, "arrowSize") ?? l.ArrowSize; l.FillOpacity = ChartJson.Num(m, "fillOpacity") ?? l.FillOpacity;
                    return l;
                }
            case "shapes": return new ShapeLayer(id, m.TryGetProperty("shapes", out var sh) ? sh.EnumerateArray().Select(Shape) : []);
            case "trail": return new TrailLayer(id, (int)(ChartJson.Num(m, "maxPoints") ?? 2000), Stroke(m, "stroke"));
            default: return null;
        }
    }

    /// <summary>Update a layer from a push payload (same shapes as the TS <c>applyLayerPayload</c>); false when not understood.</summary>
    public static bool ApplyPayload(ILayer layer, object? payload)
    {
        if (layer is PointCloud3DLayer pc3 && LayerMessage.HasBinary(payload)) return ApplyCloud(pc3, (IReadOnlyDictionary<string, object?>)payload!);
        if (layer is MeshesLayer ml) return ApplyMesh(ml, payload);
        var p = ToElement(payload);
        if (p.ValueKind != JsonValueKind.Object) return false;
        var handled = false;
        if (ChartJson.Bool(p, "visible") is { } vis) { layer.Visible = vis; handled = true; }
        if (ChartJson.Num(p, "opacity") is { } op) { layer.Opacity = op; layer.Dirty = true; handled = true; }
        var stamp = ChartJson.Num(p, "stamp");
        if (ChartJson.Str(p, "frame") is { } frameId)
        {
            switch (layer) { case Path3DLayer x: x.Frame = frameId; handled = true; break; case Pose3DLayer x: x.Frame = frameId; handled = true; break; case MarkerLayer x: x.Frame = frameId; handled = true; break; case LaserScan3DLayer x: x.Cloud.Frame = frameId; handled = true; break; case OccupancyGridPlaneLayer x: x.Frame = frameId; handled = true; break; }
        }
        switch (layer)
        {
            case Path3DLayer path:
                if (ChartJson.Doubles(p, "positions") is { } pp3) { path.SetPoints(pp3.Select(v => (float)v).ToArray(), stamp); return true; }
                if (ChartJson.Doubles(p, "append") is { } ap3) { for (var i = 0; i + 2 < ap3.Length; i += 3) path.Append(ap3[i], ap3[i + 1], ap3[i + 2]); return true; }
                if (ChartJson.Bool(p, "clear") == true) { path.Clear(); return true; }
                return handled;
            case Pose3DLayer pose:
                if (p.TryGetProperty("pose", out var pj) || p.TryGetProperty("t", out _)) { pose.SetPose(Pose3(p.TryGetProperty("pose", out pj) ? pj : p), stamp); return true; }
                if (ChartJson.Str(p, "label") is { } lb) { pose.Label = lb; handled = true; }
                return handled;
            case MarkerLayer mk:
                {
                    var now = ChartJson.Num(p, "now") ?? 0;
                    if (p.TryGetProperty("markers", out var ms) && ms.ValueKind == JsonValueKind.Array) { mk.SetMarkers(ms.EnumerateArray().Select(MarkerFrom), now); return true; }
                    if (p.TryGetProperty("upsert", out var up3)) { mk.Upsert(MarkerFrom(up3), now); return true; }
                    if (ChartJson.Str(p, "remove") is { } rm3) { mk.Remove(rm3); return true; }
                    if (ChartJson.Bool(p, "clear") == true) { mk.Clear(); return true; }
                    return handled;
                }
            case LaserScan3DLayer ls:
                if (p.TryGetProperty("scan", out var sc3)) { ls.SetScan(Scan(sc3), stamp); return true; }
                break;
            case OccupancyGridPlaneLayer og3:
                if (ChartJson.Num(p, "z") is { } z3) { og3.Z = z3; og3.MarkDirty(); handled = true; }
                return ApplyPayload(og3.Grid, payload) || handled;
            case FramesLayer fl:
                if (p.TryGetProperty("transforms", out var tfs) && tfs.ValueKind == JsonValueKind.Array)
                {
                    fl.Apply(tfs.EnumerateArray().Select(t => new FrameTransformMessage(t.GetProperty("child").GetString()!, t.GetProperty("parent").GetString()!, ChartJson.Doubles(t, "t") ?? [0, 0, 0], ChartJson.Doubles(t, "q") ?? [0, 0, 0, 1], ChartJson.Num(t, "time"))));
                    return true;
                }
                break;
            case PointCloud3DLayer pc3j:
                if (ChartJson.Str(p, "frame") is { } fr3) { pc3j.Frame = fr3; handled = true; }
                if (ChartJson.Doubles(p, "positions") is { } pos3)
                {
                    pc3j.SetPoints(pos3.Select(v => (float)v).ToArray(), ChartJson.Doubles(p, "intensities")?.Select(v => (float)v).ToArray(), ChartJson.Doubles(p, "colors")?.Select(v => (byte)v).ToArray(), ChartJson.Num(p, "stamp"));
                    return true;
                }
                break;
            case PoseLayer pl when ChartJson.Num(p, "x") is { } x && ChartJson.Num(p, "y") is { } y: pl.SetPose(new Pose2D(x, y, ChartJson.Num(p, "yaw") ?? pl.Pose.Yaw)); return true;
            case PointCloudLayer pc:
                if (p.TryGetProperty("scan", out var sc)) { pc.SetScan(Scan(sc), p.TryGetProperty("pose", out var sp) ? Pose(sp) : default); return true; }
                if (ChartJson.Doubles(p, "points") is { } pts) { pc.SetPoints(pts, ChartJson.Doubles(p, "intensities") ?? []); return true; }
                break;
            case OccupancyGridLayer og:
                if (p.TryGetProperty("origin", out var o)) { og.SetOrigin(Pose(o)); handled = true; }
                if (ChartJson.Doubles(p, "data") is { } data) { og.SetData(data); handled = true; }
                if (p.TryGetProperty("cells", out var cells) && cells.ValueKind == JsonValueKind.Array) { foreach (var c in cells.EnumerateArray()) { var a = ChartJson.Doubles(c); og.Set((int)a[0], (int)a[1], (int)a[2]); } handled = true; }
                return handled;
            case ShapeLayer sl:
                if (p.TryGetProperty("shapes", out var shapes) && shapes.ValueKind == JsonValueKind.Array) { sl.SetShapes(shapes.EnumerateArray().Select(Shape)); return true; }
                if (p.TryGetProperty("upsert", out var up)) { sl.Upsert(Shape(up)); return true; }
                if (ChartJson.Str(p, "remove") is { } rm) { sl.RemoveShape(rm); return true; }
                break;
            case PolylineLayer poly:
                if (ChartJson.Doubles(p, "points") is { } pp) { poly.SetPoints(pp); return true; }
                if (ChartJson.Doubles(p, "append") is { } ap) { for (var i = 0; i + 1 < ap.Length; i += 2) poly.Append(ap[i], ap[i + 1]); return true; }
                break;
            case PointsLayer pts2:
                if (ChartJson.Doubles(p, "points") is { } q) { pts2.SetPoints(q); return true; }
                break;
            case BitmapLayer bm:
                if (p.TryGetProperty("placement", out var pl2)) { bm.SetPlacement(Placement(pl2)); handled = true; }
                break;
        }
        return handled;
    }

    private static bool ApplyMesh(MeshesLayer l, object? payload)
    {
        if (LayerMessage.HasBinary(payload) && payload is IReadOnlyDictionary<string, object?> d)
        {
            var uri = d.GetValueOrDefault("uri") switch { string s => s, JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(), _ => null };
            var pos = Floats(d.GetValueOrDefault("positions"));
            if (uri is null || pos is null) return false;
            var idx = d.GetValueOrDefault("indices") switch { uint[] ua => ua, int[] ia => ia.Select(x => (uint)x).ToArray(), JsonElement { ValueKind: JsonValueKind.Array } e => ChartJson.Doubles(e).Select(x => (uint)x).ToArray(), _ => null };
            l.Register(uri, new ParsedMesh(pos, Floats(d.GetValueOrDefault("normals")), idx));
            return true;
        }
        var p = ToElement(payload);
        if (p.ValueKind != JsonValueKind.Object || ChartJson.Str(p, "uri") is not { } uriJ || ChartJson.Doubles(p, "positions") is not { } pj) return false;
        l.Register(uriJ, new ParsedMesh(pj.Select(x => (float)x).ToArray(), ChartJson.Doubles(p, "normals")?.Select(x => (float)x).ToArray(), ChartJson.Doubles(p, "indices")?.Select(x => (uint)x).ToArray()));
        return true;
    }

    private static float[]? Floats(object? v) => v switch { float[] f => f, double[] d => d.Select(x => (float)x).ToArray(), JsonElement { ValueKind: JsonValueKind.Array } e => ChartJson.Doubles(e).Select(x => (float)x).ToArray(), _ => null };
    private static bool ApplyCloud(PointCloud3DLayer l, IReadOnlyDictionary<string, object?> d)
    {
        var handled = false;
        if (d.GetValueOrDefault("visible") is JsonElement { ValueKind: JsonValueKind.True or JsonValueKind.False } vis) { l.Visible = vis.GetBoolean(); handled = true; }
        if (d.GetValueOrDefault("opacity") is JsonElement { ValueKind: JsonValueKind.Number } op) { l.Opacity = op.GetDouble(); l.Dirty = true; handled = true; }
        if (d.GetValueOrDefault("frame") is JsonElement { ValueKind: JsonValueKind.String } fr) { l.Frame = fr.GetString(); handled = true; }
        else if (d.GetValueOrDefault("frame") is string frs) { l.Frame = frs; handled = true; }
        if (Floats(d.GetValueOrDefault("positions")) is { } pos)
        {
            var colors = d.GetValueOrDefault("colors") switch { byte[] b => b, JsonElement { ValueKind: JsonValueKind.Array } e => ChartJson.Doubles(e).Select(v => (byte)v).ToArray(), _ => null };
            var stamp = d.GetValueOrDefault("stamp") switch { JsonElement { ValueKind: JsonValueKind.Number } e => e.GetDouble(), double x => x, _ => (double?)null };
            l.SetPoints(pos, Floats(d.GetValueOrDefault("intensities")), colors, stamp);
            return true;
        }
        return handled;
    }

    /// <summary><c>{ t: [x, y, z], q: [x, y, z, w] }</c> or <c>{ x, y, z, qx, qy, qz, qw }</c>.</summary>
    public static Pose3D Pose3(JsonElement e)
    {
        if (ChartJson.Doubles(e, "t") is { } t) { var q = ChartJson.Doubles(e, "q") ?? [0, 0, 0, 1]; return new Pose3D(new Vec3(t[0], t[1], t.Length > 2 ? t[2] : 0), new Quat(q[0], q[1], q[2], q[3])); }
        double N(string k, double d) => ChartJson.Num(e, k) ?? d;
        return new Pose3D(new Vec3(N("x", 0), N("y", 0), N("z", 0)), e.TryGetProperty("qw", out _) ? new Quat(N("qx", 0), N("qy", 0), N("qz", 0), N("qw", 1)) : Quat.Identity);
    }
    /// <summary>A <see cref="Marker"/> from its JSON shape: <c>id</c>, <c>type</c>, <c>frame</c>, <c>position</c> [x, y, z], <c>orientation</c> [x, y, z, w], <c>scale</c>, <c>color</c>, <c>opacity</c>, <c>points</c>, <c>colors</c>, <c>text</c>, <c>meshResource</c>, <c>lifetime</c>, <c>stamp</c>. Missing fields take the marker defaults.</summary>
    public static Marker MarkerFrom(JsonElement e)
    {
        var pos = ChartJson.Doubles(e, "position") ?? [0, 0, 0]; var ori = ChartJson.Doubles(e, "orientation") ?? [0, 0, 0, 1]; var sc = ChartJson.Doubles(e, "scale") ?? [1, 1, 1];
        return new Marker(e.GetProperty("id").GetString()!, Marker.ParseType(ChartJson.Str(e, "type") ?? "cube"))
        {
            Frame = ChartJson.Str(e, "frame"), Position = new Vec3(pos[0], pos[1], pos[2]), Orientation = new Quat(ori[0], ori[1], ori[2], ori[3]), Scale = new Vec3(sc[0], sc[1], sc[2]),
            Color = ChartJson.Str(e, "color") ?? "#ffffff", Opacity = ChartJson.Num(e, "opacity") ?? 1,
            Points = ChartJson.Doubles(e, "points")?.Select(v => (float)v).ToArray(), Colors = ChartJson.Doubles(e, "colors")?.Select(v => (byte)v).ToArray(),
            Text = ChartJson.Str(e, "text"), MeshResource = ChartJson.Str(e, "meshResource"), Lifetime = ChartJson.Num(e, "lifetime") ?? 0, Stamp = ChartJson.Num(e, "stamp"),
        };
    }

    /// <summary><c>{ x, y, yaw }</c>; missing fields are zero.</summary>
    public static Pose2D Pose(JsonElement e) => new(ChartJson.Num(e, "x") ?? 0, ChartJson.Num(e, "y") ?? 0, ChartJson.Num(e, "yaw") ?? 0);
    /// <summary><c>{ originX, originY, resolution, rotation }</c>; resolution defaults to 1, the rest to zero.</summary>
    public static RasterPlacement Placement(JsonElement e) => new(ChartJson.Num(e, "originX") ?? 0, ChartJson.Num(e, "originY") ?? 0, ChartJson.Num(e, "resolution") ?? 1, ChartJson.Num(e, "rotation") ?? 0);
    /// <summary><c>{ angleMin, angleIncrement, ranges, rangeMin, rangeMax }</c>; <c>rangeMax</c> defaults to infinity.</summary>
    public static LaserScan Scan(JsonElement s) => new(ChartJson.Num(s, "angleMin") ?? 0, ChartJson.Num(s, "angleIncrement") ?? 0, s.TryGetProperty("ranges", out var r) ? ChartJson.Doubles(r) : [], ChartJson.Num(s, "rangeMin") ?? 0, ChartJson.Num(s, "rangeMax") ?? double.PositiveInfinity);
    /// <summary>A fill from the object at property <paramref name="n"/> (<c>{ color, opacity }</c>), or null when absent.</summary>
    public static Fill? Fill(JsonElement e, string n) => e.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.Object ? new Fill(ChartJson.Str(f, "color") ?? "#000000") { Opacity = ChartJson.Num(f, "opacity") ?? 1 } : null;
    /// <summary>A stroke from the object at property <paramref name="n"/> (<c>{ color, width, opacity, dash }</c>), or null when absent.</summary>
    public static Stroke? Stroke(JsonElement e, string n) => e.TryGetProperty(n, out var s) && s.ValueKind == JsonValueKind.Object ? new Stroke(ChartJson.Str(s, "color") ?? "#000000") { Width = ChartJson.Num(s, "width") ?? 1, Opacity = ChartJson.Num(s, "opacity") ?? 1, Dash = ChartJson.Doubles(s, "dash") } : null;

    /// <summary>A <see cref="global::Mori.SkyScope.Core.Scene.Shape"/> from <c>{ id, kind, label, ... }</c> where <c>kind</c> is <c>circle</c>, <c>rect</c>, <c>polygon</c>, <c>line</c> or <c>text</c>. Throws for an unknown kind.</summary>
    public static Shape Shape(JsonElement s)
    {
        var id = s.GetProperty("id").GetString()!; var label = ChartJson.Str(s, "label");
        Shape shape = ChartJson.Str(s, "kind") switch
        {
            "circle" => new Shape.Circle(id, ChartJson.Num(s, "x") ?? 0, ChartJson.Num(s, "y") ?? 0, ChartJson.Num(s, "r") ?? 1, Fill(s, "fill"), Stroke(s, "stroke")),
            "rect" => new Shape.Box(id, ChartJson.Num(s, "x") ?? 0, ChartJson.Num(s, "y") ?? 0, ChartJson.Num(s, "w") ?? 1, ChartJson.Num(s, "h") ?? 1, Fill(s, "fill"), Stroke(s, "stroke")),
            "polygon" => new Shape.Polygon(id, ChartJson.Doubles(s, "points") ?? [], Fill(s, "fill"), Stroke(s, "stroke")),
            "line" => new Shape.Line(id, ChartJson.Doubles(s, "points") ?? [], Stroke(s, "stroke")),
            "text" => new Shape.Text(id, ChartJson.Num(s, "x") ?? 0, ChartJson.Num(s, "y") ?? 0, ChartJson.Str(s, "text") ?? ""),
            var k => throw new InvalidOperationException($"unknown shape kind {k}"),
        };
        return label is null ? shape : shape with { Label = label };
    }
}

/// <summary>An <see cref="ILayerSink"/> that maintains a <see cref="Scene"/>: declarations create or replace layers, pushes update them. Optionally marshals onto a UI thread and reports changes.</summary>
public sealed class SceneLayerSink(Scene scene, Action<Action>? dispatch = null, Action? changed = null, LayerJson.LayerEnv? env = null) : ILayerSink
{
    /// <summary>The scene this sink maintains.</summary>
    public Scene Scene { get; } = scene;
    /// <summary>Shared by every 3D layer this sink creates.</summary>
    public FrameTree Frames { get; } = env?.Frames ?? new FrameTree();
    /// <summary>Mesh resources shared by every marker and mesh layer this sink creates.</summary>
    public MeshRegistry Meshes { get; } = env?.Meshes ?? new MeshRegistry();
    /// <summary>Frame 3D layers are drawn in when their meta names none; <c>"map"</c> by default.</summary>
    public string FixedFrame { get; set; } = env?.FixedFrame ?? "map";
    /// <summary>Layer kinds that could not be created, for diagnostics.</summary>
    public HashSet<string> Unknown { get; } = [];
    private readonly HashSet<string> _declared = [];

    /// <summary>Removes the layers this sink created (a host's own layers, e.g. a grid, stay).</summary>
    public void Reset()
    {
        void Apply() { foreach (var id in _declared) Scene.Remove(id); _declared.Clear(); Frames.Clear(); changed?.Invoke(); }
        if (dispatch is null) Apply(); else dispatch(Apply);
    }

    /// <summary>Create the layer (replacing any with the same id) and add it to the scene; unknown kinds are recorded in <see cref="Unknown"/>. Runs through the dispatcher when one was given.</summary>
    public void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null)
    {
        void Apply()
        {
            var layer = LayerJson.CreateLayer(id, kind, meta, new LayerJson.LayerEnv(Frames, FixedFrame, Meshes));
            Scene.Remove(id);
            if (layer is null) { Unknown.Add(kind); return; }
            Scene.Add(layer); _declared.Add(id);
            changed?.Invoke();
        }
        if (dispatch is null) Apply(); else dispatch(Apply);
    }

    /// <summary>Apply a payload to the layer with this id via <see cref="LayerJson.ApplyPayload"/>; silently ignored for unknown ids. Runs through the dispatcher when one was given.</summary>
    public void Push(string id, object? payload)
    {
        var el = LayerMessage.HasBinary(payload) ? payload : LayerJson.ToElement(payload);
        void Apply() { if (Scene.Get(id) is { } layer && LayerJson.ApplyPayload(layer, el)) changed?.Invoke(); }
        if (dispatch is null) Apply(); else dispatch(Apply);
    }
}

/// <summary>Fans layer messages out to several sinks; late sinks receive every declaration seen so far. Mirrors the TS <c>FanoutLayerSink</c>.</summary>
public sealed class FanoutLayerSink : ILayerSink
{
    private readonly List<ILayerSink> _sinks = [];
    private readonly Dictionary<string, (string Kind, IReadOnlyDictionary<string, object?>? Meta)> _declared = [];
    private readonly object _lock = new();
    /// <summary>Attach a sink, first replaying every declaration seen so far. Thread-safe.</summary>
    /// <returns>An action that detaches the sink.</returns>
    public Action Add(ILayerSink sink)
    {
        lock (_lock) { foreach (var (id, d) in _declared) sink.DeclareLayer(id, d.Kind, d.Meta); _sinks.Add(sink); }
        return () => { lock (_lock) _sinks.Remove(sink); };
    }
    /// <summary>Remember the declaration for late sinks and forward it to every attached sink.</summary>
    public void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null)
    {
        ILayerSink[] sinks;
        lock (_lock) { _declared[id] = (kind, meta); sinks = _sinks.ToArray(); }
        foreach (var s in sinks) s.DeclareLayer(id, kind, meta);
    }
    /// <summary>Forward a push to every attached sink; pushes are not replayed to late sinks.</summary>
    public void Push(string id, object? payload) { ILayerSink[] sinks; lock (_lock) sinks = _sinks.ToArray(); foreach (var s in sinks) s.Push(id, payload); }
    /// <summary>Forget the remembered declarations and reset every attached sink.</summary>
    public void Reset() { ILayerSink[] sinks; lock (_lock) { _declared.Clear(); sinks = _sinks.ToArray(); } foreach (var s in sinks) s.Reset(); }
}
