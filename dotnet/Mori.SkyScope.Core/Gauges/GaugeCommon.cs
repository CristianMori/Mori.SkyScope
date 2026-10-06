// Mori.SkyScope — Shared gauge building blocks.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Shared gauge building blocks. Mirrors <c>gauges/common.ts</c>. Angles: degrees, 0 = 12 o'clock, positive clockwise.</summary>
/// <param name="Background">Canvas colour.</param>
/// <param name="Face">Dial face fill.</param>
/// <param name="Track">Rings, tracks and outlines.</param>
/// <param name="Tick">Major ticks.</param>
/// <param name="MinorTick">Minor ticks.</param>
/// <param name="Text">Scale labels.</param>
/// <param name="MutedText">Captions.</param>
/// <param name="Needle">Needle and pointer fill.</param>
/// <param name="Hub">Needle hub.</param>
/// <param name="Value">Value readout.</param>
/// <param name="LedOff">Unlit lamp fill.</param>
/// <param name="Accent">Active fills (arcs, switches, sliders).</param>
/// <param name="FontFamily">CSS font family.</param>
/// <param name="FontSize">Font size in pixels.</param>
public sealed record GaugeTheme(
    string Background, string Face, string Track, string Tick, string MinorTick, string Text, string MutedText,
    string Needle, string Hub, string Value, string LedOff, string Accent, string FontFamily, double FontSize)
{
    /// <summary>Default light theme.</summary>
    public static readonly GaugeTheme Light = new("#ffffff", "#f8fafc", "#e2e8f0", "#334155", "#94a3b8", "#0f172a", "#64748b",
        "#dc2626", "#334155", "#0f172a", "#e2e8f0", "#2563eb", "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", 12);
    /// <summary>Dark theme derived from <see cref="Light"/>.</summary>
    public static readonly GaugeTheme Dark = Light with
    {
        Background = "#0f172a", Face = "#1e293b", Track = "#334155", Tick = "#e2e8f0", MinorTick = "#64748b", Text = "#f8fafc", MutedText = "#94a3b8",
        Needle = "#f87171", Hub = "#e2e8f0", Value = "#f8fafc", LedOff = "#1e293b", Accent = "#60a5fa",
    };
}

/// <summary>A coloured range on a scale (IOComp-style "sections").</summary>
/// <param name="From">Start value.</param>
/// <param name="To">End value.</param>
/// <param name="Color">CSS colour; drawn at reduced opacity on linear gauges.</param>
public sealed record Band(double From, double To, string Color) { public string? Label { get; init; } }

/// <summary>A tick on a gauge scale: its value, whether it is a major tick, and its label (null for minor ticks).</summary>
public readonly record struct ScaleTick(double Value, bool Major, string? Label);

/// <summary>Angle, rounding, clamping, snapping and tick helpers shared by the gauges.</summary>
public static class GaugeMath
{
    /// <summary>JavaScript-compatible rounding (half toward +∞); .NET Math.Round is banker's.</summary>
    public static double RoundHalfUp(double x) => Math.Floor(x + 0.5);

    /// <paramref name="v"/> limited to [lo, hi].
    public static double Clamp(double v, double lo, double hi) => Math.Min(hi, Math.Max(lo, v));
    /// <summary>Linear interpolation from <paramref name="a"/> to <paramref name="b"/> by <paramref name="t"/>.</summary>
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;
    /// <summary>Degrees to radians.</summary>
    public static double DegToRad(double d) => d * Math.PI / 180;
    /// <summary>Screen-space point on a circle for a gauge angle (0 = up, clockwise).</summary>
    public static (double X, double Y) Polar(double cx, double cy, double r, double angleDeg)
    {
        var a = DegToRad(angleDeg - 90);
        return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }
    /// <summary>Gauge angle → painter arc angle (radians from +x, clockwise).</summary>
    public static double ArcAngle(double angleDeg) => DegToRad(angleDeg - 90);

    /// <summary>Major ticks at 1-2-5 steps for about <paramref name="majorCount"/>, with minors between.</summary>
    public static List<ScaleTick> ScaleTicks(double min, double max, int majorCount = 10, int minorPerMajor = 5, int? decimals = null)
    {
        var spec = Ticks.Spec(min, max, majorCount);
        var majors = Ticks.FromSpec(min, max, spec);
        var dec = decimals ?? spec.Decimals;
        var result = new List<ScaleTick>();
        if (spec.Step == 0 || majors.Count == 0) return result;
        var minorStep = spec.Step / Math.Max(1, minorPerMajor);
        var first = majors[0];
        for (var k = 1; k < minorPerMajor; k++) { var v = first - k * minorStep; if (v >= min - 1e-9) result.Insert(0, new ScaleTick(v, false, null)); }
        for (var i = 0; i < majors.Count; i++)
        {
            var mv = majors[i];
            result.Add(new ScaleTick(mv, true, Ticks.FormatNumber(mv, dec)));
            for (var k = 1; k < minorPerMajor; k++)
            {
                var v = mv + k * minorStep;
                if (v <= max + 1e-9 && (i + 1 >= majors.Count || v < majors[i + 1] - 1e-9)) result.Add(new ScaleTick(v, false, null));
            }
        }
        return result;
    }

    /// <summary>Decimal places for a readout given the scale span: 0 from 100 up, 1 from 10, 2 from 1, else 3.</summary>
    public static int AutoDecimals(double min, double max)
    {
        var span = Math.Abs(max - min);
        return span >= 100 ? 0 : span >= 10 ? 1 : span >= 1 ? 2 : 3;
    }

    /// <summary>Snap to <paramref name="step"/> multiples (from min) and clamp; step 0 = continuous.</summary>
    public static double Snap(double v, double min, double max, double step)
    {
        var c = Clamp(v, min, max);
        return step > 0 ? Clamp(min + RoundHalfUp((c - min) / step) * step, min, max) : c;
    }
}

/// <summary>Exponential smoothing toward a target (needle damping). <c>Tau</c> seconds to close 63 % of the gap; 0 disables. <c>Wrap</c> (e.g. 360) takes the shortest path across the seam.</summary>
public sealed class SmoothedValue(double initial = 0, double tau = 0.15, double? wrap = null)
{
    /// <summary>Value being approached.</summary>
    public double Target { get; private set; } = initial;
    /// <summary>Current smoothed value.</summary>
    public double Displayed { get; private set; } = initial;
    /// <summary>Time constant in seconds; 0 or less snaps to the target.</summary>
    public double Tau { get; set; } = tau;
    /// <summary>Period of a circular quantity (e.g. 360), or null for a linear one.</summary>
    public double? Wrap { get; } = wrap;

    /// <summary>Sets the target; snaps immediately when damping is off.</summary>
    public void Set(double v) { Target = v; if (Tau <= 0) Displayed = v; }

    /// <summary>Advances by <paramref name="dt"/> seconds and returns the displayed value; snaps to the target once the remaining gap is below 1e-4.</summary>
    public double Step(double dt)
    {
        if (Tau <= 0 || dt <= 0) { Displayed = Target; return Displayed; }
        var delta = Target - Displayed;
        if (Wrap is { } w) delta = (delta % w + w * 1.5) % w - w / 2;
        var k = 1 - Math.Exp(-dt / Tau);
        Displayed += delta * k;
        if (Wrap is { } w2) Displayed = (Displayed % w2 + w2) % w2;
        if (Math.Abs(delta) * (1 - k) < 1e-4) Displayed = Wrap is { } w3 ? (Target % w3 + w3) % w3 : Target;
        return Displayed;
    }

    /// <summary>True when the displayed value equals the target.</summary>
    public bool Settled => Displayed == Target;
}
