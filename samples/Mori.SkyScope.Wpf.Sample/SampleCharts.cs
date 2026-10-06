// Mori.SkyScope — Static demo data for the analytic charts row (the same numbers as the React and Blazor samples).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Charts;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>Static demo data for the analytic charts row (the same numbers as the React and Blazor samples).</summary>
public static class SampleCharts
{
    /// <summary>Mixed XY chart: hourly throughput bars, a rolling-average line and the error rate as scatter on a second axis.</summary>
    public static CartesianChart Bars()
    {
        var c = new CartesianChartConfig { Title = "Throughput by hour", Legend = LegendPosition.TopLeft, XAxis = new() { Label = "hour" }, YAxis = new() { Unit = "msg/s" }, Y2Axis = new() { Unit = "% err" } };
        c.Series.Add(new CartesianSeriesConfig("thr") { Name = "throughput", Kind = CartesianSeriesKind.Bar, FillOpacity = 0.55, Y = [420, 380, 350, 330, 360, 410, 520, 640, 700, 690, 660, 640, 610, 600, 620, 650, 680, 720, 700, 650, 590, 540, 490, 450] });
        c.Series.Add(new CartesianSeriesConfig("avg") { Name = "3 h average", Width = 2, Color = "#dc2626", Y = [420, 400, 383, 353, 347, 367, 430, 523, 620, 677, 683, 663, 637, 617, 610, 623, 650, 683, 700, 690, 647, 593, 540, 493] });
        c.Series.Add(new CartesianSeriesConfig("err") { Name = "error rate", Kind = CartesianSeriesKind.Scatter, Axis = YAxisId.Y2, Marker = MarkerShape.Diamond, Color = "#7c3aed", Y = [1.5, 1.4, 1.6, 1.5, 1.7, 1.9, 2.4, 2.8, 2.6, 2.2, 2.0, 1.9, 2.3, 2.9, 3.1, 2.7, 2.4, 2.1, 1.9, 1.8, 1.7, 1.6, 1.5, 1.5] });
        return new CartesianChart(c);
    }

    /// <summary>Donut of CPU time by subsystem, sorted, with the legend on the right.</summary>
    public static PieChart Pie()
    {
        var c = new PieChartConfig { Title = "CPU time", Donut = 0.55, PadAngle = 1.5, Sort = true, Legend = LegendPosition.Right };
        foreach (var (n, v) in new[] { ("perception", 38.0), ("planning", 22), ("control", 17), ("telemetry", 12), ("logging", 7), ("other", 4) }) c.Slices.Add(new PieSliceConfig(n, v));
        return new PieChart(c);
    }

    /// <summary>Polygon radar comparing two robots over six categories.</summary>
    public static PolarChart Radar()
    {
        var c = new PolarChartConfig { Title = "Robot comparison", Categories = ["speed", "payload", "range", "accuracy", "autonomy", "cost"], GridShape = PolarGridShape.Polygon, Max = 10, Legend = LegendPosition.BottomRight };
        c.Series.Add(new PolarSeriesConfig("a") { Name = "AMR-200", Values = [8, 6, 7, 9, 5, 6] });
        c.Series.Add(new PolarSeriesConfig("b") { Name = "Go2", Values = [6, 3, 5, 6, 8, 9], Color = "#d97706" });
        return new PolarChart(c);
    }
}
