// Mori.SkyScope — SkyScope recordings in MCAP: one channel of binary frames, one channel with the channel catalog (JSON), and one JSON channel per scene la…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text;
using System.Text.Json;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Mcap;

/// <summary>
/// SkyScope recordings in MCAP: one channel of binary frames, one channel with the channel catalog (JSON), and one JSON
/// channel per scene layer (<c>/layers/&lt;id&gt;</c>, messages <c>{declare:{kind,meta}}</c> or <c>{push:payload}</c>).
/// <see cref="McapRecorder"/> is a tee: it forwards to the sinks a source already feeds and, while recording, writes every message.
/// Mirrors <c>recording/recorder.ts</c>; pinned by <c>spec/fixtures/mcap.json</c>.
/// </summary>
public static class SkyScopeMcap
{
    /// <summary>Topic of the binary frame channel.</summary>
    public const string FramesTopic = "/skyscope/frames";
    /// <summary>Topic of the JSON channel catalog.</summary>
    public const string ChannelsTopic = "/skyscope/channels";
    /// <summary>Prefix of layer topics; the layer id follows.</summary>
    public const string LayerTopicPrefix = "/layers/";
    /// <summary>MCAP message encoding of frame messages.</summary>
    public const string FrameMessageEncoding = "skyscope-frame";
    /// <summary>MCAP message encoding of binary layer pushes.</summary>
    public const string LayerMessageEncoding = "skyscope-layer";
    /// <summary>Web defaults (camelCase) used for every JSON written and parsed here.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Fixed key order and no null members, so both cores write identical catalog JSON.</summary>
    public static string ChannelInfoJson(ChannelInfo c)
    {
        var sb = new StringBuilder();
        sb.Append("{\"id\":").Append(c.Id).Append(",\"name\":").Append(JsonSerializer.Serialize(c.Name, Json));
        if (c.Unit is not null) sb.Append(",\"unit\":").Append(JsonSerializer.Serialize(c.Unit, Json));
        if (c.Kind is { } k) sb.Append(",\"kind\":\"").Append(k switch { ChannelKind.Digital => "digital", ChannelKind.State => "state", _ => "analog" }).Append('"');
        if (c.Timing is { } t) sb.Append(",\"timing\":\"").Append(t == ChannelTiming.Timestamped ? "timestamped" : "regular").Append('"');
        if (c.Rate is { } r) sb.Append(",\"rate\":").Append(JsonSerializer.Serialize(r, Json));
        if (c.Color is not null) sb.Append(",\"color\":").Append(JsonSerializer.Serialize(c.Color, Json));
        return sb.Append('}').ToString();
    }

    /// <summary>Serialises meta/payload values the way the layer sinks hand them over (JsonElements verbatim, objects camelCase).</summary>
    internal static void WriteValue(Utf8JsonWriter w, object? value)
    {
        if (value is JsonElement e) e.WriteTo(w); else JsonSerializer.Serialize(w, value, Json);
    }
    internal static byte[] DeclareJson(string kind, IReadOnlyDictionary<string, object?>? meta)
    {
        using var ms = new MemoryStream(); using var w = new Utf8JsonWriter(ms);
        w.WriteStartObject(); w.WritePropertyName("declare"); w.WriteStartObject(); w.WriteString("kind", kind); w.WritePropertyName("meta"); w.WriteStartObject();
        if (meta is not null) foreach (var (k, v) in meta) { w.WritePropertyName(k); WriteValue(w, v); }
        w.WriteEndObject(); w.WriteEndObject(); w.WriteEndObject(); w.Flush();
        return ms.ToArray();
    }
    internal static byte[] PushJson(object? payload)
    {
        using var ms = new MemoryStream(); using var w = new Utf8JsonWriter(ms);
        w.WriteStartObject(); w.WritePropertyName("push"); WriteValue(w, payload); w.WriteEndObject(); w.Flush();
        return ms.ToArray();
    }

    /// <summary>A parsed MCAP file → the playback <see cref="Recording"/> (frames, channel catalog, layer events). Foreign topics are ignored.</summary>
    public static Recording RecordingFromMcap(McapFile file)
    {
        var channels = new SortedDictionary<int, ChannelInfo>();
        var frames = new List<SkyScopeFrame>();
        var events = new List<LayerEvent>();
        foreach (var m in file.Messages)
        {
            if (!file.Channels.TryGetValue(m.ChannelId, out var ch)) continue;
            if (ch.Topic == FramesTopic) frames.Add(FrameCodec.Decode(m.Data));
            else if (ch.Topic == ChannelsTopic)
            {
                using var doc = JsonDocument.Parse(m.Data);
                if (doc.RootElement.TryGetProperty("channels", out var arr)) foreach (var c in arr.EnumerateArray()) { var info = ChannelCatalog.Parse(c); channels[info.Id] = info; }
            }
            else if (ch.Topic.StartsWith(LayerTopicPrefix, StringComparison.Ordinal))
            {
                var id = ch.Topic[LayerTopicPrefix.Length..];
                if (ch.MessageEncoding == LayerMessageEncoding) { events.Add(new LayerEvent(m.LogTime, id) { Payload = LayerMessage.Decode(m.Data).Payload }); continue; }
                using var doc = JsonDocument.Parse(m.Data);
                if (doc.RootElement.TryGetProperty("declare", out var d))
                {
                    var meta = d.TryGetProperty("meta", out var me) && me.ValueKind == JsonValueKind.Object ? Scene.LayerJson.ToMeta(me.Clone()) : null;
                    events.Add(new LayerEvent(m.LogTime, id) { Kind = d.GetProperty("kind").GetString(), Meta = meta });
                }
                else if (doc.RootElement.TryGetProperty("push", out var p)) events.Add(new LayerEvent(m.LogTime, id) { Payload = p.Clone() });
            }
        }
        frames.Sort((a, b) => a.T0.CompareTo(b.T0));
        double start = double.PositiveInfinity, end = double.NegativeInfinity;
        foreach (var f in frames)
        {
            start = Math.Min(start, f.T0);
            foreach (var c in f.Channels) end = Math.Max(end, c.Encoding == FrameEncoding.Timestamped ? (c.Times.Length > 0 ? c.Times[^1] : f.T0) : c.TStart + c.Dt * Math.Max(0, c.Count - 1));
        }
        foreach (var e in events) { start = Math.Min(start, e.T); end = Math.Max(end, e.T); }
        if (!double.IsFinite(start)) { start = 0; end = 0; }
        return new Recording(channels.Values.ToList(), frames, start, Math.Max(start, end)) { LayerEvents = events };
    }

    /// <summary>Parses MCAP bytes into a playback <see cref="Recording"/>.</summary>
    public static Recording ReadRecording(byte[] bytes) => RecordingFromMcap(McapReader.Read(bytes));
    /// <summary>Reads an MCAP file from disk into a playback <see cref="Recording"/>.</summary>
    public static Recording ReadRecording(string path) => ReadRecording(File.ReadAllBytes(path));
}

/// <summary>Snapshot of a recorder: whether it is recording, messages written, bytes so far (or of the last file), start time in seconds and elapsed duration.</summary>
public sealed record RecorderStats(bool Recording, long Messages, long Bytes, double? Started, double Duration);

/// <summary>Header strings and the in-memory size limit of an <see cref="McapRecorder"/>.</summary>
public sealed record McapRecorderOptions
{
    /// <summary>MCAP header profile.</summary>
    public string Profile { get; init; } = "skyscope";
    /// <summary>MCAP header library string.</summary>
    public string Library { get; init; } = "Mori.SkyScope 0.1.0";
    /// <summary>Stop automatically once the in-memory file exceeds this many bytes; 0 = no limit.</summary>
    public long MaxBytes { get; init; }
    /// <summary>Called (under the recorder's lock) when <see cref="MaxBytes"/> stops a recording.</summary>
    public Action? OnLimit { get; init; }
}

/// <summary>Tee that forwards every sink call to the inner sinks and, while recording, writes it to MCAP. Thread-safe.</summary>
/// <param name="signals">Signal sink to forward to; may be null.</param>
/// <param name="layers">Layer sink to forward to; may be null.</param>
/// <param name="clock">Source of log times; the live clock when null.</param>
/// <param name="options">Header strings and size limit; defaults when null.</param>
public sealed class McapRecorder(ISignalSink? signals = null, ILayerSink? layers = null, ITimeSource? clock = null, McapRecorderOptions? options = null) : ISignalSink, ILayerSink
{
    private readonly ITimeSource _clock = clock ?? new LiveClock();
    private readonly McapRecorderOptions _o = options ?? new McapRecorderOptions();
    private readonly Lock _lock = new();
    private McapWriter? _writer;
    private ushort _frameChannel, _catalogChannel;
    private readonly Dictionary<string, ushort> _layerChannels = [];
    private readonly Dictionary<string, ushort> _layerSchemas = [];
    private readonly SortedDictionary<int, ChannelInfo> _catalog = [];
    private readonly Dictionary<string, (string Kind, IReadOnlyDictionary<string, object?>? Meta)> _layerDecls = [];
    private long _messages;
    private double? _started;
    private byte[]? _last;
    private Stream? _owned;

    /// <summary>True between <see cref="Start"/> and <see cref="Stop"/>.</summary>
    public bool IsRecording { get { lock (_lock) return _writer is not null; } }
    /// <summary>Current counters.</summary>
    public RecorderStats Stats() { lock (_lock) return new RecorderStats(_writer is not null, _messages, _writer?.Length ?? _last?.Length ?? 0, _started, _started is { } s ? _clock.Now() - s : 0); }
    /// <summary>The bytes of the last finished recording.</summary>
    public byte[]? LastFile { get { lock (_lock) return _last; } }

    /// <summary>Record straight to a file on disk (no memory limit); <see cref="Stop"/> closes it and returns null.</summary>
    public void StartFile(string path)
    {
        var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 16);
        lock (_lock) { if (_writer is not null) { fs.Dispose(); return; } _owned = fs; }
        Start(fs);
    }

    /// <summary>Begin a new file: the channels and layers seen so far are written first, so a recording started mid-stream is self-contained. With <paramref name="output"/>, records stream out instead of accumulating in memory.</summary>
    public void Start(Stream? output = null)
    {
        lock (_lock)
        {
            if (_writer is not null) return;
            var w = new McapWriter(output, _o.Profile, _o.Library);
            var frameSchema = w.AddSchema("skyscope.Frame", "text", "SkyScopeFrame v1: binary batch of channel samples (see Mori.SkyScope streaming/frame)");
            var catalogSchema = w.AddSchema("skyscope.Channels", "jsonschema", "{}");
            _frameChannel = w.AddChannel(SkyScopeMcap.FramesTopic, frameSchema, SkyScopeMcap.FrameMessageEncoding);
            _catalogChannel = w.AddChannel(SkyScopeMcap.ChannelsTopic, catalogSchema, "json");
            _layerChannels.Clear(); _layerSchemas.Clear(); _binaryLayerChannels.Clear(); _binarySchema = null;
            _writer = w; _messages = 0; _started = _clock.Now(); _last = null;
            var now = McapTime.SecondsToNs(_started.Value);
            if (_catalog.Count > 0) Write(_catalogChannel, now, Encoding.UTF8.GetBytes("{\"channels\":[" + string.Join(",", _catalog.Values.Select(SkyScopeMcap.ChannelInfoJson)) + "]}"));
            foreach (var (id, d) in _layerDecls) Write(LayerChannel(id, d.Kind), now, SkyScopeMcap.DeclareJson(d.Kind, d.Meta));
        }
    }
    /// <summary>True while the active recording streams to a stream or file instead of memory.</summary>
    public bool IsStreaming { get { lock (_lock) return _writer?.Streaming ?? false; } }

    /// <summary>Finish the file and return its bytes (null when not recording or when it was streamed to a stream/file).</summary>
    public byte[]? Stop()
    {
        lock (_lock)
        {
            if (_writer is null) return null;
            var streaming = _writer.Streaming;
            var bytes = _writer.Finish();
            _writer = null;
            if (_owned is not null) { _owned.Dispose(); _owned = null; }
            if (streaming) { _last = null; return null; }
            _last = bytes;
            return bytes;
        }
    }
    /// <summary>Stop and write the file to disk; returns false when nothing was recording.</summary>
    public bool StopTo(string path) { var b = Stop(); if (b is null) return false; File.WriteAllBytes(path, b); return true; }

    private void Write(ushort channel, ulong timeNs, ReadOnlySpan<byte> payload)
    {
        var w = _writer!;
        w.AddMessage(channel, timeNs, payload);
        _messages++;
        if (_o.MaxBytes > 0 && !w.Streaming && w.Length > _o.MaxBytes) { var bytes = w.Finish(); _writer = null; _last = bytes; _o.OnLimit?.Invoke(); }
    }
    private readonly Dictionary<string, ushort> _binaryLayerChannels = [];
    private ushort? _binarySchema;
    /// <summary>Second channel on the same topic for pushes with typed arrays.</summary>
    private ushort BinaryLayerChannel(string id, string kind)
    {
        if (_binaryLayerChannels.TryGetValue(id, out var ch)) return ch;
        _binarySchema ??= _writer!.AddSchema("skyscope.LayerMessage", "text", "SkyScopeLayer v1: layer push with typed-array attachments (see Mori.SkyScope streaming/layer-message)");
        ch = _writer!.AddChannel(SkyScopeMcap.LayerTopicPrefix + id, _binarySchema.Value, SkyScopeMcap.LayerMessageEncoding, new Dictionary<string, string> { ["kind"] = kind });
        _binaryLayerChannels[id] = ch;
        return ch;
    }
    private ushort LayerChannel(string id, string kind)
    {
        if (_layerChannels.TryGetValue(id, out var ch)) return ch;
        if (!_layerSchemas.TryGetValue(kind, out var schema)) { schema = _writer!.AddSchema($"skyscope.layer.{kind}", "jsonschema", "{}"); _layerSchemas[kind] = schema; }
        ch = _writer!.AddChannel(SkyScopeMcap.LayerTopicPrefix + id, schema, "json", new Dictionary<string, string> { ["kind"] = kind });
        _layerChannels[id] = ch;
        return ch;
    }

    /// <summary>Forwards to the signal sink, remembers the channel for later recordings and, while recording, writes it to the catalog channel.</summary>
    public void DeclareChannel(ChannelInfo info)
    {
        signals?.DeclareChannel(info);
        lock (_lock) { _catalog[info.Id] = info; if (_writer is not null) Write(_catalogChannel, McapTime.SecondsToNs(_clock.Now()), Encoding.UTF8.GetBytes("{\"channels\":[" + SkyScopeMcap.ChannelInfoJson(info) + "]}")); }
    }
    /// <summary>Forwards to the signal sink and, while recording, writes the encoded frame at its own T0.</summary>
    public void PushFrame(SkyScopeFrame frame)
    {
        signals?.PushFrame(frame);
        lock (_lock) if (_writer is not null) Write(_frameChannel, McapTime.SecondsToNs(frame.T0), FrameCodec.Encode(frame));
    }
    /// <summary>Resets both inner sinks; the recording itself continues.</summary>
    public void Reset() { signals?.Reset(); layers?.Reset(); }

    /// <summary>Forwards to the layer sink, remembers the declaration for later recordings and, while recording, writes it.</summary>
    public void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null)
    {
        layers?.DeclareLayer(id, kind, meta);
        lock (_lock) { _layerDecls[id] = (kind, meta); if (_writer is not null) Write(LayerChannel(id, kind), McapTime.SecondsToNs(_clock.Now()), SkyScopeMcap.DeclareJson(kind, meta)); }
    }
    /// <summary>Forwards to the layer sink and, while recording, writes the push as JSON or, when it carries typed arrays, as a binary layer message.</summary>
    public void Push(string id, object? payload)
    {
        layers?.Push(id, payload);
        lock (_lock) if (_writer is not null)
        {
            var kind = _layerDecls.TryGetValue(id, out var d) ? d.Kind : "unknown";
            if (LayerMessage.HasBinary(payload)) Write(BinaryLayerChannel(id, kind), McapTime.SecondsToNs(_clock.Now()), LayerMessage.Encode(id, (IReadOnlyDictionary<string, object?>)payload!));
            else Write(LayerChannel(id, kind), McapTime.SecondsToNs(_clock.Now()), SkyScopeMcap.PushJson(payload));
        }
    }
}
