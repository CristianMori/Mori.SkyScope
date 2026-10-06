// Mori.SkyScope — A logic-analyzer view: 16 digital channels as stacked tracks above an ADC lane, rendered offscreen (--logic out.png).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.IO;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>A logic-analyzer view: 16 digital channels as stacked tracks above an ADC lane, rendered offscreen (--logic out.png).</summary>
public static class LogicSnapshot
{
    /// <summary>Synthesises 200 ms of bus activity at 10 kS/s (clock, 8-bit counter, 8-bit data bus, CS and RDY strobes, an ADC trace), shows it as digital tracks over an ADC lane with two cursors one clock apart, and writes the PNG with a Δt banner.</summary>
    /// <param name="path">Output PNG path.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    public static void Render(string path, int width, int height)
    {
        const double rate = 10_000, span = 0.2, now = 1_700_000_000, clkHz = 200;
        var n = (int)(span * rate);
        var t0 = now - span;

        // Synthetic bus activity: a clock, an 8-bit counter, an 8-bit data bus that changes on rising clock edges, CS and RDY strobes.
        static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }
        var signals = new Dictionary<string, float[]>();
        float[] Make(Func<int, double, float> f) { var a = new float[n]; for (var i = 0; i < n; i++) a[i] = f(i, i / rate); return a; }
        signals["CLK"] = Make((_, t) => t * clkHz % 1 < 0.5 ? 1 : 0);
        for (var b = 0; b < 8; b++) { var bit = b; signals[$"CNT{bit}"] = Make((_, t) => ((long)Math.Floor(t * clkHz) >> bit & 1) == 1 ? 1 : 0); }
        for (var b = 0; b < 8; b++) { var bit = b; signals[$"D{bit}"] = Make((_, t) => (Hash((uint)Math.Floor(t * clkHz) * 2654435761u) >> bit & 1) == 1 ? 1 : 0); }
        signals["CS"] = Make((_, t) => t % 0.025 < 0.005 ? 0 : 1);
        signals["RDY"] = Make((_, t) => t * clkHz % 1 is > 0.55 and < 0.7 ? 1 : 0);
        signals["ADC"] = Make((_, t) => (float)(1.6 + 1.2 * Math.Sin(2 * Math.PI * 12 * t) + 0.05 * Math.Sin(2 * Math.PI * 800 * t)));

        var store = new SignalStore(10);
        var names = signals.Keys.ToList();
        var frames = new List<FrameChannel>();
        for (var i = 0; i < names.Count; i++)
        {
            store.DeclareChannel(new ChannelInfo(i + 1, names[i]) { Rate = rate, Unit = names[i] == "ADC" ? "V" : null });
            frames.Add(FrameChannel.Regular((ushort)(i + 1), t0, 1 / rate, signals[names[i]]));
        }
        store.PushFrame(new SkyScopeFrame(1, t0, frames));

        var cfg = new TrendChartConfig
        {
            TimeSpan = span, TimeFormat = TimeFormat.Relative, Legend = LegendPosition.None, Theme = ChartTheme.Dark, TickSpacing = 90, LaneGap = 4, YAxisWidth = 56,
            Style = new ChartStyle { GridDash = [2, 3], DigitalTrackPadding = 0.22, SeriesWidth = 1.5, CursorWidth = 1.5, MarkerWidth = 2, LaneBorder = true },
        };
        cfg.Lanes.AddRange([new LaneConfig("adc") { Weight = 1.6 }, new LaneConfig("bus") { Weight = 9 }]);
        cfg.Axes.Add(new AxisConfig("axis:adc") { Label = "ADC  V", Min = 0, Max = 3.3 });
        string Color(string name) => name == "CLK" ? "#facc15" : name.StartsWith("CNT") ? "#4ade80" : name.StartsWith('D') ? "#38bdf8" : name == "CS" ? "#f87171" : "#fb923c";
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            cfg.Series.Add(name == "ADC"
                ? new SeriesConfig("adc", i + 1) { LaneId = "adc", Name = "ADC", Color = "#a78bfa" }
                : new SeriesConfig(name, i + 1) { LaneId = "bus", Kind = SeriesKind.Digital, Name = name, Color = Color(name) });
        }
        cfg.Markers.Add(new MarkerConfig("trig", t0 + 0.05) { Label = "TRIG", Color = "#f472b6" });
        var model = new TrendChartModel(store, cfg) { Now = now, CursorA = t0 + 0.1, CursorB = t0 + 0.1 + 1 / clkHz };

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(ChartTheme.Dark.Background));
        using (var painter = new SkiaPainter(canvas, width, height))
        {
            var layout = model.Layout(width, height);
            model.Update(layout);
            TrendChartRenderer.Draw(model, painter, layout);
            // cursor readout banner
            var cr = model.CursorReadouts();
            var dt = cr.Delta is { } d ? d.Dt : 0;
            var banner = $"A→B  Δt = {dt * 1000:0.00} ms  ({1 / dt:0.#} Hz)     {names.Count - 1} digital channels @ {rate / 1000:0} kS/s, {span * 1000:0} ms window";
            var bw = painter.MeasureText(banner, new Core.Paint.TextStyle("#94a3b8") { Family = ChartTheme.Dark.FontFamily, Size = 12 }).Width + 16;
            painter.Rect(width - 14 - bw, 12, bw, 22, new Core.Paint.Fill("#0f172a") { Opacity = 0.85 }, new Core.Paint.Stroke("#334155"), 4);
            painter.Text(banner, width - 22, 23, new Core.Paint.TextStyle("#e2e8f0") { Family = ChartTheme.Dark.FontFamily, Size = 12, Align = Core.Paint.TextAlign.Right, Baseline = Core.Paint.TextBaseline.Middle });
        }
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.OpenWrite(path);
        data.SaveTo(file);
        Console.WriteLine($"wrote {path} ({width}x{height})");
    }
}
