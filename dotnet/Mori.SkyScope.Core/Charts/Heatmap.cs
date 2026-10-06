// Mori.SkyScope — Heatmap / spectrogram: a rows × cols matrix mapped through a colour map onto a raster, with axes and a colour bar.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>
/// Heatmap / spectrogram: a rows × cols matrix mapped through a colour map onto a raster, with axes and a colour bar.
/// <see cref="HeatmapConfig.Rolling"/> turns it into a spectrogram: <see cref="Heatmap.PushColumn"/> appends on the right and the x extent slides.
/// Mirrors <c>charts/heatmap.ts</c>; pinned by <c>spec/fixtures/heatmap.json</c>.
/// </summary>
public sealed class HeatmapConfig
{
    /// <summary>Title centred above the plot; null reserves no title band.</summary>
    public string? Title { get; set; }
    /// <summary>Matrix width in columns; fixed once a <see cref="Heatmap"/> is created over the configuration.</summary>
    public int Cols { get; set; } = 1;
    /// <summary>Matrix height in rows; fixed once a <see cref="Heatmap"/> is created over the configuration.</summary>
    public int Rows { get; set; } = 1;
    /// <summary>Data extents of the matrix (row 0 sits at YMin, column 0 at XMin).</summary>
    public double XMin { get; set; }
    /// <summary>X at the right edge of the last column; slides in rolling mode.</summary>
    public double XMax { get; set; } = 1;
    /// <summary>Y at the bottom edge of row 0.</summary>
    public double YMin { get; set; }
    /// <summary>Y at the top edge of the last row.</summary>
    public double YMax { get; set; } = 1;
    /// <summary>Caption under the x axis; also names the x coordinate in the tooltip.</summary>
    public string? XLabel { get; set; }
    /// <summary>Caption of the y axis; also names the y coordinate in the tooltip.</summary>
    public string? YLabel { get; set; }
    /// <summary>Caption of the colour bar and name of the value in the tooltip.</summary>
    public string? ValueLabel { get; set; }
    /// <summary>A named map (see <see cref="Colormaps.Named"/>) unless <see cref="ColormapStops"/> is set.</summary>
    public string Colormap { get; set; } = "viridis";
    /// <summary>Explicit hex stops from low to high; overrides <see cref="Colormap"/> when set.</summary>
    public string[]? ColormapStops { get; set; }
    /// <summary>Colour range; null for the data range.</summary>
    public double? Min { get; set; }
    /// <summary>Upper colour bound; null for the data maximum.</summary>
    public double? Max { get; set; }
    /// <summary>Shows the colour bar to the right of the plot.</summary>
    public bool Colorbar { get; set; } = true;
    /// <summary>Width of the colour bar in pixels; its tick labels take another 44.</summary>
    public double ColorbarWidth { get; set; } = 14;
    /// <summary>Spectrogram mode: <see cref="Heatmap.PushColumn"/> scrolls the matrix.</summary>
    public bool Rolling { get; set; }
    /// <summary>Draws grid lines at the axis ticks over the raster.</summary>
    public bool ShowGrid { get; set; }
    /// <summary>Colours and type.</summary>
    public ChartTheme Theme { get; set; } = ChartTheme.Light;
    /// <summary>Widths, dashes, opacities and paddings.</summary>
    public ChartStyle Style { get; set; } = ChartStyle.Default;
    /// <summary>Pixels between the outer edge and the first band.</summary>
    public double Margin { get; set; } = 8;
    /// <summary>Pixels reserved for the y-axis strip.</summary>
    public double YAxisWidth { get; set; } = 48;
    /// <summary>Pixels reserved for the x-axis ticks; a label adds one text line.</summary>
    public double XAxisHeight { get; set; } = 24;
    /// <summary>Pixels reserved for the title band when a title is set.</summary>
    public double TitleHeight { get; set; } = 22;
    /// <summary>Target pixels per tick on the x axis; the y axis and colour bar use 0.6 of it.</summary>
    public double TickSpacing { get; set; } = 80;
}

/// <summary>Pixel rectangles of the heatmap bands for one canvas size; <paramref name="Colorbar"/> and <paramref name="Title"/> are null when absent.</summary>
public sealed record HeatmapLayout(double Width, double Height, Rect Plot, Rect YAxis, Rect XAxis, Rect? Colorbar, Rect? Title);
/// <summary>The cell under the pointer: logical column and row (row 0 at the bottom) and its value.</summary>
public sealed record CellHit(int Col, int Row, double Value);

/// <summary>Headless heatmap state and drawing over a <see cref="HeatmapConfig"/>: holds the matrix, caches the raster and answers hit tests.</summary>
public sealed class Heatmap : IDrawable
{
    /// <summary>Configuration read on every call; the matrix size is fixed at construction, the extents may change.</summary>
    public HeatmapConfig Config { get; }
    /// <summary>Row-major, row 0 = YMin; physical column = (head + logical) % cols. NaN = empty.</summary>
    public double[] Values { get; }
    private int _head;
    private long _version;
    private RasterImage? _raster;
    /// <summary>Cell under the pointer, set by <see cref="PointerMove"/>; null over empty cells.</summary>
    public CellHit? Hover { get; set; }

    /// <summary>Allocates a NaN-filled Rows × Cols matrix and copies <paramref name="values"/> (row-major) into it when given.</summary>
    public Heatmap(HeatmapConfig? config = null, ReadOnlySpan<double> values = default)
    {
        Config = config ?? new HeatmapConfig();
        Values = new double[Config.Rows * Config.Cols];
        Array.Fill(Values, double.NaN);
        if (values.Length > 0) SetValues(values);
    }

    /// <summary>Matrix width, from the configuration.</summary>
    public int Cols => Config.Cols;
    /// <summary>Matrix height, from the configuration.</summary>
    public int Rows => Config.Rows;

    /// <summary>Overwrites the matrix row-major from the start (extra input is ignored) and resets the rolling head.</summary>
    public void SetValues(ReadOnlySpan<double> values)
    {
        var n = Math.Min(values.Length, Values.Length);
        for (var i = 0; i < n; i++) Values[i] = values[i];
        _head = 0; _version++;
    }
    /// <summary>Rolling mode: the new column becomes the right-most one and the x extents slide by one column.</summary>
    public void PushColumn(ReadOnlySpan<double> column)
    {
        var c = Config;
        for (var r = 0; r < c.Rows; r++) Values[r * c.Cols + _head] = r < column.Length ? column[r] : double.NaN;
        _head = (_head + 1) % c.Cols;
        if (c.Rolling) { var dx = (c.XMax - c.XMin) / c.Cols; c.XMin += dx; c.XMax += dx; }
        _version++;
    }
    /// <summary>Value at logical (row, col).</summary>
    public double Get(int row, int col)
    {
        var c = Config;
        if (row < 0 || row >= c.Rows || col < 0 || col >= c.Cols) return double.NaN;
        return Values[row * c.Cols + (_head + col) % c.Cols];
    }
    /// <summary>Writes one cell at logical (row, col) and invalidates the raster; indices are not checked.</summary>
    public void Set(int row, int col, double v) { var c = Config; Values[row * c.Cols + (_head + col) % c.Cols] = v; _version++; }

    /// <summary>Colour range: configured bounds, else the finite data range (degenerate → [v, v + 1]).</summary>
    public (double Lo, double Hi) Range()
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        foreach (var v in Values) { if (double.IsNaN(v)) continue; if (v < lo) lo = v; if (v > hi) hi = v; }
        if (Config.Min is { } mn) lo = mn;
        if (Config.Max is { } mx) hi = mx;
        if (!double.IsFinite(lo) || !double.IsFinite(hi)) return (0, 1);
        if (hi <= lo) hi = lo + 1;
        return (lo, hi);
    }
    /// <summary>Effective colour stops: the explicit list or the named map.</summary>
    public string[] Stops() => Config.ColormapStops ?? Colormaps.Stops(Config.Colormap);
    /// <summary>Hex colour of a value under the current colour range.</summary>
    public string ColorAt(double v) { var (lo, hi) = Range(); return Colormaps.Hex(Stops(), (v - lo) / (hi - lo)); }

    /// <summary>RGBA raster (row 0 = top = YMax); rebuilt only after data changes. NaN cells are transparent.</summary>
    public RasterImage Image()
    {
        if (_raster is not null && _raster.Version == _version) return _raster;
        var c = Config; var (lo, hi) = Range(); var lut = Colormaps.Lut(Stops(), 256); var span = hi - lo;
        var rgba = new byte[c.Cols * c.Rows * 4];
        for (var row = 0; row < c.Rows; row++)
        {
            var py = c.Rows - 1 - row;
            for (var col = 0; col < c.Cols; col++)
            {
                var v = Get(row, col); var o = (py * c.Cols + col) * 4;
                if (double.IsNaN(v)) continue;
                var k = (int)Math.Floor((v - lo) / span * 255 + 0.5);
                if (k < 0) k = 0; else if (k > 255) k = 255;
                rgba[o] = lut[k * 3]; rgba[o + 1] = lut[k * 3 + 1]; rgba[o + 2] = lut[k * 3 + 2]; rgba[o + 3] = 255;
            }
        }
        return _raster = new RasterImage(c.Cols, c.Rows, rgba, _version);
    }

    /// <summary>Bands for a canvas size: margin, title, colour bar, y-axis strip, plot and x-axis band.</summary>
    public HeatmapLayout Layout(double width, double height)
    {
        var c = Config; var m = c.Margin;
        double x0 = m, y0 = m, x1 = width - m, y1 = height - m;
        Rect? title = null;
        if (c.Title is not null) { title = new Rect(x0, y0, x1 - x0, c.TitleHeight); y0 += c.TitleHeight; }
        Rect? colorbar = null;
        var xAxisH = c.XAxisHeight + (c.XLabel is not null ? c.Theme.FontSize + 4 : 0);
        if (c.Colorbar) { var w = c.ColorbarWidth; colorbar = new Rect(x1 - 44 - w, y0, w, Math.Max(0, y1 - y0 - xAxisH)); x1 = colorbar.Value.X - m; }
        var plot = new Rect(x0 + c.YAxisWidth, y0, Math.Max(0, x1 - x0 - c.YAxisWidth), Math.Max(0, y1 - y0 - xAxisH));
        return new HeatmapLayout(width, height, plot, new Rect(x0, plot.Y, c.YAxisWidth, plot.H), new Rect(plot.X, plot.Y + plot.H, plot.W, xAxisH), colorbar, title);
    }
    /// <summary>Pixel scale from the x extents across the plot width.</summary>
    public LinearScale XScale(HeatmapLayout l) => new(Config.XMin, Config.XMax, l.Plot.X, l.Plot.X + l.Plot.W);
    /// <summary>Pixel scale from the y extents over the plot height; YMin sits at the bottom.</summary>
    public LinearScale YScale(HeatmapLayout l) => new(Config.YMin, Config.YMax, l.Plot.Y + l.Plot.H, l.Plot.Y);

    /// <summary>Cell under a pixel position; null outside the plot or over a NaN cell.</summary>
    public CellHit? CellAt(double x, double y, HeatmapLayout l)
    {
        var p = l.Plot;
        if (x < p.X || x >= p.X + p.W || y < p.Y || y >= p.Y + p.H || p.W <= 0 || p.H <= 0) return null;
        int col = (int)Math.Floor((x - p.X) / p.W * Config.Cols), row = (int)Math.Floor((p.Y + p.H - y) / p.H * Config.Rows);
        var v = Get(row, col);
        return double.IsNaN(v) ? null : new CellHit(col, row, v);
    }
    /// <summary>Updates <see cref="Hover"/> for a pointer position.</summary>
    public void PointerMove(double x, double y, double width, double height) => Hover = CellAt(x, y, Layout(width, height));
    /// <summary>Clears the hover.</summary>
    public void PointerLeave() => Hover = null;

    /// <summary>Paints background, raster, grid, hover outline, axes, colour bar and tooltip.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        var c = Config; var th = c.Theme; var st = c.Style; var l = Layout(width, height); var plot = l.Plot;
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        var muted = text with { Color = th.MutedText };
        var axis = new Stroke(th.Axis);
        p.Clear(th.Background);
        if (l.Title is { } tr && c.Title is not null) p.Text(c.Title, tr.X + tr.W / 2, tr.Y + tr.H / 2, text with { Size = th.FontSize + 3, Weight = "bold", Align = TextAlign.Center, Baseline = TextBaseline.Middle });
        p.Rect(plot.X, plot.Y, plot.W, plot.H, new Fill(th.PlotBackground));
        if (plot.W > 0 && plot.H > 0) p.Image(Image(), plot.X, plot.Y, plot.W, plot.H);
        var xs = XScale(l); var ys = YScale(l);
        int xCount = Math.Max(2, ChartDrawing.RoundHalfUp(plot.W / c.TickSpacing)), yCount = Math.Max(2, ChartDrawing.RoundHalfUp(plot.H / (c.TickSpacing * 0.6)));
        var xTicks = xs.TickValues(xCount); var yTicks = ys.TickValues(yCount);
        if (c.ShowGrid)
        {
            var grid = new Stroke(th.Grid) { Width = st.GridWidth, Dash = st.GridDash, Opacity = 0.6 };
            foreach (var v in yTicks) { var y = ys.Apply(v); p.Line(plot.X, y, plot.X + plot.W, y, grid); }
            foreach (var v in xTicks) { var x = xs.Apply(v); p.Line(x, plot.Y, x, plot.Y + plot.H, grid); }
        }
        if (Hover is { } hv)
        {
            double cw = plot.W / c.Cols, ch = plot.H / c.Rows;
            p.Rect(plot.X + hv.Col * cw, plot.Y + plot.H - (hv.Row + 1) * ch, cw, ch, null, new Stroke(th.Text) { Width = 1.5 });
        }
        var ya = l.YAxis; var lineX = ya.X + ya.W;
        p.Line(lineX, ya.Y, lineX, ya.Y + ya.H, axis);
        foreach (var v in yTicks) { var y = ys.Apply(v); p.Line(lineX - st.AxisTickLength, y, lineX, y, axis); p.Text(ys.Format(v, yCount), lineX - st.AxisLabelGap, y, text with { Align = TextAlign.Right, Baseline = TextBaseline.Middle }); }
        if (c.YLabel is not null) p.Text(c.YLabel, ya.X + 2, ya.Y + 2, muted with { Baseline = TextBaseline.Top });
        var xa = l.XAxis;
        p.Line(xa.X, xa.Y, xa.X + xa.W, xa.Y, axis);
        foreach (var v in xTicks) { var x = xs.Apply(v); p.Line(x, xa.Y, x, xa.Y + st.AxisTickLength, axis); p.Text(xs.Format(v, xCount), x, xa.Y + st.AxisTickLength + 2, text with { Align = TextAlign.Center, Baseline = TextBaseline.Top }); }
        if (c.XLabel is not null) p.Text(c.XLabel, xa.X + xa.W / 2, xa.Y + xa.H - 2, muted with { Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
        if (l.Colorbar is { } cb)
        {
            var (lo, hi) = Range(); var stops = Stops(); const int n = 32;
            for (var i = 0; i < n; i++) { var y0 = cb.Y + cb.H * (1 - (i + 1.0) / n); p.Rect(cb.X, y0, cb.W, cb.H / n + 0.5, new Fill(Colormaps.Hex(stops, (i + 0.5) / n))); }
            p.Rect(cb.X, cb.Y, cb.W, cb.H, null, axis);
            var cs = new LinearScale(lo, hi, cb.Y + cb.H, cb.Y); var cCount = Math.Max(2, ChartDrawing.RoundHalfUp(cb.H / (c.TickSpacing * 0.6)));
            foreach (var v in cs.TickValues(cCount)) { var y = cs.Apply(v); p.Line(cb.X + cb.W, y, cb.X + cb.W + st.AxisTickLength, y, axis); p.Text(cs.Format(v, cCount), cb.X + cb.W + st.AxisLabelGap, y, text with { Baseline = TextBaseline.Middle }); }
            if (c.ValueLabel is not null) p.Text(c.ValueLabel, cb.X, cb.Y - 4, muted with { Baseline = TextBaseline.Bottom });
        }
        if (Hover is { } h)
        {
            double cw = (c.XMax - c.XMin) / c.Cols, ch = (c.YMax - c.YMin) / c.Rows;
            double cx = plot.X + (h.Col + 0.5) * plot.W / c.Cols, cy = plot.Y + plot.H - (h.Row + 0.5) * plot.H / c.Rows;
            var title = $"{c.XLabel ?? "x"} {TrendChartRenderer.FormatValue(c.XMin + (h.Col + 0.5) * cw)}, {c.YLabel ?? "y"} {TrendChartRenderer.FormatValue(c.YMin + (h.Row + 0.5) * ch)}";
            ChartDrawing.TooltipBox(p, th, st, plot, new Tooltip(cx, cy, title, [new TooltipLine(c.ValueLabel ?? "value", TrendChartRenderer.FormatValue(h.Value), ColorAt(h.Value))]));
        }
    }
}
