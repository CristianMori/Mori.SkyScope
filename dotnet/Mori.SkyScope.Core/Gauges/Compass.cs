// Mori.SkyScope — Compass gauge: needle or rotating card, damped across the 0/360 seam.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Needle: fixed card, rotating needle. Card: rotating card under a fixed lubber line.</summary>
public enum CompassMode { Needle, Card }

/// <summary>Mode, caption (<c>Label</c>), <c>Damping</c> in seconds, whether the heading is printed (<c>ShowValue</c>) and theme of a <see cref="Compass"/>.</summary>
public sealed class CompassConfig
{
    /// <summary>Needle: fixed card, rotating needle. Card: rotating card, fixed lubber line.</summary>
    public CompassMode Mode { get; set; } = CompassMode.Needle; public string? Label { get; set; } public double Damping { get; set; } = 0.2; public bool ShowValue { get; set; } = true;
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Heading indicator, 0–360° with wrap-aware damping. Mirrors <c>gauges/compass.ts</c>.</summary>
public sealed class Compass : IAnimatedGauge
{
    /// <summary>Configuration read on every call.</summary>
    public CompassConfig Config { get; }
    /// <summary>Damped heading in degrees [0, 360), taking the shortest path across north.</summary>
    public SmoothedValue Heading { get; }
    /// <summary>Creates the gauge at <paramref name="initial"/> degrees.</summary>
    public Compass(CompassConfig? config = null, double initial = 0) { Config = config ?? new CompassConfig(); Heading = new SmoothedValue(initial, Config.Damping, 360); }
    /// <summary>Sets the target heading; normalised to [0, 360).</summary>
    public void SetHeading(double deg) => Heading.Set((deg % 360 + 360) % 360);
    /// <summary>Advances the damping by <paramref name="dt"/> seconds and returns the displayed heading.</summary>
    public double Step(double dt) => Heading.Step(dt);
    void IAnimatedGauge.Step(double dt) => Heading.Step(dt);
    /// <summary>True until the heading has settled.</summary>
    public bool Animating => !Heading.Settled;
    /// <summary>Centre and radius for a canvas size (92 % of the half extent).</summary>
    public RadialLayout Layout(double width, double height) => new(width / 2, height / 2, Math.Min(width, height) / 2 * 0.92);

    /// <summary>Paints the card with ticks and cardinal letters, the needle or lubber line, the heading readout and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var l = Layout(width, height); var h = Heading.Displayed;
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        var rot = c.Mode == CompassMode.Card ? -h : 0;
        p.Clear(th.Background);
        p.Circle(l.Cx, l.Cy, l.R, new Fill(th.Face), new Stroke(th.Track) { Width = 2 });
        for (var d = 0; d < 360; d += 10)
        {
            var a = d + rot; var major = d % 30 == 0;
            var o = Polar(l.Cx, l.Cy, l.R * 0.96, a); var i = Polar(l.Cx, l.Cy, l.R * (major ? 0.84 : 0.9), a);
            p.Line(o.X, o.Y, i.X, i.Y, new Stroke(major ? th.Tick : th.MinorTick) { Width = major ? 2 : 1 });
            if (d % 90 == 0) { var lp = Polar(l.Cx, l.Cy, l.R * 0.7, a); p.Text("NESW"[d / 90].ToString(), lp.X, lp.Y, text with { Size = th.FontSize * 1.4, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle, Color = d == 0 ? th.Needle : th.Text }); }
            else if (major) { var lp = Polar(l.Cx, l.Cy, l.R * 0.72, a); p.Text((d / 10).ToString(), lp.X, lp.Y, text with { Color = th.MutedText, Align = TextAlign.Center, Baseline = TextBaseline.Middle }); }
        }
        if (c.Mode == CompassMode.Needle)
        {
            var tip = Polar(l.Cx, l.Cy, l.R * 0.8, h); var tail = Polar(l.Cx, l.Cy, l.R * 0.8, h + 180); var w = l.R * 0.07;
            var left = Polar(l.Cx, l.Cy, w, h - 90); var right = Polar(l.Cx, l.Cy, w, h + 90);
            p.Polygon([tip.X, tip.Y, right.X, right.Y, left.X, left.Y], new Fill(th.Needle));
            p.Polygon([tail.X, tail.Y, left.X, left.Y, right.X, right.Y], new Fill(th.Hub));
            p.Circle(l.Cx, l.Cy, l.R * 0.06, new Fill(th.Hub));
        }
        else
        {
            var t = Polar(l.Cx, l.Cy, l.R, 0);
            p.Polygon([t.X, t.Y - 2, t.X - l.R * 0.06, t.Y - l.R * 0.14, t.X + l.R * 0.06, t.Y - l.R * 0.14], new Fill(th.Needle));
            p.Line(l.Cx, l.Cy - l.R * 0.5, l.Cx, l.Cy + l.R * 0.5, new Stroke(th.Accent) { Width = 2 });
            p.Line(l.Cx - l.R * 0.3, l.Cy, l.Cx + l.R * 0.3, l.Cy, new Stroke(th.Accent) { Width = 2 });
        }
        p.Arc(l.Cx, l.Cy, l.R * 0.97, ArcAngle(0), ArcAngle(360), new Stroke(th.Track));
        if (c.ShowValue) p.Text($"{(long)RoundHalfUp(h) % 360}°", l.Cx, l.Cy + l.R * 0.45, text with { Color = th.Value, Size = th.FontSize * 1.5, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        if (c.Label is not null) p.Text(c.Label, l.Cx, l.Cy - l.R * 0.42, text with { Color = th.MutedText, Align = TextAlign.Center, Baseline = TextBaseline.Middle });
    }
}
