// Mori.SkyScope — Turns MQTT messages into samples: topic filters, JSON paths, timestamps, scale and offset, batching.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Sources;

/// <summary>A topic filter (+ and # wildcards) and how to extract one number from matching payloads. Mirrors <c>MqttRule</c>.</summary>
/// <param name="Topic">MQTT topic filter; <c>+</c> matches one level, <c>#</c> the rest.</param>
/// <param name="ChannelId">Channel the extracted samples go to; several rules may share one.</param>
public sealed record MqttRule(string Topic, int ChannelId)
{
    /// <summary>Channel display name; the topic filter when null.</summary>
    public string? Name { get; init; }
    /// <summary>Channel unit label.</summary>
    public string? Unit { get; init; }
    /// <summary>JSON path into the payload (<c>a.b[2].c</c>); null for a plain numeric payload.</summary>
    public string? Path { get; init; }
    /// <summary>JSON path of the sample time inside the payload; the receive time is used when null or not numeric.</summary>
    public string? TimePath { get; init; }
    /// <summary>Multiplier that brings the payload time to seconds (0.001 for milliseconds).</summary>
    public double TimeScale { get; init; } = 1;
    /// <summary>Multiplier applied to the value before <see cref="Offset"/>.</summary>
    public double Scale { get; init; } = 1;
    /// <summary>Added to the scaled value.</summary>
    public double Offset { get; init; }
}

/// <summary>One extracted sample.</summary>
/// <param name="ChannelId">Target channel.</param>
/// <param name="T">Sample time in seconds.</param>
/// <param name="Value">Value after scale and offset.</param>
public readonly record struct MqttSample(int ChannelId, double T, double Value);

/// <summary>Turns MQTT messages into samples. Mirrors <c>sources/mqtt-mapping.ts</c>; pinned by <c>spec/fixtures/mqtt-mapping.json</c>.</summary>
public static class MqttMapping
{
    /// <summary>MQTT topic filter match: <c>+</c> matches exactly one level, <c>#</c> matches the remaining levels, otherwise levels must be equal and the level counts the same.</summary>
    public static bool TopicMatches(string filter, string topic)
    {
        var f = filter.Split('/'); var t = topic.Split('/');
        for (var i = 0; i < f.Length; i++)
        {
            if (f[i] == "#") return true;
            if (i >= t.Length) return false;
            if (f[i] != "+" && f[i] != t[i]) return false;
        }
        return f.Length == t.Length;
    }

    private static readonly Regex Segment = new(@"^([^\[]*)((?:\[\d+\])*)$", RegexOptions.Compiled);
    private static readonly Regex Index = new(@"\[(\d+)\]", RegexOptions.Compiled);

    /// <summary>Walk a path of property names and <c>[index]</c> steps separated by <c>.</c> or <c>/</c> (a bare number also indexes an array); null when any step is missing.</summary>
    public static JsonElement? JsonPath(JsonElement value, string path)
    {
        JsonElement cur = value;
        foreach (var raw in path.Split(['.', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Segment.Match(raw); var key = m.Success ? m.Groups[1].Value : raw;
            if (key.Length > 0)
            {
                if (cur.ValueKind == JsonValueKind.Array && int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var ai)) { if (ai >= cur.GetArrayLength()) return null; cur = cur[ai]; }
                else if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(key, out var next)) return null;
                else cur = next;
            }
            if (m.Success) foreach (Match im in Index.Matches(m.Groups[2].Value)) { if (cur.ValueKind != JsonValueKind.Array) return null; var i = int.Parse(im.Groups[1].Value, CultureInfo.InvariantCulture); if (i >= cur.GetArrayLength()) return null; cur = cur[i]; }
        }
        return cur;
    }

    private static double ToNumber(JsonElement? e) => e switch
    {
        { ValueKind: JsonValueKind.Number } n => n.GetDouble(),
        { ValueKind: JsonValueKind.True } => 1,
        { ValueKind: JsonValueKind.False } => 0,
        { ValueKind: JsonValueKind.String } s => double.TryParse(s.GetString()!.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN,
        _ => double.NaN,
    };

    /// <summary>Apply every matching rule to one message. Values that are not finite are skipped; the payload is parsed as JSON at most once. <paramref name="receivedAt"/> (seconds) stamps samples without a time path.</summary>
    public static List<MqttSample> Map(IReadOnlyList<MqttRule> rules, string topic, string payload, double receivedAt)
    {
        var o = new List<MqttSample>();
        JsonElement? parsed = null; var parsedTried = false;
        JsonElement? Json() { if (!parsedTried) { parsedTried = true; try { parsed = JsonDocument.Parse(payload).RootElement.Clone(); } catch (JsonException) { parsed = null; } } return parsed; }
        foreach (var r in rules)
        {
            if (!TopicMatches(r.Topic, topic)) continue;
            double v;
            if (r.Path is not null) v = Json() is { } j ? ToNumber(JsonPath(j, r.Path)) : double.NaN;
            else { v = double.TryParse(payload.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pv) ? pv : double.NaN; if (!double.IsFinite(v) && Json() is { ValueKind: JsonValueKind.Number } jn) v = jn.GetDouble(); }
            if (!double.IsFinite(v)) continue;
            var t = receivedAt;
            if (r.TimePath is not null && Json() is { } jt) { var tv = ToNumber(JsonPath(jt, r.TimePath)); if (double.IsFinite(tv)) t = tv * r.TimeScale; }
            o.Add(new MqttSample(r.ChannelId, t, v * r.Scale + r.Offset));
        }
        return o;
    }

    /// <summary>One timestamped channel declaration per distinct channel id, named and unit-labelled by the first rule that uses it.</summary>
    public static List<ChannelInfo> Channels(IReadOnlyList<MqttRule> rules)
    {
        var seen = new Dictionary<int, ChannelInfo>();
        foreach (var r in rules) if (!seen.ContainsKey(r.ChannelId)) seen[r.ChannelId] = new ChannelInfo(r.ChannelId, r.Name ?? r.Topic) { Unit = r.Unit, Timing = ChannelTiming.Timestamped };
        return seen.Values.ToList();
    }

    /// <summary>Batch samples into one timestamped frame per channel (sorted by time within a channel).</summary>
    public static SkyScopeFrame? ToFrame(uint seq, IReadOnlyList<MqttSample> samples)
    {
        if (samples.Count == 0) return null;
        var t0 = double.PositiveInfinity;
        var channels = samples.GroupBy(s => s.ChannelId).OrderBy(g => g.Key).Select(g =>
        {
            var list = g.OrderBy(s => s.T).ToList(); t0 = Math.Min(t0, list[0].T);
            return FrameChannel.Timestamped((ushort)g.Key, list.Select(s => s.T).ToArray(), list.Select(s => (float)s.Value).ToArray());
        }).ToList();
        return new SkyScopeFrame(seq, t0, channels);
    }
}
