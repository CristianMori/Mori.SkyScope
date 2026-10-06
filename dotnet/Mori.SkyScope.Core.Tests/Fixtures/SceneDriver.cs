// Mori.SkyScope — Fixture driver for the 2D scene and its layers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the 2D scene: layer visibility, order and removal, camera pans, and hit-test, bounds, order and incremental draw queries.</summary>
public sealed class SceneDriver : IFixtureDriver
{
    /// <summary>Handles the <c>scene</c> fixtures.</summary>
    public string Component => "scene";

    private sealed class State(Camera2D camera, global::Mori.SkyScope.Core.Scene.Scene scene, RecordingPainter painter)
    {
        public Camera2D Camera { get; } = camera;
        public global::Mori.SkyScope.Core.Scene.Scene Scene { get; } = scene;
        public RecordingPainter Painter { get; } = painter;
        public int Seen { get; set; }
        public List<object?> Queries { get; } = [];
    }

    private static ILayer MakeLayer(JsonElement l)
    {
        var id = l.GetProperty("id").GetString()!;
        var pts = SignalSteps.Doubles(l.GetProperty("points"));
        return l.GetProperty("kind").GetString() switch
        {
            "polyline" => new PolylineLayer(id, pts, null, l.TryGetProperty("closed", out var c) && c.GetBoolean()),
            "points" => new PointsLayer(id, pts, l.TryGetProperty("radius", out var r) ? r.GetDouble() : 4),
            var k => throw new InvalidOperationException($"unknown layer kind {k}"),
        };
    }

    private static string OpName(JsonNode? o) => o!["op"]!.GetValue<string>() == "layerDraw"
        ? $"layerDraw({string.Join(",", o["ops"]!.AsArray().Select(x => x!["op"]!.GetValue<string>()))})"
        : o["op"]!.GetValue<string>();

    /// <summary>Builds the camera, the polyline and points layers, and a recording painter of the viewport size.</summary>
    public object Create(JsonElement setup)
    {
        var cam = CameraDriver.MakeCamera(setup.GetProperty("camera"));
        var scene = new global::Mori.SkyScope.Core.Scene.Scene();
        foreach (var l in setup.GetProperty("layers").EnumerateArray()) scene.Add(MakeLayer(l));
        return new State(cam, scene, new RecordingPainter(cam.Width, cam.Height));
    }

    /// <summary>Applies setVisible, move, remove, markDirty and pan, and answers hit, bounds, order and draw queries; draw records only the op names since the previous draw, which makes layer caching observable.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        switch (step.GetProperty("type").GetString())
        {
            case "setVisible": s.Scene.Get(step.GetProperty("id").GetString()!)!.Visible = step.GetProperty("visible").GetBoolean(); break;
            case "move": s.Scene.Move(step.GetProperty("id").GetString()!, step.GetProperty("index").GetInt32()); break;
            case "remove": s.Scene.Remove(step.GetProperty("id").GetString()!); break;
            case "markDirty": s.Scene.Get(step.GetProperty("id").GetString()!)!.Dirty = true; break;
            case "pan": s.Camera.Pan(step.GetProperty("dx").GetDouble(), step.GetProperty("dy").GetDouble()); break;
            case "query":
                if (step.TryGetProperty("hit", out var h))
                {
                    var a = SignalSteps.Doubles(h);
                    var tol = step.TryGetProperty("tolerance", out var t) ? t.GetDouble() : 6;
                    var r = s.Scene.HitTest(a[0], a[1], s.Camera, s.Camera.Width, s.Camera.Height, tol);
                    s.Queries.Add(r is null ? null : new
                    {
                        layerId = r.LayerId, world = new { x = Round.R9(r.World.X), y = Round.R9(r.World.Y), z = Round.R9(r.World.Z) },
                        screen = Round.Vec(r.Screen), distance = Round.R9(r.Distance), index = r.Index,
                    });
                }
                else if (step.TryGetProperty("bounds", out _)) s.Queries.Add(Round.Rect(s.Scene.Bounds()));
                else if (step.TryGetProperty("order", out _)) s.Queries.Add(s.Scene.Order());
                else if (step.TryGetProperty("draw", out _))
                {
                    s.Scene.Draw(s.Painter, s.Camera, 0);
                    s.Queries.Add(s.Painter.Ops.Skip(s.Seen).Select(OpName).ToList());
                    s.Seen = s.Painter.Ops.Count;
                }
                else throw new InvalidOperationException($"unknown query {step}");
                break;
            case var t: throw new InvalidOperationException($"unknown step {t}");
        }
        return s;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
