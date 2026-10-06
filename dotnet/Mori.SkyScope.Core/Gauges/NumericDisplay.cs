// Mori.SkyScope — Seven-segment numeric display with sign, decimals, unit and label.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Digit count, decimals, caption, unit, <c>SegmentColor</c>, <c>OffOpacity</c> of unlit segments, <c>Slant</c> (shear as a fraction of the cell height) and theme of a <see cref="NumericDisplay"/>.</summary>
public sealed class NumericDisplayConfig
{
    /// <summary>Digit cells including sign and integer part; the decimal point lives between cells.</summary>
    public int Digits { get; set; } = 5;
    /// <summary>Fixed number of fraction digits.</summary>
    public int Decimals { get; set; } = 1;
    /// <summary>Caption drawn below the digits.</summary>
    public string? Label { get; set; }
    /// <summary>Unit drawn beside the digits.</summary>
    public string? Unit { get; set; }
    /// <summary>Colour of lit segments.</summary>
    public string SegmentColor { get; set; } = "#16a34a";
    /// <summary>Opacity of unlit segments (the ghost of the display).</summary>
    public double OffOpacity { get; set; } = 0.08;
    /// <summary>Italic slant of the digits as a fraction of their height.</summary>
    public double Slant { get; set; } = 0.08;
    /// <summary>Colours and type.</summary>
    public GaugeTheme Theme { get; set; } = GaugeTheme.Light;
}

/// <summary>Seven-segment readout. Mirrors <c>gauges/numeric.ts</c>.</summary>
/// <param name="config">Settings; defaults when null.</param>
public sealed class NumericDisplay(NumericDisplayConfig? config = null) : IGaugeDrawable
{
    private static readonly Dictionary<char, int> Seg = new()
    {
        ['0'] = 0x3f, ['1'] = 0x06, ['2'] = 0x5b, ['3'] = 0x4f, ['4'] = 0x66, ['5'] = 0x6d, ['6'] = 0x7d, ['7'] = 0x07, ['8'] = 0x7f, ['9'] = 0x6f, ['-'] = 0x40, [' '] = 0x00,
        ['E'] = 0x79, ['r'] = 0x50, ['o'] = 0x5c, ['L'] = 0x38, ['H'] = 0x76, ['A'] = 0x77, ['C'] = 0x39, ['F'] = 0x71, ['P'] = 0x73, ['U'] = 0x3e, ['b'] = 0x7c, ['d'] = 0x5e, ['n'] = 0x54, ['t'] = 0x78,
    };
    /// <summary>Bits a..g = 1..64 (a top, b upper-right, c lower-right, d bottom, e lower-left, f upper-left, g middle).</summary>
    public static int SevenSegmentMask(char ch) => Seg.GetValueOrDefault(ch, 0);

    /// <summary>Polygons for the lit segments of one digit cell, as interleaved point lists.</summary>
    public static List<double[]> SegmentPolygons(double x, double y, double w, double h, int mask, double thickness = 0.16)
    {
        double t = w * thickness, g = t * 0.18;
        var result = new List<double[]>();
        double[] Hz(double yy) => [x + t / 2 + g, yy, x + t + g, yy - t / 2, x + w - t - g, yy - t / 2, x + w - t / 2 - g, yy, x + w - t - g, yy + t / 2, x + t + g, yy + t / 2];
        double[] Vt(double xx, double y0, double y1) => [xx, y0 + g, xx + t / 2, y0 + t / 2 + g, xx + t / 2, y1 - t / 2 - g, xx, y1 - g, xx - t / 2, y1 - t / 2 - g, xx - t / 2, y0 + t / 2 + g];
        var mid = y + h / 2;
        if ((mask & 0x01) != 0) result.Add(Hz(y + t / 2));
        if ((mask & 0x02) != 0) result.Add(Vt(x + w - t / 2, y + t / 2, mid));
        if ((mask & 0x04) != 0) result.Add(Vt(x + w - t / 2, mid, y + h - t / 2));
        if ((mask & 0x08) != 0) result.Add(Hz(y + h - t / 2));
        if ((mask & 0x10) != 0) result.Add(Vt(x + t / 2, mid, y + h - t / 2));
        if ((mask & 0x20) != 0) result.Add(Vt(x + t / 2, y + t / 2, mid));
        if ((mask & 0x40) != 0) result.Add(Hz(mid));
        return result;
    }

    /// <summary>Configuration read on every call.</summary>
    public NumericDisplayConfig Config { get; } = config ?? new NumericDisplayConfig();
    /// <summary>Value to show; null or a non-finite number shows a dash.</summary>
    public double? Value { get; set; } = 0;
    /// <summary>Sets the value.</summary>
    public void SetValue(double? v) => Value = v;

    /// <summary>Right-aligned text in the digit cells; "-" for null, "E" fill on overflow.</summary>
    public string Text()
    {
        var c = Config;
        if (Value is not { } v || !double.IsFinite(v)) return "-".PadLeft(c.Digits, ' ');
        var s = Ticks.FormatNumber(v, c.Decimals);
        if (s.Replace(".", "").Length > c.Digits) return new string('E', c.Digits);
        while (s.Replace(".", "").Length < c.Digits) s = " " + s;
        return s;
    }

    /// <summary>Paints the unlit and lit segments of every cell, the decimal point, the unit and the caption.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme;
        p.Clear(th.Background);
        var labelRoom = c.Label is null ? 0 : th.FontSize + 6;
        var unitRoom = c.Unit is null ? 0 : th.FontSize * 2.2;
        double pad = 6, availW = width - 2 * pad - unitRoom, availH = height - 2 * pad - labelRoom;
        double cellW = Math.Min(availW / (c.Digits + 0.25 * (c.Digits - 1)), availH * 0.6), cellH = Math.Min(availH, cellW / 0.6), gap = cellW * 0.25;
        double x0 = pad + (availW - (c.Digits * cellW + (c.Digits - 1) * gap)) / 2, y0 = pad + (availH - cellH) / 2;
        var text = Text();
        var cell = 0;
        p.Save(); p.Translate(cellH * c.Slant, 0);
        foreach (var ch in text)
        {
            if (ch == '.') { double cx = x0 + cell * (cellW + gap) - gap / 2, cy = y0 + cellH - cellW * 0.08; p.Circle(cx - cellH * c.Slant, cy, cellW * 0.07, new Fill(c.SegmentColor)); continue; }
            var cx2 = x0 + cell * (cellW + gap);
            var mask = SevenSegmentMask(ch);
            p.Save(); p.Translate(-(cellH * c.Slant) * ((y0 + cellH / 2) / cellH), 0);
            foreach (var poly in SegmentPolygons(cx2, y0, cellW, cellH, 0x7f)) p.Polygon(poly, new Fill(c.SegmentColor) { Opacity = c.OffOpacity });
            foreach (var poly in SegmentPolygons(cx2, y0, cellW, cellH, mask)) p.Polygon(poly, new Fill(c.SegmentColor));
            p.Restore();
            cell++;
        }
        p.Restore();
        if (c.Unit is not null) p.Text(c.Unit, width - pad, y0 + cellH, new TextStyle(th.MutedText) { Family = th.FontFamily, Size = th.FontSize * 1.2, Align = TextAlign.Right, Baseline = TextBaseline.Bottom });
        if (c.Label is not null) p.Text(c.Label, width / 2, height - 2, new TextStyle(th.MutedText) { Family = th.FontFamily, Size = th.FontSize, Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
    }
}
