// Mori.SkyScope — Fixture drivers for playback and the MQTT mapping.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture drivers for playback and the MQTT mapping. Mirrors <c>fixtures/playback-drivers.ts</c>.</summary>
public sealed class PlaybackDriver : IFixtureDriver
{
    /// <summary>Handles the <c>playback</c> fixtures.</summary>
    public string Component => "playback";
    private sealed record State(Recording Rec, SignalStore Store, PlaybackSource Src, List<object?> Queries);

    /// <summary>Parses the <c>csv</c> text into a recording and starts a playback source over a fresh store, unless <c>start</c> is false.</summary>
    public object Create(JsonElement setup)
    {
        var o = setup.TryGetProperty("csvOptions", out var co) ? co : JsonDocument.Parse("{}").RootElement;
        var opts = new CsvOptions
        {
            Delimiter = ChartJson.Str(o, "delimiter"), Rate = ChartJson.Num(o, "rate"), ChunkRows = (int)(ChartJson.Num(o, "chunkRows") ?? 256), FirstChannelId = (int)(ChartJson.Num(o, "firstChannelId") ?? 1), TimeScale = ChartJson.Num(o, "timeScale") ?? 1,
            TimeColumn = o.TryGetProperty("timeColumn", out var tc) && tc.ValueKind == JsonValueKind.String ? tc.GetString() : null,
            TimeColumnIndex = o.TryGetProperty("timeColumn", out var ti) && ti.ValueKind == JsonValueKind.Number ? ti.GetInt32() : null,
        };
        var rec = CsvRecording.Parse(setup.GetProperty("csv").GetString()!, opts);
        var store = new SignalStore(retentionSeconds: ChartJson.Num(setup, "retentionSeconds") ?? 60);
        var src = new PlaybackSource();
        var ctx = new SourceContext(store, new NullLayerSink(), src.Clock, (_, _) => { });
        if (ChartJson.Bool(setup, "start") != false) src.StartAsync(ctx, new PlaybackConfig(rec) { Speed = ChartJson.Num(setup, "speed") ?? 1, Loop = ChartJson.Bool(setup, "loop") ?? false, Autoplay = ChartJson.Bool(setup, "autoplay") ?? false }).GetAwaiter().GetResult();
        return new State(rec, store, src, []);
    }

    private static object Summary(Recording r) => new
    {
        channels = r.Channels.Select(c => new { id = c.Id, name = c.Name, timing = c.Timing switch { ChannelTiming.Timestamped => "timestamped", ChannelTiming.Regular => "regular", _ => null }, rate = c.Rate }).ToList(),
        frames = r.Frames.Select(f => new { seq = f.Seq, t0 = Round.R9(f.T0), channels = f.Channels.Select(c => new { id = (int)c.Id, encoding = c.Encoding switch { FrameEncoding.Timestamped => "timestamped", FrameEncoding.Regular => "regular", _ => "quantized" }, count = c.Count, first = Round.R9(c.Encoding == FrameEncoding.Quantized ? c.Q[0] : c.Values[0]), t = Round.R9(c.Encoding == FrameEncoding.Timestamped ? c.Times[0] : c.TStart) }).ToList() }).ToList(),
        start = Round.R9(r.Start), end = Round.R9(r.End),
    };

    /// <summary>Applies play, pause, tick, seek and setSpeed, and answers recording summary, position, buffer and clock queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var (rec, store, src, q) = (State)state;
        switch (step.GetProperty("type").GetString())
        {
            case "play": src.Play(); break;
            case "pause": src.Pause(); break;
            case "tick": q.Add(src.Tick(ChartJson.Num(step, "dt")!.Value)); break;
            case "seek": src.Seek(ChartJson.Num(step, "t")!.Value); break;
            case "setSpeed": src.Speed = ChartJson.Num(step, "speed")!.Value; break;
            case "query":
                if (step.TryGetProperty("recording", out _)) q.Add(Summary(rec));
                else if (step.TryGetProperty("position", out _)) q.Add(new { position = Round.R9(src.Position), progress = Round.R9(src.Progress), playing = src.Playing, finished = src.Finished, framesPushed = src.FramesPushed, duration = Round.R9(src.Duration) });
                else if (step.TryGetProperty("buffer", out var bid)) { var b = store.Get(bid.GetInt32())?.Buffer; q.Add(b is null ? null : new { length = b.Length, headSeq = b.HeadSeq, latestTime = b.IsEmpty ? null : (double?)Round.R9(b.LatestTime()), latest = b.IsEmpty ? null : (double?)Round.R9(b.ValueAt(b.HeadSeq - 1)) }); }
                else if (step.TryGetProperty("clock", out _)) q.Add(Round.R9(src.Clock.Now()));
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>Fixture driver for the MQTT mapping: topic matching, JSON path extraction, payload-to-sample mapping, channel declarations and frame batching.</summary>
public sealed class MqttMappingDriver : IFixtureDriver
{
    /// <summary>Handles the <c>mqtt-mapping</c> fixtures.</summary>
    public string Component => "mqtt-mapping";
    private sealed class State(List<MqttRule> rules) { public List<MqttRule> Rules = rules; public uint Seq; public List<object?> Queries = []; }

    /// <summary>Parses the <c>rules</c> list into <c>MqttRule</c>s.</summary>
    public object Create(JsonElement setup)
    {
        var rules = new List<MqttRule>();
        if (setup.TryGetProperty("rules", out var rs))
            foreach (var r in rs.EnumerateArray())
                rules.Add(new MqttRule(r.GetProperty("topic").GetString()!, r.GetProperty("channelId").GetInt32()) { Name = ChartJson.Str(r, "name"), Unit = ChartJson.Str(r, "unit"), Path = ChartJson.Str(r, "path"), TimePath = ChartJson.Str(r, "timePath"), TimeScale = ChartJson.Num(r, "timeScale") ?? 1, Scale = ChartJson.Num(r, "scale") ?? 1, Offset = ChartJson.Num(r, "offset") ?? 0 });
        return new State(rules);
    }

    private static object? Plain(JsonElement? e) => e is null ? null : e.Value.ValueKind switch
    {
        JsonValueKind.Number => e.Value.GetDouble(), JsonValueKind.String => e.Value.GetString(), JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null, _ => e.Value.Clone(),
    };

    /// <summary>Query-only: <c>matches</c>, <c>jsonPath</c>, <c>message</c>, <c>channels</c> and <c>frame</c>.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var q = s.Queries;
        if (step.GetProperty("type").GetString() != "query") throw new InvalidOperationException("unknown step " + step);
        if (step.TryGetProperty("matches", out var m)) { var a = m.EnumerateArray().ToArray(); q.Add(MqttMapping.TopicMatches(a[0].GetString()!, a[1].GetString()!)); }
        else if (step.TryGetProperty("jsonPath", out var jp)) q.Add(Plain(MqttMapping.JsonPath(jp.GetProperty("value"), jp.GetProperty("path").GetString()!)));
        else if (step.TryGetProperty("message", out var msg)) q.Add(MqttMapping.Map(s.Rules, msg.GetProperty("topic").GetString()!, msg.GetProperty("payload").GetString()!, ChartJson.Num(msg, "receivedAt") ?? 0).Select(x => new { channelId = x.ChannelId, t = Round.R9(x.T), value = Round.R9(x.Value) }).ToList());
        else if (step.TryGetProperty("channels", out _)) q.Add(MqttMapping.Channels(s.Rules).Select(c => new { id = c.Id, name = c.Name, unit = c.Unit, timing = "timestamped" }).ToList());
        else if (step.TryGetProperty("frame", out var fr))
        {
            var samples = fr.EnumerateArray().Select(x => new MqttSample(x.GetProperty("channelId").GetInt32(), x.GetProperty("t").GetDouble(), x.GetProperty("value").GetDouble())).ToList();
            var f = MqttMapping.ToFrame(s.Seq++, samples);
            q.Add(f is null ? null : new { seq = f.Seq, t0 = Round.R9(f.T0), channels = f.Channels.Select(c => new { id = (int)c.Id, times = c.Times.Select(Round.R9).ToArray(), values = c.Values.Select(v => Round.R9(v)).ToArray() }).ToList() });
        }
        else throw new InvalidOperationException("unknown query " + step);
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
