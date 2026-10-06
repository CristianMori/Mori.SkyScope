// Mori.SkyScope — Bar / thermometer gauge with a scale beside the track.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Direction of a linear gauge or slider: along x, or along y with the minimum at the bottom.</summary>
public enum Orientation { Horizontal, Vertical }

/// <summary>Pixel sizes and switches of a <see cref="LinearGauge"/>: bar thickness, tick lengths, pointer size; whether the bar fills up to the value and whether ticks, labels, the readout and bands are shown.</summary>
public sealed record LinearGaugeStyle
{
    /// <summary>Thickness of the bar in pixels.</summary>
    public double BarThickness { get; init; } = 18;
    /// <summary>Length of major tick marks in pixels.</summary>
    public double TickLength { get; init; } = 8;
    /// <summary>Length of minor tick marks in pixels.</summary>
    public double MinorTickLength { get; init; } = 4;
    /// <summary>Size of the pointer triangle in pixels.</summary>
    public double PointerSize { get; init; } = 8;
    /// <summary>Fill the bar up to the value (true) or show a pointer only.</summary>
    public bool Fill { get; init; } = true;
    /// <summary>Draw tick marks.</summary>
    public bool ShowTicks { get; init; } = true;
    /// <summary>Draw tick labels.</summary>
    public bool ShowLabels { get; init; } = true;
    /// <summary>Draw the numeric readout.</summary>
    public bool ShowValue { get; init; } = true;
    /// <summary>Draw the coloured bands.</summary>
    public bool ShowBands { get; init; } = true;
    /// <summary>All defaults.</summary>
    public static readonly LinearGaugeStyle Default = new();
}

/// <summary>Range, orientation, caption, unit, decimals, coloured bands, tick counts, <c>Damping</c> in seconds, theme and style of a <see cref="LinearGauge"/>.</summary>
public sealed class LinearGaugeConfig
{
    /// <summary>Value at the start of the bar.</summary>
    public double Min { get; set; }
    /// <summary>Value at the end of the bar.</summary>
    public double Max { get; set; } = 100;
    /// <summary>Bar direction: horizontal fills left to right, vertical bottom to top.</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    /// <summary>Caption drawn below the gauge.</summary>
    public string? Label { get; set; }
    /// <summary>Unit shown after the value.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed number of decimals in the readout; null picks from the range.</summary>
    public int? Decimals { get; set; }
    /// <summary>Coloured value ranges drawn beside the track.</summary>
    public List<Band> Bands { get; } = [];
    /// <summary>Number of major tick intervals.</summary>
    public int MajorTicks { get; set; } = 5;
    /// <summary>Minor ticks between two major ticks.</summary>
    public int MinorPerMajor { get; set; } = 5;
    /// <summary>Pointer time constant in seconds; 0 disables smoothing.</summary>
    public double Damping { get; set; } = 0.15;
    /// <summary>Colour theme.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
    /// <summary>Visual style.</summary>
    public LinearGaugeStyle Style { get; set; } = LinearGaugeStyle.Default;
}

/// <summary>Track rectangle in pixels and whether it runs horizontally.</summary>
public readonly record struct LinearLayout(Rect Track, bool Horizontal);

/// <summary>Bar / thermometer gauge with a scale beside the track. Mirrors <c>gauges/linear.ts</c>.</summary>
public sealed class LinearGauge : IAnimatedGauge
{
    /// <summary>Configuration read on every call.</summary>
    public LinearGaugeConfig Config { get; }
    /// <summary>Damped value.</summary>
    public SmoothedValue Value { get; }
    /// <summary>Creates the gauge at <paramref name="initial"/>, or at the minimum.</summary>
    public LinearGauge(LinearGaugeConfig? config = null, double? initial = null) { Config = config ?? new LinearGaugeConfig(); Value = new SmoothedValue(initial ?? Config.Min, Config.Damping); }
    /// <summary>Sets the target value.</summary>
    public void SetValue(double v) => Value.Set(v);
    /// <summary>Advances the damping by <paramref name="dt"/> seconds and returns the displayed value.</summary>
    public double Step(double dt) => Value.Step(dt);
    void IAnimatedGauge.Step(double dt) => Value.Step(dt);
    /// <summary>True until the value has settled.</summary>
    public bool Animating => !Value.Settled;

    /// <summary>Track placement for a canvas size, leaving room for labels, ticks, the readout and the caption.</summary>
    public LinearLayout Layout(double width, double height)
    {
        var c = Config; var st = c.Style; double pad = 12, labels = st.ShowLabels ? c.Theme.FontSize * 2.2 : 0, ticks = st.ShowTicks ? st.TickLength + 2 : 0;
        var valueRoom = st.ShowValue ? c.Theme.FontSize * 1.8 : 0;
        if (c.Orientation == Orientation.Horizontal) return new LinearLayout(new Rect(pad + labels / 2, (height - valueRoom - st.BarThickness) / 2, width - 2 * pad - labels, st.BarThickness), true);
        var captionRoom = c.Label is null ? 0 : c.Theme.FontSize + 8;
        return new LinearLayout(new Rect(pad + labels + ticks, pad + valueRoom, st.BarThickness, height - 2 * pad - valueRoom - captionRoom), false);
    }
    /// <summary>Pixel position along the track of a value (clamped to the range).</summary>
    public double PosFor(LinearLayout l, double v)
    {
        var c = Config; var t = (Clamp(v, c.Min, c.Max) - c.Min) / (c.Max - c.Min);
        return l.Horizontal ? l.Track.X + t * l.Track.W : l.Track.Y + l.Track.H - t * l.Track.H;
    }
    /// <summary>Scale ticks for the configured major and minor counts.</summary>
    public List<ScaleTick> Ticks() => ScaleTicks(Config.Min, Config.Max, Config.MajorTicks, Config.MinorPerMajor);
    /// <summary>Value text with the configured or automatic decimals.</summary>
    public string FormatValue(double v) => Scales.Ticks.FormatNumber(v, Config.Decimals ?? AutoDecimals(Config.Min, Config.Max));

    /// <summary>Paints track, bands, fill, ticks and labels, pointer, readout and caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var st = c.Style; var l = Layout(width, height); var t = l.Track; var v = Value.Displayed;
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        p.Clear(th.Background);
        p.Rect(t.X, t.Y, t.W, t.H, new Fill(th.Track), null, 3);
        if (st.ShowBands) foreach (var b in c.Bands)
        {
            double a = PosFor(l, b.From), z = PosFor(l, b.To);
            if (l.Horizontal) p.Rect(Math.Min(a, z), t.Y, Math.Abs(z - a), t.H, new Fill(b.Color) { Opacity = 0.35 });
            else p.Rect(t.X, Math.Min(a, z), t.W, Math.Abs(z - a), new Fill(b.Color) { Opacity = 0.35 });
        }
        double pv = PosFor(l, v), p0 = PosFor(l, c.Min);
        if (st.Fill)
        {
            if (l.Horizontal) p.Rect(p0, t.Y, pv - p0, t.H, new Fill(th.Accent), null, 3);
            else p.Rect(t.X, pv, t.W, p0 - pv, new Fill(th.Accent), null, 3);
        }
        if (st.ShowTicks) foreach (var k in Ticks())
        {
            var pos = PosFor(l, k.Value); var len = k.Major ? st.TickLength : st.MinorTickLength; var stroke = new Stroke(k.Major ? th.Tick : th.MinorTick) { Width = k.Major ? 2 : 1 };
            if (l.Horizontal) { p.Line(pos, t.Y + t.H + 2, pos, t.Y + t.H + 2 + len, stroke); if (st.ShowLabels && k.Label is not null) p.Text(k.Label, pos, t.Y + t.H + 4 + st.TickLength, text with { Align = TextAlign.Center, Baseline = TextBaseline.Top }); }
            else { p.Line(t.X - 2, pos, t.X - 2 - len, pos, stroke); if (st.ShowLabels && k.Label is not null) p.Text(k.Label, t.X - 4 - st.TickLength, pos, text with { Align = TextAlign.Right, Baseline = TextBaseline.Middle }); }
        }
        var s = st.PointerSize;
        if (l.Horizontal) p.Polygon([pv, t.Y - 2, pv - s, t.Y - 2 - s, pv + s, t.Y - 2 - s], new Fill(th.Needle));
        else p.Polygon([t.X + t.W + 2, pv, t.X + t.W + 2 + s, pv - s, t.X + t.W + 2 + s, pv + s], new Fill(th.Needle));
        if (st.ShowValue)
        {
            var label = FormatValue(v) + (c.Unit is null ? "" : $" {c.Unit}");
            if (l.Horizontal) p.Text(label, t.X + t.W, height - 6, text with { Color = th.Value, Size = th.FontSize * 1.4, Weight = "bold", Align = TextAlign.Right, Baseline = TextBaseline.Bottom });
            else p.Text(label, t.X + t.W / 2, 6, text with { Color = th.Value, Size = th.FontSize * 1.4, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Top });
        }
        if (c.Label is not null)
        {
            if (l.Horizontal) p.Text(c.Label, t.X, height - 6, text with { Color = th.MutedText, Baseline = TextBaseline.Bottom });
            else p.Text(c.Label, t.X + t.W / 2, height - 4, text with { Color = th.MutedText, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
        }
    }
}
