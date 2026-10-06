// Mori.SkyScope — Tick generation.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;

namespace Mori.SkyScope.Core.Scales;

/// <param name="Step">Distance between ticks (0 when the domain is degenerate).</param>
/// <param name="Inv">When step &lt; 1: ticks are k / Inv. 0 otherwise.</param>
/// <param name="Decimals">Decimals needed to print a tick exactly.</param>
/// <summary>Tick step description produced by <see cref="Ticks.Spec"/> and <see cref="Ticks.TimeSpec"/>.</summary>
public readonly record struct TickSpec(double Step, double Inv, int Decimals)
{
    /// <summary>No ticks (degenerate domain).</summary>
    public static readonly TickSpec None = new(0, 0, 0);
}

/// <summary>
/// Tick generation. Mirrors <c>scales/ticks.ts</c>; pinned by <c>spec/fixtures/scales.json</c>.
/// Written so both languages produce bit-identical doubles: powers of ten by repeated multiplication,
/// sub-unit ticks as k / inv.
/// </summary>
public static class Ticks
{
    private static readonly double E10 = Math.Sqrt(50), E5 = Math.Sqrt(10), E2 = Math.Sqrt(2);

    /// <summary>Identical in both languages (Math.round and Math.Round disagree on negative halves).</summary>
    private static long RoundHalfUp(double x) => (long)Math.Floor(x + 0.5);

    /// <summary>10 to the power <paramref name="p"/> by repeated multiplication, so both cores get the same double.</summary>
    public static double Pow10(int p)
    {
        double r = 1;
        var n = p < 0 ? -p : p;
        for (var i = 0; i < n; i++) r *= 10;
        return p < 0 ? 1 / r : r;
    }

    /// <summary>d3-style 1/2/5 tick step for about <paramref name="count"/> ticks.</summary>
    public static TickSpec Spec(double d0, double d1, int count = 10)
    {
        var span = Math.Abs(d1 - d0);
        if (!(span > 0) || count <= 0 || double.IsInfinity(span)) return TickSpec.None;
        var raw = span / count;
        var power = (int)Math.Floor(Math.Log10(raw));
        var error = raw / Pow10(power);
        var factor = error >= E10 ? 10 : error >= E5 ? 5 : error >= E2 ? 2 : 1;
        if (factor == 10) { factor = 1; power += 1; }
        if (power >= 0) return new TickSpec(factor * Pow10(power), 0, 0);
        var inv = Pow10(-power) / factor;
        return new TickSpec(1 / inv, inv, -power);
    }

    /// <summary>Ticks at multiples of the spec inside [min, max], ascending.</summary>
    public static List<double> FromSpec(double d0, double d1, TickSpec spec)
    {
        double lo = Math.Min(d0, d1), hi = Math.Max(d0, d1);
        var result = new List<double>();
        if (spec.Step == 0) return result;
        if (spec.Inv > 0)
        {
            long i0 = RoundHalfUp(lo * spec.Inv), i1 = RoundHalfUp(hi * spec.Inv);
            if (i0 / spec.Inv < lo) i0++;
            if (i1 / spec.Inv > hi) i1--;
            for (var i = i0; i <= i1; i++) result.Add(i / spec.Inv);
        }
        else
        {
            long i0 = RoundHalfUp(lo / spec.Step), i1 = RoundHalfUp(hi / spec.Step);
            if (i0 * spec.Step < lo) i0++;
            if (i1 * spec.Step > hi) i1--;
            for (var i = i0; i <= i1; i++) result.Add(i * spec.Step);
        }
        return result;
    }

    /// <summary>Linear ticks for about <paramref name="count"/> ticks.</summary>
    public static List<double> Linear(double d0, double d1, int count = 10) => FromSpec(d0, d1, Spec(d0, d1, count));

    /// <summary>Extend the domain outwards to tick multiples (iterates like d3 so the step is stable).</summary>
    public static (double, double) NiceLinearDomain(double d0, double d1, int count = 10)
    {
        double lo = Math.Min(d0, d1), hi = Math.Max(d0, d1), prev = 0;
        for (var i = 0; i < 10; i++)
        {
            var s = Spec(lo, hi, count);
            if (s.Step == 0 || s.Step == prev) break;
            prev = s.Step;
            if (s.Inv > 0) { lo = Math.Floor(lo * s.Inv) / s.Inv; hi = Math.Ceiling(hi * s.Inv) / s.Inv; }
            else { lo = Math.Floor(lo / s.Step) * s.Step; hi = Math.Ceiling(hi / s.Step) * s.Step; }
        }
        return d0 <= d1 ? (lo, hi) : (hi, lo);
    }

    /// <summary>Fixed <paramref name="decimals"/>, invariant culture, and never "-0".</summary>
    public static string FormatNumber(double v, int decimals)
    {
        var s = v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        return s.StartsWith("-0", StringComparison.Ordinal) && s.Skip(2).All(c => c == '0' || c == '.') ? s[1..] : s;
    }

    /// <summary>Base-10 log axis: every mantissa within a decade, 1/2/5 across a few decades, powers beyond.</summary>
    public static List<double> Log(double d0, double d1, int count = 10)
    {
        double lo = Math.Min(d0, d1), hi = Math.Max(d0, d1);
        if (!(lo > 0) || !(hi > 0) || double.IsInfinity(hi)) return [];
        double l0 = Math.Log10(lo), l1 = Math.Log10(hi);
        int p0 = (int)Math.Floor(l0), p1 = (int)Math.Ceiling(l1);
        var span = l1 - l0;
        int[] mantissas = span <= 1 ? [1, 2, 3, 4, 5, 6, 7, 8, 9] : span <= 3 ? [1, 2, 5] : [1];
        var result = new List<double>();
        for (var p = p0; p <= p1; p++)
            foreach (var m in mantissas)
            {
                var v = p >= 0 ? m * Pow10(p) : m / Pow10(-p);
                if (v >= lo && v <= hi) result.Add(v);
            }
        if (mantissas.Length == 1 && result.Count > count)
        {
            var every = (int)Math.Ceiling((double)result.Count / count);
            result = result.Where((_, i) => i % every == 0).ToList();
        }
        return result;
    }

    /// <summary>Decimals needed to print a log tick exactly: 0 at or above 1, else the magnitude of the exponent.</summary>
    public static int LogTickDecimals(double v) => !(v > 0) || v >= 1 ? 0 : -(int)Math.Floor(Math.Log10(v));

    /// <summary>Seconds. Multiples from epoch land on natural boundaries (15 s, 15 min, even hours, days…).</summary>
    public static readonly double[] TimeSteps = [0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800, 604800];

    /// <summary>Tick step in seconds for about <paramref name="count"/> ticks: the first entry of <see cref="TimeSteps"/> at or above the target, 1/2/5 days beyond a week.</summary>
    public static TickSpec TimeSpec(double d0, double d1, int count = 10)
    {
        var span = Math.Abs(d1 - d0);
        if (!(span > 0) || count <= 0 || double.IsInfinity(span)) return TickSpec.None;
        var target = span / count;
        foreach (var step in TimeSteps)
        {
            if (step < target) continue;
            if (step >= 1) return new TickSpec(step, 0, 0);
            var inv = (double)RoundHalfUp(1 / step);
            return new TickSpec(step, inv, step < 0.01 ? 3 : step < 0.1 ? 2 : 1);
        }
        var days = Spec(d0 / 86400, d1 / 86400, count);
        return new TickSpec(days.Step * 86400, 0, 0);
    }

    /// <summary>Time ticks for about <paramref name="count"/> ticks, as seconds.</summary>
    public static List<double> Time(double d0, double d1, int count = 10) => FromSpec(d0, d1, TimeSpec(d0, d1, count));
}
