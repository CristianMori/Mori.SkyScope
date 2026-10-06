// Mori.SkyScope — Analytic XY charts: line, step, scatter, area and bars (grouped or stacked) sharing axes, legend, tooltip and zoom.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>How a series is drawn: joined line, staircase (each value held until the next), markers only, filled area down to its stack base, or bars.</summary>
public enum CartesianSeriesKind { Line, Step, Scatter, Area, Bar }
/// <summary>Which value axis a series is scaled against: the left axis (Y) or the optional right axis (Y2).</summary>
public enum YAxisId { Y, Y2 }

/// <summary>Settings of one axis of a <see cref="CartesianChart"/>; bounds are in data units and default to autoscale.</summary>
public sealed class CartesianAxisConfig
{
    /// <summary>Axis title drawn beside the axis; falls back to <see cref="Unit"/> when null.</summary>
    public string? Label { get; set; }
    /// <summary>Unit text appended to values in tooltips and used as the axis title when <see cref="Label"/> is null.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed bounds; null for autoscale (data range, niced).</summary>
    public double? Min { get; set; }
    /// <summary>Upper fixed bound; null for autoscale.</summary>
    public double? Max { get; set; }
    /// <summary>Base-10 logarithmic scale; the domain must then be positive and is not niced.</summary>
    public bool Log { get; set; }
    /// <summary>Category axis (x only): positions are indices, labels come from here.</summary>
    public string[]? Categories { get; set; }
    /// <summary>Forces an autoscaled value domain to include zero (bar and area series always do).</summary>
    public bool IncludeZero { get; set; }
}

/// <summary>One data series of a <see cref="CartesianChart"/>.</summary>
/// <param name="id">Unique id used in hits, legend toggles and tooltips.</param>
public sealed class CartesianSeriesConfig(string id)
{
    /// <summary>Unique series id, as given to the constructor.</summary>
    public string Id { get; } = id;
    /// <summary>Display name for the legend and tooltip; the id when null.</summary>
    public string? Name { get; set; }
    /// <summary>Drawing style; a line by default.</summary>
    public CartesianSeriesKind Kind { get; set; } = CartesianSeriesKind.Line;
    /// <summary>Null for index positions (0, 1, 2…) — the natural fit for category axes.</summary>
    public double[]? X { get; set; }
    /// <summary>Y values in data units; when <see cref="X"/> is set too, the point count is the shorter of the two.</summary>
    public double[] Y { get; set; } = [];
    /// <summary>CSS colour; null picks from <see cref="TrendChartConfig.SeriesPalette"/> by series position.</summary>
    public string? Color { get; set; }
    /// <summary>Line width in pixels; null uses <see cref="ChartStyle.SeriesWidth"/>.</summary>
    public double? Width { get; set; }
    /// <summary>Value axis the series is scaled against (left by default).</summary>
    public YAxisId Axis { get; set; } = YAxisId.Y;
    /// <summary>Series with the same stack key (same axis) stack on top of each other (bars and areas).</summary>
    public string? Stack { get; set; }
    /// <summary>Marker drawn at each point; null means circles for scatter series and none otherwise.</summary>
    public MarkerShape? Marker { get; set; }
    /// <summary>Marker radius in pixels (default 4).</summary>
    public double? MarkerSize { get; set; }
    /// <summary>Opacity of area fills and bars; defaults to 0.25 for areas and 0.85 for bars (1 while hovered).</summary>
    public double? FillOpacity { get; set; }
    /// <summary>Bar width in x units; default fills <c>1 − BarGap</c> of the slot between bar positions.</summary>
    public double? BarWidth { get; set; }
    /// <summary>False removes the series from the chart and the legend entirely, unlike hiding it through the legend.</summary>
    public bool Visible { get; set; } = true;
}

/// <summary>Axes, series, legend placement, theme, style and the pixel sizes of the layout bands of a <see cref="CartesianChart"/>.</summary>
public sealed class CartesianChartConfig : ILegendConfig
{
    /// <summary>Title centred above the plot; null reserves no title band.</summary>
    public string? Title { get; set; }
    /// <summary>Horizontal axis settings (categories live here).</summary>
    public CartesianAxisConfig XAxis { get; set; } = new();
    /// <summary>Left value axis settings.</summary>
    public CartesianAxisConfig YAxis { get; set; } = new();
    /// <summary>Right value axis settings; null unless a series uses <see cref="YAxisId.Y2"/>.</summary>
    public CartesianAxisConfig? Y2Axis { get; set; }
    /// <summary>Series in drawing and palette order.</summary>
    public List<CartesianSeriesConfig> Series { get; } = [];
    /// <summary>Fraction of a bar slot left empty.</summary>
    public double BarGap { get; set; } = 0.2;
    /// <summary>Legend placement: corners float over the plot, Right and Top reserve space beside it.</summary>
    public LegendPosition Legend { get; set; } = LegendPosition.TopRight;
    /// <summary>Draws grid lines at the primary axes' ticks, subject to <see cref="ChartStyle.ShowValueGrid"/> and <see cref="ChartStyle.ShowTimeGrid"/>.</summary>
    public bool ShowGrid { get; set; } = true;
    /// <summary>Colours and type.</summary>
    public ChartTheme Theme { get; set; } = ChartTheme.Light;
    /// <summary>Widths, dashes, opacities and paddings.</summary>
    public ChartStyle Style { get; set; } = ChartStyle.Default;
    /// <summary>Pixels between the outer edge and the first band.</summary>
    public double Margin { get; set; } = 8;
    /// <summary>Pixels reserved for each value axis column.</summary>
    public double YAxisWidth { get; set; } = 48;
    /// <summary>Pixels reserved for the x-axis ticks; an axis label adds one text line.</summary>
    public double XAxisHeight { get; set; } = 24;
    /// <summary>Pixels reserved for the title band when a title is set.</summary>
    public double TitleHeight { get; set; } = 22;
    /// <summary>Width in pixels of a side or overlay legend.</summary>
    public double LegendWidth { get; set; } = 150;
    /// <summary>Target pixels per tick on the x axis; value axes use 0.6 of it.</summary>
    public double TickSpacing { get; set; } = 80;
    /// <summary>Pixels within which a point counts as hovered.</summary>
    public double HoverRadius { get; set; } = 12;
}

/// <summary>Pixel rectangles of the chart bands for one canvas size, from <see cref="CartesianChart.Layout"/>.</summary>
/// <param name="Width">Canvas width.</param>
/// <param name="Height">Canvas height.</param>
/// <param name="Plot">Data area.</param>
/// <param name="YAxis">Left axis strip.</param>
/// <param name="Y2Axis">Right axis strip; null when no visible series uses Y2.</param>
/// <param name="XAxis">Band under the plot for ticks and the axis label.</param>
/// <param name="Title">Title band; null without a title.</param>
/// <param name="Legend">Legend box; null when there is none.</param>
public sealed record CartesianLayout(double Width, double Height, Rect Plot, Rect YAxis, Rect? Y2Axis, Rect XAxis, Rect? Title, Rect? Legend);
/// <summary>Pixel rectangle of one bar with the series id and point index it belongs to, for drawing and hit testing.</summary>
public sealed record BarRect(string SeriesId, int Index, double X, double Y, double W, double H);
/// <summary>Rubber-band rectangle in pixels from the press point (X0, Y0) to the current pointer (X1, Y1); corners are not normalised.</summary>
public sealed record ZoomBox(double X0, double Y0, double X1, double Y1);

/// <summary>
/// Static/analytic XY charts — line, step, scatter, area, bar (grouped by default, stacked via <see cref="CartesianSeriesConfig.Stack"/>) —
/// sharing axes, grid, legend, theme and style with the TrendChart. Mirrors <c>charts/cartesian.ts</c>; pinned by <c>spec/fixtures/cartesian.json</c>.
/// </summary>
public sealed class CartesianChart : IDrawable
{
    /// <summary>Configuration read on every call; mutate it and redraw.</summary>
    public CartesianChartConfig Config { get; }
    /// <summary>Ids of series hidden through the legend; they stay listed, dimmed.</summary>
    public HashSet<string> Hidden { get; } = [];
    /// <summary>Point under the pointer, set by <see cref="PointerMove"/>.</summary>
    public ChartHit? Hover { get; set; }
    /// <summary>Id of the legend row under the pointer, or null.</summary>
    public string? LegendHover { get; set; }
    /// <summary>Rubber band while box-zooming (pixels).</summary>
    public ZoomBox? Box { get; set; }
    /// <summary>X domain override after a zoom; null for autoscale.</summary>
    public (double, double)? ZoomX { get; set; }
    /// <summary>Left value domain override after a zoom; null for autoscale.</summary>
    public (double, double)? ZoomY { get; set; }
    /// <summary>Right value domain override after a zoom; null for autoscale.</summary>
    public (double, double)? ZoomY2 { get; set; }

    /// <summary>Creates a chart over <paramref name="config"/>, or over a fresh default configuration.</summary>
    public CartesianChart(CartesianChartConfig? config = null) { Config = config ?? new CartesianChartConfig(); }

    // ---- series helpers ----
    /// <summary>Series with the given id, or null.</summary>
    public CartesianSeriesConfig? Series(string id) => Config.Series.FirstOrDefault(s => s.Id == id);
    /// <summary>Series that are visible and not hidden through the legend, in configuration order.</summary>
    public List<CartesianSeriesConfig> VisibleSeries() => Config.Series.Where(s => s.Visible && !Hidden.Contains(s.Id)).ToList();
    /// <summary>Display name: the name when set, else the id.</summary>
    public string SeriesName(CartesianSeriesConfig s) => s.Name ?? s.Id;
    /// <summary>The explicit colour, or the palette entry for the series position.</summary>
    public string SeriesColor(CartesianSeriesConfig s) => s.Color ?? TrendChartConfig.SeriesPalette[Config.Series.IndexOf(s) % TrendChartConfig.SeriesPalette.Length];
    /// <summary>Effective marker: the explicit one, else circles for scatter series and none otherwise.</summary>
    public MarkerShape MarkerOf(CartesianSeriesConfig s) => s.Marker ?? (s.Kind == CartesianSeriesKind.Scatter ? MarkerShape.Circle : MarkerShape.None);
    /// <summary>X of point <paramref name="i"/>: the explicit value or the index.</summary>
    public static double XValue(CartesianSeriesConfig s, int i) => s.X is null ? i : s.X[i];
    /// <summary>Number of points: the shorter of X and Y when both exist.</summary>
    public static int Count(CartesianSeriesConfig s) => s.X is null ? s.Y.Length : Math.Min(s.X.Length, s.Y.Length);
    /// <summary>Hides or shows a series as a legend click would.</summary>
    public void ToggleSeries(string id) { if (!Hidden.Remove(id)) Hidden.Add(id); }
    /// <summary>One legend entry per visible series, the swatch shape matching the series kind.</summary>
    public List<LegendItem> LegendItems() => Config.Series.Where(s => s.Visible).Select(s => new LegendItem(s.Id, SeriesName(s), SeriesColor(s), Hidden.Contains(s.Id),
        s.Kind is CartesianSeriesKind.Bar or CartesianSeriesKind.Area ? LegendShape.Box : s.Kind == CartesianSeriesKind.Scatter ? LegendShape.Circle : LegendShape.Line)).ToList();

    /// <summary>[base, top] for point <paramref name="i"/>: stacked series sit on the same-signed sum of the visible series before them in the stack.</summary>
    public (double Base, double Top) StackedValue(CartesianSeriesConfig s, int i)
    {
        var y = i < s.Y.Length ? s.Y[i] : 0;
        if (s.Stack is null) return s.Kind is CartesianSeriesKind.Bar or CartesianSeriesKind.Area ? (0, y) : (y, y);
        double b = 0;
        foreach (var q in VisibleSeries())
        {
            if (ReferenceEquals(q, s)) break;
            if (q.Stack != s.Stack || q.Axis != s.Axis) continue;
            var v = i < q.Y.Length ? q.Y[i] : 0;
            if (y >= 0 ? v > 0 : v < 0) b += v;
        }
        return (b, b + y);
    }

    // ---- bars ----
    /// <summary>Visible series drawn as bars.</summary>
    public List<CartesianSeriesConfig> BarSeries() => VisibleSeries().Where(s => s.Kind == CartesianSeriesKind.Bar).ToList();
    /// <summary>Smallest distance between distinct bar x positions (1 when there is only one).</summary>
    public double BarSlot()
    {
        var xs = new List<double>();
        foreach (var s in BarSeries()) for (var i = 0; i < Count(s); i++) xs.Add(XValue(s, i));
        xs.Sort();
        var slot = double.PositiveInfinity;
        for (var i = 1; i < xs.Count; i++) { var d = xs[i] - xs[i - 1]; if (d > 0 && d < slot) slot = d; }
        return double.IsFinite(slot) ? slot : 1;
    }
    /// <summary>Slot occupancy: stacked series share a column; others get their own.</summary>
    public List<string> BarGroups()
    {
        var o = new List<string>();
        foreach (var s in BarSeries()) { var k = s.Stack ?? s.Id; if (!o.Contains(k)) o.Add(k); }
        return o;
    }
    /// <summary>Width of a bar group in x units: the widest explicit bar width, else the slot minus the gap.</summary>
    public double BarWidth()
    {
        var slot = BarSlot() * (1 - Config.BarGap);
        double w = 0;
        foreach (var s in BarSeries()) w = Math.Max(w, s.BarWidth ?? slot);
        return w;
    }
    /// <summary>Pixel rectangles of every bar; grouped series split the slot, stacked series share a column.</summary>
    public List<BarRect> Bars(CartesianLayout layout)
    {
        var o = new List<BarRect>();
        var bars = BarSeries();
        if (bars.Count == 0) return o;
        var groups = BarGroups(); var bw = BarWidth(); var each = bw / groups.Count;
        var xs = XScale(layout);
        foreach (var s in bars)
        {
            var gi = groups.IndexOf(s.Stack ?? s.Id); var ys = YScale(s.Axis, layout);
            for (var i = 0; i < Count(s); i++)
            {
                var xv = XValue(s, i); var (b, t) = StackedValue(s, i);
                double x0 = xs.Apply(xv - bw / 2 + gi * each), x1 = xs.Apply(xv - bw / 2 + (gi + 1) * each);
                double y0 = ys.Apply(b), y1 = ys.Apply(t);
                o.Add(new BarRect(s.Id, i, Math.Min(x0, x1), Math.Min(y0, y1), Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
            }
        }
        return o;
    }

    // ---- domains & scales ----
    private bool UsesY2() => Config.Y2Axis is not null && VisibleSeries().Any(s => s.Axis == YAxisId.Y2);

    /// <summary>X data range: the zoom when active, else the category range or the data extent (niced unless bars, log or fixed bounds apply), then fixed bounds.</summary>
    public (double, double) XDomain()
    {
        if (ZoomX is { } z) return z;
        var ax = Config.XAxis;
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        if (ax.Categories is not null) { lo = -0.5; hi = ax.Categories.Length - 0.5; }
        else
        {
            foreach (var s in VisibleSeries()) for (var i = 0; i < Count(s); i++) { var x = XValue(s, i); if (x < lo) lo = x; if (x > hi) hi = x; }
            if (!double.IsFinite(lo)) { lo = 0; hi = 1; }
            if (lo == hi) { lo -= 1; hi += 1; }
            if (BarSeries().Count > 0) { var half = BarWidth() / 2 + BarSlot() * Config.BarGap / 2; lo -= half; hi += half; }
            else if (ax.Min is null && ax.Max is null && !ax.Log) (lo, hi) = Ticks.NiceLinearDomain(lo, hi, 10);
        }
        if (ax.Min is { } mn) lo = mn;
        if (ax.Max is { } mx) hi = mx;
        return (lo, hi);
    }

    /// <summary>Value range of an axis: the zoom when active, else the stacked data extent (zero included for bars and areas, padded when flat, niced), then fixed bounds.</summary>
    public (double, double) YDomain(YAxisId axis)
    {
        var zoom = axis == YAxisId.Y ? ZoomY : ZoomY2;
        if (zoom is { } z) return z;
        var ax = (axis == YAxisId.Y2 ? Config.Y2Axis : Config.YAxis) ?? new CartesianAxisConfig();
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity; var zero = ax.IncludeZero;
        foreach (var s in VisibleSeries())
        {
            if (s.Axis != axis) continue;
            if (s.Kind is CartesianSeriesKind.Bar or CartesianSeriesKind.Area) zero = true;
            for (var i = 0; i < Count(s); i++) { var (b, t) = StackedValue(s, i); double l = Math.Min(b, t), h = Math.Max(b, t); if (l < lo) lo = l; if (h > hi) hi = h; }
        }
        if (!double.IsFinite(lo)) { lo = 0; hi = 1; }
        if (zero && !ax.Log) { if (lo > 0) lo = 0; if (hi < 0) hi = 0; }
        if (lo == hi) { if (lo == 0) hi = 1; else { lo -= Math.Abs(lo) * 0.1; hi += Math.Abs(hi) * 0.1; } }
        if (!ax.Log && !(ax.Min is not null && ax.Max is not null)) (lo, hi) = Ticks.NiceLinearDomain(lo, hi, 10);
        if (ax.Min is { } mn) lo = mn;
        if (ax.Max is { } mx) hi = mx;
        return (lo, hi);
    }

    /// <summary>Pixel scale of the x axis across the plot width; logarithmic when configured.</summary>
    public Scale XScale(CartesianLayout l)
    {
        var (d0, d1) = XDomain(); double r0 = l.Plot.X, r1 = l.Plot.X + l.Plot.W;
        return Config.XAxis.Log ? new LogScale(d0, d1, r0, r1) : new LinearScale(d0, d1, r0, r1);
    }
    /// <summary>Pixel scale of a value axis over the plot height (top = maximum); logarithmic when configured.</summary>
    public Scale YScale(YAxisId axis, CartesianLayout l)
    {
        var ax = (axis == YAxisId.Y2 ? Config.Y2Axis : Config.YAxis) ?? new CartesianAxisConfig();
        var (d0, d1) = YDomain(axis); double r0 = l.Plot.Y + l.Plot.H, r1 = l.Plot.Y;
        return ax.Log ? new LogScale(d0, d1, r0, r1) : new LinearScale(d0, d1, r0, r1);
    }
    /// <summary>Number of x ticks for the plot width, at least 2.</summary>
    public int XTickCount(CartesianLayout l) => Math.Max(2, ChartDrawing.RoundHalfUp(l.Plot.W / Config.TickSpacing));
    /// <summary>Number of value ticks for the plot height, at least 2.</summary>
    public int YTickCount(CartesianLayout l) => Math.Max(2, ChartDrawing.RoundHalfUp(l.Plot.H / (Config.TickSpacing * 0.6)));
    /// <summary>Category ticks are thinned so labels keep at least <c>TickSpacing / 2</c> pixels.</summary>
    public List<double> XTicks(CartesianLayout l)
    {
        var cats = Config.XAxis.Categories;
        if (cats is null) return XScale(l).TickValues(XTickCount(l));
        var (lo, hi) = XDomain();
        var perCat = l.Plot.W / Math.Max(1e-9, hi - lo);
        var step = Math.Max(1, (int)Math.Ceiling(Config.TickSpacing / 2 / Math.Max(1e-9, perCat)));
        var o = new List<double>();
        for (var i = Math.Max(0, (int)Math.Ceiling(lo)); i < cats.Length && i <= hi; i += step) o.Add(i);
        return o;
    }
    /// <summary>Tick label on the x axis: the category name (empty between categories) or the formatted number.</summary>
    public string FormatX(double v, CartesianLayout l)
    {
        var cats = Config.XAxis.Categories;
        if (cats is not null) { var i = (int)Math.Floor(v + 0.5); return i >= 0 && i < cats.Length && Math.Abs(v - i) < 1e-9 ? cats[i] : ""; }
        return XScale(l).Format(v, XTickCount(l));
    }

    // ---- layout ----
    /// <summary>Bands for a canvas size: margin, title, reserved or overlay legend, axis strips and plot.</summary>
    public CartesianLayout Layout(double width, double height)
    {
        var c = Config; var m = c.Margin;
        var outer = new Rect(m, m, Math.Max(0, width - 2 * m), Math.Max(0, height - 2 * m));
        Rect? title = null;
        if (c.Title is not null) { title = new Rect(outer.X, outer.Y, outer.W, c.TitleHeight); outer = new Rect(outer.X, outer.Y + c.TitleHeight, outer.W, Math.Max(0, outer.H - c.TitleHeight)); }
        var items = LegendItems().Count;
        var (inner, reserved) = SimpleLegend.Reserve(c, outer, items);
        var xAxisH = c.XAxisHeight + (c.XAxis.Label is not null ? c.Theme.FontSize + 4 : 0);
        var y2 = UsesY2();
        var plot = new Rect(inner.X + c.YAxisWidth, inner.Y, Math.Max(0, inner.W - c.YAxisWidth - (y2 ? c.YAxisWidth : 0)), Math.Max(0, inner.H - xAxisH));
        var yAxis = new Rect(inner.X, plot.Y, c.YAxisWidth, plot.H);
        Rect? y2Axis = y2 ? new Rect(plot.X + plot.W, plot.Y, c.YAxisWidth, plot.H) : null;
        var xAxis = new Rect(plot.X, plot.Y + plot.H, plot.W, xAxisH);
        return new CartesianLayout(width, height, plot, yAxis, y2Axis, xAxis, title, reserved ?? SimpleLegend.OverlayRect(c, plot, items));
    }

    // ---- geometry ----
    /// <summary>Interleaved pixel polyline of a line/step/area/scatter series (top of the stack for stacked areas).</summary>
    public List<double> PixelPoints(CartesianSeriesConfig s, CartesianLayout l)
    {
        var xs = XScale(l); var ys = YScale(s.Axis, l); var step = s.Kind == CartesianSeriesKind.Step;
        var o = new List<double>(); double prevY = 0;
        for (var i = 0; i < Count(s); i++)
        {
            double x = xs.Apply(XValue(s, i)), y = ys.Apply(StackedValue(s, i).Top);
            if (step && i > 0) { o.Add(x); o.Add(prevY); }
            o.Add(x); o.Add(y); prevY = y;
        }
        return o;
    }
    /// <summary>Closed polygon: the top polyline followed by the base polyline reversed.</summary>
    public List<double> AreaPolygon(CartesianSeriesConfig s, CartesianLayout l)
    {
        var top = PixelPoints(s, l);
        var xs = XScale(l); var ys = YScale(s.Axis, l);
        for (var i = Count(s) - 1; i >= 0; i--) { top.Add(xs.Apply(XValue(s, i))); top.Add(ys.Apply(StackedValue(s, i).Base)); }
        return top;
    }

    // ---- interaction ----
    /// <summary>Nearest non-bar point within <see cref="CartesianChartConfig.HoverRadius"/>, else the bar under the point; null outside the plot.</summary>
    public ChartHit? HitTest(double x, double y, CartesianLayout l)
    {
        var p = l.Plot;
        if (x < p.X || x > p.X + p.W || y < p.Y || y > p.Y + p.H) return null;
        ChartHit? best = null; var bestD = Config.HoverRadius * Config.HoverRadius;
        foreach (var s in VisibleSeries())
        {
            if (s.Kind == CartesianSeriesKind.Bar) continue;
            var xs = XScale(l); var ys = YScale(s.Axis, l);
            for (var i = 0; i < Count(s); i++)
            {
                double dx = xs.Apply(XValue(s, i)) - x, dy = ys.Apply(StackedValue(s, i).Top) - y, d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = new ChartHit(s.Id, i); }
            }
        }
        if (best is not null) return best;
        foreach (var b in Bars(l)) if (x >= b.X && x <= b.X + b.W && y >= b.Y && y <= b.Y + b.H) return new ChartHit(b.SeriesId, b.Index);
        return null;
    }
    /// <summary>Tooltip content anchored at the hit point; null when the series no longer exists.</summary>
    public Tooltip? TooltipFor(ChartHit hit, CartesianLayout l)
    {
        var s = Series(hit.SeriesId);
        if (s is null) return null;
        var xv = XValue(s, hit.Index); var cats = Config.XAxis.Categories;
        var title = cats is not null ? (hit.Index < cats.Length ? cats[hit.Index] : Ticks.FormatNumber(xv, 0)) : $"{Config.XAxis.Label ?? "x"} {TrendChartRenderer.FormatValue(xv)}{(Config.XAxis.Unit is null ? "" : " " + Config.XAxis.Unit)}";
        var unit = ((s.Axis == YAxisId.Y2 ? Config.Y2Axis : Config.YAxis) ?? new CartesianAxisConfig()).Unit;
        var lines = new[] { new TooltipLine(SeriesName(s), TrendChartRenderer.FormatValue(hit.Index < s.Y.Length ? s.Y[hit.Index] : 0) + (unit is null ? "" : $" {unit}"), SeriesColor(s)) };
        var xs = XScale(l); var ys = YScale(s.Axis, l);
        return new Tooltip(xs.Apply(xv), ys.Apply(StackedValue(s, hit.Index).Top), title, lines);
    }
    /// <summary>Updates hover, legend hover and an active zoom box for a pointer position.</summary>
    public void PointerMove(double x, double y, double width, double height)
    {
        var l = Layout(width, height);
        Hover = HitTest(x, y, l);
        LegendHover = SimpleLegend.ItemAt(Config, l.Legend, LegendItems(), x, y);
        if (Box is { } b) Box = b with { X1 = x, Y1 = y };
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
    /// <summary>Starts a box zoom at the press point.</summary>
    public void BeginBox(double x, double y) => Box = new ZoomBox(x, y, x, y);
    /// <summary>Ends a box zoom; boxes under 6 px are treated as clicks and ignored.</summary>
    public bool EndBox(double width, double height)
    {
        var b = Box; Box = null;
        if (b is null || Math.Abs(b.X1 - b.X0) < 6 || Math.Abs(b.Y1 - b.Y0) < 6) return false;
        ZoomTo(b.X0, b.Y0, b.X1, b.Y1, Layout(width, height));
        return true;
    }
    /// <summary>Zooms every axis to the pixel rectangle; the corners may be given in any order.</summary>
    public void ZoomTo(double x0, double y0, double x1, double y1, CartesianLayout l)
    {
        var xs = XScale(l); var ys = YScale(YAxisId.Y, l);
        ZoomX = (xs.Invert(Math.Min(x0, x1)), xs.Invert(Math.Max(x0, x1)));
        ZoomY = (ys.Invert(Math.Max(y0, y1)), ys.Invert(Math.Min(y0, y1)));
        if (UsesY2()) { var y2 = YScale(YAxisId.Y2, l); ZoomY2 = (y2.Invert(Math.Max(y0, y1)), y2.Invert(Math.Min(y0, y1))); }
    }
    /// <summary>Wheel zoom about the pointer: factor 2^(−dy/400), matching the scene interaction.</summary>
    public void WheelZoom(double x, double y, double deltaY, CartesianLayout l)
    {
        var f = Math.Pow(2, -deltaY / 400);
        static (double, double) Zoom(Scale sc, double px, double f) { var c = sc.Invert(px); return (c + (sc.D0 - c) / f, c + (sc.D1 - c) / f); }
        ZoomX = Zoom(XScale(l), x, f);
        ZoomY = Zoom(YScale(YAxisId.Y, l), y, f);
        if (UsesY2()) ZoomY2 = Zoom(YScale(YAxisId.Y2, l), y, f);
    }
    /// <summary>Returns every axis to autoscale.</summary>
    public void ResetZoom() { ZoomX = ZoomY = ZoomY2 = null; }
    /// <summary>True while an x or left-y zoom override is active.</summary>
    public bool Zoomed => ZoomX is not null || ZoomY is not null;

    // ---- drawing ----
    /// <summary>Paints the chart in order: background, grid, areas, bars, lines, markers, hover ring, axes, legend, tooltip and zoom box.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var st = c.Style; var l = Layout(width, height);
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        var muted = text with { Color = th.MutedText };
        var grid = new Stroke(th.Grid) { Width = st.GridWidth, Dash = st.GridDash };
        var axis = new Stroke(th.Axis);
        var plot = l.Plot;
        p.Clear(th.Background);
        if (l.Title is { } tr && c.Title is not null) p.Text(c.Title, tr.X + tr.W / 2, tr.Y + tr.H / 2, text with { Size = th.FontSize + 3, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        p.Rect(plot.X, plot.Y, plot.W, plot.H, new Fill(th.PlotBackground));
        var xs = XScale(l); var ys = YScale(YAxisId.Y, l); var xTicks = XTicks(l); var yTicks = ys.TickValues(YTickCount(l));
        if (c.ShowGrid)
        {
            if (st.ShowValueGrid) foreach (var v in yTicks) { var y = ys.Apply(v); p.Line(plot.X, y, plot.X + plot.W, y, grid); }
            if (st.ShowTimeGrid && c.XAxis.Categories is null) foreach (var v in xTicks) { var x = xs.Apply(v); p.Line(x, plot.Y, x, plot.Y + plot.H, grid); }
        }
        p.Save();
        p.ClipRect(plot.X, plot.Y, plot.W, plot.H);
        var vis = VisibleSeries();
        foreach (var s in vis) if (s.Kind == CartesianSeriesKind.Area && Count(s) > 1) p.Polygon(AreaPolygon(s, l).ToArray(), new Fill(SeriesColor(s)) { Opacity = s.FillOpacity ?? 0.25 });
        foreach (var b in Bars(l))
        {
            var s = Series(b.SeriesId)!;
            var hot = Hover is not null && Hover.SeriesId == b.SeriesId && Hover.Index == b.Index;
            p.Rect(b.X, b.Y, b.W, b.H, new Fill(SeriesColor(s)) { Opacity = s.FillOpacity ?? (hot ? 1 : 0.85) }, hot ? new Stroke(th.Text) : null);
        }
        foreach (var s in vis)
        {
            if (s.Kind is CartesianSeriesKind.Bar or CartesianSeriesKind.Scatter || Count(s) < 2) continue;
            p.Polyline(PixelPoints(s, l).ToArray(), new Stroke(SeriesColor(s)) { Width = s.Width ?? st.SeriesWidth, Join = LineJoin.Round });
        }
        foreach (var s in vis)
        {
            var marker = MarkerOf(s);
            if (marker == MarkerShape.None || s.Kind == CartesianSeriesKind.Bar) continue;
            var size = s.MarkerSize ?? 4; var color = SeriesColor(s); var ysA = YScale(s.Axis, l);
            for (var i = 0; i < Count(s); i++) ChartDrawing.Marker(p, marker, xs.Apply(XValue(s, i)), ysA.Apply(StackedValue(s, i).Top), size, color);
        }
        if (Hover is { } hv && Series(hv.SeriesId) is { } hs && hs.Kind != CartesianSeriesKind.Bar)
        {
            var ysA = YScale(hs.Axis, l);
            p.Circle(xs.Apply(XValue(hs, hv.Index)), ysA.Apply(StackedValue(hs, hv.Index).Top), (hs.MarkerSize ?? 4) + 3, null, new Stroke(SeriesColor(hs)) { Width = 2 });
        }
        p.Restore();

        var ya = l.YAxis; var lineX = ya.X + ya.W;
        p.Line(lineX, ya.Y, lineX, ya.Y + ya.H, axis);
        var yCount = YTickCount(l);
        foreach (var v in yTicks) { var y = ys.Apply(v); p.Line(lineX - st.AxisTickLength, y, lineX, y, axis); p.Text(ys.Format(v, yCount), lineX - st.AxisLabelGap, y, text with { Align = TextAlign.Right, Baseline = TextBaseline.Middle }); }
        var yTitle = c.YAxis.Label ?? c.YAxis.Unit;
        if (yTitle is not null) p.Text(yTitle, ya.X + 2, ya.Y + 2, muted with { Baseline = TextBaseline.Top });
        if (l.Y2Axis is { } r2 && c.Y2Axis is { } a2)
        {
            var y2 = YScale(YAxisId.Y2, l);
            p.Line(r2.X, r2.Y, r2.X, r2.Y + r2.H, axis);
            foreach (var v in y2.TickValues(yCount)) { var y = y2.Apply(v); p.Line(r2.X, y, r2.X + st.AxisTickLength, y, axis); p.Text(y2.Format(v, yCount), r2.X + st.AxisLabelGap, y, text with { Baseline = TextBaseline.Middle }); }
            var t2 = a2.Label ?? a2.Unit;
            if (t2 is not null) p.Text(t2, r2.X + r2.W - 2, r2.Y + 2, muted with { Align = TextAlign.Right, Baseline = TextBaseline.Top });
        }
        var xa = l.XAxis;
        p.Line(xa.X, xa.Y, xa.X + xa.W, xa.Y, axis);
        foreach (var v in xTicks) { var x = xs.Apply(v); p.Line(x, xa.Y, x, xa.Y + st.AxisTickLength, axis); p.Text(FormatX(v, l), x, xa.Y + st.AxisTickLength + 2, text with { Align = TextAlign.Center, Baseline = TextBaseline.Top }); }
        if (c.XAxis.Label is not null) p.Text(c.XAxis.Label + (c.XAxis.Unit is null ? "" : $" ({c.XAxis.Unit})"), xa.X + xa.W / 2, xa.Y + xa.H - 2, muted with { Align = TextAlign.Center, Baseline = TextBaseline.Bottom });

        if (l.Legend is { } lg) SimpleLegend.Draw(p, c, lg, LegendItems(), LegendHover);
        if (Hover is not null && TooltipFor(Hover, l) is { } tt) ChartDrawing.TooltipBox(p, th, st, plot, tt);
        if (Box is { } bx) p.Rect(Math.Min(bx.X0, bx.X1), Math.Min(bx.Y0, bx.Y1), Math.Abs(bx.X1 - bx.X0), Math.Abs(bx.Y1 - bx.Y0), new Fill(th.DropIndicator) { Opacity = 0.1 }, new Stroke(th.DropIndicator) { Dash = [4, 3] });
    }
}
