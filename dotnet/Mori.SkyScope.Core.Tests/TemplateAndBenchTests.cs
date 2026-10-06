// Mori.SkyScope — The ingest budget from the plan: 500 channels × 1 kHz through encode → decode → store with zero drops.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;
using Mori.SkyScope.Sources.Template;
using Xunit.Abstractions;

namespace Mori.SkyScope.Core.Tests;

/// <summary>The template counter source behaves as a plugin: it declares its channel, pushes counting frames and is discoverable by assembly scan.</summary>
public sealed class TemplateSourceTests
{
    /// <summary>Two manual pushes at 100 Hz with 50 ms batches produce ten consecutive integers with correctly spaced timestamps and no drops.</summary>
    [Fact]
    public async Task CounterDeclaresItsChannelAndPushesCountingFrames()
    {
        var store = new SignalStore(retentionSeconds: 10);
        var clock = new ManualClock(100);
        var src = new CounterSource();
        await src.StartAsync(new SourceContext(store, new NullLayerSink(), clock, (_, _) => { }), new CounterConfig { ChannelId = 7, Rate = 100, BatchMs = 50, UseTimer = false });
        src.Push(); clock.Advance(0.05); src.Push();
        await src.StopAsync();
        var ch = store.Get(7)!;
        Assert.Equal("counter", ch.Info.Name);
        Assert.Equal(10, ch.Buffer.Length);
        Assert.Equal(9, ch.Buffer.ValueAt(9));
        Assert.Equal(100.05, ch.Buffer.TimeAt(5), 9);
        Assert.Equal(0, store.Dropped);
    }

    /// <summary>Scanning the template assembly registers the <c>counter</c> factory, and the registry creates a <c>CounterSource</c> from it.</summary>
    [Fact]
    public void FactoryIsDiscoveredByAssemblyScan()
    {
        var registry = new SourceRegistry().Scan(typeof(CounterSource).Assembly);
        Assert.Contains(registry.List(), f => f.Type == "counter");
        Assert.IsType<CounterSource>(registry.Create("counter"));
    }
}

/// <summary>The ingest budget from the plan: 500 channels × 1 kHz through encode → decode → store with zero drops. CI runs 2 s of data; `dotnet test --filter Bench` with BENCH_SECONDS=60 for the full run.</summary>
public sealed class IngestBenchTests(ITestOutputHelper output)
{
    /// <summary>Pushes the full channel load through encode, decode and store, prints the throughput and requires zero drops and faster-than-real-time ingest.</summary>
    [Fact]
    [Trait("Category", "Bench")]
    public void Ingest500ChannelsAt1kHzWithoutDrops()
    {
        var seconds = double.TryParse(Environment.GetEnvironmentVariable("BENCH_SECONDS"), out var s) ? s : 2;
        const int channels = 500, rate = 1000, batchMs = 20;
        var store = new SignalStore(retentionSeconds: 5);
        for (var c = 0; c < channels; c++) store.DeclareChannel(new ChannelInfo(c + 1, $"ch{c + 1}") { Rate = rate, Timing = ChannelTiming.Regular });
        var perFrame = rate * batchMs / 1000; var frames = (int)(seconds * 1000 / batchMs);
        var values = new float[perFrame];
        var sw = Stopwatch.StartNew();
        long bytes = 0;
        for (var f = 0; f < frames; f++)
        {
            var t0 = f * batchMs / 1000.0;
            for (var i = 0; i < perFrame; i++) values[i] = (float)Math.Sin((t0 + i / (double)rate) * 6.283);
            var chans = new List<FrameChannel>(channels);
            for (var c = 0; c < channels; c++) chans.Add(FrameChannel.Regular((ushort)(c + 1), t0, 1.0 / rate, values));
            var encoded = FrameCodec.Encode(new SkyScopeFrame((uint)f, t0, chans));
            bytes += encoded.Length;
            store.PushFrame(FrameCodec.Decode(encoded));
        }
        sw.Stop();
        var samples = (long)frames * channels * perFrame;
        output.WriteLine($"{channels} ch × {rate} Hz for {seconds} s: {samples:N0} samples, {bytes / 1e6:F1} MB in {sw.Elapsed.TotalMilliseconds:F0} ms → {samples / sw.Elapsed.TotalSeconds / 1e6:F1} Msamples/s, dropped {store.Dropped}");
        Assert.Equal(0, store.Dropped);
        Assert.True(sw.Elapsed.TotalSeconds < seconds, "ingest must be faster than real time");
    }
}
