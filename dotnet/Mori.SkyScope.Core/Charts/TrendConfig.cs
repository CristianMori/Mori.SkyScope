// Mori.SkyScope — TrendChart configuration.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Analog series draw on a Y axis; digital ones become logic-analyzer tracks in the lane's stack.</summary>
public enum SeriesKind { Analog, Digital }
/// <summary>Side of the lane a Y axis is drawn on.</summary>
public enum AxisSide { Left, Right }
/// <summary>Corner values float inside the plot; Right/Top reserve space beside it.</summary>
public enum LegendPosition { TopLeft, TopRight, BottomLeft, BottomRight, Right, Top, None }

/// <summary>
/// Where a dragged series would land (ibaAnalyzer rules): onto an axis strip or a series label → that axis (Join);
/// into a lane's free area → the lane with an own axis (OwnAxis); onto the time axis, between lanes or outside → a new lane.
/// </summary>
public abstract record DropTarget
{
    /// <summary>Put the series on an existing axis of a lane.</summary>
    public sealed record Join(string LaneId, string AxisId) : DropTarget;
    /// <summary>Put the series in a lane on a new axis of its own.</summary>
    public sealed record OwnAxis(string LaneId) : DropTarget;
    /// <summary>The lane's logic-analyzer stack (digital signals only ever land there).</summary>
    public sealed record Stack(string LaneId) : DropTarget;
    /// <summary>Create a lane at <paramref name="Index"/> (0 = top); <paramref name="AfterLaneId"/> is the lane above it, null at the top.</summary>
    public sealed record NewLane(int Index, string? AfterLaneId) : DropTarget;
    /// <summary>Nothing would happen.</summary>
    public sealed record None : DropTarget;
}

/// <summary>What is under a point of the chart, for hosts to route gestures.</summary>
public abstract record HitRegion
{
    /// <summary>An in-plot series label (drag handle).</summary>
    public sealed record Label(string SeriesId, string LaneId) : HitRegion;
    /// <summary>A legend row of a series.</summary>
    public sealed record LegendRow(string SeriesId) : HitRegion;
    /// <summary>A lane header bar and which part of it.</summary>
    public sealed record Header(string LaneId, HeaderPart Part) : HitRegion;
    /// <summary>A Y-axis strip and which zone of it.</summary>
    public sealed record Axis(string AxisId, string LaneId, AxisZone Zone) : HitRegion;
    /// <summary>The navigator strip and the frame zone under the point.</summary>
    public sealed record Navigator(NavigatorZone Zone) : HitRegion;
    /// <summary>A lane's analog plot area.</summary>
    public sealed record Plot(string LaneId) : HitRegion;
    /// <summary>A lane's logic-analyzer stack.</summary>
    public sealed record Stack(string LaneId) : HitRegion;
    /// <summary>The time axis band.</summary>
    public sealed record TimeAxis : HitRegion;
    /// <summary>The gap between two open lanes: drag to move height from one to the other.</summary>
    public sealed record LaneGap(string AboveLaneId, string BelowLaneId) : HitRegion;
    /// <summary>Within a few pixels of cursor A or B: drag to move it.</summary>
    public sealed record Cursor(char Which) : HitRegion;
    /// <summary>The measurement table over the cursor span.</summary>
    public sealed record Measure : HitRegion;
    /// <summary>Nothing of interest.</summary>
    public sealed record None : HitRegion;
}
/// <summary>Parts of a lane header: the drag grip in the middle, the fold arrow at the top, the remove cross at the bottom.</summary>
public enum HeaderPart { Grip, Collapse, Remove }
/// <summary>Zones of an axis strip: the top and bottom fifths stretch the scale, the middle shifts it.</summary>
public enum AxisZone { Top, Middle, Bottom }
/// <summary>Where in the navigator a point is: on an edge of the frame, inside it, or outside.</summary>
public enum NavigatorZone { LeftEdge, RightEdge, Inside, Outside }

/// <summary>Series (or channels from a signal tree) being dragged; a group dropped into free space shares one axis.</summary>
public sealed record DragState(string SeriesId, IReadOnlyList<string> SeriesIds, IReadOnlyList<int> ChannelIds, double X, double Y, DropTarget Target, bool Group)
{
    /// <summary>Set when the payload is known to be all digital (or not) before its channels are: a native drag-over can preview a logic-stack drop.</summary>
    public bool? Digital { get; init; }
}
/// <summary>A lane header being dragged: the lane, the pointer position and the current insertion index.</summary>
public sealed record LaneDragState(string LaneId, double X, double Y, int Index);
/// <summary>A lane gap being dragged: the two lanes' heights and weights when the drag began.</summary>
public sealed record LaneResizeState(string AboveLaneId, string BelowLaneId, double Y0, double HA0, double HB0, double WA0, double WB0);
/// <summary>A time cursor ('a' or 'b') being dragged.</summary>
public sealed record CursorDragState(char Which);
/// <summary>One signal's figures over the cursor span; nulls when a cursor has no sample before it or the span holds no samples.</summary>
public sealed record Measurement(string SeriesId, double? A, double? B, double? Delta, double? Min, double? Max, double? Mean, int Count);
/// <summary>The measurement table: span bounds, signed Δt (B − A), its frequency, one row per visible analog signal.</summary>
public sealed record Measurements(double T0, double T1, double Dt, double? Hz, IReadOnlyList<Measurement> Rows);
/// <summary>An axis being dragged: the zone grabbed, the pointer y at the start and the axis range at that moment.</summary>
public sealed record AxisDragState(string AxisId, string LaneId, AxisZone Zone, double Y0, double Min0, double Max0);
/// <summary>The navigator frame being dragged: the zone grabbed, the pointer x at the start and the window at that moment.</summary>
public sealed record NavigatorDragState(NavigatorZone Zone, double X0, double T0, double T1);

/// <summary>Y-axis settings. Lane default axes are named <c>axis:&lt;laneId&gt;</c>; hosts may use any other id.</summary>
/// <param name="id">Unique axis id.</param>
public sealed class AxisConfig(string id)
{
    /// <summary>Unique axis id, as given to the constructor.</summary>
    public string Id { get; } = id;
    /// <summary>Axis title; the unit when null.</summary>
    public string? Label { get; set; }
    /// <summary>Unit shown in the legend readouts and as the title when there is no label.</summary>
    public string? Unit { get; set; }
    /// <summary>Fixed range; null for autoscale.</summary>
    public double? Min { get; set; }
    /// <summary>Fixed upper bound; null for autoscale.</summary>
    public double? Max { get; set; }
    /// <summary>Side of the lane the axis is drawn on.</summary>
    public AxisSide Side { get; set; } = AxisSide.Left;
    /// <summary>Optional CSS colour of the axis, for hosts that tint axes.</summary>
    public string? Color { get; set; }
}

/// <summary>A horizontal band of the chart; lanes stack top to bottom and share the time axis.</summary>
/// <param name="id">Unique lane id.</param>
public sealed class LaneConfig(string id)
{
    /// <summary>Unique lane id, as given to the constructor.</summary>
    public string Id { get; } = id;
    /// <summary>Optional lane caption.</summary>
    public string? Label { get; set; }
    /// <summary>Share of the vertical space relative to the other open lanes.</summary>
    public double Weight { get; set; } = 1;
    /// <summary>Folded to a thin bar (series stay configured, nothing is drawn).</summary>
    public bool Collapsed { get; set; }
}

/// <summary>A channel plotted in a lane.</summary>
/// <param name="id">Unique series id.</param>
/// <param name="channelId">Store channel the series reads.</param>
public sealed class SeriesConfig(string id, int channelId)
{
    /// <summary>Unique series id, as given to the constructor.</summary>
    public string Id { get; } = id;
    /// <summary>Store channel the series reads.</summary>
    public int ChannelId { get; } = channelId;
    /// <summary>Display name; null falls back to the channel name, then the id.</summary>
    public string? Name { get; set; }
    /// <summary>CSS colour; null picks from <see cref="TrendChartConfig.SeriesPalette"/> by series position.</summary>
    public string? Color { get; set; }
    /// <summary>Axis the series is scaled on; null means the lane's default axis.</summary>
    public string? AxisId { get; set; }
    /// <summary>Lane the series lives in; null means the first lane.</summary>
    public string? LaneId { get; set; }
    /// <summary>Line width in pixels; null uses <see cref="ChartStyle.SeriesWidth"/>.</summary>
    public double? Width { get; set; }
    /// <summary>Hidden series are neither drawn nor listed.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>Analog (on an axis) or digital (a logic track).</summary>
    public SeriesKind Kind { get; set; } = SeriesKind.Analog;
}

/// <summary>A horizontal reference on an axis: a line at <paramref name="From"/>, or a band from <paramref name="From"/> to <c>To</c> when that is set; <c>Label</c> is an optional caption.</summary>
public sealed record ThresholdConfig(string Id, string AxisId, double From, string Color) { public double? To { get; init; } public string? Label { get; init; } }
/// <summary>A vertical marker at <paramref name="Time"/> seconds, with an optional <c>Label</c> and <c>Color</c> (the theme's marker colour when null).</summary>
public sealed record MarkerConfig(string Id, double Time) { public string? Label { get; init; } public string? Color { get; init; } }

/// <summary>Colours and type. Everything the chart paints takes its colour from here.</summary>
/// <param name="Background">Canvas colour.</param>
/// <param name="PlotBackground">Lane and plot fill.</param>
/// <param name="Grid">Grid lines.</param>
/// <param name="Axis">Axis lines and ticks.</param>
/// <param name="Text">Primary text.</param>
/// <param name="MutedText">Secondary text (axis titles, tooltips titles).</param>
/// <param name="CursorA">Cursor A line.</param>
/// <param name="CursorB">Cursor B line.</param>
/// <param name="Hover">Hover line and legend hover fill.</param>
/// <param name="Marker">Default time marker colour.</param>
/// <param name="LegendBackground">Legend and tooltip box fill.</param>
/// <param name="FontFamily">CSS font family.</param>
/// <param name="FontSize">Font size in pixels.</param>
public sealed record ChartTheme(
    string Background, string PlotBackground, string Grid, string Axis, string Text, string MutedText,
    string CursorA, string CursorB, string Hover, string Marker, string LegendBackground, string FontFamily, double FontSize)
{
    /// <summary>Legend and tooltip box border.</summary>
    public string LegendBorder { get; init; } = "#e2e8f0";
    /// <summary>Lane outline and navigator border.</summary>
    public string LaneBorder { get; init; } = "#e2e8f0";
    /// <summary>Drop target and drag cue colour.</summary>
    public string DropIndicator { get; init; } = "#2563eb";
    /// <summary>Lane header fill.</summary>
    public string LaneHeader { get; init; } = "#e2e8f0";
    /// <summary>Lane header glyphs.</summary>
    public string LaneHeaderText { get; init; } = "#475569";
    /// <summary>Navigator frame outline and handles.</summary>
    public string NavigatorFrame { get; init; } = "#dc2626";
    /// <summary>Navigator strip fill.</summary>
    public string NavigatorBackground { get; init; } = "#f1f5f9";

    /// <summary>Default light theme.</summary>
    public static readonly ChartTheme Light = new("#ffffff", "#f8fafc", "#e2e8f0", "#94a3b8", "#0f172a", "#64748b",
        "#2563eb", "#dc2626", "#94a3b8", "#d97706", "#ffffff", "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", 11);
    /// <summary>Dark theme derived from <see cref="Light"/>.</summary>
    public static readonly ChartTheme Dark = Light with
    {
        Background = "#0f172a", PlotBackground = "#020617", Grid = "#1e293b", Axis = "#475569", Text = "#f8fafc", MutedText = "#94a3b8",
        CursorA = "#60a5fa", CursorB = "#f87171", Hover = "#64748b", Marker = "#fbbf24", LegendBackground = "#0f172a",
        LegendBorder = "#1e293b", LaneBorder = "#1e293b", DropIndicator = "#60a5fa", LaneHeader = "#1e293b", LaneHeaderText = "#94a3b8", NavigatorFrame = "#f87171", NavigatorBackground = "#0b1220",
    };
}

/// <summary>Widths, dashes, opacities and paddings. Mirrors <c>ChartStyle</c> in TS.</summary>
public sealed record ChartStyle
{
    /// <summary>Grid line width in pixels.</summary>
    public double GridWidth { get; init; } = 1;
    /// <summary>Grid dash pattern; null for solid.</summary>
    public double[]? GridDash { get; init; }
    /// <summary>Horizontal grid lines at value ticks.</summary>
    public bool ShowValueGrid { get; init; } = true;
    /// <summary>Vertical grid lines at time ticks.</summary>
    public bool ShowTimeGrid { get; init; } = true;
    /// <summary>Lines between logic-analyzer tracks.</summary>
    public bool TrackSeparators { get; init; } = true;
    /// <summary>Outlines each lane.</summary>
    public bool LaneBorder { get; init; }
    /// <summary>Lane outline width in pixels.</summary>
    public double LaneBorderWidth { get; init; } = 1;
    /// <summary>Tick mark length in pixels.</summary>
    public double AxisTickLength { get; init; } = 4;
    /// <summary>Gap in pixels between the axis line and tick labels.</summary>
    public double AxisLabelGap { get; init; } = 6;
    /// <summary>Default series line width in pixels.</summary>
    public double SeriesWidth { get; init; } = 1.5;
    /// <summary>Opacity of threshold bands.</summary>
    public double ThresholdBandOpacity { get; init; } = 0.15;
    /// <summary>Dash pattern of threshold lines.</summary>
    public double[] ThresholdLineDash { get; init; } = [4, 3];
    /// <summary>Time marker line width in pixels.</summary>
    public double MarkerWidth { get; init; } = 1;
    /// <summary>Dash pattern of time markers.</summary>
    public double[] MarkerDash { get; init; } = [3, 3];
    /// <summary>Width in pixels of cursor and hover lines.</summary>
    public double CursorWidth { get; init; } = 1;
    /// <summary>Dash pattern of the hover line.</summary>
    public double[] HoverDash { get; init; } = [2, 2];
    /// <summary>Inner padding of the legend box in pixels.</summary>
    public double LegendPadding { get; init; } = 8;
    /// <summary>Legend row height in pixels; null means font size + 8.</summary>
    public double? LegendRowHeight { get; init; }
    /// <summary>Opacity of the legend background.</summary>
    public double LegendOpacity { get; init; } = 0.88;
    /// <summary>Corner radius of legend and tooltip boxes.</summary>
    public double LegendRadius { get; init; } = 3;
    /// <summary>Length in pixels of the colour swatch line.</summary>
    public double LegendSwatchLength { get; init; } = 14;
    /// <summary>Fraction of a digital track's height kept free above the high level and below the low level.</summary>
    public double DigitalTrackPadding { get; init; } = 0.15;
    /// <summary>Opacity of the solid fill under a high logic level.</summary>
    public double DigitalFillOpacity { get; init; } = 0.3;

    /// <summary>All defaults.</summary>
    public static readonly ChartStyle Default = new();
}

/// <summary>TrendChart configuration. Mirrors <c>charts/trend-config.ts</c>. A chart is a set of lanes sharing the time axis; each lane hosts series that reference Y axes.</summary>
public sealed class TrendChartConfig
{
    /// <summary>Ten colours assigned by position (cycling) to series without an explicit colour.</summary>
    public static readonly string[] SeriesPalette = ["#2563eb", "#dc2626", "#16a34a", "#d97706", "#7c3aed", "#0891b2", "#db2777", "#65a30d", "#ea580c", "#4f46e5"];

    /// <summary>Seconds visible.</summary>
    public double TimeSpan { get; set; } = 30;
    /// <summary>Time tick labels: UTC clock time, or seconds relative to the right edge.</summary>
    public TimeFormat TimeFormat { get; set; } = TimeFormat.Relative;
    /// <summary>Lanes top to bottom; the model adds a default one when empty.</summary>
    public List<LaneConfig> Lanes { get; } = [];
    /// <summary>Explicit axis settings; axes not listed here use defaults.</summary>
    public List<AxisConfig> Axes { get; } = [];
    /// <summary>Series in configuration (palette and legend) order.</summary>
    public List<SeriesConfig> Series { get; } = [];
    /// <summary>Threshold lines and bands, drawn in the lanes whose axes they reference.</summary>
    public List<ThresholdConfig> Thresholds { get; } = [];
    /// <summary>Vertical time markers.</summary>
    public List<MarkerConfig> Markers { get; } = [];
    /// <summary>Legend placement: corners float over the plot, Right and Top reserve space beside it.</summary>
    public LegendPosition Legend { get; set; } = LegendPosition.TopLeft;
    /// <summary>Draws grid lines, subject to the style's grid switches.</summary>
    public bool ShowGrid { get; set; } = true;
    /// <summary>Colours and type.</summary>
    public ChartTheme Theme { get; set; } = ChartTheme.Light;
    /// <summary>Widths, dashes, opacities and paddings.</summary>
    public ChartStyle Style { get; set; } = ChartStyle.Default;
    /// <summary>Effective legend row height in pixels.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public double LegendRowHeight => Style.LegendRowHeight ?? Theme.FontSize + 8;
    /// <summary>Pixels between lanes.</summary>
    public double LaneGap { get; set; } = 6;
    /// <summary>Pixels between the outer edge and the first band.</summary>
    public double Margin { get; set; } = 8;
    /// <summary>Pixels per Y-axis column.</summary>
    public double YAxisWidth { get; set; } = 48;
    /// <summary>Pixels reserved for the time axis under the lanes.</summary>
    public double TimeAxisHeight { get; set; } = 22;
    /// <summary>Width in pixels of a side or overlay legend.</summary>
    public double LegendWidth { get; set; } = 170;
    /// <summary>Target pixels per tick.</summary>
    public double TickSpacing { get; set; } = 80;
    /// <summary>Series names inside each lane (top-left) — the drag handles for moving signals.</summary>
    public bool PlotLabels { get; set; } = true;
    /// <summary>A slim bar left of the axes per lane: drag to reorder, fold arrow, remove cross.</summary>
    public bool LaneHeaders { get; set; } = true;
    /// <summary>Width in pixels of the lane header bar.</summary>
    public double LaneHeaderWidth { get; set; } = 14;
    /// <summary>Height in pixels of a folded lane.</summary>
    public double CollapsedLaneHeight { get; set; } = 16;
    /// <summary>Overview strip under the time axis showing the whole retained history with the visible window framed.</summary>
    public bool Navigator { get; set; }
    /// <summary>Height in pixels of the navigator strip.</summary>
    public double NavigatorHeight { get; set; } = 48;
    /// <summary>Navigator frame keeps its width: dragging its edges is disabled, only moves apply.</summary>
    public bool NavigatorFixedRange { get; set; }
    /// <summary>Show the measurement table (value at A and B, delta, min, max, mean per signal) while both cursors are set.</summary>
    public bool MeasurePanel { get; set; } = true;
    /// <summary>Height of one logic-analyzer track when a lane mixes analog and digital signals (digital-only lanes share the whole lane).</summary>
    public double DigitalTrackHeight { get; set; } = 18;
    /// <summary>Largest share of a mixed lane the digital stack may take.</summary>
    public double DigitalStackShare { get; set; } = 0.5;
}

/// <summary>Pixel rectangle of one Y-axis strip and the side it sits on.</summary>
public sealed record AxisLayout(string AxisId, Rect Rect, AxisSide Side);
/// <summary>An in-plot series label (drag handle); widths use the painter-free estimate 0.6 × fontSize per character.</summary>
public sealed record LabelLayout(string SeriesId, Rect Rect);
/// <summary><paramref name="Analog"/> is the part numeric series draw in; <paramref name="Stack"/> (bottom of the lane) holds the logic-analyzer tracks, null when the lane has none.</summary>
public sealed record LaneLayout(string LaneId, Rect Rect, Rect Analog, Rect? Stack, IReadOnlyList<AxisLayout> Axes, Rect? Header, IReadOnlyList<LabelLayout> Labels, bool Collapsed);
/// <summary>Pixel layout of the whole chart: the plot, the lanes, the time axis, and the legend and navigator rectangles when present.</summary>
public sealed record TrendLayout(double Width, double Height, Rect Plot, IReadOnlyList<LaneLayout> Lanes, Rect TimeAxis, Rect? Legend, Rect? Navigator, Rect? Measure);
/// <summary>What the layout engine needs to know about a lane; built by <see cref="TrendChartModel.Layout"/>.</summary>
public sealed record LaneLayoutInput(string LaneId, double Weight, IReadOnlyList<string> LeftAxes, IReadOnlyList<string> RightAxes)
{
    /// <summary>Folded to a thin bar.</summary>
    public bool Collapsed { get; init; }
    /// <summary>Number of digital (logic-analyzer) tracks stacked at the bottom of the lane.</summary>
    public int Tracks { get; init; }
    /// <summary>Whether the lane has analog series (the stack then takes at most <c>DigitalStackShare</c>).</summary>
    public bool Analog { get; init; } = true;
    /// <summary>Series to label inside the lane, in legend order, with whether each is digital.</summary>
    public IReadOnlyList<(string SeriesId, string Name, bool Digital)> Labels { get; init; } = [];
}

/// <summary>Pure layout: margins → legend → y-axis columns → stacked lanes → time axis. Mirrors <c>layoutTrendChart</c>.</summary>
public static class TrendLayoutEngine
{
    /// <summary>True for the four corner positions that float over the plot.</summary>
    public static bool IsOverlay(LegendPosition p) => p is LegendPosition.TopLeft or LegendPosition.TopRight or LegendPosition.BottomLeft or LegendPosition.BottomRight;

    /// <summary>Height of an overlay legend listing <paramref name="rows"/> series across <paramref name="groups"/> lanes.</summary>
    public static double LegendHeight(TrendChartConfig config, int rows, int groups, int deltaRows = 0)
    {
        var rowH = config.LegendRowHeight;
        return 2 * config.Style.LegendPadding + rows * rowH + deltaRows * (rowH - 4) + (deltaRows > 0 ? rowH - 4 : 0) + Math.Max(0, groups - 1) * 4;
    }

    /// <summary>Painter-free text width: 0.6 × fontSize per character (the RecordingPainter's metric), identical in both cores.</summary>
    public static double EstimateTextWidth(string text, double fontSize) => text.Length * fontSize * 0.6;

    /// <summary>
    /// In-plot label boxes along the top of a lane: a swatch, the name, 6 px gaps; labels that would overflow wrap to the next
    /// row. Rows that would run under an overlay legend (<paramref name="avoid"/>) start right of it when it sits on the left, else stop before it.
    /// </summary>
    public static List<LabelLayout> LayoutLabels(TrendChartConfig config, Rect lane, IReadOnlyList<(string SeriesId, string Name)> labels, Rect? avoid = null)
    {
        var result = new List<LabelLayout>();
        var h = config.Theme.FontSize + 6; var sw = config.Style.LegendSwatchLength;
        (double X0, double X1) Bounds(double y)
        {
            double x0 = lane.X + 6, x1 = lane.X + lane.W - 4;
            if (avoid is { } a && y < a.Y + a.H && y + h > a.Y && a.X < x1 && a.X + a.W > x0)
            {
                if (a.X + a.W / 2 < lane.X + lane.W / 2) x0 = Math.Max(x0, a.X + a.W + 6); else x1 = Math.Min(x1, a.X - 6);
            }
            return (x0, x1);
        }
        var y = lane.Y + 4; var b = Bounds(y); var x = b.X0;
        foreach (var (seriesId, name) in labels)
        {
            var w = sw + 4 + EstimateTextWidth(name, config.Theme.FontSize) + 8;
            if (x + w > b.X1 && x > b.X0) { y += h + 2; b = Bounds(y); x = b.X0; }
            if (y + h > lane.Y + lane.H) break;
            result.Add(new LabelLayout(seriesId, new Rect(x, y, w, h)));
            x += w + 6;
        }
        return result;
    }

    /// <summary>Computes the full layout; <paramref name="legendRows"/> sizes an overlay legend and <paramref name="measureRows"/> the measurement table (0 hides it).</summary>
    public static TrendLayout Layout(TrendChartConfig config, double width, double height, IReadOnlyList<LaneLayoutInput> lanes, int legendRows = 0, int measureRows = 0)
    {
        var m = config.Margin;
        double x0 = m, x1 = width - m, y0 = m, y1 = height - m;
        Rect? legend = null;
        if (config.Legend == LegendPosition.Right) { legend = new Rect(x1 - config.LegendWidth, y0, config.LegendWidth, y1 - y0); x1 = legend.Value.X - m; }
        else if (config.Legend == LegendPosition.Top) { legend = new Rect(x0, y0, x1 - x0, 24); y0 += 24 + m; }

        Rect? navigator = null;
        if (config.Navigator) { navigator = new Rect(x0, y1 - config.NavigatorHeight, Math.Max(0, x1 - x0), config.NavigatorHeight); y1 = navigator.Value.Y - m; }
        // measurement table over the cursor span: its own band above the navigator (title row, header row, one row per analog signal)
        Rect? measure = null;
        if (config.MeasurePanel && measureRows > 0)
        {
            var mh = Math.Min(Math.Max(0, y1 - y0) / 2, (measureRows + 2) * config.LegendRowHeight + 2 * config.Style.LegendPadding);
            measure = new Rect(x0, y1 - mh, Math.Max(0, x1 - x0), mh);
            y1 = measure.Value.Y - m;
        }

        var headerW = config.LaneHeaders ? config.LaneHeaderWidth : 0;
        var leftCols = lanes.Count == 0 ? 0 : lanes.Max(l => l.LeftAxes.Count);
        var rightCols = lanes.Count == 0 ? 0 : lanes.Max(l => l.RightAxes.Count);
        var plotX = x0 + headerW + leftCols * config.YAxisWidth;
        var plotRight = x1 - rightCols * config.YAxisWidth;
        var timeAxis = new Rect(plotX, y1 - config.TimeAxisHeight, Math.Max(0, plotRight - plotX), config.TimeAxisHeight);
        double plotTop = y0, plotBottom = timeAxis.Y;
        if (navigator is { } nv) navigator = nv with { X = plotX, W = Math.Max(0, plotRight - plotX) };
        if (measure is { } mv) measure = mv with { X = plotX, W = Math.Max(0, plotRight - plotX) };

        var plot = new Rect(plotX, plotTop, Math.Max(0, plotRight - plotX), Math.Max(0, plotBottom - plotTop));
        if (IsOverlay(config.Legend) && legendRows > 0)
        {
            double lw = Math.Min(config.LegendWidth, plot.W), lh = Math.Min(LegendHeight(config, legendRows, lanes.Count), plot.H);
            bool right = config.Legend is LegendPosition.TopRight or LegendPosition.BottomRight, bottom = config.Legend is LegendPosition.BottomLeft or LegendPosition.BottomRight;
            legend = new Rect(right ? plot.X + plot.W - lw - m : plot.X + m, bottom ? plot.Y + plot.H - lh - m : plot.Y + m, lw, lh);
        }
        var avoid = IsOverlay(config.Legend) ? legend : null;

        var n = Math.Max(1, lanes.Count);
        var open = lanes.Where(l => !l.Collapsed).ToList(); var collapsedCount = lanes.Count - open.Count;
        var totalWeight = open.Sum(l => Math.Max(0.01, l.Weight));
        if (totalWeight == 0) totalWeight = 1;
        var avail = Math.Max(0, plotBottom - plotTop - config.LaneGap * (n - 1) - collapsedCount * config.CollapsedLaneHeight);
        var result = new List<LaneLayout>();
        var y = plotTop;
        foreach (var lane in lanes)
        {
            var h = lane.Collapsed ? config.CollapsedLaneHeight : avail * Math.Max(0.01, lane.Weight) / totalWeight;
            var rect = new Rect(plotX, y, Math.Max(0, plotRight - plotX), h);
            // logic-analyzer tracks sit at the bottom; a digital-only lane is all stack
            Rect analog = rect; Rect? stack = null;
            if (!lane.Collapsed && lane.Tracks > 0)
            {
                var sh = lane.Analog ? Math.Min(lane.Tracks * config.DigitalTrackHeight, h * config.DigitalStackShare) : h;
                stack = new Rect(rect.X, y + h - sh, rect.W, sh);
                analog = new Rect(rect.X, y, rect.W, h - sh);
            }
            var axes = new List<AxisLayout>();
            if (!lane.Collapsed)
            {
                for (var i = 0; i < lane.LeftAxes.Count; i++) axes.Add(new AxisLayout(lane.LeftAxes[i], new Rect(plotX - (i + 1) * config.YAxisWidth, analog.Y, config.YAxisWidth, analog.H), AxisSide.Left));
                for (var i = 0; i < lane.RightAxes.Count; i++) axes.Add(new AxisLayout(lane.RightAxes[i], new Rect(plotRight + i * config.YAxisWidth, analog.Y, config.YAxisWidth, analog.H), AxisSide.Right));
            }
            Rect? header = headerW > 0 ? new Rect(x0, y, headerW, h) : null;
            // analog labels sit along the top of the analog area; a digital track's label is its name at the left of its band
            var labels = new List<LabelLayout>();
            if (!lane.Collapsed && config.PlotLabels)
            {
                var analogLabels = lane.Labels.Where(l => !l.Digital).Select(l => (l.SeriesId, l.Name)).ToList();
                var digitalLabels = lane.Labels.Where(l => l.Digital).ToList();
                labels = LayoutLabels(config, analog, analogLabels, avoid);
                if (stack is { } sk && digitalLabels.Count > 0)
                {
                    var th = sk.H / digitalLabels.Count; var lh = config.Theme.FontSize + 6; var sw = config.Style.LegendSwatchLength;
                    for (var i = 0; i < digitalLabels.Count; i++)
                        labels.Add(new LabelLayout(digitalLabels[i].SeriesId, new Rect(sk.X + 4, sk.Y + i * th + th / 2 - lh / 2, sw + 4 + EstimateTextWidth(digitalLabels[i].Name, config.Theme.FontSize) + 8, lh)));
                }
            }
            result.Add(new LaneLayout(lane.LaneId, rect, analog, stack, axes, header, labels, lane.Collapsed));
            y += h + config.LaneGap;
        }
        return new TrendLayout(width, height, plot, result, timeAxis, legend, navigator, measure);
    }
}
