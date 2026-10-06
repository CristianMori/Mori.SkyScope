// Mori.SkyScope — Artificial horizon: pitch (deg, nose up positive) and roll (deg, right wing down positive).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Colours and geometry of an <see cref="AttitudeIndicator"/>: sky, ground, horizon and symbol colours; <c>PitchLadderDeg</c> (ladder spacing) and <c>VisiblePitchDeg</c> (pitch range from centre to rim) in degrees; <c>Damping</c> in seconds.</summary>
public sealed class AttitudeConfig
{
    /// <summary>Colour of the sky half.</summary>
    public string Sky { get; set; } = "#3b82f6";
    /// <summary>Colour of the ground half.</summary>
    public string Ground { get; set; } = "#92400e";
    /// <summary>Colour of the horizon line and the pitch ladder.</summary>
    public string Horizon { get; set; } = "#ffffff";
    /// <summary>Colour of the fixed aircraft symbol.</summary>
    public string Symbol { get; set; } = "#facc15";
    /// <summary>Degrees between pitch ladder rungs.</summary>
    public double PitchLadderDeg { get; set; } = 10;
    /// <summary>Degrees of pitch visible from the centre to the edge of the instrument.</summary>
    public double VisiblePitchDeg { get; set; } = 45;
    /// <summary>Smoothing time constant in seconds; 0 disables damping.</summary>
    public double Damping { get; set; } = 0.12;
    /// <summary>Background, track and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Artificial horizon: pitch (deg, nose up positive) and roll (deg, right wing down positive). Mirrors <c>gauges/attitude.ts</c>.</summary>
public sealed class AttitudeIndicator : IAnimatedGauge
{
    /// <summary>Configuration read on every call.</summary>
    public AttitudeConfig Config { get; }
    /// <summary>Damped pitch in degrees, nose up positive.</summary>
    public SmoothedValue Pitch { get; }
    /// <summary>Damped roll in degrees, wrapping at 360.</summary>
    public SmoothedValue Roll { get; }
    /// <summary>Creates the gauge level, with the configured damping.</summary>
    public AttitudeIndicator(AttitudeConfig? config = null) { Config = config ?? new AttitudeConfig(); Pitch = new SmoothedValue(0, Config.Damping); Roll = new SmoothedValue(0, Config.Damping, 360); }
    /// <summary>Sets the target attitude; roll is normalised to [0, 360).</summary>
    public void Set(double pitchDeg, double rollDeg) { Pitch.Set(pitchDeg); Roll.Set((rollDeg % 360 + 360) % 360); }
    /// <summary>Advances both damped values by <paramref name="dt"/> seconds.</summary>
    public void Step(double dt) { Pitch.Step(dt); Roll.Step(dt); }
    /// <summary>True until pitch and roll have both settled.</summary>
    public bool Animating => !Pitch.Settled || !Roll.Settled;
    /// <summary>Centre and radius for a canvas size (92 % of the half extent).</summary>
    public RadialLayout Layout(double width, double height) => new(width / 2, height / 2, Math.Min(width, height) / 2 * 0.92);
    /// <summary>Pixels per degree of pitch for radius <paramref name="r"/>.</summary>
    public double PxPerDeg(double r) => r / Config.VisiblePitchDeg;

    /// <summary>Ground polygon (screen space): a big rectangle below the horizon, rotated by −roll about the centre and shifted by pitch.</summary>
    public double[] GroundPolygon(RadialLayout l)
    {
        double roll = Roll.Displayed, dy = Pitch.Displayed * PxPerDeg(l.R), big = l.R * 3;
        double c = Math.Cos(DegToRad(-roll)), s = Math.Sin(DegToRad(-roll));
        double[][] pts = [[-big, dy], [big, dy], [big, dy + big], [-big, dy + big]];
        var result = new double[8]; var k = 0;
        foreach (var pt in pts) { result[k++] = l.Cx + pt[0] * c - pt[1] * s; result[k++] = l.Cy + pt[0] * s + pt[1] * c; }
        return result;
    }

    /// <summary>Paints sky, ground, horizon, pitch ladder, roll scale, roll pointer and the aircraft symbol.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var l = Layout(width, height);
        var text = new TextStyle(c.Horizon) { Family = th.FontFamily, Size = th.FontSize };
        p.Clear(th.Background);
        p.Circle(l.Cx, l.Cy, l.R, new Fill(c.Sky));
        p.Save();
        p.ClipRect(l.Cx - l.R, l.Cy - l.R, 2 * l.R, 2 * l.R);
        p.Polygon(GroundPolygon(l), new Fill(c.Ground));
        p.Save(); p.Translate(l.Cx, l.Cy); p.Rotate(DegToRad(-Roll.Displayed)); p.Translate(0, Pitch.Displayed * PxPerDeg(l.R));
        p.Line(-l.R, 0, l.R, 0, new Stroke(c.Horizon) { Width = 2 });
        var first = -Math.Floor(c.VisiblePitchDeg / c.PitchLadderDeg) * c.PitchLadderDeg;
        for (var d = first; d <= c.VisiblePitchDeg; d += c.PitchLadderDeg)
        {
            if (d == 0) continue;
            var y = -d * PxPerDeg(l.R); var w = l.R * (d % (2 * c.PitchLadderDeg) == 0 ? 0.3 : 0.18);
            p.Line(-w, y, w, y, new Stroke(c.Horizon));
            p.Text(Math.Abs(d).ToString(System.Globalization.CultureInfo.InvariantCulture), w + 6, y, text with { Baseline = TextBaseline.Middle });
        }
        p.Restore();
        p.Restore();
        foreach (var d in new[] { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 })
        {
            var o = Polar(l.Cx, l.Cy, l.R, d); var i = Polar(l.Cx, l.Cy, l.R * (d % 30 == 0 ? 0.9 : 0.94), d);
            p.Line(o.X, o.Y, i.X, i.Y, new Stroke(c.Horizon) { Width = d == 0 ? 3 : 1.5 });
        }
        var rp = Polar(l.Cx, l.Cy, l.R * 0.88, -Roll.Displayed);
        p.Polygon([rp.X, rp.Y, rp.X - l.R * 0.05, rp.Y + l.R * 0.09, rp.X + l.R * 0.05, rp.Y + l.R * 0.09], new Fill(c.Symbol));
        var s2 = l.R * 0.25;
        p.Line(l.Cx - s2 * 1.6, l.Cy, l.Cx - s2 * 0.5, l.Cy, new Stroke(c.Symbol) { Width = 4 });
        p.Line(l.Cx + s2 * 0.5, l.Cy, l.Cx + s2 * 1.6, l.Cy, new Stroke(c.Symbol) { Width = 4 });
        p.Circle(l.Cx, l.Cy, s2 * 0.12, new Fill(c.Symbol));
        p.Arc(l.Cx, l.Cy, l.R, ArcAngle(0), ArcAngle(360), new Stroke(th.Track) { Width = 2 });
    }
}
