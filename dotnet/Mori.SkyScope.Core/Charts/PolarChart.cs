// Mori.SkyScope — Polar chart (angle in degrees, radius = value) and radar chart (categories at equal angles).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Drawing style of a polar series: joined line, filled polygon, or markers only.</summary>
public enum PolarSeriesKind { Line, Area, Scatter }
/// <summary>One series of a <see cref="PolarChart"/>: a value per angle, or per category in radar mode.</summary>
/// <param name="id">Unique id used in hits and legend toggles.</param>
public sealed class PolarSeriesConfig(string id)
{
    /// <summary>Unique series id, as given to the constructor.</summary>
    public string Id { get; } = id;
    /// <summary>Display name for the legend and tooltip; the id when null.</summary>
    public string? Name { get; set; }
    /// <summary>Drawing style; null means area in radar mode and line otherwise.</summary>
    public PolarSeriesKind? Kind { get; set; }
    /// <summary>Degrees; null for categories (radar) or equally spaced samples.</summary>
    public double[]? Angles { get; set; }
    /// <summary>Radius values, one per angle or category.</summary>
    public double[] Values { get; set; } = [];
    /// <summary>CSS colour; null picks from <see cref="TrendChartConfig.SeriesPalette"/> by series position.</summary>
    public string? Color { get; set; }
    /// <summary>Line width in pixels; null uses <see cref="ChartStyle.SeriesWidth"/>.</summary>
    public double? Width { get; set; }
    /// <summary>Opacity of an area fill (default 0.25).</summary>
    public double? FillOpacity { get; set; }
    /// <summary>Join the last point back to the first (radar default).</summary>
    public bool? Closed { get; set; }
    /// <summary>Marker at each point; null means circles for scatter series and in radar mode, none otherwise.</summary>
    public MarkerShape? Marker { get; set; }
    /// <summary>Marker radius in pixels (default 3.5).</summary>
    public double? MarkerSize { get; set; }
    /// <summary>False removes the series from the chart and the legend entirely.</summary>
    public bool Visible { get; set; } = true;
}
/// <summary>Ring shape: circles, or polygons through the spokes (the radar look).</summary>
public enum PolarGridShape { Circle, Polygon }

/// <summary>Series, angular layout, grid, legend, theme and style of a <see cref="PolarChart"/>.</summary>
public sealed class PolarChartConfig : ILegendConfig
{
    /// <summary>Title centred above the plot; null reserves no title band.</summary>
    public string? Title { get; set; }
    /// <summary>Series in drawing and palette order.</summary>
    public List<PolarSeriesConfig> Series { get; } = [];
    /// <summary>Radar mode: one spoke per category, series values indexed by category.</summary>
    public string[]? Categories { get; set; }
    /// <summary>Lower radius bound; null autoscales (never above zero).</summary>
    public double? Min { get; set; }
    /// <summary>Upper radius bound; null autoscales, niced to the ring count.</summary>
    public double? Max { get; set; }
    /// <summary>Screen angle in degrees where data angle 0 points (0 = up, clockwise).</summary>
    public double StartAngle { get; set; }
    /// <summary>Data angles increase clockwise on screen; false for counter-clockwise.</summary>
    public bool Clockwise { get; set; } = true;
    /// <summary>Spoke spacing in degrees (ignored with categories).</summary>
    public double AngleStep { get; set; } = 30;
    /// <summary>Shape of the rings.</summary>
    public PolarGridShape GridShape { get; set; } = PolarGridShape.Circle;
    /// <summary>Target ring count.</summary>
    public int Rings { get; set; } = 5;
    /// <summary>Legend placement: corners float over the plot, Right and Top reserve space beside it.</summary>
    public LegendPosition Legend { get; set; } = LegendPosition.TopRight;
    /// <summary>Draws rings and spokes.</summary>
    public bool ShowGrid { get; set; } = true;
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
    /// <summary>Pixels within which a point counts as hovered.</summary>
    public double HoverRadius { get; set; } = 12;
}

/// <summary>Centre and radius of the polar plot in pixels, plus the plot, title and legend rectangles.</summary>
public sealed record PolarLayout(double Width, double Height, double Cx, double Cy, double R, Rect Plot, Rect? Title, Rect? Legend);

/// <summary>
/// Polar chart (angle in degrees, radius = value) and radar chart (categories at equal angles). Angles follow the gauge
/// convention on screen: <see cref="PolarChartConfig.StartAngle"/> is where 0° points (0 = up), clockwise by default.
/// Mirrors <c>charts/polar.ts</c>; pinned by <c>spec/fixtures/polar.json</c>.
/// </summary>
public sealed class PolarChart : IDrawable
{
    /// <summary>Configuration read on every call; mutate it and redraw.</summary>
    public PolarChartConfig Config { get; }
    /// <summary>Ids of series hidden through the legend.</summary>
    public HashSet<string> Hidden { get; } = [];
    /// <summary>Point under the pointer, set by <see cref="PointerMove"/>.</summary>
    public ChartHit? Hover { get; set; }
    /// <summary>Id of the legend row under the pointer, or null.</summary>
    public string? LegendHover { get; set; }

    /// <summary>Creates a chart over <paramref name="config"/>, or over a fresh default configuration.</summary>
    public PolarChart(PolarChartConfig? config = null) { Config = config ?? new PolarChartConfig(); }

    /// <summary>Series with the given id, or null.</summary>
    public PolarSeriesConfig? Series(string id) => Config.Series.FirstOrDefault(s => s.Id == id);
    /// <summary>Series that are visible and not hidden through the legend, in configuration order.</summary>
    public List<PolarSeriesConfig> VisibleSeries() => Config.Series.Where(s => s.Visible && !Hidden.Contains(s.Id)).ToList();
    /// <summary>Effective kind: the explicit one, else area in radar mode and line otherwise.</summary>
    public PolarSeriesKind KindOf(PolarSeriesConfig s) => s.Kind ?? (Config.Categories is not null ? PolarSeriesKind.Area : PolarSeriesKind.Line);
    /// <summary>Display name: the name when set, else the id.</summary>
    public string SeriesName(PolarSeriesConfig s) => s.Name ?? s.Id;
    /// <summary>The explicit colour, or the palette entry for the series position.</summary>
    public string SeriesColor(PolarSeriesConfig s) => s.Color ?? TrendChartConfig.SeriesPalette[Config.Series.IndexOf(s) % TrendChartConfig.SeriesPalette.Length];
    /// <summary>Whether the polyline joins back to its first point: explicit, else true in radar mode and for areas.</summary>
    public bool IsClosed(PolarSeriesConfig s) => s.Closed ?? (Config.Categories is not null || KindOf(s) == PolarSeriesKind.Area);
    /// <summary>Effective marker: explicit, else circles for scatter series and in radar mode, none otherwise.</summary>
    public MarkerShape MarkerOf(PolarSeriesConfig s) => s.Marker ?? (KindOf(s) == PolarSeriesKind.Scatter || Config.Categories is not null ? MarkerShape.Circle : MarkerShape.None);
    /// <summary>Number of points, limited by the category or angle count when present.</summary>
    public int Count(PolarSeriesConfig s) => Config.Categories is { } cats ? Math.Min(s.Values.Length, cats.Length) : s.Angles is { } a ? Math.Min(a.Length, s.Values.Length) : s.Values.Length;
    /// <summary>Hides or shows a series as a legend click would.</summary>
    public void ToggleSeries(string id) { if (!Hidden.Remove(id)) Hidden.Add(id); }
    /// <summary>One legend entry per visible series, the swatch shape matching the effective kind.</summary>
    public List<LegendItem> LegendItems() => Config.Series.Where(s => s.Visible).Select(s => new LegendItem(s.Id, SeriesName(s), SeriesColor(s), Hidden.Contains(s.Id),
        KindOf(s) == PolarSeriesKind.Area ? LegendShape.Box : KindOf(s) == PolarSeriesKind.Scatter ? LegendShape.Circle : LegendShape.Line)).ToList();

    /// <summary>Data angle (degrees) of point <paramref name="i"/>: explicit, category spoke, or equally spaced over 360°.</summary>
    public double AngleOf(PolarSeriesConfig s, int i)
    {
        if (Config.Categories is { } cats) return i * 360.0 / Math.Max(1, cats.Length);
        if (s.Angles is { } a) return a[i];
        return i * 360.0 / Math.Max(1, s.Values.Length);
    }
    /// <summary>Data angle → screen angle (gauge convention).</summary>
    public double ScreenAngle(double deg) => Config.StartAngle + (Config.Clockwise ? deg : -deg);

    /// <summary>Radius range: the data extent including zero, niced to the ring count unless both bounds are fixed.</summary>
    public (double, double) RDomain()
    {
        var c = Config;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var s in VisibleSeries()) for (var i = 0; i < Count(s); i++) { var v = s.Values[i]; if (v < lo) lo = v; if (v > hi) hi = v; }
        if (!double.IsFinite(lo)) { lo = 0; hi = 1; }
        if (lo > 0) lo = 0;
        if (hi <= lo) hi = lo + 1;
        if (!(c.Min is not null && c.Max is not null)) (lo, hi) = Ticks.NiceLinearDomain(lo, hi, c.Rings);
        if (c.Min is { } mn) lo = mn;
        if (c.Max is { } mx) hi = mx;
        return (lo, hi);
    }
    /// <summary>Pixel scale from the radius domain onto [0, R].</summary>
    public LinearScale RScale(PolarLayout l) { var (d0, d1) = RDomain(); return new LinearScale(d0, d1, 0, l.R); }
    /// <summary>Data angles of the spokes: one per category, else every <see cref="PolarChartConfig.AngleStep"/> degrees from 0.</summary>
    public List<double> GridAngles()
    {
        var c = Config; var o = new List<double>();
        if (c.Categories is { } cats) { for (var i = 0; i < cats.Length; i++) o.Add(i * 360.0 / cats.Length); return o; }
        for (var a = 0.0; a < 360 - 1e-9; a += Math.Max(1, c.AngleStep)) o.Add(a);
        return o;
    }

    /// <summary>Bands and radius for a canvas size, leaving room for the spoke labels.</summary>
    public PolarLayout Layout(double width, double height)
    {
        var c = Config; var m = c.Margin;
        var outer = new Rect(m, m, Math.Max(0, width - 2 * m), Math.Max(0, height - 2 * m));
        Rect? title = null;
        if (c.Title is not null) { title = new Rect(outer.X, outer.Y, outer.W, c.TitleHeight); outer = new Rect(outer.X, outer.Y + c.TitleHeight, outer.W, Math.Max(0, outer.H - c.TitleHeight)); }
        var items = LegendItems().Count;
        var (plot, reserved) = SimpleLegend.Reserve(c, outer, items);
        var r = Math.Max(0, Math.Min(plot.W, plot.H) / 2 - c.Theme.FontSize * 2.2);
        return new PolarLayout(width, height, plot.X + plot.W / 2, plot.Y + plot.H / 2, r, plot, title, reserved ?? SimpleLegend.OverlayRect(c, plot, items));
    }

    /// <summary>Pixel position of (data angle, value); values below the domain start collapse to the centre.</summary>
    public (double X, double Y) Point(PolarLayout l, double angleDeg, double value) => GaugeMath.Polar(l.Cx, l.Cy, Math.Max(0, RScale(l).Apply(value)), ScreenAngle(angleDeg));
    /// <summary>Interleaved pixel polyline of a series, closed by repeating the first point when applicable.</summary>
    public List<double> PixelPoints(PolarSeriesConfig s, PolarLayout l)
    {
        var o = new List<double>(); var n = Count(s);
        for (var i = 0; i < n; i++) { var pt = Point(l, AngleOf(s, i), s.Values[i]); o.Add(pt.X); o.Add(pt.Y); }
        if (IsClosed(s) && n > 1) { o.Add(o[0]); o.Add(o[1]); }
        return o;
    }

    /// <summary>Nearest point within <see cref="PolarChartConfig.HoverRadius"/>, or null.</summary>
    public ChartHit? HitTest(double x, double y, PolarLayout l)
    {
        ChartHit? best = null; var bestD = Config.HoverRadius * Config.HoverRadius;
        foreach (var s in VisibleSeries()) for (var i = 0; i < Count(s); i++)
        {
            var pt = Point(l, AngleOf(s, i), s.Values[i]); double dx = pt.X - x, dy = pt.Y - y, d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = new ChartHit(s.Id, i); }
        }
        return best;
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
        ToggleSeries(id);
        return true;
    }
    /// <summary>Spoke label for a data angle: the category name or the angle in whole degrees.</summary>
    public string AngleLabel(double deg)
    {
        if (Config.Categories is { } cats) { var i = (int)Math.Floor(deg * cats.Length / 360 + 0.5); return i >= 0 && i < cats.Length ? cats[i] : ""; }
        return $"{(int)Math.Floor(deg + 0.5)}°";
    }

    /// <summary>Paints title, grid, spoke and ring labels, areas, lines, markers, hover ring, legend and tooltip.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var st = c.Style; var l = Layout(width, height);
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        var muted = text with { Color = th.MutedText };
        var grid = new Stroke(th.Grid) { Width = st.GridWidth, Dash = st.GridDash };
        p.Clear(th.Background);
        if (l.Title is { } tr && c.Title is not null) p.Text(c.Title, tr.X + tr.W / 2, tr.Y + tr.H / 2, text with { Size = th.FontSize + 3, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        var angles = GridAngles(); var rs = RScale(l); var ringTicks = rs.TickValues(c.Rings);
        double[] RingPoly(double rr) { var o = new List<double>(); foreach (var a in angles) { var pt = GaugeMath.Polar(l.Cx, l.Cy, rr, ScreenAngle(a)); o.Add(pt.X); o.Add(pt.Y); } return o.ToArray(); }
        var poly = c.GridShape == PolarGridShape.Polygon && angles.Count > 2;
        if (poly) p.Polygon(RingPoly(l.R), new Fill(th.PlotBackground)); else p.Circle(l.Cx, l.Cy, l.R, new Fill(th.PlotBackground));
        if (c.ShowGrid)
        {
            foreach (var v in ringTicks)
            {
                var rr = rs.Apply(v);
                if (rr <= 0) continue;
                if (poly) p.Polygon(RingPoly(rr), null, grid); else p.Circle(l.Cx, l.Cy, rr, null, grid);
            }
            foreach (var a in angles) { var pt = GaugeMath.Polar(l.Cx, l.Cy, l.R, ScreenAngle(a)); p.Line(l.Cx, l.Cy, pt.X, pt.Y, grid); }
        }
        foreach (var a in angles)
        {
            var pt = GaugeMath.Polar(l.Cx, l.Cy, l.R + th.FontSize * 0.9, ScreenAngle(a)); var dx = pt.X - l.Cx;
            p.Text(AngleLabel(a), pt.X, pt.Y, text with { Align = Math.Abs(dx) < 1 ? TextAlign.Center : dx > 0 ? TextAlign.Left : TextAlign.Right, Baseline = TextBaseline.Middle });
        }
        foreach (var v in ringTicks) { var rr = rs.Apply(v); if (rr <= 0) continue; var pt = GaugeMath.Polar(l.Cx, l.Cy, rr, ScreenAngle(0)); p.Text(rs.Format(v, c.Rings), pt.X + 3, pt.Y, muted with { Size = th.FontSize - 1, Baseline = TextBaseline.Middle }); }
        var vis = VisibleSeries();
        foreach (var s in vis) if (KindOf(s) == PolarSeriesKind.Area && Count(s) > 2) p.Polygon(PixelPoints(s, l).ToArray(), new Fill(SeriesColor(s)) { Opacity = s.FillOpacity ?? 0.25 });
        foreach (var s in vis) if (KindOf(s) != PolarSeriesKind.Scatter && Count(s) > 1) p.Polyline(PixelPoints(s, l).ToArray(), new Stroke(SeriesColor(s)) { Width = s.Width ?? st.SeriesWidth, Join = LineJoin.Round });
        foreach (var s in vis)
        {
            var marker = MarkerOf(s);
            if (marker == MarkerShape.None) continue;
            for (var i = 0; i < Count(s); i++) { var pt = Point(l, AngleOf(s, i), s.Values[i]); ChartDrawing.Marker(p, marker, pt.X, pt.Y, s.MarkerSize ?? 3.5, SeriesColor(s)); }
        }
        if (Hover is { } hv && Series(hv.SeriesId) is { } hs)
        {
            var pt = Point(l, AngleOf(hs, hv.Index), hs.Values[hv.Index]);
            p.Circle(pt.X, pt.Y, (hs.MarkerSize ?? 3.5) + 3, null, new Stroke(SeriesColor(hs)) { Width = 2 });
            if (l.Legend is { } lg2) SimpleLegend.Draw(p, c, lg2, LegendItems(), LegendHover);
            ChartDrawing.TooltipBox(p, th, st, l.Plot, new Tooltip(pt.X, pt.Y, AngleLabel(AngleOf(hs, hv.Index)), [new TooltipLine(SeriesName(hs), TrendChartRenderer.FormatValue(hs.Values[hv.Index]), SeriesColor(hs))]));
            return;
        }
        if (l.Legend is { } lg) SimpleLegend.Draw(p, c, lg, LegendItems(), LegendHover);
    }
}
