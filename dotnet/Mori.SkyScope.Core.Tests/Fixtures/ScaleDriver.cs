// Mori.SkyScope — Fixture driver for scales, ticks and number/time formatting.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for linear, log and time scales: mapping, inversion, ticks, tick spec, formatting and nice domains.</summary>
public sealed class ScaleDriver : IFixtureDriver
{
    /// <summary>Handles the <c>scales</c> fixtures.</summary>
    public string Component => "scales";
    private sealed record State(Scale Scale, List<object?> Queries);

    /// <summary>Creates the scale from <c>kind</c>, <c>domain</c>, <c>range</c> and the optional time <c>mode</c>.</summary>
    public object Create(JsonElement setup)
    {
        var d = SignalSteps.Doubles(setup.GetProperty("domain"));
        var r = SignalSteps.Doubles(setup.GetProperty("range"));
        var kind = setup.GetProperty("kind").GetString() switch { "linear" => ScaleKind.Linear, "log" => ScaleKind.Log, "time" => ScaleKind.Time, var k => throw new InvalidOperationException($"unknown kind {k}") };
        var mode = setup.TryGetProperty("mode", out var m) && m.GetString() == "relative" ? TimeFormat.Relative : TimeFormat.Utc;
        return new State(Scale.Create(kind, d[0], d[1], r[0], r[1], mode), []);
    }

    /// <summary>Query-only: <c>scale</c>, <c>invert</c>, <c>ticks</c>, <c>tickSpec</c>, <c>format</c> and <c>nice</c>.</summary>
    public object Step(object state, JsonElement step)
    {
        var (scale, q) = (State)state;
        if (step.GetProperty("type").GetString() != "query") throw new InvalidOperationException($"unknown step {step}");
        if (step.TryGetProperty("scale", out var a)) q.Add(scale.Apply(a.GetDouble()));
        else if (step.TryGetProperty("invert", out var b)) q.Add(scale.Invert(b.GetDouble()));
        else if (step.TryGetProperty("ticks", out var c)) q.Add(scale.TickValues(c.GetInt32()));
        else if (step.TryGetProperty("tickSpec", out var d)) { var s = scale.Spec(d.GetInt32()); q.Add(new { step = s.Step, inv = s.Inv, decimals = s.Decimals }); }
        else if (step.TryGetProperty("format", out var f)) { var p = SignalSteps.Doubles(f); q.Add(scale.Format(p[0], (int)p[1])); }
        else if (step.TryGetProperty("nice", out var n)) { var ns = scale.Nice(n.GetInt32()); q.Add(new[] { ns.D0, ns.D1 }); }
        else throw new InvalidOperationException($"unknown query {step}");
        return state;
    }

    /// <summary>Kind, domain and range of the scale plus the recorded query answers.</summary>
    public JsonNode Snapshot(object state)
    {
        var (scale, q) = (State)state;
        var kind = scale.Kind switch { ScaleKind.Linear => "linear", ScaleKind.Log => "log", _ => "time" };
        return JsonSerializer.SerializeToNode(new { kind, domain = new[] { scale.D0, scale.D1 }, range = new[] { scale.R0, scale.R1 }, queries = q }, Fixtures.Json)!;
    }
}
