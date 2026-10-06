// Mori.SkyScope — Streams an MCAP file through the recorder and reader and checks what comes back.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text;
using Mori.SkyScope.Core.Mcap;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests;

/// <summary>Streams an MCAP file through the writer, the recorder and the reader and checks what comes back.</summary>
public sealed class McapStreamTests
{
    private static void Fill(McapWriter w)
    {
        var s = w.AddSchema("t", "jsonschema", "{}");
        var c = w.AddChannel("/a", s, "json", new Dictionary<string, string> { ["k"] = "v" });
        for (var i = 0; i < 50; i++) w.AddMessage(c, 1_000_000_000UL + (ulong)i * 1000, Encoding.UTF8.GetBytes($"{{\"i\":{i}}}"));
    }

    /// <summary>Writing straight to a stream yields the same bytes as buffering in memory, reports the same length and reads back every message.</summary>
    [Fact]
    public void StreamedOutputIsByteIdenticalToBufferedOutput()
    {
        var buffered = new McapWriter(); Fill(buffered);
        using var ms = new MemoryStream();
        var streamed = new McapWriter(ms); Fill(streamed);
        var a = buffered.Finish(); var tail = streamed.Finish();
        Assert.Empty(tail);
        Assert.True(streamed.Streaming);
        Assert.Equal(a, ms.ToArray());
        Assert.Equal(a.Length, streamed.Length);
        Assert.Equal(50, McapReader.Read(ms.ToArray()).Messages.Count);
    }

    /// <summary>A recorder started on a file path streams to disk, grows past <c>MaxBytes</c> without stopping and returns null from Stop because nothing was buffered.</summary>
    [Fact]
    public void RecorderStreamsToAFileAndIgnoresTheSizeLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"skyscope-{Guid.NewGuid():N}.mcap");
        try
        {
            var rec = new McapRecorder(clock: new ManualClock(5), options: new McapRecorderOptions { MaxBytes = 200 });
            rec.DeclareChannel(new ChannelInfo(1, "a"));
            rec.StartFile(path);
            for (var i = 0; i < 20; i++) rec.PushFrame(new SkyScopeFrame((uint)i, 5 + i, [FrameChannel.Regular(1, 5 + i, 1, [i])]));
            Assert.True(rec.IsRecording); Assert.True(rec.IsStreaming);
            Assert.Null(rec.Stop());
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length > 200);
            var r = SkyScopeMcap.ReadRecording(bytes);
            Assert.Equal(20, r.Frames.Count);
            Assert.Equal("a", r.Channels[0].Name);
        }
        finally { File.Delete(path); }
    }
}
