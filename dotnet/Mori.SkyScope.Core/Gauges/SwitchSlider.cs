// Mori.SkyScope — Toggle switch and slider inputs.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Caption, the texts shown in the on and off states, and theme of a <see cref="Switch"/>.</summary>
public sealed class SwitchConfig
{
    /// <summary>Caption drawn beside the switch.</summary>
    public string? Label { get; set; }
    /// <summary>Text shown while on.</summary>
    public string OnLabel { get; set; } = "ON";
    /// <summary>Text shown while off.</summary>
    public string OffLabel { get; set; } = "OFF";
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Toggle switch. Mirrors <c>Switch</c> in <c>gauges/switch-slider.ts</c>.</summary>
/// <param name="config">Settings; defaults when null.</param>
/// <param name="on">Initial state.</param>
public sealed class Switch(SwitchConfig? config = null, bool on = false) : IGaugeDrawable
{
    /// <summary>Configuration read on every call.</summary>
    public SwitchConfig Config { get; } = config ?? new SwitchConfig();
    /// <summary>Current state.</summary>
    public bool On { get; private set; } = on;
    /// <summary>Raised with the new state on every toggle.</summary>
    public event Action<bool>? Changed;
    /// <summary>Flips the state and raises <see cref="Changed"/>.</summary>
    public void Toggle() { On = !On; Changed?.Invoke(On); }
    /// <summary>Sets the state, raising <see cref="Changed"/> only when it changes.</summary>
    public void Set(bool on) { if (on != On) Toggle(); }

    /// <summary>Paints the pill, the knob, the state text and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var labelRoom = c.Label is null ? 0 : th.FontSize + 6;
        double w = Math.Min(width - 8, 64), h = Math.Min(height - labelRoom - 8, w / 2), x = (width - w) / 2, y = (height - labelRoom - h) / 2, r = h / 2;
        p.Clear(th.Background);
        p.Rect(x, y, w, h, new Fill(On ? th.Accent : th.Track), null, r);
        p.Circle(On ? x + w - r : x + r, y + r, r - 3, new Fill("#ffffff"), new Stroke(th.MinorTick));
        p.Text(On ? c.OnLabel : c.OffLabel, On ? x + r * 0.6 : x + w - r * 0.6, y + r, new TextStyle(On ? "#ffffff" : th.MutedText) { Family = th.FontFamily, Size = th.FontSize * 0.9, Weight = "bold", Align = On ? TextAlign.Left : TextAlign.Right, Baseline = TextBaseline.Middle });
        if (c.Label is not null) p.Text(c.Label, width / 2, height - 2, new TextStyle(th.MutedText) { Family = th.FontFamily, Size = th.FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
    }
}

/// <summary>Range (<c>Min</c>, <c>Max</c>, <c>Step</c>; step 0 = continuous), orientation, caption, unit, decimals and theme of a <see cref="Slider"/>.</summary>
public sealed class SliderConfig
{
    /// <summary>Value at the start of the track.</summary>
    public double Min { get; set; }
    /// <summary>Value at the end of the track.</summary>
    public double Max { get; set; } = 100;
    /// <summary>Snap increment from <see cref="Min"/>; 0 = continuous.</summary>
    public double Step { get; set; }
    /// <summary>Track direction: horizontal runs left to right, vertical bottom to top.</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    /// <summary>Caption drawn beside the slider.</summary>
    public string? Label { get; set; }
    /// <summary>Unit shown after the value.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed number of decimals in the readout; null picks from the step and range.</summary>
    public int? Decimals { get; set; }
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Track geometry in pixels: <paramref name="X0"/> and <paramref name="X1"/> are the minimum and maximum ends along the drag axis, <paramref name="Y"/> the position across it.</summary>
public readonly record struct SliderTrack(double X0, double X1, double Y, bool Horizontal);

/// <summary>Linear input with a drag handle. Mirrors <c>Slider</c> in <c>gauges/switch-slider.ts</c>.</summary>
public sealed class Slider : IGaugeDrawable
{
    /// <summary>Configuration read on every call.</summary>
    public SliderConfig Config { get; }
    /// <summary>Current value, snapped to the step and clamped to the range.</summary>
    public double Value { get; private set; }
    /// <summary>True between pointer down and pointer up.</summary>
    public bool Dragging { get; private set; }
    /// <summary>Raised with the new value whenever it changes.</summary>
    public event Action<double>? Changed;
    /// <summary>Creates the slider at <paramref name="initial"/>, or at the minimum.</summary>
    public Slider(SliderConfig? config = null, double? initial = null) { Config = config ?? new SliderConfig(); Value = initial ?? Config.Min; }

    /// <summary>Track end points along the drag axis (X0 = min end, X1 = max end) and its cross position.</summary>
    public SliderTrack Track(double width, double height)
    {
        var labelRoom = Config.Label is null ? 0 : Config.Theme.FontSize + 6;
        return Config.Orientation == Orientation.Horizontal ? new SliderTrack(14, width - 14, (height - labelRoom) / 2, true) : new SliderTrack(height - 14 - labelRoom, 14, width / 2, false);
    }
    /// <summary>Value for a pointer position projected onto the track, snapped and clamped.</summary>
    public double ValueFromPoint(double width, double height, double x, double y)
    {
        var c = Config; var t = Track(width, height); var pos = t.Horizontal ? x : y;
        return Snap(c.Min + Clamp((pos - t.X0) / (t.X1 - t.X0), 0, 1) * (c.Max - c.Min), c.Min, c.Max, c.Step);
    }
    private void SetValueInternal(double v) { if (v != Value) { Value = v; Changed?.Invoke(v); } }
    /// <summary>Sets the value (snapped and clamped) and raises <see cref="Changed"/> when it differs.</summary>
    public void SetValue(double v) => SetValueInternal(Snap(v, Config.Min, Config.Max, Config.Step));
    /// <summary>Starts a drag and jumps to the value under the pointer.</summary>
    public void PointerDown(double width, double height, double x, double y) { Dragging = true; SetValueInternal(ValueFromPoint(width, height, x, y)); }
    /// <summary>Follows the pointer while dragging.</summary>
    public void PointerMove(double width, double height, double x, double y) { if (Dragging) SetValueInternal(ValueFromPoint(width, height, x, y)); }
    /// <summary>Ends the drag.</summary>
    public void PointerUp() => Dragging = false;
    /// <summary>Wheel or arrow keys: one step (or 1 % of the range) per notch.</summary>
    public void Nudge(double direction) { var c = Config; var s = c.Step > 0 ? c.Step : (c.Max - c.Min) / 100; SetValueInternal(Snap(Value + Math.Sign(direction) * s, c.Min, c.Max, c.Step)); }

    /// <summary>Paints the track, the filled part, the handle, the readout and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var t = Track(width, height); var f = (Value - c.Min) / (c.Max - c.Min); var pos = t.X0 + f * (t.X1 - t.X0);
        p.Clear(th.Background);
        void Seg(double a, double b, string color) { if (t.Horizontal) p.Line(a, t.Y, b, t.Y, new Stroke(color) { Width = 6, Cap = LineCap.Round }); else p.Line(t.Y, a, t.Y, b, new Stroke(color) { Width = 6, Cap = LineCap.Round }); }
        Seg(t.X0, t.X1, th.Track);
        Seg(t.X0, pos, th.Accent);
        if (t.Horizontal) p.Circle(pos, t.Y, 9, new Fill("#ffffff"), new Stroke(th.Accent) { Width = 2 }); else p.Circle(t.Y, pos, 9, new Fill("#ffffff"), new Stroke(th.Accent) { Width = 2 });
        var label = Ticks.FormatNumber(Value, c.Decimals ?? AutoDecimals(c.Min, c.Max)) + (c.Unit is null ? "" : $" {c.Unit}");
        if (t.Horizontal) p.Text(label, pos, t.Y - 14, new TextStyle(th.Value) { Family = th.FontFamily, Size = th.FontSize, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
        else p.Text(label, t.Y + 14, pos, new TextStyle(th.Value) { Family = th.FontFamily, Size = th.FontSize, Weight = "bold", Baseline = TextBaseline.Middle });
        if (c.Label is not null) p.Text(c.Label, width / 2, height - 2, new TextStyle(th.MutedText) { Family = th.FontFamily, Size = th.FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
    }
}
