// Mori.SkyScope — Trend chart drawing in three passes (background, series, foreground): lanes, axes, logic tracks, headers, labels, navigator.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Charts;

/// <summary>
/// Paints a TrendChart with any <see cref="IPainter"/> in three passes — background (lane fills, thresholds, grid),
/// series, foreground (markers, cursors, axes, legend) — so a GPU pass can replace the middle one.
/// Every number goes through the same code as <c>charts/trend-draw.ts</c>; RecordingPainter output is byte-comparable.
/// </summary>
public static partial class TrendChartRenderer
{
    private sealed record Ctx(ChartTheme Th, TextStyle Text, TextStyle Muted, Stroke Grid, Stroke Axis, TimeScale Ts, List<double> TimeTicks, int TimeTickCount, double T0, double T1);

    private static int YTickCount(double height, double spacing) => Math.Max(2, (int)Math.Round(height / (spacing * 0.6)));

    private static Ctx MakeCtx(TrendChartModel m, TrendLayout layout)
    {
        var th = m.Config.Theme;
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize };
        var ts = m.TimeScale(layout);
        var count = Math.Max(2, (int)Math.Round(layout.Plot.W / m.Config.TickSpacing));
        var (t0, t1) = m.Window();
        var st = m.Config.Style;
        return new Ctx(th, text, text with { Color = th.MutedText }, new Stroke(th.Grid) { Width = st.GridWidth, Dash = st.GridDash }, new Stroke(th.Axis), ts, ts.TickValues(count), count, t0, t1);
    }

    /// <summary>All three passes; <paramref name="drawSeries"/> false skips the series pass for hosts that draw it on the GPU.</summary>
    public static void Draw(TrendChartModel m, IPainter p, TrendLayout layout, bool drawSeries = true)
    {
        DrawBackground(m, p, layout);
        if (drawSeries) DrawSeries(m, p, layout);
        DrawForeground(m, p, layout);
    }

    /// <summary>Pass 1: lane fills, threshold bands and lines, grids, track separators, lane borders and the navigator background.</summary>
    public static void DrawBackground(TrendChartModel m, IPainter p, TrendLayout layout)
    {
        var c = MakeCtx(m, layout); var st = m.Config.Style;
        p.Clear(c.Th.Background);
        foreach (var lane in layout.Lanes)
        {
            var r = lane.Rect;
            p.Rect(r.X, r.Y, r.W, r.H, new Fill(c.Th.PlotBackground));
            if (lane.Collapsed) continue;
            // analog content (thresholds, value grid, series) is confined to the analog area above the logic stack
            var a = lane.Analog;
            p.Save();
            p.ClipRect(a.X, a.Y, a.W, a.H);
            var axes = m.AxesIn(lane.LaneId);
            foreach (var t in m.Config.Thresholds)
            {
                if (axes.All(a => a.Id != t.AxisId)) continue;
                var ys = m.YScale(t.AxisId, lane);
                if (t.To is { } to) { double y1 = ys.Apply(to), y0 = ys.Apply(t.From); p.Rect(r.X, Math.Min(y0, y1), r.W, Math.Abs(y1 - y0), new Fill(t.Color) { Opacity = st.ThresholdBandOpacity }); }
                else { var y = ys.Apply(t.From); p.Line(r.X, y, r.X + r.W, y, new Stroke(t.Color) { Dash = st.ThresholdLineDash }); }
            }
            if (m.Config.ShowGrid)
            {
                if (st.ShowValueGrid && axes.FirstOrDefault() is { } first)
                {
                    var ys = m.YScale(first.Id, lane);
                    foreach (var v in ys.TickValues(YTickCount(a.H, m.Config.TickSpacing))) { var y = ys.Apply(v); p.Line(r.X, y, r.X + r.W, y, c.Grid); }
                }
            }
            p.Restore();
            p.Save();
            p.ClipRect(r.X, r.Y, r.W, r.H);
            if (m.Config.ShowGrid)
            {
                var tracks = st.TrackSeparators ? m.DigitalTracks(lane.LaneId) : [];
                for (var i = 1; i < tracks.Count; i++) { var t = TrendChartModel.TrackRect(lane, i, tracks.Count); p.Line(r.X, t.Y, r.X + r.W, t.Y, c.Grid); }
                if (lane.Stack is { } sk && lane.Analog.H > 0) p.Line(r.X, sk.Y, r.X + r.W, sk.Y, c.Axis);
                if (st.ShowTimeGrid) foreach (var v in c.TimeTicks) { var x = c.Ts.Apply(v); p.Line(x, r.Y, x, r.Y + r.H, c.Grid); }
            }
            p.Restore();
            if (st.LaneBorder) p.Rect(r.X, r.Y, r.W, r.H, null, new Stroke(c.Th.LaneBorder) { Width = st.LaneBorderWidth });
        }
        if (layout.Navigator is { } nv) p.Rect(nv.X, nv.Y, nv.W, nv.H, new Fill(c.Th.NavigatorBackground), new Stroke(c.Th.LaneBorder));
    }

    /// <summary>Pass 2: analog series polylines, clipped to each lane's analog area.</summary>
    public static void DrawSeries(TrendChartModel m, IPainter p, TrendLayout layout)
    {
        var c = MakeCtx(m, layout);
        var scratch = new List<double>();
        foreach (var lane in layout.Lanes)
        {
            if (lane.Collapsed || lane.Analog.H <= 0) continue;
            var r = lane.Analog;
            p.Save();
            p.ClipRect(r.X, r.Y, r.W, r.H);
            foreach (var s in m.SeriesIn(lane.LaneId))
            {
                if (s.Kind == SeriesKind.Digital) continue;   // logic tracks are drawn in the foreground pass (filled, 2D)
                var pts = m.SeriesPolyline(s.Id);
                if (pts.Length < 4) continue;
                var ys = m.SeriesScale(s, lane);
                scratch.Clear();
                for (var i = 0; i < pts.Length; i += 2) { scratch.Add(c.Ts.Apply(pts[i])); scratch.Add(ys.Apply(pts[i + 1])); }
                p.Polyline(scratch.ToArray(), new Stroke(m.SeriesColor(s)) { Width = s.Width ?? m.Config.Style.SeriesWidth, Join = LineJoin.Round });
            }
            p.Restore();
        }
    }

    /// <summary>Pass 3: lane headers, logic tracks, markers, cursors, in-plot labels, axes, time axis, navigator, legend and drag cues.</summary>
    public static void DrawForeground(TrendChartModel m, IPainter p, TrendLayout layout)
    {
        var c = MakeCtx(m, layout); var st = m.Config.Style;
        foreach (var lane in layout.Lanes)
        {
            var r = lane.Rect;
            if (lane.Header is not null) DrawLaneHeader(m, p, lane);
            if (lane.Collapsed)
            {
                var names = string.Join(", ", m.SeriesIn(lane.LaneId).Select(m.SeriesName));
                p.Text(names, r.X + 6, r.Y + r.H / 2, c.Muted with { Baseline = TextBaseline.Middle });
                continue;
            }
            p.Save();
            p.ClipRect(r.X, r.Y, r.W, r.H);
            DrawDigitalTracks(m, p, lane, c);
            foreach (var mk in m.Config.Markers)
            {
                if (mk.Time < c.T0 || mk.Time > c.T1) continue;
                var x = c.Ts.Apply(mk.Time);
                var color = mk.Color ?? c.Th.Marker;
                p.Line(x, r.Y, x, r.Y + r.H, new Stroke(color) { Width = st.MarkerWidth, Dash = st.MarkerDash });
                if (mk.Label is not null) p.Text(mk.Label, x + 3, r.Y + 3, c.Text with { Color = color, Baseline = TextBaseline.Top });
            }
            if (m.HoverTime is { } ht) { var x = c.Ts.Apply(ht); p.Line(x, r.Y, x, r.Y + r.H, new Stroke(c.Th.Hover) { Width = st.CursorWidth, Dash = st.HoverDash }); }
            if (m.CursorA is { } sa && m.CursorB is { } sb) { double xa = c.Ts.Apply(sa), xb = c.Ts.Apply(sb); p.Rect(Math.Min(xa, xb), r.Y, Math.Abs(xb - xa), r.H, new Fill(c.Th.CursorA) { Opacity = 0.06 }); }
            if (m.CursorA is { } ca) { var x = c.Ts.Apply(ca); p.Line(x, r.Y, x, r.Y + r.H, new Stroke(c.Th.CursorA) { Width = st.CursorWidth }); }
            if (m.CursorB is { } cb) { var x = c.Ts.Apply(cb); p.Line(x, r.Y, x, r.Y + r.H, new Stroke(c.Th.CursorB) { Width = st.CursorWidth }); }
            if (lane == layout.Lanes.FirstOrDefault(l => !l.Collapsed))
            {
                // the cursors' times at the top of the first open lane
                void Tag(double t, string color, string label)
                {
                    var x = c.Ts.Apply(t); var s = $"{label} {TimeFormatting.FormatTick(t - c.Ts.Origin, 0.001, c.Ts.Mode, 3, Math.Abs(c.Ts.D1 - c.Ts.D0))}"; var w = TrendLayoutEngine.EstimateTextWidth(s, c.Th.FontSize) + 8;
                    p.Rect(x + 2, r.Y + 2, w, c.Th.FontSize + 4, new Fill(color) { Opacity = 0.85 }, null, 2);
                    p.Text(s, x + 6, r.Y + 4 + c.Th.FontSize / 2, c.Text with { Color = c.Th.Background, Baseline = TextBaseline.Middle });
                }
                if (m.CursorA is { } cta) Tag(cta, c.Th.CursorA, "A");
                if (m.CursorB is { } ctb) Tag(ctb, c.Th.CursorB, "B");
            }
            DrawPlotLabels(m, p, lane, c.Text, c.Muted);
            p.Restore();

            foreach (var al in lane.Axes)
            {
                var ax = m.Axis(al.AxisId); var ys = m.YScale(al.AxisId, lane); var ar = al.Rect;
                var left = al.Side == AxisSide.Left;
                var lineX = left ? ar.X + ar.W : ar.X;
                p.Line(lineX, ar.Y, lineX, ar.Y + ar.H, c.Axis);
                var count = YTickCount(ar.H, m.Config.TickSpacing);
                foreach (var v in ys.TickValues(count))
                {
                    var y = ys.Apply(v);
                    p.Line(left ? lineX - st.AxisTickLength : lineX, y, left ? lineX : lineX + st.AxisTickLength, y, c.Axis);
                    p.Text(ys.Format(v, count), left ? lineX - st.AxisLabelGap : lineX + st.AxisLabelGap, y, c.Text with { Align = left ? TextAlign.Right : TextAlign.Left, Baseline = TextBaseline.Middle });
                }
                var title = ax.Label ?? ax.Unit;
                if (title is not null) p.Text(title, left ? ar.X + 2 : ar.X + ar.W - 2, ar.Y + 2, c.Muted with { Align = left ? TextAlign.Left : TextAlign.Right, Baseline = TextBaseline.Top });
            }
        }

        var ta = layout.TimeAxis;
        p.Line(ta.X, ta.Y, ta.X + ta.W, ta.Y, c.Axis);
        foreach (var v in c.TimeTicks)
        {
            var x = c.Ts.Apply(v);
            p.Line(x, ta.Y, x, ta.Y + st.AxisTickLength, c.Axis);
            p.Text(c.Ts.Format(v, c.TimeTickCount), x, ta.Y + st.AxisTickLength + 2, c.Text with { Align = TextAlign.Center, Baseline = TextBaseline.Top });
        }

        if (layout.Navigator is not null) DrawNavigator(m, p, layout, c);
        if (layout.Measure is not null) DrawMeasurePanel(m, p, layout, c);
        if (layout.Legend is { } legend) DrawLegend(m, p, legend, c.Text, c.Muted);
        if (m.LaneDrag is not null) DrawLaneDragIndicator(m, p, layout);
        if (m.Drag is not null) DrawDragIndicator(m, p, layout, c.Text);
    }

    /// <summary>
    /// Logic-analyzer tracks: each digital series fills its band solid while high, with a stepped outline; its label (the drag
    /// handle) sits at the left of the band. No Y axis: the stack is the lane's bottom part (the whole lane when it has no analog series).
    /// </summary>
    private static void DrawDigitalTracks(TrendChartModel m, IPainter p, LaneLayout lane, Ctx c)
    {
        var tracks = m.DigitalTracks(lane.LaneId);
        if (tracks.Count == 0 || lane.Stack is null) return;
        var st = m.Config.Style;
        for (var i = 0; i < tracks.Count; i++)
        {
            var s = tracks[i];
            var ys = m.SeriesScale(s, lane); var color = m.SeriesColor(s);
            var pts = m.SeriesPolyline(s.Id);
            if (pts.Length >= 4)
            {
                var baseY = ys.Apply(0);
                var poly = new List<double> { c.Ts.Apply(pts[0]), baseY };
                for (var k = 0; k < pts.Length; k += 2) { poly.Add(c.Ts.Apply(pts[k])); poly.Add(ys.Apply(pts[k + 1])); }
                poly.Add(c.Ts.Apply(pts[^2])); poly.Add(baseY);
                p.Polygon(poly.ToArray(), new Fill(color) { Opacity = st.DigitalFillOpacity });
                p.Polyline(poly.GetRange(2, poly.Count - 4).ToArray(), new Stroke(color) { Width = s.Width ?? st.SeriesWidth, Join = LineJoin.Round });
            }
        }
    }

    /// <summary>Header bar: fold arrow at the top, grip in the middle, remove cross at the bottom.</summary>
    private static void DrawLaneHeader(TrendChartModel m, IPainter p, LaneLayout lane)
    {
        var h = lane.Header!.Value; var th = m.Config.Theme; var color = th.LaneHeaderText;
        p.Rect(h.X, h.Y, h.W, h.H, new Fill(th.LaneHeader), null, 2);
        var cx = h.X + h.W / 2;
        var ay = h.Y + 6;
        if (lane.Collapsed) p.Polygon([cx - 3, ay - 3, cx + 3, ay, cx - 3, ay + 3], new Fill(color));
        else p.Polygon([cx - 3, ay - 2, cx + 3, ay - 2, cx, ay + 3], new Fill(color));
        if (h.H >= 36)
        {
            var gy = h.Y + h.H / 2;
            foreach (var dy in new[] { -6.0, 0, 6 }) p.Circle(cx, gy + dy, 1.2, new Fill(color));
            var ry = h.Y + h.H - 6;
            p.Line(cx - 3, ry - 3, cx + 3, ry + 3, new Stroke(color) { Width = 1.2 });
            p.Line(cx - 3, ry + 3, cx + 3, ry - 3, new Stroke(color) { Width = 1.2 });
        }
        if (m.LaneDrag?.LaneId == lane.LaneId) p.Rect(h.X, h.Y, h.W, h.H, new Fill(th.DropIndicator) { Opacity = 0.25 });
    }

    /// <summary>In-plot series names (the drag handles); the dragged one dims and gets a wavy underline.</summary>
    private static void DrawPlotLabels(TrendChartModel m, IPainter p, LaneLayout lane, TextStyle text, TextStyle muted)
    {
        var st = m.Config.Style; var th = m.Config.Theme; var sw = st.LegendSwatchLength;
        foreach (var l in lane.Labels)
        {
            var s = m.Config.Series.FirstOrDefault(x => x.Id == l.SeriesId);
            if (s is null) continue;
            var r = l.Rect; var dragging = m.Drag?.SeriesIds.Contains(s.Id) ?? false; var color = m.SeriesColor(s);
            p.Rect(r.X, r.Y, r.W, r.H, new Fill(th.LegendBackground) { Opacity = st.LegendOpacity * 0.8 }, null, 3);
            p.Line(r.X + 4, r.Y + r.H / 2, r.X + 4 + sw, r.Y + r.H / 2, new Stroke(color) { Width = 2, Opacity = dragging ? 0.35 : 1 });
            p.Text(m.SeriesName(s), r.X + 4 + sw + 4, r.Y + r.H / 2, (dragging ? muted : text) with { Color = dragging ? th.MutedText : color, Baseline = TextBaseline.Middle });
            if (dragging)
            {
                var pts = new List<double>();
                for (var x = r.X + 4; x <= r.X + r.W - 4; x += 3) { pts.Add(x); pts.Add(r.Y + r.H - 2 + (((x - r.X) / 3) % 2 == 0 ? -1.5 : 1.5)); }
                p.Polyline(pts.ToArray(), new Stroke(th.DropIndicator));
            }
        }
    }

    private static void DrawLaneDragIndicator(TrendChartModel m, IPainter p, TrendLayout layout)
    {
        var d = m.LaneDrag!; var color = m.Config.Theme.DropIndicator;
        var at = d.Index < layout.Lanes.Count ? layout.Lanes[d.Index] : null;
        var x0 = layout.Lanes.Count > 0 && layout.Lanes[0].Header is { } hh ? hh.X : layout.Plot.X; var x1 = layout.Plot.X + layout.Plot.W;
        if (at is not null) p.Rect(x0, at.Rect.Y, x1 - x0, at.Rect.H, null, new Stroke(color) { Width = 3, Opacity = 0.9 });
        else { var last = layout.Lanes.Count > 0 ? layout.Lanes[^1] : null; var y = last is null ? layout.Plot.Y : last.Rect.Y + last.Rect.H; p.Line(x0, y, x1, y, new Stroke(color) { Width = 3, Opacity = 0.9 }); }
    }

    /// <summary>Overview strip: silhouettes of the top lane's series, the frame of the visible window, dimmed outside.</summary>
    private static void DrawNavigator(TrendChartModel m, IPainter p, TrendLayout layout, Ctx c)
    {
        var nav = layout.Navigator!.Value; var th = m.Config.Theme;
        p.Save();
        p.ClipRect(nav.X, nav.Y, nav.W, nav.H);
        foreach (var sil in m.NavigatorSilhouettes(layout))
        {
            var n = sil.Points.Length / 3; var pts = new List<double>();
            for (var i = 0; i < n; i++) { pts.Add(sil.Points[3 * i]); pts.Add(sil.Points[3 * i + 2]); }
            for (var i = n - 1; i >= 0; i--) { pts.Add(sil.Points[3 * i]); pts.Add(sil.Points[3 * i + 1]); }
            if (pts.Count >= 6) p.Polygon(pts.ToArray(), new Fill(sil.Color) { Opacity = 0.35 }, new Stroke(sil.Color) { Opacity = 0.8 });
        }
        if (m.NavigatorFrame(layout) is { } f)
        {
            p.Rect(nav.X, nav.Y, Math.Max(0, f.X - nav.X), nav.H, new Fill(th.Background) { Opacity = 0.45 });
            p.Rect(f.X + f.W, nav.Y, Math.Max(0, nav.X + nav.W - f.X - f.W), nav.H, new Fill(th.Background) { Opacity = 0.45 });
            p.Rect(f.X, f.Y + 1, f.W, f.H - 2, null, new Stroke(th.NavigatorFrame) { Width = 2 });
            if (!m.Config.NavigatorFixedRange) { p.Rect(f.X - 2, f.Y + f.H / 2 - 6, 4, 12, new Fill(th.NavigatorFrame)); p.Rect(f.X + f.W - 2, f.Y + f.H / 2 - 6, 4, 12, new Fill(th.NavigatorFrame)); }
        }
        p.Restore();
        var (t0, t1) = m.NavigatorFullRange(); var ns = m.NavigatorScale(layout); var count = Math.Max(2, (int)Math.Round(nav.W / m.Config.TickSpacing));
        foreach (var v in ns.TickValues(count)) { var x = ns.Apply(v); p.Line(x, nav.Y + nav.H - 4, x, nav.Y + nav.H, c.Axis); }
        p.Text(ns.Format(t0, count), nav.X + 3, nav.Y + 2, c.Muted with { Baseline = TextBaseline.Top });
        p.Text(ns.Format(t1, count), nav.X + nav.W - 3, nav.Y + 2, c.Muted with { Align = TextAlign.Right, Baseline = TextBaseline.Top });
    }
}
