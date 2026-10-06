// Mori.SkyScope — Round-trips the golden SkyScopeFrame binaries in spec/frames through the codec.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Streaming;
using Mori.SkyScope.Core.Tests.Fixtures;

namespace Mori.SkyScope.Core.Tests;

/// <summary>Round-trips the golden SkyScopeFrame binaries in <c>spec/frames</c> through the C# codec, so both cores agree byte for byte.</summary>
public class FrameGoldenTests
{
    private static readonly string Dir = Path.Combine(Fixtures.Fixtures.Directory, "..", "frames");

    /// <summary>Lists the golden frame names (every JSON sidecar except the <c>layer-</c> ones) as theory data.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var f in Directory.GetFiles(Dir, "*.json").Order()) if (!Path.GetFileName(f).StartsWith("layer-")) data.Add(Path.GetFileNameWithoutExtension(f));
        return data;
    }

    /// <summary>Decoding the golden <c>.bin</c> must produce exactly the JSON of its sidecar.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Decode_matches_json(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, name + ".json")))!;
        var bytes = File.ReadAllBytes(Path.Combine(Dir, name + ".bin"));
        var decoded = JsonNode.Parse(FrameJson.ToJson(FrameCodec.Decode(bytes)).ToJsonString())!;
        Assert.True(JsonNode.DeepEquals(json, decoded), $"decoded:\n{decoded.ToJsonString()}\nexpected:\n{json.ToJsonString()}");
    }

    /// <summary>Encoding the sidecar JSON must reproduce the golden bytes, and <c>ByteLength</c> must predict their size.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void Encode_matches_bin(string name)
    {
        var json = JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, name + ".json")))!;
        var expected = File.ReadAllBytes(Path.Combine(Dir, name + ".bin"));
        var frame = FrameJson.FromJson(json);
        Assert.Equal(expected.Length, frame.ByteLength);
        Assert.Equal(expected, FrameCodec.Encode(frame));
    }

    /// <summary>Fifty seeded random frames mixing all three channel encodings survive encode then decode unchanged.</summary>
    [Fact]
    public void Random_frames_round_trip()
    {
        var rnd = new Random(12345);
        for (var k = 0; k < 50; k++)
        {
            var channels = new List<FrameChannel>();
            var n = rnd.Next(6);
            for (var c = 0; c < n; c++)
            {
                var len = rnd.Next(9);
                var id = (ushort)rnd.Next(65536);
                var values = Enumerable.Range(0, len).Select(_ => (float)(rnd.NextDouble() * 200 - 100)).ToArray();
                channels.Add(rnd.Next(3) switch
                {
                    0 => FrameChannel.Timestamped(id, Enumerable.Range(0, len).Select(i => i * 0.5 + rnd.NextDouble()).ToArray(), values),
                    1 => FrameChannel.Regular(id, rnd.NextDouble() * 1e6, 1.0 / (1 + rnd.Next(1000)), values),
                    _ => FrameChannel.Quantized(id, rnd.NextDouble() * 1e6, 0.01, (float)rnd.NextDouble(), (float)(rnd.NextDouble() * 10),
                        Enumerable.Range(0, len).Select(_ => (short)rnd.Next(-32768, 32768)).ToArray()),
                });
            }
            var frame = new SkyScopeFrame((uint)rnd.NextInt64(0, 1L << 32), rnd.NextDouble() * 1e9, channels);
            var bytes = FrameCodec.Encode(frame);
            var back = FrameCodec.Decode(bytes);
            Assert.True(JsonNode.DeepEquals(FrameJson.ToJson(frame), FrameJson.ToJson(back)));
        }
    }

    /// <summary>Truncated input and a wrong magic byte are rejected with the stable <c>truncated-frame</c> and <c>bad-magic</c> messages.</summary>
    [Fact]
    public void Rejects_garbage()
    {
        Assert.Equal("truncated-frame", Assert.Throws<FormatException>(() => FrameCodec.Decode(new byte[3])).Message);
        var bad = FrameCodec.Encode(new SkyScopeFrame(1, 0, [])); bad[0] = 0;
        Assert.Equal("bad-magic", Assert.Throws<FormatException>(() => FrameCodec.Decode(bad)).Message);
        var trunc = FrameCodec.Encode(new SkyScopeFrame(1, 0, [FrameChannel.Regular(1, 0, 1, new float[4])]));
        Assert.Equal("truncated-frame", Assert.Throws<FormatException>(() => FrameCodec.Decode(trunc.AsSpan(0, trunc.Length - 8))).Message);
    }
}
