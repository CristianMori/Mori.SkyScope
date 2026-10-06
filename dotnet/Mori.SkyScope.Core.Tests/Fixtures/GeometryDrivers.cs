// Mori.SkyScope — Fixture drivers for 2D geometry and the 2D camera, plus the rounding helpers shared by every driver.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Rounding and JSON shape helpers shared by every driver, so answers compare exactly with the TS side.</summary>
internal static class Round
{
    /// <summary>9-decimal rounding shared with the TS drivers; -0 normalised to 0.</summary>
    public static double R9(double x) { var v = Math.Floor(x * 1e9 + 0.5) / 1e9; return v == 0 ? 0 : v; }
    /// <summary>A rect as a rounded <c>{x, y, w, h}</c> object, or null.</summary>
    public static object? Rect(Rect? r) => r is { } q ? new { x = R9(q.X), y = R9(q.Y), w = R9(q.W), h = R9(q.H) } : null;
    /// <summary>A 2D vector as a rounded <c>{x, y}</c> object.</summary>
    public static object Vec(Vec2 v) => new { x = R9(v.X), y = R9(v.Y) };
    /// <summary>A 2D affine matrix as a rounded <c>{a, b, c, d, e, f}</c> object.</summary>
    public static object Mat(Mat3 m) => new { a = R9(m.A), b = R9(m.B), c = R9(m.C), d = R9(m.D), e = R9(m.E), f = R9(m.F) };
    /// <summary>Parses a <c>{x, y, w, h}</c> object.</summary>
    public static Rect ToRect(JsonElement e) => new(e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble(), e.GetProperty("w").GetDouble(), e.GetProperty("h").GetDouble());
    /// <summary>Parses a rect object, or returns null for JSON null.</summary>
    public static Rect? ToRectOrNull(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : ToRect(e);
}

/// <summary>Fixture driver for 2D affine matrices and the geometry helpers: segment distance, point in polygon and rect algebra.</summary>
public sealed class GeometryDriver : IFixtureDriver
{
    /// <summary>Handles the <c>geometry</c> fixtures.</summary>
    public string Component => "geometry";
    private sealed record State(List<object?> Queries);

    private static Mat3 Matrix(JsonElement s)
    {
        if (s.TryGetProperty("translation", out var t)) { var a = SignalSteps.Doubles(t); return Mat3.Translation(a[0], a[1]); }
        if (s.TryGetProperty("scaling", out var sc)) { var a = SignalSteps.Doubles(sc); return Mat3.Scaling(a[0], a[1]); }
        if (s.TryGetProperty("rotation", out var r)) return Mat3.Rotation(r.GetDouble());
        if (s.TryGetProperty("mul", out var m)) { var parts = m.EnumerateArray().ToArray(); return Matrix(parts[0]) * Matrix(parts[1]); }
        return new Mat3(s.GetProperty("a").GetDouble(), s.GetProperty("b").GetDouble(), s.GetProperty("c").GetDouble(), s.GetProperty("d").GetDouble(), s.GetProperty("e").GetDouble(), s.GetProperty("f").GetDouble());
    }

    /// <summary>No setup; the state is just the query list.</summary>
    public object Create(JsonElement setup) => new State([]);

    /// <summary>Evaluates one query: matrix apply, invert or compose, distance to segment, point in polygon, or rect from points, union, normalize and transform.</summary>
    public object Step(object state, JsonElement step)
    {
        var q = ((State)state).Queries;
        if (step.TryGetProperty("apply", out var ap)) { var a = ap.EnumerateArray().ToArray(); q.Add(Round.Vec(Matrix(a[0]).Apply(a[1].GetDouble(), a[2].GetDouble()))); }
        else if (step.TryGetProperty("invert", out var inv)) { var m = Matrix(inv).Invert(); q.Add(m is { } mm ? Round.Mat(mm) : null); }
        else if (step.TryGetProperty("matrix", out var mx)) q.Add(Round.Mat(Matrix(mx)));
        else if (step.TryGetProperty("distToSegment", out var ds)) { var a = SignalSteps.Doubles(ds); q.Add(Round.R9(Geometry.DistToSegment(a[0], a[1], a[2], a[3], a[4], a[5]))); }
        else if (step.TryGetProperty("pointInPolygon", out var pp)) { var a = pp.EnumerateArray().ToArray(); q.Add(Geometry.PointInPolygon(a[0].GetDouble(), a[1].GetDouble(), SignalSteps.Doubles(a[2]))); }
        else if (step.TryGetProperty("rectFromPoints", out var rp)) q.Add(Round.Rect(Geometry.RectFromPoints(SignalSteps.Doubles(rp))));
        else if (step.TryGetProperty("rectUnion", out var ru)) { var a = ru.EnumerateArray().ToArray(); q.Add(Round.Rect(Geometry.RectUnion(Round.ToRectOrNull(a[0]), Round.ToRectOrNull(a[1])))); }
        else if (step.TryGetProperty("rectNormalize", out var rn)) { var a = SignalSteps.Doubles(rn); q.Add(Round.Rect(Geometry.RectNormalize(a[0], a[1], a[2], a[3]))); }
        else if (step.TryGetProperty("transformRect", out var tr)) { var a = tr.EnumerateArray().ToArray(); q.Add(Round.Rect(Geometry.TransformRect(Matrix(a[0]), Round.ToRect(a[1])))); }
        else throw new InvalidOperationException($"unknown query {step}");
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the 2D camera: pan, zoom, fit and viewport commands, then project, unproject and bounds queries.</summary>
public sealed class CameraDriver : IFixtureDriver
{
    /// <summary>Handles the <c>camera</c> fixtures.</summary>
    public string Component => "camera";
    private sealed record State(Camera2D Camera, List<object?> Queries);

    private static double D(JsonElement e, string n, double dflt) => e.TryGetProperty(n, out var v) ? v.GetDouble() : dflt;

    /// <summary>Builds a <c>Camera2D</c> from a JSON object, defaulting every absent field; shared with the scene driver.</summary>
    public static Camera2D MakeCamera(JsonElement s) => new(D(s, "width", 1), D(s, "height", 1), D(s, "centerX", 0), D(s, "centerY", 0), D(s, "zoom", 1), D(s, "rotation", 0),
        !s.TryGetProperty("flipY", out var f) || f.GetBoolean(), D(s, "minZoom", 1e-6), D(s, "maxZoom", 1e6));

    /// <summary>Creates the camera from the setup object.</summary>
    public object Create(JsonElement setup) => new State(MakeCamera(setup), []);

    /// <summary>Applies pan, zoomAt, fitBounds, fitScreenRect, setZoom, setCenter and setViewport, and answers project, unproject, worldBounds and zoom queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (cam, q) = (State)state;
        switch (step.GetProperty("type").GetString())
        {
            case "pan": cam.Pan(D(step, "dx", 0), D(step, "dy", 0)); break;
            case "zoomAt": cam.ZoomAt(D(step, "x", 0), D(step, "y", 0), D(step, "factor", 1)); break;
            case "fitBounds": cam.FitBounds(Round.ToRect(step.GetProperty("rect")), D(step, "padding", 0)); break;
            case "fitScreenRect": cam.FitScreenRect(Round.ToRect(step.GetProperty("rect")), D(step, "padding", 0)); break;
            case "setZoom": cam.SetZoom(D(step, "zoom", 1)); break;
            case "setCenter": cam.SetCenter(D(step, "x", 0), D(step, "y", 0)); break;
            case "setViewport": cam.SetViewport(D(step, "width", 1), D(step, "height", 1)); break;
            case "query":
                if (step.TryGetProperty("project", out var p)) { var a = SignalSteps.Doubles(p); q.Add(Round.Vec(cam.Project(a[0], a[1]))); }
                else if (step.TryGetProperty("unproject", out var u)) { var a = SignalSteps.Doubles(u); q.Add(Round.Vec(cam.Unproject(a[0], a[1]))); }
                else if (step.TryGetProperty("worldBounds", out _)) q.Add(Round.Rect(cam.WorldBounds()));
                else if (step.TryGetProperty("zoom", out _)) q.Add(Round.R9(cam.Zoom));
                else throw new InvalidOperationException($"unknown query {step}");
                break;
            case var t: throw new InvalidOperationException($"unknown step {t}");
        }
        return state;
    }

    /// <summary>Center, zoom, rotation and version of the camera plus the recorded query answers.</summary>
    public JsonNode Snapshot(object state)
    {
        var (cam, q) = (State)state;
        return JsonSerializer.SerializeToNode(new { centerX = Round.R9(cam.CenterX), centerY = Round.R9(cam.CenterY), zoom = Round.R9(cam.Zoom), rotation = Round.R9(cam.Rotation), version = cam.Version, queries = q }, Fixtures.Json)!;
    }
}
