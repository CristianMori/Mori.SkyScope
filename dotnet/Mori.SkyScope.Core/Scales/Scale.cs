// Mori.SkyScope — Linear, logarithmic and time scales mapping a data domain onto a pixel range, with ticks and labels.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;

namespace Mori.SkyScope.Core.Scales;

/// <summary>Family of a <see cref="Scale"/>: linear, base-10 logarithmic, or time (linear over seconds with time ticks).</summary>
public enum ScaleKind { Linear, Log, Time }
/// <summary>Time tick labels as UTC clock time, or as seconds relative to an origin.</summary>
public enum TimeFormat { Utc, Relative }

/// <summary>Deterministic time label formatting shared with the TS core.</summary>
public static class TimeFormatting
{
    /// <summary>
    /// Deterministic tick labels shared with the TS side (UTC, no locale). Mirrors <c>formatTimeTick</c>.
    /// <paramref name="step"/> sets the precision; <paramref name="span"/> (the axis extent) sets the style,
    /// so every label on one axis uses the same shape (relative: s → m:ss → h:mm:ss).
    /// </summary>
    public static string FormatTick(double t, double step, TimeFormat mode, int decimals = 0, double? span = null)
    {
        var extent = span ?? step;
        if (mode == TimeFormat.Relative)
        {
            var neg = t < 0; var a = Math.Abs(t);
            string Secs(double s) => (s < 10 ? "0" : "") + Ticks.FormatNumber(s, decimals);
            if (extent >= 3600) { var h = Math.Floor(a / 3600); var m = Math.Floor((a - h * 3600) / 60); return $"{(neg ? "-" : "")}{h:0}:{m:00}:{Secs(a - h * 3600 - m * 60)}"; }
            if (extent >= 60) { var m = Math.Floor(a / 60); return $"{(neg ? "-" : "")}{m:0}:{Secs(a - m * 60)}"; }
            return Ticks.FormatNumber(t, decimals);
        }
        var ms = (long)Math.Floor(t * 1000 + 0.5);
        var d = DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        if (step < 1)
        {
            var frac = (ms - (long)Math.Floor(ms / 1000.0) * 1000).ToString("000", CultureInfo.InvariantCulture)[..Math.Max(1, decimals)];
            return $"{d:HH:mm:ss}.{frac}";
        }
        if (step < 60) return d.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (step < 86400) return d.ToString("HH:mm", CultureInfo.InvariantCulture);
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

/// <summary>Maps a data domain onto a pixel range. Immutable. Mirrors <c>scales/scale.ts</c>.</summary>
public abstract class Scale
{
    /// <summary>Domain [<paramref name="d0"/>, <paramref name="d1"/>] onto range [<paramref name="r0"/>, <paramref name="r1"/>]; either may be descending.</summary>
    protected Scale(double d0, double d1, double r0, double r1) { D0 = d0; D1 = d1; R0 = r0; R1 = r1; }

    /// <summary>Scale family.</summary>
    public abstract ScaleKind Kind { get; }
    /// <summary>Start of the data domain.</summary>
    public double D0 { get; }
    /// <summary>End of the data domain.</summary>
    public double D1 { get; }
    /// <summary>Pixel mapped from <see cref="D0"/>.</summary>
    public double R0 { get; }
    /// <summary>Pixel mapped from <see cref="D1"/>.</summary>
    public double R1 { get; }

    /// <summary>Forward transform applied to domain values before the linear mapping (identity or log10).</summary>
    protected abstract double Fwd(double x);
    /// <summary>Inverse of <see cref="Fwd"/>.</summary>
    protected abstract double Inv(double u);
    /// <summary>Same kind of scale with other bounds; used by <see cref="WithDomain"/> and <see cref="WithRange"/>.</summary>
    protected abstract Scale Make(double d0, double d1, double r0, double r1);

    /// <summary>Data value to pixel; a degenerate domain maps everything to <see cref="R0"/>.</summary>
    public double Apply(double x)
    {
        double u0 = Fwd(D0), u1 = Fwd(D1);
        if (u1 == u0) return R0;
        return R0 + (Fwd(x) - u0) / (u1 - u0) * (R1 - R0);
    }

    /// <summary>Pixel to data value; a degenerate range maps everything to <see cref="D0"/>.</summary>
    public double Invert(double y)
    {
        double u0 = Fwd(D0), u1 = Fwd(D1);
        if (R1 == R0) return D0;
        return Inv(u0 + (y - R0) / (R1 - R0) * (u1 - u0));
    }

    /// <summary>About <paramref name="count"/> tick values inside the domain, ascending.</summary>
    public abstract List<double> TickValues(int count = 10);
    /// <summary>Tick step and decimals for about <paramref name="count"/> ticks.</summary>
    public abstract TickSpec Spec(int count = 10);
    /// <summary>Label for a tick value, precise to the tick step for <paramref name="count"/> ticks.</summary>
    public abstract string Format(double v, int count = 10);
    /// <summary>Copy whose domain is extended outwards to tick multiples; the scale itself by default.</summary>
    public virtual Scale Nice(int count = 10) => this;
    /// <summary>Copy with another domain and the same range.</summary>
    public Scale WithDomain(double d0, double d1) => Make(d0, d1, R0, R1);
    /// <summary>Copy with another range and the same domain.</summary>
    public Scale WithRange(double r0, double r1) => Make(D0, D1, r0, r1);

    /// <summary>Factory by kind; <paramref name="mode"/> only matters for time scales.</summary>
    public static Scale Create(ScaleKind kind, double d0, double d1, double r0, double r1, TimeFormat mode = TimeFormat.Utc) => kind switch
    {
        ScaleKind.Linear => new LinearScale(d0, d1, r0, r1),
        ScaleKind.Log => new LogScale(d0, d1, r0, r1),
        _ => new TimeScale(d0, d1, r0, r1, mode),
    };
}

/// <summary>Straight-line mapping with 1/2/5 ticks.</summary>
public sealed class LinearScale(double d0, double d1, double r0, double r1) : Scale(d0, d1, r0, r1)
{
    /// <inheritdoc/>
    public override ScaleKind Kind => ScaleKind.Linear;
    /// <inheritdoc/>
    protected override double Fwd(double x) => x;
    /// <inheritdoc/>
    protected override double Inv(double u) => u;
    /// <inheritdoc/>
    protected override Scale Make(double d0, double d1, double r0, double r1) => new LinearScale(d0, d1, r0, r1);
    /// <inheritdoc/>
    public override TickSpec Spec(int count = 10) => Ticks.Spec(D0, D1, count);
    /// <inheritdoc/>
    public override List<double> TickValues(int count = 10) => Ticks.FromSpec(D0, D1, Spec(count));
    /// <inheritdoc/>
    public override string Format(double v, int count = 10) => Ticks.FormatNumber(v, Spec(count).Decimals);
    /// <inheritdoc/>
    public override Scale Nice(int count = 10) { var (a, b) = Ticks.NiceLinearDomain(D0, D1, count); return new LinearScale(a, b, R0, R1); }
}

/// <summary>Base-10 logarithmic mapping; the domain must be positive. Ticks follow <see cref="Ticks.Log"/>.</summary>
public sealed class LogScale(double d0, double d1, double r0, double r1) : Scale(d0, d1, r0, r1)
{
    /// <inheritdoc/>
    public override ScaleKind Kind => ScaleKind.Log;
    /// <inheritdoc/>
    protected override double Fwd(double x) => Math.Log10(x);
    /// <inheritdoc/>
    protected override double Inv(double u) => Math.Pow(10, u);
    /// <inheritdoc/>
    protected override Scale Make(double d0, double d1, double r0, double r1) => new LogScale(d0, d1, r0, r1);
    /// <summary>Always <see cref="TickSpec.None"/>: log ticks are not evenly spaced.</summary>
    public override TickSpec Spec(int count = 10) => TickSpec.None;
    /// <inheritdoc/>
    public override List<double> TickValues(int count = 10) => Ticks.Log(D0, D1, count);
    /// <inheritdoc/>
    public override string Format(double v, int count = 10) => Ticks.FormatNumber(v, Ticks.LogTickDecimals(v));
}

/// <param name="origin">Ticks are placed and labelled relative to it (relative mode: the live edge, so labels read −30 … 0).</param>
/// <summary>Linear scale over seconds with time-aware ticks and labels.</summary>
/// <param name="d0">Domain start in seconds.</param>
/// <param name="d1">Domain end in seconds.</param>
/// <param name="r0">Pixel of <paramref name="d0"/>.</param>
/// <param name="r1">Pixel of <paramref name="d1"/>.</param>
/// <param name="mode">Label style.</param>
public sealed class TimeScale(double d0, double d1, double r0, double r1, TimeFormat mode = TimeFormat.Utc, double origin = 0) : Scale(d0, d1, r0, r1)
{
    /// <summary>Label style.</summary>
    public TimeFormat Mode { get; } = mode;
    /// <summary>Time in seconds that ticks are placed and labelled relative to.</summary>
    public double Origin { get; } = origin;
    /// <inheritdoc/>
    public override ScaleKind Kind => ScaleKind.Time;
    /// <inheritdoc/>
    protected override double Fwd(double x) => x;
    /// <inheritdoc/>
    protected override double Inv(double u) => u;
    /// <inheritdoc/>
    protected override Scale Make(double d0, double d1, double r0, double r1) => new TimeScale(d0, d1, r0, r1, Mode, Origin);
    /// <inheritdoc/>
    public override TickSpec Spec(int count = 10) => Ticks.TimeSpec(D0, D1, count);
    /// <summary>Ticks at step multiples counted from <see cref="Origin"/>.</summary>
    public override List<double> TickValues(int count = 10) => Ticks.FromSpec(D0 - Origin, D1 - Origin, Spec(count)).Select(t => t + Origin).ToList();
    /// <inheritdoc/>
    public override string Format(double v, int count = 10) { var s = Spec(count); return TimeFormatting.FormatTick(v - Origin, s.Step, Mode, s.Decimals, Math.Abs(D1 - D0)); }
}
