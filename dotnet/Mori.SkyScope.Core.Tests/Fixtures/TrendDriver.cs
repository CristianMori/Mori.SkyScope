// Mori.SkyScope — Fixture driver for the trend chart: configuration parsing and setup.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the trend chart model: store and config from JSON, then commands, drags, layout, hit-test, readout, navigator and drawing queries.</summary>
public sealed partial class TrendDriver : IFixtureDriver
{
    /// <summary>Handles the <c>trend-chart</c> fixtures.</summary>
    public string Component => "trend-chart";

    private sealed class State(TrendChartModel model, double width, double height)
    {
        /// <summary>Configuration snapshot taken by the snapshotConfig step.</summary>
        public string? Snapshot { get; set; }
        public TrendChartModel Model { get; } = model;
        public double Width { get; } = width;
        public double Height { get; } = height;
        public TrendLayout? Layout { get; set; }
        public List<object?> Queries { get; } = [];
        public TrendLayout Ensure() => Layout ??= Model.Layout(Width, Height);
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static bool? Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static TrendChartConfig ParseConfig(JsonElement c)
    {
        var cfg = new TrendChartConfig();
        if (Num(c, "timeSpan") is { } ts) cfg.TimeSpan = ts;
        if (Str(c, "timeFormat") is { } tf) cfg.TimeFormat = tf == "utc" ? TimeFormat.Utc : TimeFormat.Relative;
        if (c.TryGetProperty("plotLabels", out var pl)) cfg.PlotLabels = pl.GetBoolean();
        if (c.TryGetProperty("laneHeaders", out var lh)) cfg.LaneHeaders = lh.GetBoolean();
        if (Num(c, "laneHeaderWidth") is { } lhw) cfg.LaneHeaderWidth = lhw;
        if (Num(c, "collapsedLaneHeight") is { } clh) cfg.CollapsedLaneHeight = clh;
        if (c.TryGetProperty("navigator", out var nvg)) cfg.Navigator = nvg.GetBoolean();
        if (Num(c, "navigatorHeight") is { } nh) cfg.NavigatorHeight = nh;
        if (c.TryGetProperty("navigatorFixedRange", out var nfr)) cfg.NavigatorFixedRange = nfr.GetBoolean();
        if (c.TryGetProperty("measurePanel", out var mp)) cfg.MeasurePanel = mp.GetBoolean();
        if (Num(c, "digitalTrackHeight") is { } dth) cfg.DigitalTrackHeight = dth;
        if (Num(c, "digitalStackShare") is { } dss) cfg.DigitalStackShare = dss;
        if (Str(c, "legend") is { } lg) cfg.Legend = lg switch { "top-left" => LegendPosition.TopLeft, "top-right" => LegendPosition.TopRight, "bottom-left" => LegendPosition.BottomLeft, "bottom-right" => LegendPosition.BottomRight, "top" => LegendPosition.Top, "none" => LegendPosition.None, _ => LegendPosition.Right };
        if (c.TryGetProperty("lanes", out var lanes))
            foreach (var l in lanes.EnumerateArray()) cfg.Lanes.Add(new LaneConfig(l.GetProperty("id").GetString()!) { Label = Str(l, "label"), Weight = Num(l, "weight") ?? 1, Collapsed = l.TryGetProperty("collapsed", out var col) && col.ValueKind == JsonValueKind.True });
        if (c.TryGetProperty("axes", out var axes))
            foreach (var a in axes.EnumerateArray())
                cfg.Axes.Add(new AxisConfig(a.GetProperty("id").GetString()!) { Label = Str(a, "label"), Unit = Str(a, "unit"), Min = Num(a, "min"), Max = Num(a, "max"), Side = Str(a, "side") == "right" ? AxisSide.Right : AxisSide.Left, Color = Str(a, "color") });
        if (c.TryGetProperty("series", out var series))
            foreach (var s in series.EnumerateArray())
                cfg.Series.Add(new SeriesConfig(s.GetProperty("id").GetString()!, s.GetProperty("channelId").GetInt32())
                {
                    Name = Str(s, "name"), Color = Str(s, "color"), AxisId = Str(s, "axisId"), LaneId = Str(s, "laneId"), Width = Num(s, "width"),
                    Visible = !s.TryGetProperty("visible", out var vis) || vis.GetBoolean(), Kind = Str(s, "kind") == "digital" ? SeriesKind.Digital : SeriesKind.Analog,
                });
        if (c.TryGetProperty("thresholds", out var th))
            foreach (var t in th.EnumerateArray()) cfg.Thresholds.Add(new ThresholdConfig(t.GetProperty("id").GetString()!, t.GetProperty("axisId").GetString()!, t.GetProperty("from").GetDouble(), t.GetProperty("color").GetString()!) { To = Num(t, "to"), Label = Str(t, "label") });
        if (c.TryGetProperty("markers", out var mk))
            foreach (var m in mk.EnumerateArray()) cfg.Markers.Add(new MarkerConfig(m.GetProperty("id").GetString()!, m.GetProperty("time").GetDouble()) { Label = Str(m, "label"), Color = Str(m, "color") });
        if (c.TryGetProperty("theme", out var themeEl))
        {
            var t = cfg.Theme;
            cfg.Theme = t with
            {
                Background = Str(themeEl, "background") ?? t.Background, PlotBackground = Str(themeEl, "plotBackground") ?? t.PlotBackground, Grid = Str(themeEl, "grid") ?? t.Grid, Axis = Str(themeEl, "axis") ?? t.Axis,
                Text = Str(themeEl, "text") ?? t.Text, MutedText = Str(themeEl, "mutedText") ?? t.MutedText, CursorA = Str(themeEl, "cursorA") ?? t.CursorA, CursorB = Str(themeEl, "cursorB") ?? t.CursorB,
                Hover = Str(themeEl, "hover") ?? t.Hover, Marker = Str(themeEl, "marker") ?? t.Marker, LegendBackground = Str(themeEl, "legendBackground") ?? t.LegendBackground, LegendBorder = Str(themeEl, "legendBorder") ?? t.LegendBorder,
                LaneBorder = Str(themeEl, "laneBorder") ?? t.LaneBorder, DropIndicator = Str(themeEl, "dropIndicator") ?? t.DropIndicator, FontFamily = Str(themeEl, "fontFamily") ?? t.FontFamily, FontSize = Num(themeEl, "fontSize") ?? t.FontSize,
                LaneHeader = Str(themeEl, "laneHeader") ?? t.LaneHeader, LaneHeaderText = Str(themeEl, "laneHeaderText") ?? t.LaneHeaderText, NavigatorFrame = Str(themeEl, "navigatorFrame") ?? t.NavigatorFrame, NavigatorBackground = Str(themeEl, "navigatorBackground") ?? t.NavigatorBackground,
            };
        }
        if (c.TryGetProperty("style", out var st))
        {
            var d = cfg.Style;
            static bool B(JsonElement e, string n, bool dflt) => e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : dflt;
            static double[]? Dash(JsonElement e, string n, double[]? dflt) => e.TryGetProperty(n, out var v) ? (v.ValueKind == JsonValueKind.Array ? SignalSteps.Doubles(v) : null) : dflt;
            cfg.Style = new ChartStyle
            {
                GridWidth = Num(st, "gridWidth") ?? d.GridWidth, GridDash = Dash(st, "gridDash", d.GridDash), ShowValueGrid = B(st, "showValueGrid", d.ShowValueGrid), ShowTimeGrid = B(st, "showTimeGrid", d.ShowTimeGrid),
                TrackSeparators = B(st, "trackSeparators", d.TrackSeparators), LaneBorder = B(st, "laneBorder", d.LaneBorder), LaneBorderWidth = Num(st, "laneBorderWidth") ?? d.LaneBorderWidth,
                AxisTickLength = Num(st, "axisTickLength") ?? d.AxisTickLength, AxisLabelGap = Num(st, "axisLabelGap") ?? d.AxisLabelGap, SeriesWidth = Num(st, "seriesWidth") ?? d.SeriesWidth,
                ThresholdBandOpacity = Num(st, "thresholdBandOpacity") ?? d.ThresholdBandOpacity, ThresholdLineDash = Dash(st, "thresholdLineDash", d.ThresholdLineDash) ?? d.ThresholdLineDash,
                MarkerWidth = Num(st, "markerWidth") ?? d.MarkerWidth, MarkerDash = Dash(st, "markerDash", d.MarkerDash) ?? d.MarkerDash, CursorWidth = Num(st, "cursorWidth") ?? d.CursorWidth, HoverDash = Dash(st, "hoverDash", d.HoverDash) ?? d.HoverDash,
                LegendPadding = Num(st, "legendPadding") ?? d.LegendPadding, LegendRowHeight = Num(st, "legendRowHeight") ?? d.LegendRowHeight, LegendOpacity = Num(st, "legendOpacity") ?? d.LegendOpacity,
                LegendRadius = Num(st, "legendRadius") ?? d.LegendRadius, LegendSwatchLength = Num(st, "legendSwatchLength") ?? d.LegendSwatchLength, DigitalTrackPadding = Num(st, "digitalTrackPadding") ?? d.DigitalTrackPadding, DigitalFillOpacity = Num(st, "digitalFillOpacity") ?? d.DigitalFillOpacity,
            };
        }
        return cfg;
    }

    /// <summary>Declares channels and pushes frames into a store, parses the chart <c>config</c> and sets the <c>now</c> of the model.</summary>
    public object Create(JsonElement setup)
    {
        var store = new SignalStore(100, 4096);
        if (setup.TryGetProperty("channels", out var chs)) foreach (var ch in chs.EnumerateArray()) store.DeclareChannel(ChannelCatalog.Parse(ch));
        if (setup.TryGetProperty("frames", out var frames)) foreach (var f in frames.EnumerateArray()) store.PushFrame(FrameJson.FromJson(JsonNode.Parse(f.GetRawText())!));
        var model = new TrendChartModel(store, setup.TryGetProperty("config", out var c) ? ParseConfig(c) : new TrendChartConfig()) { Now = Num(setup, "now") ?? 0 };
        return new State(model, setup.GetProperty("width").GetDouble(), setup.GetProperty("height").GetDouble());
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
