// Mori.SkyScope — A single indicator lamp.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using static Mori.SkyScope.Core.Gauges.GaugeMath;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Lamp outline: a circle or a rounded square.</summary>
public enum LedShape { Round, Square }

/// <summary>Caption, <c>OnColor</c>, <c>OffColor</c> (the theme's unlit colour when null), shape and theme of a <see cref="Led"/>.</summary>
public sealed class LedConfig
{
    /// <summary>Caption drawn beside the lamp.</summary>
    public string? Label { get; set; }
    /// <summary>Colour while on.</summary>
    public string OnColor { get; set; } = "#16a34a";
    /// <summary>Colour while off; null dims <see cref="OnColor"/>.</summary>
    public string? OffColor { get; set; }
    /// <summary>Round or square lamp.</summary>
    public LedShape Shape { get; set; } = LedShape.Round;
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>A single indicator lamp. Mirrors <c>Led</c> in <c>gauges/led.ts</c>.</summary>
/// <param name="config">Settings; defaults when null.</param>
/// <param name="on">Initial state.</param>
public sealed class Led(LedConfig? config = null, bool on = false) : IGaugeDrawable
{
    /// <summary>Configuration read on every call.</summary>
    public LedConfig Config { get; } = config ?? new LedConfig();
    /// <summary>Lit state.</summary>
    public bool On { get; set; } = on;

    /// <summary>Paints the lamp (with a highlight when lit) and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme;
        p.Clear(th.Background);
        var labelRoom = c.Label is null ? 0 : th.FontSize + 6;
        double d = Math.Min(width, height - labelRoom) * 0.7, cx = width / 2, cy = (height - labelRoom) / 2;
        var fill = new Fill(On ? c.OnColor : c.OffColor ?? th.LedOff); var stroke = new Stroke(th.Track);
        if (c.Shape == LedShape.Round) p.Circle(cx, cy, d / 2, fill, stroke); else p.Rect(cx - d / 2, cy - d / 2, d, d, fill, stroke, d * 0.15);
        if (On) p.Circle(cx - d * 0.18, cy - d * 0.18, d * 0.12, new Fill("#ffffff") { Opacity = 0.55 });
        if (c.Label is not null) p.Text(c.Label, cx, height - 2, new TextStyle(th.MutedText) { Family = th.FontFamily, Size = th.FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
    }
}

/// <summary>Level: the first lamps light in proportion to a value within the range. Bits: lamp i mirrors bit i of a mask.</summary>
public enum LedArrayMode { Level, Bits }

/// <summary>Lamp <c>Count</c>, orientation, mode, value range (<c>Min</c>, <c>Max</c>) for level mode, colours, caption, <c>Gap</c> between lamps in pixels and theme of a <see cref="LedArray"/>.</summary>
public sealed class LedArrayConfig
{
    /// <summary>Number of lamps.</summary>
    public int Count { get; set; } = 10;
    /// <summary>Direction of the array: left to right, or bottom to top.</summary>
    public Orientation Orientation { get; set; } = Orientation.Horizontal;
    /// <summary>Level mode lights lamps up to the value; bit mode lights one lamp per set bit.</summary>
    public LedArrayMode Mode { get; set; } = LedArrayMode.Level;
    /// <summary>Value at which no lamp is lit (level mode).</summary>
    public double Min { get; set; }
    /// <summary>Value at which every lamp is lit (level mode).</summary>
    public double Max { get; set; } = 100;
    /// <summary>Colour per index; fewer entries repeat the last. Null = green → amber → red thirds.</summary>
    public string[]? Colors { get; set; } public string? Label { get; set; } public double Gap { get; set; } = 3;
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Bar-graph or bit-field LED strip. Mirrors <c>LedArray</c> in <c>gauges/led.ts</c>.</summary>
/// <param name="config">Settings; defaults when null.</param>
public sealed class LedArray(LedArrayConfig? config = null) : IGaugeDrawable
{
    /// <summary>Configuration read on every call.</summary>
    public LedArrayConfig Config { get; } = config ?? new LedArrayConfig();
    private double _value; private uint _bits;
    private string[] Colors => Config.Colors ?? Enumerable.Range(0, Config.Count).Select(i => i < Config.Count * 0.6 ? "#16a34a" : i < Config.Count * 0.85 ? "#d97706" : "#dc2626").ToArray();

    /// <summary>Level mode: the value, mapped into [Min, Max] when drawn.</summary>
    public void SetValue(double v) => _value = v;
    /// <summary>Bits mode: bit i lights lamp i.</summary>
    public void SetBits(uint mask) => _bits = mask;
    /// <summary>Which LEDs are lit, index 0 = first (left / bottom).</summary>
    public bool[] Lit()
    {
        var c = Config;
        if (c.Mode == LedArrayMode.Bits) return Enumerable.Range(0, c.Count).Select(i => ((_bits >> i) & 1) == 1).ToArray();
        var n = (int)RoundHalfUp(Clamp((_value - c.Min) / (c.Max - c.Min), 0, 1) * c.Count);
        return Enumerable.Range(0, c.Count).Select(i => i < n).ToArray();
    }
    /// <summary>Colour of lamp <paramref name="i"/>: the configured list (last entry repeating) or the default green/amber/red split.</summary>
    public string ColorAt(int i) { var cs = Colors; return cs.Length == 0 ? "#16a34a" : cs[Math.Min(i, cs.Length - 1)]; }

    /// <summary>Paints the lamps along the orientation and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var lit = Lit();
        p.Clear(th.Background);
        var labelRoom = c.Label is null ? 0 : th.FontSize + 6;
        var horizontal = c.Orientation == Orientation.Horizontal;
        var span = horizontal ? width - 8 : height - 8 - labelRoom;
        var cell = (span - c.Gap * (c.Count - 1)) / c.Count;
        for (var i = 0; i < c.Count; i++)
        {
            var fill = new Fill(lit[i] ? ColorAt(i) : th.LedOff);
            if (horizontal) p.Rect(4 + i * (cell + c.Gap), 4, cell, height - 8 - labelRoom, fill, null, 2);
            else p.Rect(4, height - labelRoom - 4 - (i + 1) * cell - i * c.Gap, width - 8, cell, fill, null, 2);
        }
        if (c.Label is not null) p.Text(c.Label, width / 2, height - 2, new TextStyle(th.MutedText) { Family = th.FontFamily, Size = th.FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
    }
}
