// Mori.SkyScope — The analytic charts on one page, rendered offscreen (--charts out.png): XY mixed, histogram, donut, radar, spectrogram, stacked area.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.IO;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>The analytic charts on one page, rendered offscreen (--charts out.png): XY mixed, histogram, donut, radar, spectrogram, stacked area.</summary>
public static class ChartsSnapshot
{
    /// <summary>Builds six analytic charts from seeded synthetic data (one with a hover tooltip), draws them in a 3×2 grid and writes the PNG.</summary>
    /// <param name="path">Output PNG path.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="dark">Use the dark chart theme.</param>
    public static void Render(string path, int width, int height, bool dark = false)
    {
        var theme = dark ? ChartTheme.Dark : ChartTheme.Light;
        var rng = new Random(7);
        double Gauss() { double u = 1 - rng.NextDouble(), v = rng.NextDouble(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v); }

        // 1. Mixed XY: hourly throughput bars, rolling average line, error rate on a second axis.
        var hours = Enumerable.Range(0, 24).Select(h => (double)h).ToArray();
        var thr = hours.Select(h => 400 + 300 * Math.Sin((h - 6) / 24 * 2 * Math.PI) + 40 * Gauss()).ToArray();
        var avg = hours.Select((_, i) => thr.Skip(Math.Max(0, i - 2)).Take(Math.Min(3, i + 1)).Average()).ToArray();
        var err = hours.Select(h => 1.5 + 1.2 * Math.Max(0, Math.Sin((h - 14) / 24 * 2 * Math.PI)) + 0.2 * Math.Abs(Gauss())).ToArray();
        var xy = new CartesianChartConfig { Title = "Throughput by hour", Theme = theme, XAxis = new() { Label = "hour", Unit = "h" }, YAxis = new() { Unit = "msg/s" }, Y2Axis = new() { Unit = "% err" }, Legend = LegendPosition.TopLeft };
        xy.Series.Add(new CartesianSeriesConfig("thr") { Name = "throughput", Kind = CartesianSeriesKind.Bar, X = hours, Y = thr, FillOpacity = 0.55 });
        xy.Series.Add(new CartesianSeriesConfig("avg") { Name = "3 h average", X = hours, Y = avg, Width = 2, Color = "#dc2626" });
        xy.Series.Add(new CartesianSeriesConfig("err") { Name = "error rate", Kind = CartesianSeriesKind.Scatter, Axis = YAxisId.Y2, X = hours, Y = err, Marker = MarkerShape.Diamond, Color = "#7c3aed" });
        var xyChart = new CartesianChart(xy);
        xyChart.PointerMove(430, 200, width / 3.0, height / 2.0);

        // 2. Histogram of latency samples (log-normal-ish).
        var lat = Enumerable.Range(0, 4000).Select(_ => Math.Exp(2.3 + 0.35 * Gauss())).ToArray();
        var hist = new CartesianChartConfig { Title = "Latency distribution (n = 4000)", Theme = theme, XAxis = new() { Label = "latency", Unit = "ms" }, YAxis = new() { Label = "count" }, Legend = LegendPosition.None };
        hist.Series.Add(Histogram.Series("lat", lat, new HistogramOptions { Bins = 25 }));
        hist.Series[0].Color = "#0891b2";

        // 3. Donut.
        var pie = new PieChartConfig { Title = "CPU time by subsystem", Theme = theme, Donut = 0.55, PadAngle = 1.5, Labels = PieLabelMode.Percent, Legend = LegendPosition.Right, Sort = true };
        foreach (var (name, v) in new[] { ("perception", 38.0), ("planning", 22), ("control", 17), ("telemetry", 12), ("logging", 7), ("other", 4) }) pie.Slices.Add(new PieSliceConfig(name, v));
        var pieChart = new PieChart(pie) { Hover = "planning" };

        // 4. Radar.
        var radar = new PolarChartConfig { Title = "Robot comparison", Theme = theme, Categories = ["speed", "payload", "range", "accuracy", "autonomy", "cost"], GridShape = PolarGridShape.Polygon, Max = 10, Legend = LegendPosition.BottomRight };
        radar.Series.Add(new PolarSeriesConfig("a") { Name = "AMR-200", Values = [8, 6, 7, 9, 5, 6] });
        radar.Series.Add(new PolarSeriesConfig("b") { Name = "Go2", Values = [6, 3, 5, 6, 8, 9], Color = "#d97706" });

        // 5. Spectrogram: a chirp, a steady carrier and a burst over a noise floor.
        const int cols = 160, rows = 64;
        var spec = new HeatmapConfig { Title = "Spectrogram", Theme = theme, Cols = cols, Rows = rows, XMin = 0, XMax = 8, YMin = 0, YMax = 2000, XLabel = "t (s)", YLabel = "Hz", ValueLabel = "dB", Colormap = "inferno", Min = -70, Max = 0 };
        var values = new double[rows * cols];
        for (var c = 0; c < cols; c++)
            for (var r = 0; r < rows; r++)
            {
                var t = c / (double)cols; var f = r / (double)rows;
                var chirp = Math.Exp(-Math.Pow((f - (0.1 + 0.7 * t)) * rows / 2.5, 2));
                var carrier = Math.Exp(-Math.Pow((f - 0.62) * rows / 1.8, 2)) * 0.8;
                var burst = t is > 0.55 and < 0.7 ? Math.Exp(-Math.Pow((f - 0.3) * rows / 6, 2)) * 0.9 : 0;
                values[r * cols + c] = -62 + 6 * Gauss() * 0.6 + 60 * Math.Max(chirp, Math.Max(carrier, burst));
            }
        var heat = new Heatmap(spec, values);
        heat.PointerMove(300, 150, width / 3.0, height / 2.0);

        // 6. Stacked area.
        var days = Enumerable.Range(0, 30).Select(d => (double)d).ToArray();
        var area = new CartesianChartConfig { Title = "Fleet utilisation", Theme = theme, XAxis = new() { Label = "day" }, YAxis = new() { Unit = "h" }, Legend = LegendPosition.Top };
        string[] names = ["charging", "idle", "transport", "picking"];
        string[] colors = ["#94a3b8", "#fbbf24", "#2563eb", "#16a34a"];
        for (var k = 0; k < names.Length; k++)
        {
            var kk = k;
            area.Series.Add(new CartesianSeriesConfig(names[k]) { Kind = CartesianSeriesKind.Area, Stack = "u", X = days, Y = days.Select(d => 3 + kk * 1.5 + 2 * Math.Sin(d / 5 + kk) + 0.5 * Math.Abs(Gauss())).ToArray(), Color = colors[k], FillOpacity = 0.7 });
        }

        IDrawable[] charts = [xyChart, new CartesianChart(hist), pieChart, new PolarChart(radar), heat, new CartesianChart(area)];
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColor.Parse(theme.Background));
        double cw = width / 3.0, ch = height / 2.0;
        using (var painter = new SkiaPainter(canvas, width, height))
        {
            for (var i = 0; i < charts.Length; i++)
            {
                double x = i % 3 * cw, y = i / 3 * ch;
                painter.Save(); painter.Translate(x, y); painter.ClipRect(0, 0, cw, ch);
                charts[i].Draw(painter, cw, ch);
                painter.Restore();
            }
        }
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.OpenWrite(path);
        data.SaveTo(file);
        Console.WriteLine($"wrote {path} ({width}x{height})");
    }
}
