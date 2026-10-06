// Mori.SkyScope — Fixture drivers for the robotics layers and the scene controller.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture drivers for the robotics layers and the scene controller. Mirrors <c>fixtures/robot-drivers.ts</c>.</summary>
internal static class RobotJson
{
    private sealed record FakeImage(double Width, double Height) : IImageHandle;

    /// <summary>Parses <c>{x, y, yaw}</c>, defaulting absent fields to 0.</summary>
    public static Pose2D Pose(JsonElement e) => new(ChartJson.Num(e, "x") ?? 0, ChartJson.Num(e, "y") ?? 0, ChartJson.Num(e, "yaw") ?? 0);
    /// <summary>Parses the named fill object, or null when absent.</summary>
    public static Fill? Fill(JsonElement e, string n) => e.TryGetProperty(n, out var f) && f.ValueKind == JsonValueKind.Object ? new Fill(ChartJson.Str(f, "color")!) { Opacity = ChartJson.Num(f, "opacity") ?? 1 } : null;
    /// <summary>Parses the named stroke object, or null when absent.</summary>
    public static Stroke? Stroke(JsonElement e, string n) => e.TryGetProperty(n, out var s) && s.ValueKind == JsonValueKind.Object ? new Stroke(ChartJson.Str(s, "color")!) { Width = ChartJson.Num(s, "width") ?? 1, Opacity = ChartJson.Num(s, "opacity") ?? 1 } : null;
    /// <summary>Parses a laser scan: angle minimum and increment, ranges and range limits.</summary>
    public static LaserScan Scan(JsonElement s) => new(ChartJson.Num(s, "angleMin") ?? 0, ChartJson.Num(s, "angleIncrement") ?? 0, ChartJson.Doubles(s.GetProperty("ranges")), ChartJson.Num(s, "rangeMin") ?? 0, ChartJson.Num(s, "rangeMax") ?? double.PositiveInfinity);
    /// <summary>The optional <c>pose</c> of a scan, or the identity pose.</summary>
    public static Pose2D ScanPose(JsonElement s) => s.TryGetProperty("pose", out var p) ? Pose(p) : default;

    /// <summary>Parses one shape spec (circle, rect, polygon, line, text) with its optional label.</summary>
    public static Shape Shape(JsonElement s)
    {
        var id = s.GetProperty("id").GetString()!; var label = ChartJson.Str(s, "label");
        Shape shape = ChartJson.Str(s, "kind") switch
        {
            "circle" => new Shape.Circle(id, ChartJson.Num(s, "x")!.Value, ChartJson.Num(s, "y")!.Value, ChartJson.Num(s, "r")!.Value, Fill(s, "fill"), Stroke(s, "stroke")),
            "rect" => new Shape.Box(id, ChartJson.Num(s, "x")!.Value, ChartJson.Num(s, "y")!.Value, ChartJson.Num(s, "w")!.Value, ChartJson.Num(s, "h")!.Value, Fill(s, "fill"), Stroke(s, "stroke")),
            "polygon" => new Shape.Polygon(id, ChartJson.Doubles(s.GetProperty("points")), Fill(s, "fill"), Stroke(s, "stroke")),
            "line" => new Shape.Line(id, ChartJson.Doubles(s.GetProperty("points")), Stroke(s, "stroke")),
            "text" => new Shape.Text(id, ChartJson.Num(s, "x")!.Value, ChartJson.Num(s, "y")!.Value, ChartJson.Str(s, "text")!),
            var k => throw new InvalidOperationException($"unknown shape {k}"),
        };
        return label is null ? shape : shape with { Label = label };
    }

    /// <summary>Builds a layer from its JSON spec by <c>kind</c>: bitmap, occupancy grid, point cloud, pose, shapes, trail, grid, polyline or points.</summary>
    public static ILayer Layer(JsonElement spec)
    {
        var id = spec.GetProperty("id").GetString()!;
        switch (ChartJson.Str(spec, "kind"))
        {
            case "bitmap": { var img = spec.GetProperty("image"); var pl = spec.GetProperty("placement"); return new BitmapLayer(id, new FakeImage(ChartJson.Num(img, "width")!.Value, ChartJson.Num(img, "height")!.Value), new RasterPlacement(ChartJson.Num(pl, "originX") ?? 0, ChartJson.Num(pl, "originY") ?? 0, ChartJson.Num(pl, "resolution") ?? 1, ChartJson.Num(pl, "rotation") ?? 0)); }
            case "occupancy":
                {
                    var l = new OccupancyGridLayer(id, (int)ChartJson.Num(spec, "width")!.Value, (int)ChartJson.Num(spec, "height")!.Value, ChartJson.Num(spec, "resolution") ?? 1);
                    if (spec.TryGetProperty("origin", out var o)) l.SetOrigin(Pose(o));
                    if (spec.TryGetProperty("data", out var d)) l.SetData(ChartJson.Doubles(d));
                    l.FreeColor = ChartJson.Str(spec, "freeColor") ?? l.FreeColor; l.OccupiedColor = ChartJson.Str(spec, "occupiedColor") ?? l.OccupiedColor; l.UnknownColor = ChartJson.Str(spec, "unknownColor") ?? l.UnknownColor; l.UnknownOpacity = ChartJson.Num(spec, "unknownOpacity") ?? l.UnknownOpacity;
                    return l;
                }
            case "pointCloud":
                {
                    var l = new PointCloudLayer(id) { Color = ChartJson.Str(spec, "color") ?? "#dc2626", PointSize = ChartJson.Num(spec, "pointSize") ?? 2 };
                    if (spec.TryGetProperty("colormap", out var cm)) { if (cm.ValueKind == JsonValueKind.Array) l.ColormapStops = cm.EnumerateArray().Select(x => x.GetString()!).ToArray(); else l.Colormap = cm.GetString()!; }
                    if (ChartJson.Doubles(spec, "intensityRange") is { Length: 2 } ir) l.IntensityRange = (ir[0], ir[1]);
                    var ints = ChartJson.Doubles(spec, "intensities") ?? [];
                    if (spec.TryGetProperty("scan", out var sc)) l.SetScan(Scan(sc), ScanPose(sc));
                    else l.SetPoints(ChartJson.Doubles(spec, "points") ?? [], ints);
                    return l;
                }
            case "pose":
                {
                    var l = new PoseLayer(id, Pose(spec.GetProperty("pose"))) { Footprint = ChartJson.Doubles(spec, "footprint"), Label = ChartJson.Str(spec, "label") };
                    l.Color = ChartJson.Str(spec, "color") ?? l.Color; l.ArrowSize = ChartJson.Num(spec, "arrowSize") ?? l.ArrowSize; l.FillOpacity = ChartJson.Num(spec, "fillOpacity") ?? l.FillOpacity;
                    return l;
                }
            case "shapes": return new ShapeLayer(id, spec.GetProperty("shapes").EnumerateArray().Select(Shape));
            case "trail": { var t = new TrailLayer(id, (int)(ChartJson.Num(spec, "maxPoints") ?? 2000)); var pts = ChartJson.Doubles(spec, "points") ?? []; for (var i = 0; i < pts.Length; i += 2) t.Append(pts[i], pts[i + 1]); return t; }
            case "grid": return new GridLayer(id);
            case "polyline": return new PolylineLayer(id, ChartJson.Doubles(spec.GetProperty("points")), null, ChartJson.Bool(spec, "closed") ?? false);
            case "points": return new PointsLayer(id, ChartJson.Doubles(spec.GetProperty("points")));
            case var k: throw new InvalidOperationException($"unknown layer kind {k}");
        }
    }

    /// <summary>A hit result as JSON with rounded world point and distance, or null.</summary>
    public static object? Hit(HitResult? h) => h is null ? null : new { layerId = h.LayerId, world = new { x = Round.R9(h.World.X), y = Round.R9(h.World.Y) }, distance = Round.R9(h.Distance), index = h.Index, data = h.Data is double d ? Round.R9(d) : h.Data is int i ? (object)i : h.Data };
    /// <summary>A 2D affine matrix as a rounded <c>{a, b, c, d, e, f}</c> object.</summary>
    public static object Mat(Mat3 m) => new { a = Round.R9(m.A), b = Round.R9(m.B), c = Round.R9(m.C), d = Round.R9(m.D), e = Round.R9(m.E), f = Round.R9(m.F) };
    /// <summary>Parses a <c>{a, b, c, d, e, f}</c> matrix object.</summary>
    public static Mat3 ToMat(JsonElement e) => new(ChartJson.Num(e, "a")!.Value, ChartJson.Num(e, "b")!.Value, ChartJson.Num(e, "c")!.Value, ChartJson.Num(e, "d")!.Value, ChartJson.Num(e, "e")!.Value, ChartJson.Num(e, "f")!.Value);

    /// <summary>Parses an input event object, defaulting coordinates, button and modifiers.</summary>
    public static InputEvent Event(JsonElement e)
    {
        Modifiers Mods() => e.TryGetProperty("modifiers", out var m) && m.ValueKind == JsonValueKind.Object ? new Modifiers(ChartJson.Bool(m, "shift") ?? false, ChartJson.Bool(m, "ctrl") ?? false, ChartJson.Bool(m, "alt") ?? false) : default;
        double X() => ChartJson.Num(e, "x") ?? 0; double Y() => ChartJson.Num(e, "y") ?? 0;
        return ChartJson.Str(e, "type") switch
        {
            "pointerdown" => new InputEvent.PointerDown(X(), Y(), (int)(ChartJson.Num(e, "button") ?? 0), Mods()),
            "pointermove" => new InputEvent.PointerMove(X(), Y(), Mods()),
            "pointerup" => new InputEvent.PointerUp(X(), Y(), (int)(ChartJson.Num(e, "button") ?? 0), Mods()),
            "pointercancel" => new InputEvent.PointerCancel(),
            "wheel" => new InputEvent.Wheel(X(), Y(), ChartJson.Num(e, "deltaY") ?? 0, Mods()),
            "dblclick" => new InputEvent.DoubleClick(X(), Y()),
            "keydown" => new InputEvent.KeyDown(ChartJson.Str(e, "key")!),
            "keyup" => new InputEvent.KeyUp(ChartJson.Str(e, "key")!),
            var t => throw new InvalidOperationException($"unknown event {t}"),
        };
    }

    /// <summary>Builds a 2D camera from JSON, with an optional viewport override.</summary>
    public static Camera2D Camera(JsonElement c, double? w = null, double? h = null) => new(w ?? ChartJson.Num(c, "width") ?? 1, h ?? ChartJson.Num(c, "height") ?? 1, ChartJson.Num(c, "centerX") ?? 0, ChartJson.Num(c, "centerY") ?? 0, ChartJson.Num(c, "zoom") ?? 1, ChartJson.Num(c, "rotation") ?? 0, ChartJson.Bool(c, "flipY") ?? true);
    /// <summary>Maps a tool name to a <c>SceneTool</c>, pan by default.</summary>
    public static SceneTool Tool(string s) => s switch { "boxZoom" => SceneTool.BoxZoom, "measure" => SceneTool.Measure, "select" => SceneTool.Select, _ => SceneTool.Pan };
    /// <summary>Rounds every value to 9 decimals.</summary>
    public static double[] Arr9(IEnumerable<double> a) => a.Select(Round.R9).ToArray();
}

/// <summary>Fixture driver for the robotics layers in a scene: pose, occupancy grid, point cloud, polyline and shape mutations, then bounds, hit-test, image and drawing queries.</summary>
public sealed class RobotLayersDriver : IFixtureDriver
{
    /// <summary>Handles the <c>robot-layers</c> fixtures.</summary>
    public string Component => "robot-layers";
    private sealed record State(Camera2D Camera, global::Mori.SkyScope.Core.Scene.Scene Scene, List<object?> Queries);

    /// <summary>Builds a scene from the <c>layers</c> specs and a camera from <c>camera</c>.</summary>
    public object Create(JsonElement setup)
    {
        var scene = new global::Mori.SkyScope.Core.Scene.Scene();
        if (setup.TryGetProperty("layers", out var layers)) foreach (var l in layers.EnumerateArray()) scene.Add(RobotJson.Layer(l));
        return new State(RobotJson.Camera(setup.GetProperty("camera")), scene, []);
    }

    /// <summary>Mutates one layer by id (setPose, setData, setCell, setOrigin, append, setPoints, upsert, removeShape) or answers bounds, hit, pixel-to-world matrix, decomposition, laser-scan, colour, image hash and pixel, cell, arrow, footprint, point and draw queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (camera, scene, q) = (State)state;
        T Get<T>(string n) where T : class => (T)scene.Get(step.GetProperty(n).GetString()!)!;
        switch (step.GetProperty("type").GetString())
        {
            case "setPose": Get<PoseLayer>("id").SetPose(RobotJson.Pose(step.GetProperty("pose"))); break;
            case "setData": Get<OccupancyGridLayer>("id").SetData(ChartJson.Doubles(step.GetProperty("data"))); break;
            case "setCell": Get<OccupancyGridLayer>("id").Set((int)ChartJson.Num(step, "row")!.Value, (int)ChartJson.Num(step, "col")!.Value, (int)ChartJson.Num(step, "value")!.Value); break;
            case "setOrigin": Get<OccupancyGridLayer>("id").SetOrigin(RobotJson.Pose(step.GetProperty("pose"))); break;
            case "append": Get<PolylineLayer>("id").Append(ChartJson.Num(step, "x")!.Value, ChartJson.Num(step, "y")!.Value); break;
            case "setPoints": Get<PointCloudLayer>("id").SetPoints(ChartJson.Doubles(step.GetProperty("points")), ChartJson.Doubles(step, "intensities") ?? []); break;
            case "upsert": Get<ShapeLayer>("id").Upsert(RobotJson.Shape(step.GetProperty("shape"))); break;
            case "removeShape": q.Add(Get<ShapeLayer>("id").RemoveShape(step.GetProperty("shapeId").GetString()!)); break;
            case "query":
                if (step.TryGetProperty("bounds", out var bid)) q.Add(Round.Rect(scene.Get(bid.GetString()!)!.Bounds()));
                else if (step.TryGetProperty("sceneBounds", out _)) q.Add(Round.Rect(scene.Bounds()));
                else if (step.TryGetProperty("hit", out var hit)) { var xy = ChartJson.Doubles(hit); q.Add(RobotJson.Hit(scene.HitTest(xy[0], xy[1], camera, camera.Width, camera.Height, ChartJson.Num(step, "tolerance") ?? 6))); }
                else if (step.TryGetProperty("pixelToWorld", out var pid)) { var l = scene.Get(pid.GetString()!); q.Add(RobotJson.Mat(l is BitmapLayer b ? b.PixelToWorld() : ((OccupancyGridLayer)l!).PixelToWorld())); }
                else if (step.TryGetProperty("decompose", out var dm)) { var d = RobotGeometry.DecomposeConformal(RobotJson.ToMat(dm)); q.Add(new { tx = Round.R9(d.Tx), ty = Round.R9(d.Ty), rotation = Round.R9(d.Rotation), sx = Round.R9(d.Sx), sy = Round.R9(d.Sy) }); }
                else if (step.TryGetProperty("laserScan", out var sc)) q.Add(RobotJson.Arr9(RobotGeometry.LaserScanToPoints(RobotJson.Scan(sc), RobotJson.ScanPose(sc))));
                else if (step.TryGetProperty("colorOf", out var co)) { var a = co.EnumerateArray().ToArray(); q.Add(((PointCloudLayer)scene.Get(a[0].GetString()!)!).ColorOf(a[1].GetInt32())); }
                else if (step.TryGetProperty("hash", out var hid)) { var img = ((OccupancyGridLayer)scene.Get(hid.GetString()!)!).Image(); q.Add(new { width = img.PixelWidth, height = img.PixelHeight, hash = RecordingPainter.Fnv1a(img.Rgba) }); }
                else if (step.TryGetProperty("pixel", out var px)) { var a = px.EnumerateArray().ToArray(); var img = ((OccupancyGridLayer)scene.Get(a[0].GetString()!)!).Image(); var o = (a[2].GetInt32() * img.PixelWidth + a[1].GetInt32()) * 4; q.Add(new int[] { img.Rgba[o], img.Rgba[o + 1], img.Rgba[o + 2], img.Rgba[o + 3] }); }
                else if (step.TryGetProperty("cellAt", out var ca)) { var a = ca.EnumerateArray().ToArray(); var c = ((OccupancyGridLayer)scene.Get(a[0].GetString()!)!).CellAt(a[1].GetDouble(), a[2].GetDouble()); q.Add(c is null ? null : new { row = c.Value.Row, col = c.Value.Col }); }
                else if (step.TryGetProperty("arrow", out var aid)) q.Add(RobotJson.Arr9(((PoseLayer)scene.Get(aid.GetString()!)!).Arrow(new HitContext(camera, camera.Width, camera.Height, 0))));
                else if (step.TryGetProperty("worldFootprint", out var wf)) { var f = ((PoseLayer)scene.Get(wf.GetString()!)!).WorldFootprint(); q.Add(f is null ? null : RobotJson.Arr9(f)); }
                else if (step.TryGetProperty("pointCount", out var pc)) { var l = scene.Get(pc.GetString()!); q.Add(l is PointCloudLayer p ? p.PointCount : ((PolylineLayer)l!).PointCount); }
                else if (step.TryGetProperty("points", out var pts)) q.Add(RobotJson.Arr9(((PolylineLayer)scene.Get(pts.GetString()!)!).RawPoints()));
                else if (step.TryGetProperty("draw", out _)) { var p = new RecordingPainter(camera.Width, camera.Height); scene.Draw(p, camera, 0); q.Add(p.Ops); }
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the 2D scene controller: input events through its tools, camera and theme options, and HUD state queries.</summary>
public sealed class SceneControllerDriver : IFixtureDriver
{
    /// <summary>Handles the <c>scene-controller</c> fixtures.</summary>
    public string Component => "scene-controller";
    private sealed record State(SceneController C, double W, double H, List<object?> Queries);

    /// <summary>Builds the controller with camera, tool, theme, unit and overlay options, sizes its viewport and adds the layer specs to its scene.</summary>
    public object Create(JsonElement setup)
    {
        double w = ChartJson.Num(setup, "width")!.Value, h = ChartJson.Num(setup, "height")!.Value;
        var o = setup.TryGetProperty("options", out var oo) ? oo : JsonDocument.Parse("{}").RootElement;
        var cam = o.TryGetProperty("camera", out var ce) ? RobotJson.Camera(ce) : new Camera2D();
        var c = new SceneController(cam, ChartJson.Str(o, "tool") is { } t ? RobotJson.Tool(t) : SceneTool.Pan);
        if (o.TryGetProperty("theme", out var th))
        {
            var d = c.Theme;
            c.Theme = d with { Background = ChartJson.Str(th, "background") ?? d.Background, Overlay = ChartJson.Str(th, "overlay") ?? d.Overlay, OverlayText = ChartJson.Str(th, "overlayText") ?? d.OverlayText, Selection = ChartJson.Str(th, "selection") ?? d.Selection, Measure = ChartJson.Str(th, "measure") ?? d.Measure, BoxZoom = ChartJson.Str(th, "boxZoom") ?? d.BoxZoom, FontFamily = ChartJson.Str(th, "fontFamily") ?? d.FontFamily, FontSize = ChartJson.Num(th, "fontSize") ?? d.FontSize };
        }
        c.Unit = ChartJson.Str(o, "unit") ?? c.Unit; c.ShowScaleBar = ChartJson.Bool(o, "showScaleBar") ?? c.ShowScaleBar; c.ShowCursor = ChartJson.Bool(o, "showCursor") ?? c.ShowCursor;
        c.HitTolerance = ChartJson.Num(o, "hitTolerance") ?? c.HitTolerance; c.FitPadding = ChartJson.Num(o, "fitPadding") ?? c.FitPadding;
        c.SetViewport(w, h);
        if (setup.TryGetProperty("layers", out var layers)) foreach (var l in layers.EnumerateArray()) c.Scene.Add(RobotJson.Layer(l));
        return new State(c, w, h, []);
    }

    /// <summary>Feeds an event (recording whether it was handled), switches tools, rotates or fits, and answers camera, tool, measure, selection, hover, cursor, box preview, scale bar and draw queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (c, w, h, q) = (State)state;
        switch (step.GetProperty("type").GetString())
        {
            case "event": q.Add(c.Handle(RobotJson.Event(step.GetProperty("event")))); break;
            case "setTool": c.SetTool(RobotJson.Tool(step.GetProperty("tool").GetString()!)); break;
            case "rotateBy": c.RotateBy(ChartJson.Num(step, "radians")!.Value); break;
            case "fitAll": q.Add(c.FitAll()); break;
            case "query":
                if (step.TryGetProperty("camera", out _)) q.Add(new { centerX = Round.R9(c.Camera.CenterX), centerY = Round.R9(c.Camera.CenterY), zoom = Round.R9(c.Camera.Zoom), rotation = Round.R9(c.Camera.Rotation) });
                else if (step.TryGetProperty("tool", out _)) q.Add(c.ActiveTool switch { SceneTool.BoxZoom => "boxZoom", SceneTool.Measure => "measure", SceneTool.Select => "select", _ => "pan" });
                else if (step.TryGetProperty("measure", out _)) { var d = c.MeasureDistance(); q.Add(new { points = c.Measure.Select(p => new { x = Round.R9(p.X), y = Round.R9(p.Y) }).ToList(), distance = d is null ? null : (double?)Round.R9(d.Value) }); }
                else if (step.TryGetProperty("selection", out _)) q.Add(RobotJson.Hit(c.Selection));
                else if (step.TryGetProperty("hover", out _)) q.Add(RobotJson.Hit(c.Hover));
                else if (step.TryGetProperty("cursor", out _)) q.Add(c.Cursor is { } cu ? new { x = Round.R9(cu.X), y = Round.R9(cu.Y) } : null);
                else if (step.TryGetProperty("boxPreview", out _)) q.Add(Round.Rect(c.BoxPreview));
                else if (step.TryGetProperty("scaleBar", out var sb)) { var r = c.ScaleBar(sb.ValueKind == JsonValueKind.Number ? sb.GetDouble() : 100); q.Add(new { world = Round.R9(r.World), px = Round.R9(r.Px) }); }
                else if (step.TryGetProperty("draw", out _)) { var p = new RecordingPainter(w, h); c.Draw(p, w, h); q.Add(p.Ops); }
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
