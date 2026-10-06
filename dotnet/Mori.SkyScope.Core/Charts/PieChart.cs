// Mori.SkyScope — Pie / donut chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Pie / donut chart. Angles follow the gauge convention (degrees, 0 = up, clockwise). Mirrors <c>charts/pie.ts</c>; pinned by <c>spec/fixtures/pie.json</c>.</summary>
public sealed class PieSliceConfig(string id, double value)
{
    /// <summary>Unique slice id, as given to the constructor.</summary>
    public string Id { get; } = id;
    /// <summary>Legend and label name; the id when null.</summary>
    public string? Name { get; set; }
    /// <summary>Weight of the slice; slices with a value of zero or less are not drawn.</summary>
    public double Value { get; set; } = value;
    /// <summary>CSS colour; null picks from <see cref="TrendChartConfig.SeriesPalette"/> by slice position.</summary>
    public string? Color { get; set; }
}
/// <summary>What a slice label shows: nothing, the share in percent, the value, or the name.</summary>
public enum PieLabelMode { None, Percent, Value, Name }
/// <summary>Labels inside the slice, or outside it with a leader line.</summary>
public enum PieLabelPosition { Inside, Outside }

/// <summary>Slices, geometry, labelling, legend, theme and style of a <see cref="PieChart"/>.</summary>
public sealed class PieChartConfig : ILegendConfig
{
    /// <summary>Title centred above the pie; null reserves no title band.</summary>
    public string? Title { get; set; }
    /// <summary>Slices in configuration (palette) order.</summary>
    public List<PieSliceConfig> Slices { get; } = [];
    /// <summary>Inner radius as a fraction of the outer one; 0 = pie.</summary>
    public double Donut { get; set; }
    /// <summary>Angle in degrees where the first slice starts (0 = up, clockwise).</summary>
    public double StartAngle { get; set; }
    /// <summary>Degrees of gap between slices.</summary>
    public double PadAngle { get; set; }
    /// <summary>What slice labels show.</summary>
    public PieLabelMode Labels { get; set; } = PieLabelMode.Percent;
    /// <summary>Where slice labels sit; outside labels shrink the pie to make room.</summary>
    public PieLabelPosition LabelPosition { get; set; } = PieLabelPosition.Inside;
    /// <summary>Slices under this fraction get no inside label.</summary>
    public double MinLabelFraction { get; set; } = 0.04;
    /// <summary>Draws slices in descending value order instead of configuration order.</summary>
    public bool Sort { get; set; }
    /// <summary>Text in the middle; null shows the total for donuts and nothing for pies.</summary>
    public string? CenterText { get; set; }
    /// <summary>Hovered slice offset as a fraction of the radius.</summary>
    public double HoverExplode { get; set; } = 0.04;
    /// <summary>Decimal places of value and percentage labels.</summary>
    public int Decimals { get; set; }
    /// <summary>Legend placement: corners float over the plot, Right and Top reserve space beside it.</summary>
    public LegendPosition Legend { get; set; } = LegendPosition.Right;
    /// <summary>Colours and type.</summary>
    public ChartTheme Theme { get; set; } = ChartTheme.Light;
    /// <summary>Widths, dashes, opacities and paddings.</summary>
    public ChartStyle Style { get; set; } = ChartStyle.Default;
    /// <summary>Pixels between the outer edge and the first band.</summary>
    public double Margin { get; set; } = 8;
    /// <summary>Pixels reserved for the title band when a title is set.</summary>
    public double TitleHeight { get; set; } = 22;
    /// <summary>Width in pixels of a side or overlay legend.</summary>
    public double LegendWidth { get; set; } = 150;
}

/// <summary>Angular geometry of one visible slice: its value, its share <paramref name="Frac"/> of the total, and start, end and middle angles in degrees (0 = up, clockwise).</summary>
public sealed record PieSliceGeometry(string Id, double Value, double Frac, double Start, double End, double Mid);
/// <summary>Centre, outer radius <paramref name="R"/> and inner radius <paramref name="Inner"/> of the pie in pixels, plus the plot, title and legend rectangles.</summary>
public sealed record PieLayout(double Width, double Height, double Cx, double Cy, double R, double Inner, Rect Plot, Rect? Title, Rect? Legend);

/// <summary>Headless pie/donut state and drawing: hidden slices, hover, geometry, hit testing and painting.</summary>
public sealed class PieChart : IDrawable
{
    /// <summary>Configuration read on every call; mutate it and redraw.</summary>
    public PieChartConfig Config { get; }
    /// <summary>Ids of slices hidden through the legend.</summary>
    public HashSet<string> Hidden { get; } = [];
    /// <summary>Id of the slice under the pointer, or null.</summary>
    public string? Hover { get; set; }
    /// <summary>Id of the legend row under the pointer, or null.</summary>
    public string? LegendHover { get; set; }

    /// <summary>Creates a chart over <paramref name="config"/>, or over a fresh default configuration.</summary>
    public PieChart(PieChartConfig? config = null) { Config = config ?? new PieChartConfig(); }

    /// <summary>Slice with the given id, or null.</summary>
    public PieSliceConfig? Slice(string id) => Config.Slices.FirstOrDefault(s => s.Id == id);
    /// <summary>Display name: the name when set, else the id.</summary>
    public string SliceName(PieSliceConfig s) => s.Name ?? s.Id;
    /// <summary>The explicit colour, or the palette entry for the slice position.</summary>
    public string SliceColor(PieSliceConfig s) => s.Color ?? TrendChartConfig.SeriesPalette[Config.Slices.IndexOf(s) % TrendChartConfig.SeriesPalette.Length];
    /// <summary>Hides or shows a slice as a legend click would.</summary>
    public void ToggleSlice(string id) { if (!Hidden.Remove(id)) Hidden.Add(id); }
    /// <summary>Slices that are not hidden and have a positive value, sorted by value when configured.</summary>
    public List<PieSliceConfig> VisibleSlices()
    {
        var o = Config.Slices.Where(s => !Hidden.Contains(s.Id) && s.Value > 0).ToList();
        if (Config.Sort) o = o.OrderByDescending(s => s.Value).ToList();
        return o;
    }
    /// <summary>Sum of the visible slice values.</summary>
    public double Total() { double t = 0; foreach (var s in VisibleSlices()) t += s.Value; return t; }
    /// <summary>One legend entry per configured slice, hidden ones dimmed.</summary>
    public List<LegendItem> LegendItems() => Config.Slices.Select(s => new LegendItem(s.Id, SliceName(s), SliceColor(s), Hidden.Contains(s.Id), LegendShape.Box)).ToList();

    /// <summary>Slice angles in degrees (gauge convention), pad applied symmetrically inside each slice.</summary>
    public List<PieSliceGeometry> Slices()
    {
        var total = Total(); var o = new List<PieSliceGeometry>();
        if (total <= 0) return o;
        var a = Config.StartAngle;
        foreach (var s in VisibleSlices())
        {
            double frac = s.Value / total, span = frac * 360, pad = Math.Min(Config.PadAngle, span);
            o.Add(new PieSliceGeometry(s.Id, s.Value, frac, a + pad / 2, a + span - pad / 2, a + span / 2));
            a += span;
        }
        return o;
    }

    /// <summary>Bands and radii for a canvas size; outside labels reduce the radius to 70 % of the half extent.</summary>
    public PieLayout Layout(double width, double height)
    {
        var c = Config; var m = c.Margin;
        var outer = new Rect(m, m, Math.Max(0, width - 2 * m), Math.Max(0, height - 2 * m));
        Rect? title = null;
        if (c.Title is not null) { title = new Rect(outer.X, outer.Y, outer.W, c.TitleHeight); outer = new Rect(outer.X, outer.Y + c.TitleHeight, outer.W, Math.Max(0, outer.H - c.TitleHeight)); }
        var items = LegendItems().Count;
        var (plot, reserved) = SimpleLegend.Reserve(c, outer, items);
        var outside = c.Labels != PieLabelMode.None && c.LabelPosition == PieLabelPosition.Outside;
        var r = Math.Max(0, Math.Min(plot.W, plot.H) / 2 * (outside ? 0.7 : 0.92));
        return new PieLayout(width, height, plot.X + plot.W / 2, plot.Y + plot.H / 2, r, r * c.Donut, plot, title, reserved ?? SimpleLegend.OverlayRect(c, plot, items));
    }

    /// <summary>Slice under (x, y): between the inner and outer radius and inside the slice's angular span.</summary>
    public string? HitTest(double x, double y, PieLayout l)
    {
        double dx = x - l.Cx, dy = y - l.Cy, d = Math.Sqrt(dx * dx + dy * dy);
        if (d > l.R || d < l.Inner) return null;
        var ang = ((Math.Atan2(dy, dx) * 180 / Math.PI + 90) % 360 + 360) % 360;
        foreach (var s in Slices())
        {
            var start = (s.Start % 360 + 360) % 360;
            if (((ang - start) % 360 + 360) % 360 <= s.End - s.Start) return s.Id;
        }
        return null;
    }
    /// <summary>Updates hover and legend hover for a pointer position.</summary>
    public void PointerMove(double x, double y, double width, double height)
    {
        var l = Layout(width, height);
        Hover = HitTest(x, y, l);
        LegendHover = SimpleLegend.ItemAt(Config, l.Legend, LegendItems(), x, y);
    }
    /// <summary>Clears hover and legend hover.</summary>
    public void PointerLeave() { Hover = null; LegendHover = null; }
    /// <summary>Click: toggles a legend item; returns true when it consumed the click.</summary>
    public bool Click(double x, double y, double width, double height)
    {
        var l = Layout(width, height);
        var id = SimpleLegend.ItemAt(Config, l.Legend, LegendItems(), x, y);
        if (id is null) return false;
        ToggleSlice(id);
        return true;
    }

    /// <summary>Label text of a slice under the configured mode; null for <see cref="PieLabelMode.None"/>.</summary>
    public string? LabelFor(PieSliceGeometry g)
    {
        var c = Config; var s = Slice(g.Id)!;
        return c.Labels switch
        {
            PieLabelMode.Percent => Ticks.FormatNumber(g.Frac * 100, c.Decimals) + "%",
            PieLabelMode.Value => Ticks.FormatNumber(g.Value, c.Decimals),
            PieLabelMode.Name => SliceName(s),
            _ => null,
        };
    }

    /// <summary>Paints title, slices (the hovered one exploded), labels, centre text, legend and tooltip.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var l = Layout(width, height);
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        p.Clear(th.Background);
        if (l.Title is { } tr && c.Title is not null) p.Text(c.Title, tr.X + tr.W / 2, tr.Y + tr.H / 2, text with { Size = th.FontSize + 3, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        var slices = Slices();
        foreach (var g in slices)
        {
            var s = Slice(g.Id)!; var hot = Hover == g.Id || LegendHover == g.Id;
            var off = hot ? GaugeMath.Polar(0, 0, l.R * c.HoverExplode, g.Mid) : (0, 0);
            p.Sector(l.Cx + off.Item1, l.Cy + off.Item2, l.Inner, l.R, GaugeMath.ArcAngle(g.Start), GaugeMath.ArcAngle(g.End), new Fill(SliceColor(s)) { Opacity = hot ? 1 : 0.92 }, new Stroke(th.Background));
        }
        if (c.Labels != PieLabelMode.None)
        {
            foreach (var g in slices)
            {
                var label = LabelFor(g);
                if (label is null) continue;
                if (c.LabelPosition == PieLabelPosition.Inside)
                {
                    if (g.Frac < c.MinLabelFraction) continue;
                    var pt = GaugeMath.Polar(l.Cx, l.Cy, (l.Inner + l.R) / 2, g.Mid);
                    p.Text(label, pt.X, pt.Y, text with { Color = th.Background, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
                }
                else
                {
                    var a = GaugeMath.Polar(l.Cx, l.Cy, l.R * 1.02, g.Mid); var b = GaugeMath.Polar(l.Cx, l.Cy, l.R * 1.12, g.Mid); var right = b.X >= l.Cx;
                    p.Line(a.X, a.Y, b.X, b.Y, new Stroke(th.Axis));
                    p.Line(b.X, b.Y, b.X + (right ? 8 : -8), b.Y, new Stroke(th.Axis));
                    p.Text(label, b.X + (right ? 11 : -11), b.Y, text with { Align = right ? TextAlign.Left : TextAlign.Right, Baseline = TextBaseline.Middle });
                }
            }
        }
        var center = c.CenterText ?? (c.Donut > 0 ? Ticks.FormatNumber(Total(), c.Decimals) : null);
        if (center is not null) p.Text(center, l.Cx, l.Cy, text with { Size = th.FontSize + 5, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        if (l.Legend is { } lg) SimpleLegend.Draw(p, c, lg, LegendItems(), LegendHover);
        if (Hover is not null && slices.FirstOrDefault(x => x.Id == Hover) is { } hg)
        {
            var s = Slice(hg.Id)!; var pt = GaugeMath.Polar(l.Cx, l.Cy, l.R * 0.8, hg.Mid);
            ChartDrawing.TooltipBox(p, th, c.Style, l.Plot, new Tooltip(pt.X, pt.Y, SliceName(s), [new TooltipLine(Ticks.FormatNumber(hg.Value, c.Decimals), Ticks.FormatNumber(hg.Frac * 100, 1) + "%", SliceColor(s))]));
        }
    }
}
