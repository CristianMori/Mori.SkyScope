// Mori.SkyScope — Radial gauge: sweep, bands, ticks, needle or filled arc, value readout.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Indicator: a needle over the dial, or an arc filled from the minimum to the value.</summary>
public enum NeedleStyle { Needle, Arc }

/// <summary>Proportions of the radius (ring width, tick lengths, needle width, hub radius, label radius), indicator style and visibility switches of a <see cref="RadialGauge"/>.</summary>
public sealed record RadialGaugeStyle
{
    /// <summary>Width of the outer ring as a fraction of the radius.</summary>
    public double RingWidth { get; init; } = 0.08;
    /// <summary>Length of major tick marks as a fraction of the radius.</summary>
    public double TickLength { get; init; } = 0.12;
    /// <summary>Length of minor tick marks as a fraction of the radius.</summary>
    public double MinorTickLength { get; init; } = 0.06;
    /// <summary>Width of the needle at the hub as a fraction of the radius.</summary>
    public double NeedleWidth { get; init; } = 0.04;
    /// <summary>Radius of the needle hub as a fraction of the radius.</summary>
    public double HubRadius { get; init; } = 0.08;
    /// <summary>Radius at which tick labels sit as a fraction of the radius.</summary>
    public double LabelRadius { get; init; } = 0.68;
    /// <summary>Needle or filled arc.</summary>
    public NeedleStyle NeedleStyle { get; init; } = NeedleStyle.Needle;
    /// <summary>Draw tick marks.</summary>
    public bool ShowTicks { get; init; } = true;
    /// <summary>Draw tick labels.</summary>
    public bool ShowLabels { get; init; } = true;
    /// <summary>Draw the numeric readout.</summary>
    public bool ShowValue { get; init; } = true;
    /// <summary>Draw the coloured bands.</summary>
    public bool ShowBands { get; init; } = true;
    /// <summary>All defaults.</summary>
    public static readonly RadialGaugeStyle Default = new();
}

/// <summary>Range, sweep geometry, caption, unit, decimals, bands, tick counts, damping, theme and style of a <see cref="RadialGauge"/>.</summary>
public sealed class RadialGaugeConfig
{
    /// <summary>Value at the start angle.</summary>
    public double Min { get; set; }
    /// <summary>Value at the end of the sweep.</summary>
    public double Max { get; set; } = 100;
    /// <summary>Angle of <see cref="Min"/> in degrees, 0 = up, clockwise. Default −135.</summary>
    public double StartAngle { get; set; } = -135;
    /// <summary>Angular range in degrees from <see cref="Min"/> to <see cref="Max"/>. Default 270.</summary>
    public double Sweep { get; set; } = 270;
    /// <summary>Caption drawn below the dial.</summary>
    public string? Label { get; set; }
    /// <summary>Unit shown after the value.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed number of decimals in the readout; null picks from the range.</summary>
    public int? Decimals { get; set; }
    /// <summary>Coloured value ranges drawn on the ring.</summary>
    public List<Band> Bands { get; } = [];
    /// <summary>Number of major tick intervals.</summary>
    public int MajorTicks { get; set; } = 10;
    /// <summary>Minor ticks between two major ticks.</summary>
    public int MinorPerMajor { get; set; } = 5;
    /// <summary>Needle time constant in seconds; 0 disables smoothing.</summary>
    public double Damping { get; set; } = 0.15;
    /// <summary>Colour theme.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
    /// <summary>Visual style.</summary>
    public RadialGaugeStyle Style { get; set; } = RadialGaugeStyle.Default;
}

/// <summary>Centre and radius of a round gauge in pixels.</summary>
public readonly record struct RadialLayout(double Cx, double Cy, double R);

/// <summary>Analog dial: bands, ticks, labels, needle (or filled arc), value readout. Mirrors <c>gauges/radial.ts</c>.</summary>
public sealed class RadialGauge : IAnimatedGauge
{
    /// <summary>Configuration read on every call.</summary>
    public RadialGaugeConfig Config { get; }
    /// <summary>Damped value.</summary>
    public SmoothedValue Value { get; }
    /// <summary>Creates the gauge at <paramref name="initial"/>, or at the minimum.</summary>
    public RadialGauge(RadialGaugeConfig? config = null, double? initial = null)
    {
        Config = config ?? new RadialGaugeConfig();
        Value = new SmoothedValue(initial ?? Config.Min, Config.Damping);
    }
    /// <summary>Sets the target value.</summary>
    public void SetValue(double v) => Value.Set(v);
    /// <summary>Advances the damping by <paramref name="dt"/> seconds and returns the displayed value.</summary>
    public double Step(double dt) => Value.Step(dt);
    void IAnimatedGauge.Step(double dt) => Value.Step(dt);
    /// <summary>True until the value has settled.</summary>
    public bool Animating => !Value.Settled;

    /// <summary>Centre and radius for a canvas size; sweeps of 180° or less put the centre near the bottom.</summary>
    public RadialLayout Layout(double width, double height)
    {
        var half = Math.Abs(Config.Sweep) <= 180;
        var r = half ? Math.Min(width / 2, height) * 0.92 : Math.Min(width, height) / 2 * 0.92;
        return new RadialLayout(width / 2, half ? height * 0.9 : height / 2, r);
    }
    /// <summary>Screen angle in degrees of a value along the sweep (clamped).</summary>
    public double AngleFor(double v) => Config.StartAngle + (Clamp(v, Config.Min, Config.Max) - Config.Min) / (Config.Max - Config.Min) * Config.Sweep;
    /// <summary>Scale ticks for the configured major and minor counts.</summary>
    public List<ScaleTick> Ticks() => ScaleTicks(Config.Min, Config.Max, Config.MajorTicks, Config.MinorPerMajor);

    /// <summary>Needle polygon (tip, right base, tail, left base) for a value.</summary>
    public double[] Needle(RadialLayout l, double? value = null)
    {
        var a = AngleFor(value ?? Value.Displayed); var w = l.R * Config.Style.NeedleWidth;
        var tip = Polar(l.Cx, l.Cy, l.R * 0.9, a); var tail = Polar(l.Cx, l.Cy, l.R * 0.15, a + 180);
        var left = Polar(l.Cx, l.Cy, w, a - 90); var right = Polar(l.Cx, l.Cy, w, a + 90);
        return [tip.X, tip.Y, right.X, right.Y, tail.X, tail.Y, left.X, left.Y];
    }
    /// <summary>Value text with the configured or automatic decimals.</summary>
    public string FormatValue(double v) => Scales.Ticks.FormatNumber(v, Config.Decimals ?? AutoDecimals(Config.Min, Config.Max));

    /// <summary>Paints face, ring, bands, arc or needle, ticks and labels, readout and caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var st = c.Style; var l = Layout(width, height);
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        p.Clear(th.Background);
        p.Circle(l.Cx, l.Cy, l.R, new Fill(th.Face));
        double a0 = AngleFor(c.Min), a1 = AngleFor(c.Max), ring = l.R * st.RingWidth;
        p.Arc(l.Cx, l.Cy, l.R - ring / 2, ArcAngle(a0), ArcAngle(a1), new Stroke(th.Track) { Width = ring });
        if (st.ShowBands) foreach (var b in c.Bands) p.Arc(l.Cx, l.Cy, l.R - ring / 2, ArcAngle(AngleFor(b.From)), ArcAngle(AngleFor(b.To)), new Stroke(b.Color) { Width = ring });
        if (st.NeedleStyle == NeedleStyle.Arc) p.Arc(l.Cx, l.Cy, l.R - ring * 1.7, ArcAngle(a0), ArcAngle(AngleFor(Value.Displayed)), new Stroke(th.Accent) { Width = ring * 1.2 });
        if (st.ShowTicks) foreach (var t in Ticks())
        {
            var a = AngleFor(t.Value); var len = l.R * (t.Major ? st.TickLength : st.MinorTickLength);
            var o = Polar(l.Cx, l.Cy, l.R - ring, a); var i = Polar(l.Cx, l.Cy, l.R - ring - len, a);
            p.Line(o.X, o.Y, i.X, i.Y, new Stroke(t.Major ? th.Tick : th.MinorTick) { Width = t.Major ? 2 : 1 });
            if (st.ShowLabels && t.Major && t.Label is not null) { var lp = Polar(l.Cx, l.Cy, l.R * st.LabelRadius, a); p.Text(t.Label, lp.X, lp.Y, text with { Align = TextAlign.Center, Baseline = TextBaseline.Middle }); }
        }
        if (st.NeedleStyle == NeedleStyle.Needle)
        {
            p.Polygon(Needle(l), new Fill(th.Needle));
            p.Circle(l.Cx, l.Cy, l.R * st.HubRadius, new Fill(th.Hub));
        }
        if (st.ShowValue)
        {
            var vy = l.Cy + l.R * (Math.Abs(c.Sweep) <= 180 ? -0.25 : 0.3);
            p.Text(FormatValue(Value.Displayed) + (c.Unit is null ? "" : $" {c.Unit}"), l.Cx, vy, text with { Color = th.Value, Size = th.FontSize * 1.6, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        }
        if (c.Label is not null) p.Text(c.Label, l.Cx, l.Cy + l.R * (Math.Abs(c.Sweep) <= 180 ? -0.55 : 0.52), text with { Color = th.MutedText, Align = TextAlign.Center, Baseline = TextBaseline.Middle });
    }
}
