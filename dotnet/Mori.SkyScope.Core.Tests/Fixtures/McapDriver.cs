// Mori.SkyScope — Fixture driver for MCAP recording and playback.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Mcap;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for MCAP recording and playback. Mirrors <c>fixtures/mcap-driver.ts</c>.</summary>
public sealed class McapDriver : IFixtureDriver
{
    /// <summary>Handles the <c>mcap</c> fixtures.</summary>
    public string Component => "mcap";

    /// <summary>Records layer calls as strings so both cores can compare them.</summary>
    private sealed class LayerLog : ILayerSink
    {
        public readonly List<string> Log = [];
        public void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null) => Log.Add($"declare:{id}:{kind}");
        public void Push(string id, object? payload) => Log.Add($"push:{id}:{JsonSerializer.Serialize(LayerJson.ToElement(payload), SkyScopeMcap.Json)}");
        public void Reset() => Log.Add("reset");
    }

    private sealed class Play(PlaybackSource src, SignalStore store, LayerLog layers) { public PlaybackSource Src = src; public SignalStore Store = store; public LayerLog Layers = layers; }
    private sealed class State(ManualClock clock, SignalStore store, LayerLog layers, McapRecorder rec)
    {
        public ManualClock Clock = clock; public SignalStore Store = store; public LayerLog Layers = layers; public McapRecorder Rec = rec;
        public byte[]? Bytes; public Play? Play; public List<object?> Queries = [];
    }

    /// <summary>Builds a recorder over a manual clock, a signal store and a layer log, optionally seeded with a base64 recording to read back.</summary>
    public object Create(JsonElement setup)
    {
        var clock = new ManualClock(ChartJson.Num(setup, "time") ?? 0); var store = new SignalStore(retentionSeconds: 60); var layers = new LayerLog();
        var rec = new McapRecorder(store, layers, clock, new McapRecorderOptions { MaxBytes = (long)(ChartJson.Num(setup, "maxBytes") ?? 0) });
        return new State(clock, store, layers, rec) { Bytes = ChartJson.Str(setup, "base64") is { } b64 ? Convert.FromBase64String(b64) : null };
    }

    private static SkyScopeFrame Frame(JsonElement f) => new((uint)ChartJson.Num(f, "seq")!.Value, ChartJson.Num(f, "t0")!.Value,
        f.GetProperty("channels").EnumerateArray().Select(c => FrameChannel.Regular((ushort)ChartJson.Num(c, "id")!.Value, ChartJson.Num(c, "tStart")!.Value, ChartJson.Num(c, "dt")!.Value, ChartJson.Doubles(c.GetProperty("values")).Select(v => (float)v).ToArray())).ToList());

    /// <summary>Drives the recorder (clock, start, stop, channel and layer calls), loads the recording into a playback source (<c>load</c>, <c>tick</c>, <c>seek</c>) and answers byte-hash, stats, raw file, decoded recording, LZ4, read-error, layer-log and playback-state queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var rec = s.Rec; var q = s.Queries;
        switch (step.GetProperty("type").GetString())
        {
            case "clock": s.Clock.Set(ChartJson.Num(step, "t")!.Value); break;
            case "start": rec.Start(); break;
            case "stop": s.Bytes = rec.Stop(); break;
            case "declareChannel": rec.DeclareChannel(ChannelCatalog.Parse(step.GetProperty("info"))); break;
            case "pushFrame": rec.PushFrame(Frame(step.GetProperty("frame"))); break;
            case "declareLayer": rec.DeclareLayer(step.GetProperty("id").GetString()!, step.GetProperty("kind").GetString()!, step.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object ? LayerJson.ToMeta(m.Clone()) : null); break;
            case "push": rec.Push(step.GetProperty("id").GetString()!, step.TryGetProperty("payload", out var p) ? p.Clone() : null); break;
            case "reset": rec.Reset(); break;
            case "load":
                {
                    var recording = SkyScopeMcap.ReadRecording(s.Bytes!);
                    var store = new SignalStore(retentionSeconds: 60); var layers = new LayerLog(); var src = new PlaybackSource();
                    src.StartAsync(new SourceContext(store, layers, src.Clock, (_, _) => { }), new PlaybackConfig(recording) { Autoplay = true, Loop = ChartJson.Bool(step, "loop") ?? false }).GetAwaiter().GetResult();
                    s.Play = new Play(src, store, layers);
                    break;
                }
            case "tick": q.Add(s.Play!.Src.Tick(ChartJson.Num(step, "dt")!.Value)); break;
            case "seek": s.Play!.Src.Seek(ChartJson.Num(step, "t")!.Value); break;
            case "query":
                if (step.TryGetProperty("bytes", out _)) { var b = s.Bytes ?? rec.LastFile; q.Add(b is null ? null : new { length = b.Length, hash = RecordingPainter.Fnv1a(b) }); }
                else if (step.TryGetProperty("stats", out _)) { var st = rec.Stats(); q.Add(new { recording = st.Recording, messages = st.Messages, started = st.Started is { } v ? (double?)Round.R9(v) : null }); }
                else if (step.TryGetProperty("file", out _))
                {
                    var f = McapReader.Read(s.Bytes!);
                    var byTopic = new Dictionary<string, int>();
                    foreach (var msg in f.Messages) { var t = f.Channels[msg.ChannelId].Topic; byTopic[t] = byTopic.GetValueOrDefault(t) + 1; }
                    q.Add(new
                    {
                        profile = f.Profile, library = f.Library,
                        schemas = f.Schemas.Values.OrderBy(x => x.Id).Select(x => new { id = (int)x.Id, name = x.Name, encoding = x.Encoding }).ToList(),
                        channels = f.Channels.Values.OrderBy(c => c.Id).Select(c => new { id = (int)c.Id, schemaId = (int)c.SchemaId, topic = c.Topic, messageEncoding = c.MessageEncoding, metadata = c.Metadata }).ToList(),
                        messages = f.Messages.Count, byTopic, start = Round.R9(f.MessageStart), end = Round.R9(f.MessageEnd),
                    });
                }
                else if (step.TryGetProperty("recording", out _))
                {
                    var r = SkyScopeMcap.ReadRecording(s.Bytes!);
                    q.Add(new
                    {
                        channels = r.Channels.Select(c => JsonSerializer.Deserialize<JsonElement>(SkyScopeMcap.ChannelInfoJson(c))).ToList(),
                        frames = r.Frames.Select(f => new { seq = f.Seq, t0 = Round.R9(f.T0), ids = f.Channels.Select(c => (int)c.Id).ToList(), first = Round.R9(f.Channels[0].Values[0]) }).ToList(),
                        layerEvents = r.LayerEvents.Select(e => new { t = Round.R9(e.T), id = e.Id, kind = e.Kind, meta = e.Meta is null ? null : (object)e.Meta, payload = e.Payload }).ToList(),
                        start = Round.R9(r.Start), end = Round.R9(r.End),
                    });
                }
                else if (step.TryGetProperty("lz4Block", out var lb)) { var src = Convert.FromBase64String(lb.GetProperty("data").GetString()!); var dst = new byte[lb.GetProperty("size").GetInt32()]; string outHex; try { outHex = Convert.ToHexStringLower(dst.AsSpan(0, Lz4.DecodeBlock(src, 0, src.Length, dst, 0))); } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException) { outHex = "error"; } q.Add(outHex); }
                else if (step.TryGetProperty("lz4", out var lz)) q.Add(Convert.ToHexStringLower(Lz4.Decompress(Convert.FromBase64String(lz.GetProperty("data").GetString()!), lz.GetProperty("size").GetInt32())));
                else if (step.TryGetProperty("readError", out _)) { try { McapReader.Read(s.Bytes!); q.Add(null); } catch (Exception e) when (e is InvalidDataException or NotSupportedException) { q.Add(e.Message); } }
                else if (step.TryGetProperty("innerLog", out _)) q.Add(s.Layers.Log.ToList());
                else if (step.TryGetProperty("playLog", out _)) q.Add(s.Play!.Layers.Log.ToList());
                else if (step.TryGetProperty("playBuffer", out var pb)) { var b = s.Play!.Store.Get(pb.GetInt32())?.Buffer; q.Add(b is null ? null : new { length = b.Length, latest = b.IsEmpty ? null : (double?)Round.R9(b.ValueAt(b.HeadSeq - 1)) }); }
                else if (step.TryGetProperty("playState", out _)) { var p2 = s.Play!.Src; q.Add(new { position = Round.R9(p2.Position), finished = p2.Finished, playing = p2.Playing, framesPushed = p2.FramesPushed }); }
                else throw new InvalidOperationException("unknown query " + step);
                break;
            default: throw new InvalidOperationException("unknown step " + step);
        }
        return state;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}

/// <summary>The golden MCAP written by the TypeScript core must parse here and hash the same.</summary>
public sealed class McapGoldenTests
{
    /// <summary>The length and FNV-1a hash of the sample match the sidecar, and it decodes to the recorded frame, layer-event and channel counts.</summary>
    [Fact]
    public void GoldenSampleParsesAndMatchesSidecar()
    {
        var dir = Path.Combine(Fixtures.Directory, "..", "mcap");
        var bytes = File.ReadAllBytes(Path.Combine(dir, "sample.mcap"));
        using var side = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "sample.json")));
        Assert.Equal(side.RootElement.GetProperty("length").GetInt32(), bytes.Length);
        Assert.Equal(side.RootElement.GetProperty("hash").GetUInt32(), RecordingPainter.Fnv1a(bytes));
        var rec = SkyScopeMcap.ReadRecording(bytes);
        Assert.Equal(side.RootElement.GetProperty("frames").GetInt32(), rec.Frames.Count);
        Assert.Equal(side.RootElement.GetProperty("layerEvents").GetInt32(), rec.LayerEvents.Count);
        Assert.Equal(side.RootElement.GetProperty("channels").EnumerateArray().Select(x => x.GetInt32()), rec.Channels.Select(c => c.Id));
    }

    /// <summary>The lz4- and zstd-chunked copies of the sample (the latter also as written by the C# writer) parse to the same records and decode to the same recording as the unchunked file.</summary>
    [Theory]
    [InlineData("sample-lz4.mcap")]
    [InlineData("sample-zstd.mcap")]
    [InlineData("sample-zstd-written.mcap")]
    public void ChunkedSampleReadsLikeTheUnchunkedSample(string name)
    {
        var dir = Path.Combine(Fixtures.Directory, "..", "mcap");
        var plain = File.ReadAllBytes(Path.Combine(dir, "sample.mcap"));
        var chunked = File.ReadAllBytes(Path.Combine(dir, name));
        var a = McapReader.Read(plain); var b = McapReader.Read(chunked);
        Assert.Equal(a.Profile, b.Profile);
        Assert.Equal(a.Library, b.Library);
        Assert.Equal(a.Schemas.Count, b.Schemas.Count);
        Assert.Equal(a.Channels.Values.Select(c => c.Topic), b.Channels.Values.Select(c => c.Topic));
        Assert.Equal(a.Messages.Count, b.Messages.Count);
        Assert.Equal(a.MessageStart, b.MessageStart);
        Assert.Equal(a.MessageEnd, b.MessageEnd);
        for (var i = 0; i < a.Messages.Count; i++)
        {
            var x = a.Messages[i]; var y = b.Messages[i];
            Assert.Equal((x.ChannelId, x.Sequence, x.LogTime, x.PublishTime), (y.ChannelId, y.Sequence, y.LogTime, y.PublishTime));
            Assert.Equal(x.Data, y.Data);
        }

        var ra = SkyScopeMcap.ReadRecording(plain); var rb = SkyScopeMcap.ReadRecording(chunked);
        Assert.Equal(ra.Channels.Select(c => c.Id), rb.Channels.Select(c => c.Id));
        Assert.Equal(ra.LayerEvents.Count, rb.LayerEvents.Count);
        Assert.Equal(ra.Start, rb.Start);
        Assert.Equal(ra.End, rb.End);
        Assert.Equal(ra.Frames.Count, rb.Frames.Count);
        Assert.NotEmpty(ra.Frames);
        for (var i = 0; i < ra.Frames.Count; i++)
        {
            var x = ra.Frames[i]; var y = rb.Frames[i];
            Assert.Equal(x.T0, y.T0);
            Assert.Equal(x.Channels.Count, y.Channels.Count);
            for (var c = 0; c < x.Channels.Count; c++)
            {
                Assert.Equal(x.Channels[c].Id, y.Channels[c].Id);
                Assert.Equal(x.Channels[c].Encoding, y.Channels[c].Encoding);
                Assert.Equal(x.Channels[c].Times, y.Channels[c].Times);
                Assert.Equal(x.Channels[c].Values, y.Channels[c].Values);
            }
        }
    }
}
