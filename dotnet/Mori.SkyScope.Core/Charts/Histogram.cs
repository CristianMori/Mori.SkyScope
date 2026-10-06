// Mori.SkyScope — Histogram binning with 1-2-5 "nice" bin widths, so bin edges land on round numbers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Histogram binning with 1-2-5 "nice" bin widths, so bin edges land on round numbers. Mirrors <c>charts/histogram.ts</c>.</summary>
public sealed record HistogramOptions
{
    /// <summary>Target bin count (default 10) when <see cref="BinWidth"/> is not given.</summary>
    public int? Bins { get; init; }
    /// <summary>Explicit bin width in data units; overrides <see cref="Bins"/>.</summary>
    public double? BinWidth { get; init; }
    /// <summary>Lower data bound; null uses the smallest finite value.</summary>
    public double? Min { get; init; }
    /// <summary>Upper data bound; null uses the largest finite value.</summary>
    public double? Max { get; init; }
    /// <summary>Normalise counts to densities (count / (n × width)).</summary>
    public bool Density { get; init; }
}

/// <summary>Bins of a histogram.</summary>
/// <param name="Edges">Bin boundaries, one more than the bin count.</param>
/// <param name="Counts">Count (or density) per bin.</param>
/// <param name="Centers">Midpoint of each bin.</param>
/// <param name="BinWidth">Width of every bin in data units.</param>
/// <param name="Total">Samples counted; NaN and out-of-range values are excluded.</param>
public sealed record HistogramResult(double[] Edges, double[] Counts, double[] Centers, double BinWidth, int Total);

/// <summary>Bins samples into counts or densities; see <see cref="HistogramOptions"/>.</summary>
public static class Histogram
{
    /// <summary>Bins the finite values. Edges are multiples of the width; the last bin is closed so the maximum falls inside it. Empty input yields one empty bin [0, 1].</summary>
    public static HistogramResult Compute(ReadOnlySpan<double> values, HistogramOptions? o = null)
    {
        o ??= new HistogramOptions();
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var v in values) { if (double.IsNaN(v)) continue; if (v < lo) lo = v; if (v > hi) hi = v; }
        if (o.Min is { } mn) lo = mn;
        if (o.Max is { } mx) hi = mx;
        if (!double.IsFinite(lo) || !double.IsFinite(hi)) return new HistogramResult([0, 1], [0], [0.5], 1, 0);
        var w = o.BinWidth ?? Ticks.Spec(lo, hi, o.Bins ?? 10).Step;
        if (!(w > 0)) w = 1;
        var start = Math.Floor(lo / w) * w;
        // The last bin is closed ([edge, max]) so the maximum lands in it instead of opening a new bin.
        var n = Math.Max(1, (int)Math.Ceiling((hi - start) / w - 1e-9));
        var counts = new double[n];
        var total = 0;
        foreach (var v in values)
        {
            if (double.IsNaN(v) || v < start || v > start + n * w) continue;
            var k = (int)Math.Floor((v - start) / w);
            if (k >= n) k = n - 1;
            counts[k]++; total++;
        }
        var edges = new double[n + 1]; var centers = new double[n];
        for (var k = 0; k <= n; k++) edges[k] = start + k * w;
        for (var k = 0; k < n; k++) centers[k] = start + (k + 0.5) * w;
        if (o.Density && total > 0) for (var k = 0; k < n; k++) counts[k] /= total * w;
        return new HistogramResult(edges, counts, centers, w, total);
    }

    /// <summary>A histogram as a bar series for <see cref="CartesianChart"/> (bars centred on the bins, width = bin width).</summary>
    public static CartesianSeriesConfig Series(string id, ReadOnlySpan<double> values, HistogramOptions? o = null)
    {
        var h = Compute(values, o);
        return new CartesianSeriesConfig(id) { Kind = CartesianSeriesKind.Bar, X = h.Centers, Y = h.Counts, BarWidth = h.BinWidth };
    }
}
