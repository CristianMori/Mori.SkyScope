// Mori.SkyScope — Fixture driver for the clip-space transform used by the GPU line pass.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the clip-space transform used by the GPU line pass: builds it from two scales and a viewport and answers coefficient and point queries.</summary>
public sealed class ClipDriver : IFixtureDriver
{
    /// <summary>Handles the <c>clip-transform</c> fixtures.</summary>
    public string Component => "clip-transform";
    private sealed record State(ClipTransform T, double XOrigin, List<object?> Queries);

    private static Scale Mk(JsonElement s)
    {
        var kind = s.TryGetProperty("kind", out var k) && k.GetString() == "time" ? ScaleKind.Time : ScaleKind.Linear;
        var d = SignalSteps.Doubles(s.GetProperty("domain")); var r = SignalSteps.Doubles(s.GetProperty("range"));
        return Scale.Create(kind, d[0], d[1], r[0], r[1]);
    }

    /// <summary>Builds the transform from the <c>x</c> and <c>y</c> scales, the viewport size and the optional <c>xOrigin</c>.</summary>
    public object Create(JsonElement setup)
    {
        var v = SignalSteps.Doubles(setup.GetProperty("viewport"));
        var origin = setup.TryGetProperty("xOrigin", out var o) ? o.GetDouble() : 0;
        return new State(ClipTransform.From(Mk(setup.GetProperty("x")), Mk(setup.GetProperty("y")), v[0], v[1], origin), origin, []);
    }

    /// <summary>Answers <c>transform</c> (the four coefficients) and <c>apply</c> (one data point mapped to clip space) queries, rounded to 9 decimals.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        if (step.TryGetProperty("transform", out _)) s.Queries.Add(new { sx = Round.R9(s.T.Sx), ox = Round.R9(s.T.Ox), sy = Round.R9(s.T.Sy), oy = Round.R9(s.T.Oy) });
        else if (step.TryGetProperty("apply", out var a)) { var p = SignalSteps.Doubles(a); var (x, y) = s.T.Apply(p[0], p[1], s.XOrigin); s.Queries.Add(new { x = Round.R9(x), y = Round.R9(y) }); }
        else throw new InvalidOperationException($"unknown query {step}");
        return s;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
