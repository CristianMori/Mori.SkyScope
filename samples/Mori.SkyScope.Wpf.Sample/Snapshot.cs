// Mori.SkyScope — Renders the same dashboard the window shows, but offscreen with synthetic data, to a PNG — no window, no GPU, no server.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.IO;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>Renders the same dashboard the window shows, but offscreen with synthetic data, to a PNG — no window, no GPU, no server.</summary>
public static class Snapshot
{
    /// <summary>Fills a store with 30 s of synthetic signals at 1 kHz, draws the trend chart with two cursors above a row of settled gauges, and writes the PNG.</summary>
    /// <param name="path">Output PNG path.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    public static void Render(string path, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse("#F8FAFC"));

        // 30 s of synthetic signals, 1 kHz, through the real store.
        var store = new SignalStore(60);
        var channels = SyntheticSourceConfig.AutoChannels(10, 1);
        const double rate = 1000, span = 30, now = 1_700_000_000;
        foreach (var c in channels) store.DeclareChannel(new ChannelInfo(c.Id, c.Name ?? $"ch{c.Id}") { Rate = rate });
        var frames = new List<FrameChannel>();
        foreach (var c in channels) frames.Add(FrameChannel.Regular((ushort)c.Id, now - span, 1 / rate, Synth.Synthesize(c.Spec, now - span, 1 / rate, (int)(span * rate))));
        store.PushFrame(new SkyScopeFrame(1, now - span, frames));

        var cfg = new TrendChartConfig { TimeSpan = span, TimeFormat = TimeFormat.Utc };
        cfg.Lanes.AddRange([new LaneConfig("analog") { Weight = 2 }, new LaneConfig("fast"), new LaneConfig("digital") { Weight = 0.5 }]);
        cfg.Axes.AddRange([new AxisConfig("axis:analog") { Label = "sine / tri" }, new AxisConfig("axis:fast") { Label = "saw" }]);
        cfg.Series.AddRange([
            new SeriesConfig("s1", 1) { LaneId = "analog" }, new SeriesConfig("s2", 2) { LaneId = "analog" },
            new SeriesConfig("s3", 3) { LaneId = "fast" }, new SeriesConfig("s5", 5) { LaneId = "fast", AxisId = "noise", Width = 1 },
            new SeriesConfig("s4", 4) { LaneId = "digital", Kind = SeriesKind.Digital }, new SeriesConfig("s9", 9) { LaneId = "digital", Kind = SeriesKind.Digital },
        ]);
        cfg.Thresholds.Add(new ThresholdConfig("hi", "axis:analog", 1.5, "#dc2626") { To = 3 });
        var model = new TrendChartModel(store, cfg) { Now = now, CursorA = now - 20, CursorB = now - 12 };

        double pad = 8, chartH = height - 236 - 2 * pad;
        Paint(canvas, pad, pad, width - 2 * pad, chartH, (p, w, h) => { var l = model.Layout(w, h); model.Update(l); TrendChartRenderer.Draw(model, p, l); });

        IGaugeDrawable[] gauges =
        [
            Settled(new RadialGauge(new RadialGaugeConfig { Min = -4, Max = 4, Unit = "m/s", Label = "sine", Decimals = 2, Bands = { new Band(3, 4, "#dc2626"), new Band(-4, -3, "#dc2626") } }), g => g.SetValue(2.35)),
            Settled(new LinearGauge(new LinearGaugeConfig { Min = -4, Max = 4, Unit = "°C", Label = "triangle", Orientation = Orientation.Vertical, Decimals = 1 }), g => g.SetValue(1.2)),
            Settled(new Compass(new CompassConfig { Label = "HDG" }), g => g.SetHeading(227)),
            Settled(new AttitudeIndicator(), g => g.Set(8, -20)),
            Set(new NumericDisplay(new NumericDisplayConfig { Digits = 6, Decimals = 3, Unit = "V", Label = "sawtooth" }), g => g.SetValue(-1.732)),
            Set(new LedArray(new LedArrayConfig { Count = 12, Min = -1.5, Max = 1.5, Label = "noise", Orientation = Orientation.Vertical }), g => g.SetValue(0.7)),
            new Knob(new KnobConfig { Min = 0, Max = 10, Step = 0.5, Label = "gain", Unit = "x" }, 7.5),
        ];
        var cellW = (width - 2 * pad) / gauges.Length; var gy = pad + chartH + pad;
        for (var i = 0; i < gauges.Length; i++) { var g = gauges[i]; Paint(canvas, pad + i * cellW, gy, cellW - 6, 220, (p, w, h) => g.Draw(p, w, h)); }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.OpenWrite(path);
        data.SaveTo(file);
        Console.WriteLine($"wrote {path} ({width}x{height})");
    }

    /// <summary>Applies a value and advances the gauge animation far enough for the needle to settle on it.</summary>
    private static T Settled<T>(T g, Action<T> set) where T : IAnimatedGauge { set(g); g.Step(60); return g; }
    /// <summary>Applies a value to a gauge that has no animation.</summary>
    private static T Set<T>(T g, Action<T> set) { set(g); return g; }

    /// <summary>Runs a draw callback inside a translated, clipped region of the canvas with a painter sized to that region.</summary>
    private static void Paint(SKCanvas canvas, double x, double y, double w, double h, Action<SkiaPainter, double, double> draw)
    {
        canvas.Save();
        canvas.Translate((float)x, (float)y);
        canvas.ClipRect(SKRect.Create(0, 0, (float)w, (float)h));
        using var painter = new SkiaPainter(canvas, w, h);
        draw(painter, w, h);
        canvas.Restore();
    }
}
