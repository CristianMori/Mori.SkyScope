// Mori.SkyScope — Rotary input: the value follows the pointer's angle about the centre.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Range (<c>Min</c>, <c>Max</c>, <c>Step</c>; step 0 = continuous), arc geometry (<c>StartAngle</c> and <c>Sweep</c> in degrees, 0 = up, clockwise), caption, unit, decimals and theme of a <see cref="Knob"/>.</summary>
public sealed class KnobConfig
{
    /// <summary>Lowest value, at the start angle.</summary>
    public double Min { get; set; }
    /// <summary>Highest value, at the end of the sweep.</summary>
    public double Max { get; set; } = 100;
    /// <summary>Snap increment from <see cref="Min"/>; 0 = continuous.</summary>
    public double Step { get; set; }
    /// <summary>Angle of <see cref="Min"/> in degrees, 0 = up, clockwise.</summary>
    public double StartAngle { get; set; } = -135;
    /// <summary>Angular range in degrees covered from <see cref="Min"/> to <see cref="Max"/>.</summary>
    public double Sweep { get; set; } = 270;
    /// <summary>Caption drawn below the knob.</summary>
    public string? Label { get; set; }
    /// <summary>Unit shown after the value.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed number of decimals in the readout; null picks from the step and range.</summary>
    public int? Decimals { get; set; }
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Rotary input: the value follows the pointer's angle about the centre. Mirrors <c>gauges/knob.ts</c>.</summary>
public sealed class Knob : IGaugeDrawable
{
    /// <summary>Configuration read on every call.</summary>
    public KnobConfig Config { get; }
    /// <summary>Current value, snapped to the step and clamped to the range.</summary>
    public double Value { get; private set; }
    /// <summary>True between pointer down and pointer up.</summary>
    public bool Dragging { get; private set; }
    /// <summary>Raised with the new value whenever it changes.</summary>
    public event Action<double>? Changed;
    /// <summary>Creates the knob at <paramref name="initial"/>, or at the minimum.</summary>
    public Knob(KnobConfig? config = null, double? initial = null) { Config = config ?? new KnobConfig(); Value = initial ?? Config.Min; }

    /// <summary>Centre and radius for a canvas size (80 % of the half extent; the arc sits outside the radius).</summary>
    public RadialLayout Layout(double width, double height) => new(width / 2, height / 2, Math.Min(width, height) / 2 * 0.8);
    /// <summary>Screen angle in degrees of a value along the sweep (clamped).</summary>
    public double AngleFor(double v) => Config.StartAngle + (Clamp(v, Config.Min, Config.Max) - Config.Min) / (Config.Max - Config.Min) * Config.Sweep;

    /// <summary>Value for a pointer position: its angle about the centre, clamped to the sweep; the dead zone below snaps to the nearer end.</summary>
    public double ValueFromPoint(RadialLayout l, double x, double y)
    {
        var c = Config;
        var a = Math.Atan2(x - l.Cx, -(y - l.Cy)) * 180 / Math.PI;
        var rel = ((a - c.StartAngle) % 360 + 360) % 360;
        if (rel > c.Sweep) rel = rel - c.Sweep > (360 - c.Sweep) / 2 ? 0 : c.Sweep;
        return Snap(c.Min + rel / c.Sweep * (c.Max - c.Min), c.Min, c.Max, c.Step);
    }
    private void SetValueInternal(double v) { if (v != Value) { Value = v; Changed?.Invoke(v); } }
    /// <summary>Sets the value (snapped and clamped) and raises <see cref="Changed"/> when it differs.</summary>
    public void SetValue(double v) => SetValueInternal(Snap(v, Config.Min, Config.Max, Config.Step));
    /// <summary>Starts a drag and jumps to the value under the pointer.</summary>
    public void PointerDown(RadialLayout l, double x, double y) { Dragging = true; SetValueInternal(ValueFromPoint(l, x, y)); }
    /// <summary>Follows the pointer while dragging.</summary>
    public void PointerMove(RadialLayout l, double x, double y) { if (Dragging) SetValueInternal(ValueFromPoint(l, x, y)); }
    /// <summary>Ends the drag.</summary>
    public void PointerUp() => Dragging = false;
    /// <summary>Wheel or arrow keys: one step (or 1 % of range) per notch.</summary>
    public void Nudge(double direction)
    {
        var c = Config; var s = c.Step > 0 ? c.Step : (c.Max - c.Min) / 100;
        SetValueInternal(Snap(Value + Math.Sign(direction) * s, c.Min, c.Max, c.Step));
    }

    /// <summary>Paints the track arc, the filled arc up to the value, ticks, the dial, the pointer line, the readout and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var l = Layout(width, height);
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        p.Clear(th.Background);
        p.Arc(l.Cx, l.Cy, l.R * 1.12, ArcAngle(c.StartAngle), ArcAngle(c.StartAngle + c.Sweep), new Stroke(th.Track) { Width = l.R * 0.1 });
        p.Arc(l.Cx, l.Cy, l.R * 1.12, ArcAngle(c.StartAngle), ArcAngle(AngleFor(Value)), new Stroke(th.Accent) { Width = l.R * 0.1 });
        foreach (var t in ScaleTicks(c.Min, c.Max, 5, 1, c.Decimals))
        {
            var o = Polar(l.Cx, l.Cy, l.R * 1.22, AngleFor(t.Value)); var i = Polar(l.Cx, l.Cy, l.R * 1.18, AngleFor(t.Value));
            p.Line(o.X, o.Y, i.X, i.Y, new Stroke(th.Tick));
        }
        p.Circle(l.Cx, l.Cy, l.R, new Fill(th.Face), new Stroke(th.Track) { Width = 2 });
        var g0 = Polar(l.Cx, l.Cy, l.R * 0.55, AngleFor(Value)); var g1 = Polar(l.Cx, l.Cy, l.R * 0.92, AngleFor(Value));
        p.Line(g0.X, g0.Y, g1.X, g1.Y, new Stroke(th.Needle) { Width = Math.Max(2, l.R * 0.08), Cap = LineCap.Round });
        p.Text(Ticks.FormatNumber(Value, c.Decimals ?? AutoDecimals(c.Min, c.Max)) + (c.Unit is null ? "" : $" {c.Unit}"), l.Cx, l.Cy + l.R * 0.05, text with { Color = th.Value, Size = th.FontSize * 1.2, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        if (c.Label is not null) p.Text(c.Label, l.Cx, height - 2, text with { Color = th.MutedText, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
    }
}
