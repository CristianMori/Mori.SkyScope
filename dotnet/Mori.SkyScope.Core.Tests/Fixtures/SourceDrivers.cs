// Mori.SkyScope — Fixture drivers for the signal store and the synthetic source.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the signal store: channel declarations, frame pushes, drop accounting and change notifications.</summary>
public sealed class SignalStoreDriver : IFixtureDriver
{
    /// <summary>Handles the <c>signal-store</c> fixtures.</summary>
    public string Component => "signal-store";
    private sealed record State(SignalStore Store, List<int[]> Notifications, List<object?> Queries);

    /// <summary>Creates the store with the given retention and default capacity and subscribes so that the channel ids of every notification are recorded.</summary>
    public object Create(JsonElement setup)
    {
        var retention = setup.TryGetProperty("retentionSeconds", out var r) ? r.GetDouble() : 60;
        var capacity = setup.TryGetProperty("defaultCapacity", out var c) ? c.GetInt32() : 65536;
        var store = new SignalStore(retention, capacity);
        var s = new State(store, [], []);
        store.Subscribe((ids, _) => s.Notifications.Add([.. ids]));
        return s;
    }

    /// <summary>Declares a channel, pushes a JSON frame, or answers channels and samples queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (store, _, queries) = (State)state;
        switch (step.GetProperty("type").GetString())
        {
            case "declare":
                store.DeclareChannel(new ChannelInfo(step.GetProperty("id").GetInt32(), step.GetProperty("name").GetString()!)
                {
                    Unit = step.TryGetProperty("unit", out var u) ? u.GetString() : null,
                    Rate = step.TryGetProperty("rate", out var ra) ? ra.GetDouble() : null,
                    Timing = step.TryGetProperty("timing", out var t) ? (t.GetString() == "timestamped" ? ChannelTiming.Timestamped : ChannelTiming.Regular) : null,
                    Kind = step.TryGetProperty("kind", out var k) ? Enum.Parse<ChannelKind>(k.GetString()!, true) : null,
                });
                return state;
            case "pushFrame":
                store.PushFrame(FrameJson.FromJson(JsonNode.Parse(step.GetProperty("frame").GetRawText())!));
                return state;
            case "query":
                if (step.TryGetProperty("channels", out _))
                    queries.Add(store.Channels.Values.OrderBy(c => c.Info.Id).Select(c => new
                    {
                        id = c.Info.Id, name = c.Info.Name, unit = c.Info.Unit, kind = c.Info.Kind?.ToString().ToLowerInvariant(),
                        timing = (c.Info.Timing ?? ChannelTiming.Regular) == ChannelTiming.Timestamped ? "timestamped" : "regular", rate = c.Info.Rate,
                        capacity = c.Buffer.Capacity, length = c.Buffer.Length, latestTime = c.Buffer.IsEmpty ? null : (double?)c.Buffer.LatestTime(),
                    }).ToList());
                else if (step.TryGetProperty("samples", out var id))
                {
                    var ch = store.Get(id.GetInt32());
                    queries.Add(ch is null ? new List<double[]>() : SignalSteps.Samples(ch.Buffer));
                }
                else throw new InvalidOperationException($"unknown query {step}");
                return state;
        }
        throw new InvalidOperationException($"unknown step {step}");
    }

    /// <summary>Channel count, drop count and reason, every notification and the query answers.</summary>
    public JsonNode Snapshot(object state)
    {
        var (store, notifications, queries) = (State)state;
        return JsonSerializer.SerializeToNode(new { channelCount = store.Channels.Count, dropped = store.Dropped, lastDropReason = store.LastDropReason, notifications, queries }, Fixtures.Json)!;
    }
}

/// <summary>Fixture driver for the waveform synthesizer.</summary>
public sealed class SynthDriver : IFixtureDriver
{
    /// <summary>Handles the <c>synthetic</c> fixtures.</summary>
    public string Component => "synthetic";
    private sealed record State(List<object?> Queries);

    /// <summary>No setup; the state is the query list.</summary>
    public object Create(JsonElement setup) => new State([]);

    /// <summary>Synthesizes <c>count</c> samples of the <c>spec</c> waveform from <c>tStart</c> and records them.</summary>
    public object Step(object state, JsonElement step)
    {
        if (step.GetProperty("type").GetString() != "synth") throw new InvalidOperationException($"unknown step {step}");
        var sp = step.GetProperty("spec");
        var spec = new SynthSpec(Enum.Parse<Waveform>(sp.GetProperty("waveform").GetString()!, true))
        {
            Frequency = sp.TryGetProperty("frequency", out var f) ? f.GetDouble() : 1,
            Amplitude = sp.TryGetProperty("amplitude", out var a) ? a.GetDouble() : 1,
            Offset = sp.TryGetProperty("offset", out var o) ? o.GetDouble() : 0,
            Phase = sp.TryGetProperty("phase", out var p) ? p.GetDouble() : 0,
            Noise = sp.TryGetProperty("noise", out var n) ? n.GetDouble() : 0,
            Seed = sp.TryGetProperty("seed", out var s) ? s.GetInt32() : 0,
        };
        var start = step.TryGetProperty("startIndex", out var si) ? si.GetInt32() : 0;
        ((State)state).Queries.Add(Synth.Synthesize(spec, step.GetProperty("tStart").GetDouble(), step.GetProperty("dt").GetDouble(), step.GetProperty("count").GetInt32(), start).Select(v => (double)v).ToArray());
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
