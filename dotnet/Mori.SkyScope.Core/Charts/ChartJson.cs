// Mori.SkyScope — Builds analytic chart configurations from the JSON option shape used by the TypeScript core (camelCase, kebab-case legend positions, part…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;

namespace Mori.SkyScope.Core.Charts;

/// <summary>
/// Builds analytic chart configurations from the JSON option shape used by the TypeScript core (camelCase, kebab-case legend
/// positions, partial theme/style objects merged over the defaults). Used by the fixture drivers and by hosts that load chart
/// definitions from files or the wire.
/// </summary>
public static class ChartJson
{
    /// <summary>String property, or null when missing or not a string.</summary>
    public static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    /// <summary>Numeric property, or null when missing or not a number.</summary>
    public static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    /// <summary>Boolean property, or null when missing or not a boolean.</summary>
    public static bool? Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    /// <summary>Numeric array property, or null when missing or not an array.</summary>
    public static double[]? Doubles(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array ? Doubles(v) : null;
    /// <summary>Elements of a JSON array as doubles; non-numeric elements become NaN.</summary>
    public static double[] Doubles(JsonElement a) => a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Number ? x.GetDouble() : double.NaN).ToArray();
    /// <summary>String array property, or null when missing or not an array.</summary>
    public static string[]? Strings(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray().Select(x => x.GetString()!).ToArray() : null;
    private static JsonElement? Obj(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    /// <summary>Kebab-case legend position; unknown values fall back to Right.</summary>
    public static LegendPosition ParseLegend(string s) => s switch
    {
        "top-left" => LegendPosition.TopLeft, "top-right" => LegendPosition.TopRight, "bottom-left" => LegendPosition.BottomLeft, "bottom-right" => LegendPosition.BottomRight,
        "top" => LegendPosition.Top, "none" => LegendPosition.None, _ => LegendPosition.Right,
    };
    /// <summary>Marker name; unknown values mean no marker.</summary>
    public static MarkerShape ParseMarker(string s) => s switch { "circle" => MarkerShape.Circle, "square" => MarkerShape.Square, "diamond" => MarkerShape.Diamond, _ => MarkerShape.None };

    /// <summary>Partial theme JSON merged over <paramref name="t"/>.</summary>
    public static ChartTheme MergeTheme(ChartTheme t, JsonElement e) => t with
    {
        Background = Str(e, "background") ?? t.Background, PlotBackground = Str(e, "plotBackground") ?? t.PlotBackground, Grid = Str(e, "grid") ?? t.Grid, Axis = Str(e, "axis") ?? t.Axis,
        Text = Str(e, "text") ?? t.Text, MutedText = Str(e, "mutedText") ?? t.MutedText, CursorA = Str(e, "cursorA") ?? t.CursorA, CursorB = Str(e, "cursorB") ?? t.CursorB,
        Hover = Str(e, "hover") ?? t.Hover, Marker = Str(e, "marker") ?? t.Marker, LegendBackground = Str(e, "legendBackground") ?? t.LegendBackground, LegendBorder = Str(e, "legendBorder") ?? t.LegendBorder,
        LaneBorder = Str(e, "laneBorder") ?? t.LaneBorder, DropIndicator = Str(e, "dropIndicator") ?? t.DropIndicator, FontFamily = Str(e, "fontFamily") ?? t.FontFamily, FontSize = Num(e, "fontSize") ?? t.FontSize,
    };

    /// <summary>Partial style JSON merged over <paramref name="d"/>.</summary>
    public static ChartStyle MergeStyle(ChartStyle d, JsonElement st)
    {
        static double[]? Dash(JsonElement e, string n, double[]? dflt) => e.TryGetProperty(n, out var v) ? (v.ValueKind == JsonValueKind.Array ? Doubles(v) : null) : dflt;
        return new ChartStyle
        {
            GridWidth = Num(st, "gridWidth") ?? d.GridWidth, GridDash = Dash(st, "gridDash", d.GridDash), ShowValueGrid = Bool(st, "showValueGrid") ?? d.ShowValueGrid, ShowTimeGrid = Bool(st, "showTimeGrid") ?? d.ShowTimeGrid,
            TrackSeparators = Bool(st, "trackSeparators") ?? d.TrackSeparators, LaneBorder = Bool(st, "laneBorder") ?? d.LaneBorder, LaneBorderWidth = Num(st, "laneBorderWidth") ?? d.LaneBorderWidth,
            AxisTickLength = Num(st, "axisTickLength") ?? d.AxisTickLength, AxisLabelGap = Num(st, "axisLabelGap") ?? d.AxisLabelGap, SeriesWidth = Num(st, "seriesWidth") ?? d.SeriesWidth,
            ThresholdBandOpacity = Num(st, "thresholdBandOpacity") ?? d.ThresholdBandOpacity, ThresholdLineDash = Dash(st, "thresholdLineDash", d.ThresholdLineDash) ?? d.ThresholdLineDash,
            MarkerWidth = Num(st, "markerWidth") ?? d.MarkerWidth, MarkerDash = Dash(st, "markerDash", d.MarkerDash) ?? d.MarkerDash, CursorWidth = Num(st, "cursorWidth") ?? d.CursorWidth, HoverDash = Dash(st, "hoverDash", d.HoverDash) ?? d.HoverDash,
            LegendPadding = Num(st, "legendPadding") ?? d.LegendPadding, LegendRowHeight = Num(st, "legendRowHeight") ?? d.LegendRowHeight, LegendOpacity = Num(st, "legendOpacity") ?? d.LegendOpacity,
            LegendRadius = Num(st, "legendRadius") ?? d.LegendRadius, LegendSwatchLength = Num(st, "legendSwatchLength") ?? d.LegendSwatchLength, DigitalTrackPadding = Num(st, "digitalTrackPadding") ?? d.DigitalTrackPadding,
        };
    }

    private static CartesianAxisConfig Axis(JsonElement a) => new()
    {
        Label = Str(a, "label"), Unit = Str(a, "unit"), Min = Num(a, "min"), Max = Num(a, "max"), Log = Bool(a, "log") ?? false, Categories = Strings(a, "categories"), IncludeZero = Bool(a, "includeZero") ?? false,
    };

    /// <summary>Builds a <see cref="CartesianChartConfig"/> from its JSON options; absent keys keep the defaults.</summary>
    public static CartesianChartConfig Cartesian(JsonElement o)
    {
        var c = new CartesianChartConfig { Title = Str(o, "title") };
        if (Obj(o, "xAxis") is { } xa) c.XAxis = Axis(xa);
        if (Obj(o, "yAxis") is { } ya) c.YAxis = Axis(ya);
        if (Obj(o, "y2Axis") is { } y2) c.Y2Axis = Axis(y2);
        if (o.TryGetProperty("series", out var series))
            foreach (var s in series.EnumerateArray())
                c.Series.Add(new CartesianSeriesConfig(s.GetProperty("id").GetString()!)
                {
                    Name = Str(s, "name"), X = Doubles(s, "x"), Y = Doubles(s, "y") ?? [], Color = Str(s, "color"), Width = Num(s, "width"), Stack = Str(s, "stack"),
                    Kind = Str(s, "kind") switch { "step" => CartesianSeriesKind.Step, "scatter" => CartesianSeriesKind.Scatter, "area" => CartesianSeriesKind.Area, "bar" => CartesianSeriesKind.Bar, _ => CartesianSeriesKind.Line },
                    Axis = Str(s, "axis") == "y2" ? YAxisId.Y2 : YAxisId.Y, Marker = Str(s, "marker") is { } mk ? ParseMarker(mk) : null, MarkerSize = Num(s, "markerSize"),
                    FillOpacity = Num(s, "fillOpacity"), BarWidth = Num(s, "barWidth"), Visible = Bool(s, "visible") ?? true,
                });
        c.BarGap = Num(o, "barGap") ?? c.BarGap;
        if (Str(o, "legend") is { } lg) c.Legend = ParseLegend(lg);
        c.ShowGrid = Bool(o, "showGrid") ?? c.ShowGrid;
        Common(o, c.Theme, c.Style, (t, s) => { c.Theme = t; c.Style = s; });
        c.Margin = Num(o, "margin") ?? c.Margin; c.YAxisWidth = Num(o, "yAxisWidth") ?? c.YAxisWidth; c.XAxisHeight = Num(o, "xAxisHeight") ?? c.XAxisHeight; c.TitleHeight = Num(o, "titleHeight") ?? c.TitleHeight;
        c.LegendWidth = Num(o, "legendWidth") ?? c.LegendWidth; c.TickSpacing = Num(o, "tickSpacing") ?? c.TickSpacing; c.HoverRadius = Num(o, "hoverRadius") ?? c.HoverRadius;
        return c;
    }

    /// <summary>Builds a <see cref="PieChartConfig"/> from its JSON options; absent keys keep the defaults.</summary>
    public static PieChartConfig Pie(JsonElement o)
    {
        var c = new PieChartConfig { Title = Str(o, "title") };
        if (o.TryGetProperty("slices", out var slices))
            foreach (var s in slices.EnumerateArray()) c.Slices.Add(new PieSliceConfig(s.GetProperty("id").GetString()!, Num(s, "value") ?? 0) { Name = Str(s, "name"), Color = Str(s, "color") });
        c.Donut = Num(o, "donut") ?? c.Donut; c.StartAngle = Num(o, "startAngle") ?? c.StartAngle; c.PadAngle = Num(o, "padAngle") ?? c.PadAngle;
        c.Labels = Str(o, "labels") switch { "none" => PieLabelMode.None, "value" => PieLabelMode.Value, "name" => PieLabelMode.Name, null => c.Labels, _ => PieLabelMode.Percent };
        c.LabelPosition = Str(o, "labelPosition") == "outside" ? PieLabelPosition.Outside : PieLabelPosition.Inside;
        c.MinLabelFraction = Num(o, "minLabelFraction") ?? c.MinLabelFraction; c.Sort = Bool(o, "sort") ?? c.Sort; c.CenterText = Str(o, "centerText");
        c.HoverExplode = Num(o, "hoverExplode") ?? c.HoverExplode; c.Decimals = (int)(Num(o, "decimals") ?? c.Decimals);
        if (Str(o, "legend") is { } lg) c.Legend = ParseLegend(lg);
        Common(o, c.Theme, c.Style, (t, s) => { c.Theme = t; c.Style = s; });
        c.Margin = Num(o, "margin") ?? c.Margin; c.TitleHeight = Num(o, "titleHeight") ?? c.TitleHeight; c.LegendWidth = Num(o, "legendWidth") ?? c.LegendWidth;
        return c;
    }

    /// <summary>Builds a <see cref="PolarChartConfig"/> from its JSON options; absent keys keep the defaults.</summary>
    public static PolarChartConfig Polar(JsonElement o)
    {
        var c = new PolarChartConfig { Title = Str(o, "title"), Categories = Strings(o, "categories"), Min = Num(o, "min"), Max = Num(o, "max") };
        if (o.TryGetProperty("series", out var series))
            foreach (var s in series.EnumerateArray())
                c.Series.Add(new PolarSeriesConfig(s.GetProperty("id").GetString()!)
                {
                    Name = Str(s, "name"), Angles = Doubles(s, "angles"), Values = Doubles(s, "values") ?? [], Color = Str(s, "color"), Width = Num(s, "width"), FillOpacity = Num(s, "fillOpacity"),
                    Kind = Str(s, "kind") switch { "line" => PolarSeriesKind.Line, "area" => PolarSeriesKind.Area, "scatter" => PolarSeriesKind.Scatter, _ => null },
                    Closed = Bool(s, "closed"), Marker = Str(s, "marker") is { } mk ? ParseMarker(mk) : null, MarkerSize = Num(s, "markerSize"), Visible = Bool(s, "visible") ?? true,
                });
        c.StartAngle = Num(o, "startAngle") ?? c.StartAngle; c.Clockwise = Bool(o, "clockwise") ?? c.Clockwise; c.AngleStep = Num(o, "angleStep") ?? c.AngleStep;
        c.GridShape = Str(o, "gridShape") == "polygon" ? PolarGridShape.Polygon : PolarGridShape.Circle; c.Rings = (int)(Num(o, "rings") ?? c.Rings);
        if (Str(o, "legend") is { } lg) c.Legend = ParseLegend(lg);
        c.ShowGrid = Bool(o, "showGrid") ?? c.ShowGrid;
        Common(o, c.Theme, c.Style, (t, s) => { c.Theme = t; c.Style = s; });
        c.Margin = Num(o, "margin") ?? c.Margin; c.TitleHeight = Num(o, "titleHeight") ?? c.TitleHeight; c.LegendWidth = Num(o, "legendWidth") ?? c.LegendWidth; c.HoverRadius = Num(o, "hoverRadius") ?? c.HoverRadius;
        return c;
    }

    /// <summary>Builds a <see cref="HeatmapConfig"/> from its JSON options; <c>colormap</c> may be a map name or an array of hex stops.</summary>
    public static HeatmapConfig Heatmap(JsonElement o)
    {
        var c = new HeatmapConfig { Title = Str(o, "title"), XLabel = Str(o, "xLabel"), YLabel = Str(o, "yLabel"), ValueLabel = Str(o, "valueLabel"), Min = Num(o, "min"), Max = Num(o, "max") };
        c.Cols = (int)(Num(o, "cols") ?? c.Cols); c.Rows = (int)(Num(o, "rows") ?? c.Rows);
        c.XMin = Num(o, "xMin") ?? c.XMin; c.XMax = Num(o, "xMax") ?? c.XMax; c.YMin = Num(o, "yMin") ?? c.YMin; c.YMax = Num(o, "yMax") ?? c.YMax;
        if (o.TryGetProperty("colormap", out var cm)) { if (cm.ValueKind == JsonValueKind.Array) c.ColormapStops = cm.EnumerateArray().Select(x => x.GetString()!).ToArray(); else c.Colormap = cm.GetString()!; }
        c.Colorbar = Bool(o, "colorbar") ?? c.Colorbar; c.ColorbarWidth = Num(o, "colorbarWidth") ?? c.ColorbarWidth; c.Rolling = Bool(o, "rolling") ?? c.Rolling; c.ShowGrid = Bool(o, "showGrid") ?? c.ShowGrid;
        Common(o, c.Theme, c.Style, (t, s) => { c.Theme = t; c.Style = s; });
        c.Margin = Num(o, "margin") ?? c.Margin; c.YAxisWidth = Num(o, "yAxisWidth") ?? c.YAxisWidth; c.XAxisHeight = Num(o, "xAxisHeight") ?? c.XAxisHeight; c.TitleHeight = Num(o, "titleHeight") ?? c.TitleHeight; c.TickSpacing = Num(o, "tickSpacing") ?? c.TickSpacing;
        return c;
    }

    private static void Common(JsonElement o, ChartTheme theme, ChartStyle style, Action<ChartTheme, ChartStyle> set)
    {
        if (Obj(o, "theme") is { } t) theme = MergeTheme(theme, t);
        if (Obj(o, "style") is { } s) style = MergeStyle(style, s);
        set(theme, style);
    }
}
