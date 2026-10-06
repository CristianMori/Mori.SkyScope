// Mori.SkyScope — Plain-JSON form of a frame — golden files, debugging tools and tests.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mori.SkyScope.Core.Streaming;

/// <summary>Plain-JSON form of a frame — golden files, debugging tools and tests. Mirrors <c>frame-json.ts</c>.</summary>
public static class FrameJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Frame as a JSON object (seq, t0, channels) with camelCase keys.</summary>
    public static JsonNode ToJson(SkyScopeFrame frame)
    {
        var channels = new JsonArray();
        foreach (var ch in frame.Channels)
        {
            object o = ch.Encoding switch
            {
                FrameEncoding.Timestamped => new { id = ch.Id, encoding = "timestamped", times = ch.Times, values = ch.Values.Select(v => (double)v) },
                FrameEncoding.Regular => new { id = ch.Id, encoding = "regular", tStart = ch.TStart, dt = ch.Dt, values = ch.Values.Select(v => (double)v) },
                _ => new { id = ch.Id, encoding = "quantized", tStart = ch.TStart, dt = ch.Dt, scale = (double)ch.Scale, offset = (double)ch.Offset, q = ch.Q },
            };
            channels.Add(JsonSerializer.SerializeToNode(o, Options));
        }
        return new JsonObject { ["seq"] = frame.Seq, ["t0"] = frame.T0, ["channels"] = channels };
    }

    /// <summary>Parses the object form; throws <see cref="FormatException"/> on an unknown encoding.</summary>
    public static SkyScopeFrame FromJson(JsonNode json)
    {
        var o = json.AsObject();
        var channels = new List<FrameChannel>();
        foreach (var c in o["channels"]!.AsArray())
        {
            var ch = c!.AsObject();
            var id = (ushort)ch["id"]!.GetValue<int>();
            channels.Add(ch["encoding"]!.GetValue<string>() switch
            {
                "timestamped" => FrameChannel.Timestamped(id, Doubles(ch["times"]!), Singles(ch["values"]!)),
                "regular" => FrameChannel.Regular(id, ch["tStart"]!.GetValue<double>(), ch["dt"]!.GetValue<double>(), Singles(ch["values"]!)),
                "quantized" => FrameChannel.Quantized(id, ch["tStart"]!.GetValue<double>(), ch["dt"]!.GetValue<double>(),
                    (float)ch["scale"]!.GetValue<double>(), (float)ch["offset"]!.GetValue<double>(), ch["q"]!.AsArray().Select(x => (short)x!.GetValue<int>()).ToArray()),
                var e => throw new FormatException($"bad-encoding:{e}"),
            });
        }
        return new SkyScopeFrame(o["seq"]!.GetValue<uint>(), o["t0"]!.GetValue<double>(), channels);
    }

    private static double[] Doubles(JsonNode n) => n.AsArray().Select(x => x!.GetValue<double>()).ToArray();
    private static float[] Singles(JsonNode n) => n.AsArray().Select(x => (float)x!.GetValue<double>()).ToArray();
}
