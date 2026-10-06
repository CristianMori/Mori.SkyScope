// Mori.SkyScope — Trend chart legend, drag cues and the per-series geometry for a GPU line pass.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>What a GPU line pass needs for one series: raw data-space [t, v, …] points, the <paramref name="XOrigin"/> to subtract from t before upload, the clip transform, colour, width and scissor rectangle.</summary>
public sealed record SeriesGeometry(string SeriesId, double[] Points, double XOrigin, ClipTransform Clip, string Color, double Width, Rect Scissor);

public static partial class TrendChartRenderer
{
    /// <summary>Legend/readout number formatting shared with TS: fewer decimals for bigger magnitudes.</summary>
    public static string FormatValue(double v)
    {
        var a = Math.Abs(v);
        return Ticks.FormatNumber(v, a >= 1000 ? 0 : a >= 10 ? 1 : a >= 1 ? 2 : 3);
    }

    private static void DrawLegend(TrendChartModel m, IPainter p, Rect r, TextStyle text, TextStyle muted)
    {
        var th = m.Config.Theme; var st = m.Config.Style; var pad = st.LegendPadding; var sw = st.LegendSwatchLength;
        p.Rect(r.X, r.Y, r.W, r.H, new Fill(th.LegendBackground) { Opacity = st.LegendOpacity }, new Stroke(th.LegendBorder), st.LegendRadius);
        var y = r.Y + pad;
        var rowH = m.Config.LegendRowHeight;
        var lanes = m.Lanes();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (i > 0) { p.Line(r.X + pad, y + 2, r.X + r.W - pad, y + 2, new Stroke(th.LegendBorder)); y += 4; }
            foreach (var (_, series) in m.LegendGroups(lanes[i].Id, true))
            {
                double first = -1, last = -1;
                foreach (var s in series)
                {
                    if (y + rowH > r.Y + r.H + 1) return;
                    // hidden signals stay in the legend, dimmed, so a click can bring them back
                    var hidden = !s.Visible; var dim = hidden || (m.Drag?.SeriesIds.Contains(s.Id) ?? false);
                    var color = m.SeriesColor(s);
                    if (first < 0) first = y + rowH / 2;
                    last = y + rowH / 2;
                    p.Line(r.X + pad, y + rowH / 2, r.X + pad + sw, y + rowH / 2, new Stroke(color) { Width = 2, Opacity = dim ? 0.35 : 1 });
                    p.Text(m.SeriesName(s), r.X + pad + sw + 6, y + rowH / 2, (dim ? muted : text) with { Baseline = TextBaseline.Middle });
                    var v = hidden ? null : m.LegendValue(s);
                    var unit = m.UnitOf(s);
                    var shown = hidden ? "hidden" : v is null ? "—" : s.Kind == SeriesKind.Digital ? (v.Value >= 0.5 ? "true" : "false") : FormatValue(v.Value) + (unit is null ? "" : $" {unit}");
                    p.Text(shown, r.X + r.W - pad, y + rowH / 2, (hidden ? muted : text) with { Align = TextAlign.Right, Baseline = TextBaseline.Middle });
                    y += rowH;
                }
                // shared axis: a bracket on the left joins its rows
                if (series.Count > 1 && first >= 0)
                {
                    var bx = r.X + 3;
                    p.Line(bx, first, bx, last, new Stroke(th.MutedText));
                    p.Line(bx, first, bx + 3, first, new Stroke(th.MutedText));
                    p.Line(bx, last, bx + 3, last, new Stroke(th.MutedText));
                }
            }
        }
    }

    /// <summary>Seconds as a compact label: ms below one second, else seconds with three decimals.</summary>
    private static string FormatSeconds(double s) { var a = Math.Abs(s); return a < 1 ? $"{Ticks.FormatNumber(s * 1000, 1)} ms" : $"{Ticks.FormatNumber(s, 3)} s"; }

    /// <summary>
    /// The measurement table over the cursor span: a title row with Δt and its frequency, a header row, then per analog
    /// signal the value at A and B, their difference, and the minimum, maximum and mean of the samples in the span.
    /// </summary>
    private static void DrawMeasurePanel(TrendChartModel m, IPainter p, TrendLayout layout, Ctx c)
    {
        if (layout.Measure is not { } r || m.Measurements() is not { } ms) return;
        var th = m.Config.Theme; var st = m.Config.Style; var pad = st.LegendPadding; var rowH = m.Config.LegendRowHeight;
        p.Rect(r.X, r.Y, r.W, r.H, new Fill(th.LegendBackground) { Opacity = st.LegendOpacity }, new Stroke(th.LegendBorder), st.LegendRadius);
        p.Save();
        p.ClipRect(r.X, r.Y, r.W, r.H);
        var y = r.Y + pad;
        var title = $"A → B   Δt {FormatSeconds(ms.Dt)}{(ms.Hz is { } hz ? $"   {Ticks.FormatNumber(hz, 3)} Hz" : "")}";
        p.Text(title, r.X + pad, y + rowH / 2, c.Text with { Baseline = TextBaseline.Middle });
        y += rowH;
        var nameW = Math.Max(60, r.W * 0.22); var colW = (r.W - 2 * pad - nameW) / 6;
        string[] cols = ["A", "B", "Δ", "min", "max", "mean"];
        for (var i = 0; i < cols.Length; i++) p.Text(cols[i], r.X + pad + nameW + (i + 1) * colW, y + rowH / 2, c.Muted with { Align = TextAlign.Right, Baseline = TextBaseline.Middle });
        p.Line(r.X + pad, y + rowH - 1, r.X + r.W - pad, y + rowH - 1, new Stroke(th.LegendBorder));
        y += rowH;
        foreach (var row in ms.Rows)
        {
            var s = m.Config.Series.FirstOrDefault(x => x.Id == row.SeriesId);
            if (s is null) continue;
            var color = m.SeriesColor(s);
            p.Line(r.X + pad, y + rowH / 2, r.X + pad + st.LegendSwatchLength, y + rowH / 2, new Stroke(color) { Width = 2 });
            p.Text(m.SeriesName(s), r.X + pad + st.LegendSwatchLength + 6, y + rowH / 2, c.Text with { Baseline = TextBaseline.Middle });
            double?[] values = [row.A, row.B, row.Delta, row.Min, row.Max, row.Mean];
            for (var i = 0; i < values.Length; i++) p.Text(values[i] is { } v ? FormatValue(v) : "—", r.X + pad + nameW + (i + 1) * colW, y + rowH / 2, c.Text with { Align = TextAlign.Right, Baseline = TextBaseline.Middle });
            y += rowH;
        }
        p.Restore();
    }

    private static void DrawDragIndicator(TrendChartModel m, IPainter p, TrendLayout layout, TextStyle text)
    {
        var d = m.Drag!; var th = m.Config.Theme; var color = th.DropIndicator;
        switch (d.Target)
        {
            case DropTarget.Join j:
            {
                var lane = layout.Lanes.FirstOrDefault(l => l.LaneId == j.LaneId);
                var axis = lane?.Axes.FirstOrDefault(a => a.AxisId == j.AxisId);
                if (axis is not null)
                {
                    var r = axis.Rect;
                    p.Rect(r.X, r.Y, r.W, r.H, new Fill(color) { Opacity = 0.12 }, new Stroke(color) { Width = 2, Opacity = 0.8 });
                    var ax = axis.Side == AxisSide.Left ? r.X + r.W - 4 : r.X + 4; var ay = d.Y; var dir = axis.Side == AxisSide.Left ? -1 : 1;
                    p.Polygon([ax, ay, ax + dir * 8, ay - 5, ax + dir * 8, ay + 5], new Fill(th.MutedText));
                }
                else if (lane is not null) p.Rect(lane.Rect.X, lane.Rect.Y, lane.Rect.W, lane.Rect.H, null, new Stroke(color) { Width = 2, Opacity = 0.8 });
                break;
            }
            case DropTarget.OwnAxis o:
                if (layout.Lanes.FirstOrDefault(l => l.LaneId == o.LaneId) is { } ol)
                    p.Rect(ol.Rect.X, ol.Rect.Y, ol.Rect.W, ol.Rect.H, new Fill(color) { Opacity = 0.06 }, new Stroke(color) { Width = 2, Dash = [6, 4], Opacity = 0.8 });
                break;
            case DropTarget.Stack k:
            {
                // logic stack of this lane: solid highlight of the stack band (or the whole lane when it has none yet)
                var lane = layout.Lanes.FirstOrDefault(l => l.LaneId == k.LaneId);
                if (lane is not null) { var s = lane.Stack ?? lane.Rect; p.Rect(s.X, s.Y, s.W, s.H, new Fill(color) { Opacity = 0.12 }, new Stroke(color) { Width = 2, Opacity = 0.8 }); }
                break;
            }
            case DropTarget.NewLane n:
            {
                var after = n.AfterLaneId is null ? null : layout.Lanes.FirstOrDefault(l => l.LaneId == n.AfterLaneId);
                var y = after is null ? layout.Plot.Y : after.Rect.Y + after.Rect.H + m.Config.LaneGap / 2;
                p.Line(layout.Plot.X, y, layout.Plot.X + layout.Plot.W, y, new Stroke(color) { Width = 3, Opacity = 0.9 });
                break;
            }
        }
        var names = d.SeriesIds.Select(id => m.Config.Series.FirstOrDefault(x => x.Id == id)).Where(x => x is not null).Select(x => m.SeriesName(x!)).ToList();
        foreach (var ch in d.ChannelIds) names.Add(m.Store.Get(ch)?.Info.Name ?? $"#{ch}");
        if (names.Count > 0)
        {
            var label = names.Count > 3 ? $"{string.Join(", ", names.Take(3))} +{names.Count - 3}" : string.Join(", ", names);
            var w = p.MeasureText(label, text).Width + 16;
            p.Rect(d.X + 12, d.Y - 9, w, 18, new Fill(th.LegendBackground) { Opacity = 0.95 }, new Stroke(color), 3);
            p.Text(label, d.X + 20, d.Y, text with { Baseline = TextBaseline.Middle });
        }
    }

    /// <summary>Per-series data-space polylines plus clip transforms for a GPU line pass (scissor = the lane's analog area).</summary>
    public static List<SeriesGeometry> Geometry(TrendChartModel m, TrendLayout layout)
    {
        var ts = m.TimeScale(layout);
        var (t0, _) = m.Window();
        var result = new List<SeriesGeometry>();
        foreach (var lane in layout.Lanes)
            foreach (var s in lane.Collapsed ? [] : m.SeriesIn(lane.LaneId).Where(s => s.Kind != SeriesKind.Digital))
            {
                var points = m.SeriesPolyline(s.Id);
                if (points.Length < 4) continue;
                var ys = m.SeriesScale(s, lane);
                result.Add(new SeriesGeometry(s.Id, points, t0, ClipTransform.From(ts, ys, layout.Width, layout.Height, t0), m.SeriesColor(s), s.Width ?? m.Config.Style.SeriesWidth, lane.Analog));
            }
        return result;
    }
}
