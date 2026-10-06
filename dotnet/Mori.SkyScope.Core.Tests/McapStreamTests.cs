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

    private static McapRecorder Record(McapCompression compression, int chunkBytes, out byte[] bytes)
    {
        var rec = new McapRecorder(clock: new ManualClock(5), options: new McapRecorderOptions { Compression = compression, ChunkBytes = chunkBytes });
        rec.DeclareChannel(new ChannelInfo(1, "a") { Unit = "V" });
        rec.DeclareChannel(new ChannelInfo(2, "b"));
        rec.DeclareLayer("robot", "markers", new Dictionary<string, object?> { ["frame"] = "map" });
        rec.Start();
        for (var i = 0; i < 40; i++)
        {
            var values = Enumerable.Range(0, 50).Select(k => (float)Math.Sin((i * 50 + k) * 0.01)).ToArray();
            rec.PushFrame(new SkyScopeFrame((uint)i, 5 + i * 0.05, [FrameChannel.Regular(1, 5 + i * 0.05, 0.001, values), FrameChannel.Regular(2, 5 + i * 0.05, 0.001, values.Select(v => v * 2).ToArray())]));
            if (i % 10 == 0) rec.Push("robot", new Dictionary<string, object?> { ["upsert"] = new Dictionary<string, object?> { ["id"] = "m", ["type"] = "cube", ["position"] = new[] { i, 0.0, 0.0 } } });
        }
        bytes = rec.Stop()!;
        return rec;
    }

    private static void AssertSameFile(McapFile a, McapFile b)
    {
        Assert.Equal(a.Profile, b.Profile);
        Assert.Equal(a.Library, b.Library);
        Assert.Equal(a.Schemas.Keys.Order(), b.Schemas.Keys.Order());
        foreach (var (id, s) in a.Schemas) { Assert.Equal((s.Name, s.Encoding), (b.Schemas[id].Name, b.Schemas[id].Encoding)); Assert.Equal(s.Data, b.Schemas[id].Data); }
        Assert.Equal(a.Channels.Keys.Order(), b.Channels.Keys.Order());
        foreach (var (id, c) in a.Channels) Assert.Equal((c.SchemaId, c.Topic, c.MessageEncoding, c.Metadata.Count), (b.Channels[id].SchemaId, b.Channels[id].Topic, b.Channels[id].MessageEncoding, b.Channels[id].Metadata.Count));
        Assert.Equal(a.Messages.Count, b.Messages.Count);
        Assert.Equal(a.MessageStart, b.MessageStart);
        Assert.Equal(a.MessageEnd, b.MessageEnd);
        for (var i = 0; i < a.Messages.Count; i++)
        {
            var x = a.Messages[i]; var y = b.Messages[i];
            Assert.Equal((x.ChannelId, x.Sequence, x.LogTime, x.PublishTime), (y.ChannelId, y.Sequence, y.LogTime, y.PublishTime));
            Assert.Equal(x.Data, y.Data);
        }
    }

    private static void AssertSameRecording(Recording a, Recording b)
    {
        Assert.Equal(a.Channels.Select(c => (c.Id, c.Name, c.Unit)), b.Channels.Select(c => (c.Id, c.Name, c.Unit)));
        Assert.Equal(a.LayerEvents.Select(e => (e.T, e.Id, e.Kind)), b.LayerEvents.Select(e => (e.T, e.Id, e.Kind)));
        Assert.Equal(a.Start, b.Start);
        Assert.Equal(a.End, b.End);
        Assert.Equal(a.Frames.Count, b.Frames.Count);
        for (var i = 0; i < a.Frames.Count; i++)
        {
            var x = a.Frames[i]; var y = b.Frames[i];
            Assert.Equal(x.T0, y.T0);
            Assert.Equal(x.Channels.Count, y.Channels.Count);
            for (var c = 0; c < x.Channels.Count; c++) { Assert.Equal((x.Channels[c].Id, x.Channels[c].Encoding), (y.Channels[c].Id, y.Channels[c].Encoding)); Assert.Equal(x.Channels[c].Values, y.Channels[c].Values); }
        }
    }

    /// <summary>A recording written as zstd chunks (several of them, with message and chunk indexes) reads back exactly like the unchunked one, is smaller, and streams byte-identically.</summary>
    [Fact]
    public void ZstdChunkedRecordingReadsLikeTheUnchunkedOne()
    {
        Record(McapCompression.None, 0, out var plain);
        Record(McapCompression.Zstd, 4096, out var zstd);
        Assert.True(zstd.Length < plain.Length, $"zstd {zstd.Length} should be smaller than {plain.Length}");
        Assert.Contains("zstd", Encoding.Latin1.GetString(zstd));
        AssertSameFile(McapReader.Read(plain), McapReader.Read(zstd));
        AssertSameRecording(SkyScopeMcap.ReadRecording(plain), SkyScopeMcap.ReadRecording(zstd));
        // the file carries Chunk, MessageIndex and ChunkIndex records, and the statistics count the chunks
        var ops = RecordOps(zstd);
        Assert.True(ops.Count(o => o == McapOp.Chunk) > 1);
        Assert.True(ops.Count(o => o == McapOp.MessageIndex) >= ops.Count(o => o == McapOp.Chunk));
        Assert.Equal(ops.Count(o => o == McapOp.Chunk), ops.Count(o => o == McapOp.ChunkIndex));
        Assert.DoesNotContain(McapOp.Message, ops);
        // streamed output is identical to the buffered one
        using var ms = new MemoryStream();
        var streamed = new McapWriter(ms, new McapWriterOptions { Compression = McapCompression.Zstd, ChunkBytes = 4096 }); Fill(streamed);
        var buffered = new McapWriter(null, new McapWriterOptions { Compression = McapCompression.Zstd, ChunkBytes = 4096 }); Fill(buffered);
        Assert.Empty(streamed.Finish());
        Assert.Equal(buffered.Finish(), ms.ToArray());
        AssertSameFile(McapReader.Read(ms.ToArray()), McapReader.Read(new McapWriter().Also(Fill).Finish()));
    }

    /// <summary>Top-level record opcodes of a file, in order (chunk contents are not expanded).</summary>
    private static List<byte> RecordOps(byte[] bytes)
    {
        var ops = new List<byte>(); var r = new ByteReader(bytes, McapWriter.Magic.Length, bytes.Length - McapWriter.Magic.Length);
        while (r.Remaining >= 9) { var op = r.U8(); var len = (int)r.U64(); ops.Add(op); r.Pos += len; }
        return ops;
    }

    /// <summary>
    /// <c>spec/mcap/sample.mcap</c> re-written as zstd chunks is the golden <c>sample-zstd-written.mcap</c> that both cores read; the golden
    /// reads like a fresh re-write. Set <c>SKYSCOPE_MCAP_DUMP</c> to a directory to regenerate it.
    /// </summary>
    [Fact]
    public void ZstdWrittenGoldenMatchesARewriteOfTheSample()
    {
        var dir = Path.Combine(Fixtures.Fixtures.Directory, "..", "mcap");
        var sample = McapReader.Read(File.ReadAllBytes(Path.Combine(dir, "sample.mcap")));
        var w = new McapWriter(null, new McapWriterOptions { Profile = sample.Profile, Library = sample.Library, Compression = McapCompression.Zstd, ChunkBytes = 1024 });
        foreach (var s in sample.Schemas.Values.OrderBy(s => s.Id)) w.AddSchema(s.Name, s.Encoding, s.Data);
        foreach (var c in sample.Channels.Values.OrderBy(c => c.Id)) w.AddChannel(c.Topic, c.SchemaId, c.MessageEncoding, c.Metadata);
        foreach (var m in sample.Messages) w.AddMessage(m.ChannelId, McapTime.SecondsToNs(m.LogTime), McapTime.SecondsToNs(m.PublishTime), m.Data);
        var rewritten = w.Finish();
        if (Environment.GetEnvironmentVariable("SKYSCOPE_MCAP_DUMP") is { Length: > 0 } dump) File.WriteAllBytes(Path.Combine(dump, "sample-zstd-written.mcap"), rewritten);
        AssertSameFile(sample, McapReader.Read(rewritten));
        var golden = File.ReadAllBytes(Path.Combine(dir, "sample-zstd-written.mcap"));
        AssertSameFile(McapReader.Read(rewritten), McapReader.Read(golden));
        Assert.True(golden.Length < 2314);
    }
}

file static class WriterExtensions
{
    public static McapWriter Also(this McapWriter w, Action<McapWriter> fill) { fill(w); return w; }
}
