// Mori.SkyScope — Trend chart model: polylines, lane and series commands, hit test, drop rules, drags, axis manipulation, navigator.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Signals;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

public sealed partial class TrendChartModel
{
    /// <summary>Decimated [t, v, …] for the visible window; digital series become steps.</summary>
    public double[] SeriesPolyline(string seriesId)
    {
        var s = Config.Series.FirstOrDefault(x => x.Id == seriesId);
        var b = s is null ? null : Bucket(s);
        if (s is null || b is null) return [];
        var (t0, t1) = Window();
        var pts = b.Polyline(t0 - _quantum, t1);
        if (s.Kind != SeriesKind.Digital || pts.Length < 4) return pts;
        // digital series become true/false steps (high when the value is ≥ 0.5)
        static double Bit(double v) => v >= 0.5 ? 1 : 0;
        var result = new double[pts.Length * 2 - 2];
        result[0] = pts[0]; result[1] = Bit(pts[1]);
        var j = 2;
        for (var i = 2; i < pts.Length; i += 2) { result[j++] = pts[i]; result[j++] = Bit(pts[i - 1]); result[j++] = pts[i]; result[j++] = Bit(pts[i + 1]); }
        return result;
    }

    /// <summary>Value of each visible series at or before <paramref name="time"/>.</summary>
    public Readout ReadoutAt(double time)
    {
        var values = new List<SeriesValue>();
        foreach (var s in VisibleSeries())
        {
            var buf = Store.Get(s.ChannelId)?.Buffer;
            if (buf is null || buf.IsEmpty) continue;
            var seq = buf.IndexAfterTime(time) - 1;
            if (seq < buf.FirstSeq) continue;
            values.Add(new SeriesValue(s.Id, buf.TimeAt(seq), buf.ValueAt(seq)));
        }
        return new Readout(time, values);
    }

    /// <summary>Values at cursor A and B and, when both are set, the per-series differences.</summary>
    public CursorReadouts CursorReadouts()
    {
        var a = CursorA is { } ca ? ReadoutAt(ca) : null;
        var b = CursorB is { } cb ? ReadoutAt(cb) : null;
        CursorDelta? delta = null;
        if (a is not null && b is not null)
            delta = new CursorDelta(b.Time - a.Time, a.Values.Select(va => (va, vb: b.Values.FirstOrDefault(v => v.SeriesId == va.SeriesId)))
                .Where(p => p.vb is not null).Select(p => new SeriesDelta(p.va.SeriesId, p.vb!.Value - p.va.Value)).ToList());
        return new CursorReadouts(a, b, delta);
    }

    /// <summary>What the legend shows: cursor A's value when set, else the latest sample.</summary>
    public double? LegendValue(SeriesConfig s)
    {
        var buf = Store.Get(s.ChannelId)?.Buffer;
        if (buf is null || buf.IsEmpty) return null;
        if (CursorA is { } ca) return ReadoutAt(ca).Values.FirstOrDefault(x => x.SeriesId == s.Id)?.Value;
        return buf.ValueAt(buf.HeadSeq - 1);
    }

    // ---- commands ------------------------------------------------------------
    /// <summary>Freezes the right edge at now.</summary>
    public void Pause() { if (!Paused) { Paused = true; ReviewEnd = Now; } }
    /// <summary>Back to live: the right edge follows now again.</summary>
    public void Resume() { Paused = false; ReviewEnd = null; }
    /// <summary>Sets the visible width in seconds (at least 1 µs).</summary>
    public void SetTimeSpan(double seconds) => TimeSpan = Math.Max(1e-6, seconds);
    /// <summary>Shift the review window; pauses a live chart.</summary>
    public void ScrollBy(double seconds) { Pause(); ReviewEnd = Math.Min(Now, (ReviewEnd ?? Now) + seconds); }
    /// <summary>Review right edge, clamped to now; reaching now goes back to live.</summary>
    public void SetReviewEnd(double t) { if (t >= Now) { Resume(); return; } Paused = true; ReviewEnd = t; }
    /// <summary>Hide or show a series without removing it.</summary>
    public bool SetSeriesVisible(string seriesId, bool visible) { var s = Config.Series.FirstOrDefault(x => x.Id == seriesId); if (s is null) return false; s.Visible = visible; return true; }
    /// <summary>Places cursor <paramref name="which"/> ('a' or anything else for B) at a time; null removes it.</summary>
    public void SetCursor(char which, double? time) { if (which == 'a') CursorA = time; else CursorB = time; }

    /// <summary>Move a series into an existing lane. <paramref name="axisId"/> null = share the lane's default axis (same scale).</summary>
    public bool MoveSeries(string seriesId, string laneId, string? axisId = null)
    {
        var s = Config.Series.FirstOrDefault(x => x.Id == seriesId);
        if (s is null || Lanes().All(l => l.Id != laneId)) return false;
        s.LaneId = laneId; s.AxisId = axisId == $"axis:{laneId}" ? null : axisId;
        PruneEmptyLanes();
        return true;
    }

    /// <summary>Give a series its own new lane at <paramref name="index"/> (parallel scale, no overlap). Returns the lane id.</summary>
    public string? MoveSeriesToNewLane(string seriesId, int index)
    {
        var s = Config.Series.FirstOrDefault(x => x.Id == seriesId);
        if (s is null) return null;
        var id = InsertLane(index);
        s.LaneId = id; s.AxisId = null;
        PruneEmptyLanes();
        return id;
    }

    /// <summary>Series without an explicit lane live in the first lane; before lanes move, pin them there so they do not drift.</summary>
    private void PinLanes() { var first = Lanes()[0].Id; foreach (var s in Config.Series) s.LaneId ??= first; }

    private string InsertLane(int index)
    {
        PinLanes();
        var lanes = Lanes();
        string id;
        do { id = $"lane-{++_laneSeq}"; } while (lanes.Any(l => l.Id == id));
        lanes.Insert(Math.Clamp(index, 0, lanes.Count), new LaneConfig(id));
        return id;
    }

    /// <summary>Lanes without series vanish (the last lane always stays).</summary>
    public void PruneEmptyLanes()
    {
        var lanes = Lanes();
        for (var i = lanes.Count - 1; i >= 0 && lanes.Count > 1; i--)
            if (!Config.Series.Any(x => LaneIdOf(x) == lanes[i].Id)) lanes.RemoveAt(i);
    }

    /// <summary>Series of a lane grouped by axis (first-appearance order): shared-axis series sit together in the legend and the labels.</summary>
    public List<(string AxisId, List<SeriesConfig> Series)> LegendGroups(string laneId)
    {
        var groups = new List<(string AxisId, List<SeriesConfig> Series)>();
        foreach (var s in SeriesIn(laneId))
        {
            var axisId = AxisIdOf(s);
            var i = groups.FindIndex(g => g.AxisId == axisId);
            if (i >= 0) groups[i].Series.Add(s); else groups.Add((axisId, [s]));
        }
        return groups;
    }
    /// <summary>Series of a lane in legend order (axis groups flattened).</summary>
    public List<SeriesConfig> LegendSeries(string laneId) => LegendGroups(laneId).SelectMany(g => g.Series).ToList();

    /// <summary>Legend row under a point (overlay or side legend), grouped by lane in lane order.</summary>
    public string? LegendRowAt(TrendLayout layout, double x, double y)
    {
        if (layout.Legend is not { } lg || !lg.Contains(x, y)) return null;
        var rowH = Config.LegendRowHeight;
        var yy = lg.Y + Config.Style.LegendPadding;
        var lanes = Lanes();
        for (var i = 0; i < lanes.Count; i++)
        {
            if (i > 0) yy += 4;
            foreach (var s in LegendSeries(lanes[i].Id)) { if (y >= yy && y < yy + rowH) return s.Id; yy += rowH; }
        }
        return null;
    }

    // ---- hit testing ---------------------------------------------------------
    /// <summary>What is under a point: legend row, lane header part, in-plot label, axis strip zone, navigator zone, plot lane, time axis.</summary>
    public HitRegion HitTest(TrendLayout layout, double x, double y)
    {
        if (LegendRowAt(layout, x, y) is { } row) return new HitRegion.LegendRow(row);
        foreach (var lane in layout.Lanes)
        {
            if (lane.Header is { } h && h.Contains(x, y))
            {
                var part = y < h.Y + 12 ? HeaderPart.Collapse : y > h.Y + h.H - 12 && h.H >= 36 ? HeaderPart.Remove : HeaderPart.Grip;
                return new HitRegion.Header(lane.LaneId, part);
            }
            foreach (var l in lane.Labels) if (l.Rect.Contains(x, y)) return new HitRegion.Label(l.SeriesId, lane.LaneId);
            foreach (var a in lane.Axes)
                if (a.Rect.Contains(x, y))
                {
                    var f = (y - a.Rect.Y) / Math.Max(1, a.Rect.H);
                    return new HitRegion.Axis(a.AxisId, lane.LaneId, f < 0.2 ? AxisZone.Top : f > 0.8 ? AxisZone.Bottom : AxisZone.Middle);
                }
        }
        if (layout.Navigator is { } nav && nav.Contains(x, y)) return new HitRegion.Navigator(NavigatorZoneAt(layout, x));
        foreach (var lane in layout.Lanes) if (!lane.Collapsed && lane.Rect.Contains(x, y)) return lane.Stack is { } st && st.Contains(x, y) ? new HitRegion.Stack(lane.LaneId) : new HitRegion.Plot(lane.LaneId);
        if (layout.TimeAxis.Contains(x, y)) return new HitRegion.TimeAxis();
        return new HitRegion.None();
    }

    // ---- dragging signals ----------------------------------------------------
    /// <summary>What dropping at (x, y) does — see <see cref="DropTarget"/>.</summary>
    public DropTarget DropTargetAt(TrendLayout layout, double x, double y, bool digital = false)
    {
        // digital signals only ever land in a lane's logic stack; analog signals never join a stack (own axis instead)
        DropTarget OnSeries(string seriesId)
        {
            var s = Config.Series.First(q => q.Id == seriesId); var laneId = LaneIdOf(s);
            if (digital) return new DropTarget.Stack(laneId);
            return s.Kind == SeriesKind.Digital ? new DropTarget.OwnAxis(laneId) : new DropTarget.Join(laneId, AxisIdOf(s));
        }
        switch (HitTest(layout, x, y))
        {
            case HitRegion.LegendRow lr: return OnSeries(lr.SeriesId);
            case HitRegion.Label lb: return OnSeries(lb.SeriesId);
            case HitRegion.Axis ax: return digital ? new DropTarget.Stack(ax.LaneId) : new DropTarget.Join(ax.LaneId, ax.AxisId);
            case HitRegion.Plot pl: return digital ? new DropTarget.Stack(pl.LaneId) : new DropTarget.OwnAxis(pl.LaneId);
            case HitRegion.Stack sk: return digital ? new DropTarget.Stack(sk.LaneId) : new DropTarget.OwnAxis(sk.LaneId);
            case HitRegion.TimeAxis: return new DropTarget.NewLane(layout.Lanes.Count, layout.Lanes.Count > 0 ? layout.Lanes[^1].LaneId : null);
        }
        var p = layout.Plot;
        if (x < p.X || x > p.X + p.W || layout.Lanes.Count == 0) return new DropTarget.None();
        if (y < layout.Lanes[0].Rect.Y) return new DropTarget.NewLane(0, null);
        if (y > layout.TimeAxis.Y + layout.TimeAxis.H) return new DropTarget.None();
        for (var i = 0; i < layout.Lanes.Count; i++)
        {
            var lane = layout.Lanes[i]; var r = lane.Rect;
            if (lane.Collapsed && y >= r.Y && y < r.Y + r.H) return new DropTarget.None();
            var next = i + 1 < layout.Lanes.Count ? layout.Lanes[i + 1] : null;
            if (y >= r.Y + r.H && (next is null || y < next.Rect.Y)) return new DropTarget.NewLane(i + 1, lane.LaneId);
        }
        return new DropTarget.None();
    }

    /// <summary>Applies a drop target to one series; returns true when the layout changed.</summary>
    public bool ApplyDrop(string seriesId, DropTarget target) => ApplyGroupDrop([seriesId], target, false);

    /// <summary>Join puts all on the axis; OwnAxis gives the group one new axis in the lane; NewLane makes one lane for the group when <paramref name="group"/>, else one lane per series.</summary>
    public bool ApplyGroupDrop(IReadOnlyList<string> seriesIds, DropTarget target, bool group)
    {
        var series = seriesIds.Select(id => Config.Series.FirstOrDefault(x => x.Id == id)).Where(x => x is not null).Select(x => x!).ToList();
        if (series.Count == 0) return false;
        switch (target)
        {
            case DropTarget.Join or DropTarget.OwnAxis or DropTarget.Stack:
            {
                // digital members always go to the lane's logic stack; analog members to the axis (join) or a new own axis
                var laneId = target switch { DropTarget.Join j => j.LaneId, DropTarget.OwnAxis o => o.LaneId, DropTarget.Stack k => k.LaneId, _ => "" };
                var digital = series.Where(s => s.Kind == SeriesKind.Digital).ToList(); var analog = series.Where(s => s.Kind != SeriesKind.Digital).ToList();
                var axisId = target is DropTarget.Join jn && !jn.AxisId.StartsWith("stack:") ? jn.AxisId : analog.Count > 0 ? $"axis:{analog[0].Id}" : null;
                var changed = false;
                foreach (var s in digital) { if (LaneIdOf(s) == laneId) continue; changed = MoveSeries(s.Id, laneId) || changed; }
                if (axisId is not null) foreach (var s in analog) { if (LaneIdOf(s) == laneId && AxisIdOf(s) == axisId) continue; changed = MoveSeries(s.Id, laneId, axisId) || changed; }
                return changed;
            }
            case DropTarget.NewLane n:
            {
                if (series.Count == 1)
                {
                    var s = series[0];
                    var from = Lanes().FindIndex(l => l.Id == LaneIdOf(s));
                    var alone = SeriesIn(LaneIdOf(s)).Count() == 1;
                    if (alone && (n.Index == from || n.Index == from + 1)) return false;
                    return MoveSeriesToNewLane(s.Id, n.Index) is not null;
                }
                if (group)
                {
                    var id = InsertLane(n.Index);
                    foreach (var s in series) { s.LaneId = id; s.AxisId = null; }
                    PruneEmptyLanes();
                    return true;
                }
                var index = n.Index;
                foreach (var s in series) { var before = Lanes().Count; MoveSeriesToNewLane(s.Id, index); if (Lanes().Count >= before) index++; }
                return true;
            }
            default: return false;
        }
    }

    /// <summary>Picks up one series at pixel (x, y).</summary>
    public void BeginDrag(string seriesId, double x, double y) => BeginDrag([seriesId], x, y);
    /// <summary>Pick up series (ctrl/shift group); <paramref name="channelIds"/> adds series for channels not in the chart yet (signal tree).</summary>
    public void BeginDrag(IReadOnlyList<string> seriesIds, double x, double y, bool group = false, IReadOnlyList<int>? channelIds = null)
    {
        channelIds ??= [];
        Drag = new DragState(seriesIds.Count > 0 ? seriesIds[0] : channelIds.Count > 0 ? $"ch:{channelIds[0]}" : "", seriesIds.ToList(), channelIds.ToList(), x, y, new DropTarget.None(), group);
    }
    /// <summary>A drag carrying only digital signals (series or tree channels) targets logic stacks.</summary>
    private bool DragIsDigital(DragState d)
    {
        var series = d.SeriesIds.Select(id => Config.Series.FirstOrDefault(s => s.Id == id)).Where(s => s is not null).Select(s => s!).ToList();
        var kinds = d.ChannelIds.Select(ch => Store.Get(ch)?.Info.Kind).ToList();
        return series.Count + kinds.Count > 0 && series.All(s => s.Kind == SeriesKind.Digital) && kinds.All(k => k == ChannelKind.Digital);
    }
    /// <summary>Moves the drag to (x, y) and recomputes its drop target.</summary>
    public void UpdateDrag(TrendLayout layout, double x, double y) { if (Drag is { } d) Drag = d with { X = x, Y = y, Target = DropTargetAt(layout, x, y, DragIsDigital(d)) }; }
    /// <summary>Drop: applies the target (if any) and clears the drag. Returns true when the layout changed.</summary>
    public bool EndDrag(TrendLayout layout, double x, double y)
    {
        if (Drag is not { } d) return false;
        Drag = null;
        var target = DropTargetAt(layout, x, y, DragIsDigital(d));
        if (target is DropTarget.None) return false;
        var ids = d.SeriesIds.ToList();
        foreach (var ch in d.ChannelIds) if (AddSeriesForChannel(ch) is { } s && !ids.Contains(s.Id)) ids.Add(s.Id);
        if (ids.Count == 0) return false;
        var changed = ApplyGroupDrop(ids, target, d.Group || d.ChannelIds.Count > 1);
        return changed || d.ChannelIds.Count > 0;
    }
    /// <summary>Abandons the drag without applying anything.</summary>
    public void CancelDrag() => Drag = null;

    /// <summary>A series for a channel of the store (id <c>ch:&lt;channelId&gt;</c>), created in the first lane when missing.</summary>
    public SeriesConfig? AddSeriesForChannel(int channelId)
    {
        if (Config.Series.FirstOrDefault(s => s.ChannelId == channelId) is { } existing) return existing;
        var info = Store.Get(channelId)?.Info;
        if (info is null) return null;
        var s = new SeriesConfig($"ch:{channelId}", channelId) { LaneId = Lanes()[0].Id };
        if (info.Kind == ChannelKind.Digital) s.Kind = SeriesKind.Digital;
        Config.Series.Add(s);
        return s;
    }

    // ---- lanes ---------------------------------------------------------------
    /// <summary>Move a lane to position <paramref name="index"/> (0 = top).</summary>
    public bool MoveLane(string laneId, int index)
    {
        PinLanes();
        var lanes = Lanes();
        var i = lanes.FindIndex(l => l.Id == laneId);
        if (i < 0) return false;
        var lane = lanes[i]; lanes.RemoveAt(i);
        var at = Math.Clamp(index > i ? index - 1 : index, 0, lanes.Count);
        lanes.Insert(at, lane);
        return at != i;
    }
    /// <summary>Folds or unfolds a lane; returns false when the lane is unknown or already in that state.</summary>
    public bool SetLaneCollapsed(string laneId, bool collapsed)
    {
        var lane = Lanes().FirstOrDefault(l => l.Id == laneId);
        if (lane is null || lane.Collapsed == collapsed) return false;
        lane.Collapsed = collapsed;
        return true;
    }
    /// <summary>Remove a lane and the series in it (the last lane stays, emptied).</summary>
    public bool RemoveLane(string laneId)
    {
        PinLanes();
        var lanes = Lanes();
        var i = lanes.FindIndex(l => l.Id == laneId);
        if (i < 0) return false;
        Config.Series.RemoveAll(s => LaneIdOf(s) == laneId);
        if (lanes.Count > 1) lanes.RemoveAt(i);
        return true;
    }
    /// <summary>Insertion slot for a lane dragged to <paramref name="y"/>: above the lane whose middle is below the pointer.</summary>
    public static int LaneInsertIndexAt(TrendLayout layout, double y)
    {
        for (var i = 0; i < layout.Lanes.Count; i++) if (y < layout.Lanes[i].Rect.Y + layout.Lanes[i].Rect.H / 2) return i;
        return layout.Lanes.Count;
    }
    /// <summary>Starts reordering a lane by its header.</summary>
    public void BeginLaneDrag(string laneId, double x, double y) { var index = Lanes().FindIndex(l => l.Id == laneId); if (index >= 0) LaneDrag = new LaneDragState(laneId, x, y, index); }
    /// <summary>Moves the lane drag and updates its insertion index.</summary>
    public void UpdateLaneDrag(TrendLayout layout, double x, double y) { if (LaneDrag is { } d) LaneDrag = d with { X = x, Y = y, Index = LaneInsertIndexAt(layout, y) }; }
    /// <summary>Drops the lane at the pointer; returns true when its position changed.</summary>
    public bool EndLaneDrag(TrendLayout layout, double x, double y)
    {
        if (LaneDrag is not { } d) return false;
        LaneDrag = null;
        return MoveLane(d.LaneId, LaneInsertIndexAt(layout, y));
    }
    /// <summary>Abandons the lane drag.</summary>
    public void CancelLaneDrag() => LaneDrag = null;

    // ---- Y axes --------------------------------------------------------------
    private AxisConfig FixedAxis(string axisId)
    {
        var cfg = Config.Axes.FirstOrDefault(a => a.Id == axisId);
        if (cfg is null) { cfg = new AxisConfig(axisId); Config.Axes.Add(cfg); }
        if (cfg.Min is null || cfg.Max is null) { var (lo, hi) = YDomain(axisId); cfg.Min = lo; cfg.Max = hi; }
        return cfg;
    }
    /// <summary>Pick up an axis: the middle shifts the scale, the ends stretch it about the other end.</summary>
    public void BeginAxisDrag(TrendLayout layout, string axisId, string laneId, AxisZone zone, double y)
    {
        var cfg = FixedAxis(axisId);
        AxisDrag = new AxisDragState(axisId, laneId, zone, y, cfg.Min!.Value, cfg.Max!.Value);
    }
    /// <summary>Applies the pointer's y to the dragged axis; returns false when nothing is being dragged.</summary>
    public bool UpdateAxisDrag(TrendLayout layout, double y)
    {
        if (AxisDrag is not { } d) return false;
        var lane = layout.Lanes.FirstOrDefault(l => l.LaneId == d.LaneId);
        if (lane is null || lane.Rect.H <= 0) return false;
        var cfg = FixedAxis(d.AxisId); var h = lane.Rect.H; var span0 = d.Max0 - d.Min0; if (span0 == 0) span0 = 1;
        var vpp = span0 / h;
        if (d.Zone == AxisZone.Middle) { var dv = (y - d.Y0) * vpp; cfg.Min = d.Min0 + dv; cfg.Max = d.Max0 + dv; return true; }
        var v0 = d.Max0 - (d.Y0 - lane.Rect.Y) * vpp;
        var f = Math.Min(0.95, Math.Max(0.05, (y - lane.Rect.Y) / h));
        if (d.Zone == AxisZone.Top) { var max = (v0 - f * d.Min0) / (1 - f); if (max > d.Min0) { cfg.Min = d.Min0; cfg.Max = max; } }
        else { var min = d.Max0 + (v0 - d.Max0) / f; if (min < d.Max0) { cfg.Max = d.Max0; cfg.Min = min; } }
        return true;
    }
    /// <summary>Ends the axis drag; the axis keeps its fixed range.</summary>
    public void EndAxisDrag() => AxisDrag = null;
    /// <summary>Wheel over an axis: scale the range by 1/factor about the value under the pointer.</summary>
    public bool AxisZoomAt(TrendLayout layout, string axisId, string laneId, double y, double factor)
    {
        var lane = layout.Lanes.FirstOrDefault(l => l.LaneId == laneId);
        if (lane is null) return false;
        var cfg = FixedAxis(axisId); var v = YScale(axisId, lane).Invert(y);
        cfg.Min = v + (cfg.Min!.Value - v) / factor; cfg.Max = v + (cfg.Max!.Value - v) / factor;
        return true;
    }
    /// <summary>Back to autoscale.</summary>
    public bool AxisAutoscale(string axisId)
    {
        var cfg = Config.Axes.FirstOrDefault(a => a.Id == axisId);
        if (cfg is null || (cfg.Min is null && cfg.Max is null)) return false;
        cfg.Min = null; cfg.Max = null;
        return true;
    }

    // ---- navigator -----------------------------------------------------------
    /// <summary>Full range the navigator shows: an explicit range (recordings) or the retained history up to now.</summary>
    public (double T0, double T1) NavigatorFullRange()
    {
        if (NavigatorRange is { } r) return r;
        var earliest = double.PositiveInfinity;
        foreach (var s in VisibleSeries()) { var b = Store.Get(s.ChannelId)?.Buffer; if (b is { IsEmpty: false }) earliest = Math.Min(earliest, b.TimeAt(b.FirstSeq)); }
        var t0 = Math.Max(double.IsFinite(earliest) ? earliest : Now - TimeSpan, Now - Store.RetentionSeconds);
        return (Math.Min(t0, Now - TimeSpan), Now);
    }
    /// <summary>Time-to-pixel scale across the navigator strip; the layout must have a navigator.</summary>
    public TimeScale NavigatorScale(TrendLayout layout)
    {
        var (t0, t1) = NavigatorFullRange(); var nav = layout.Navigator!.Value;
        return new TimeScale(t0, t1, nav.X, nav.X + nav.W, Config.TimeFormat, Config.TimeFormat == TimeFormat.Relative ? t1 : 0);
    }
    /// <summary>The visible window inside the navigator.</summary>
    public Rect? NavigatorFrame(TrendLayout layout)
    {
        if (layout.Navigator is not { } nav) return null;
        var ns = NavigatorScale(layout); var (t0, t1) = Window();
        double x0 = Math.Max(nav.X, ns.Apply(t0)), x1 = Math.Min(nav.X + nav.W, ns.Apply(t1));
        return new Rect(x0, nav.Y, Math.Max(2, x1 - x0), nav.H);
    }
    /// <summary>Part of the navigator frame under pixel x; edges are 4 px wide and disabled with a fixed range.</summary>
    public NavigatorZone NavigatorZoneAt(TrendLayout layout, double x)
    {
        if (NavigatorFrame(layout) is not { } f) return NavigatorZone.Outside;
        const double tol = 4;
        if (!Config.NavigatorFixedRange) { if (Math.Abs(x - f.X) <= tol) return NavigatorZone.LeftEdge; if (Math.Abs(x - (f.X + f.W)) <= tol) return NavigatorZone.RightEdge; }
        return x >= f.X && x <= f.X + f.W ? NavigatorZone.Inside : NavigatorZone.Outside;
    }
    /// <summary>Decimated outline of one series for the navigator: flat [x, yMin, yMax, …] triples in pixels.</summary>
    public sealed record NavigatorSilhouette(string SeriesId, string Color, double[] Points);
    /// <summary>Decimated [x, yMin, yMax, …] per series of the topmost open lane, normalised into the navigator rect.</summary>
    public List<NavigatorSilhouette> NavigatorSilhouettes(TrendLayout layout)
    {
        var result = new List<NavigatorSilhouette>();
        if (layout.Navigator is not { } nav) return result;
        var lane = layout.Lanes.FirstOrDefault(l => !l.Collapsed);
        if (lane is null) return result;
        var (rt0, rt1) = NavigatorFullRange(); var ns = NavigatorScale(layout);
        var q = Math.Max(1e-9, (rt1 - rt0) / Math.Max(1, nav.W));
        if (q != _navQuantum) { _navQuantum = q; foreach (var b in _navBuckets.Values) b.SetQuantum(q); }
        foreach (var s in SeriesIn(lane.LaneId))
        {
            var ch = Store.Get(s.ChannelId);
            if (ch is null) continue;
            if (!_navBuckets.TryGetValue(s.Id, out var b) || b.Buffer != ch.Buffer) { b = new BucketSeries(ch.Buffer, q); _navBuckets[s.Id] = b; }
            b.Update();
            var buckets = b.BucketsInRange(rt0, rt1);
            double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
            foreach (var k in buckets) { if (k.Min < lo) lo = k.Min; if (k.Max > hi) hi = k.Max; }
            if (!double.IsFinite(lo) || !double.IsFinite(hi)) continue;
            var span = hi - lo; if (span == 0) span = 1; const double pad = 3;
            var pts = new double[buckets.Count * 3]; var i = 0;
            foreach (var k in buckets) { pts[i++] = ns.Apply(k.T); pts[i++] = nav.Y + nav.H - pad - (k.Min - lo) / span * (nav.H - 2 * pad); pts[i++] = nav.Y + nav.H - pad - (k.Max - lo) / span * (nav.H - 2 * pad); }
            result.Add(new NavigatorSilhouette(s.Id, SeriesColor(s), pts));
        }
        return result;
    }
    /// <summary>Starts moving or resizing the frame; a press outside the frame centres it first and then moves it.</summary>
    public void BeginNavigatorDrag(TrendLayout layout, double x)
    {
        var zone = NavigatorZoneAt(layout, x);
        var (t0, t1) = Window();
        if (zone == NavigatorZone.Outside) { NavigatorCenterAt(layout, x); var w = Window(); NavDrag = new NavigatorDragState(NavigatorZone.Inside, x, w.T0, w.T1); return; }
        NavDrag = new NavigatorDragState(zone, x, t0, t1);
    }
    /// <summary>Moves or resizes the frame to pixel x: inside shifts the window, an edge changes the span; returns false when not dragging.</summary>
    public bool UpdateNavigatorDrag(TrendLayout layout, double x)
    {
        if (NavDrag is not { } d || layout.Navigator is null) return false;
        var ns = NavigatorScale(layout); var full = NavigatorFullRange();
        var dt = ns.Invert(x) - ns.Invert(d.X0);
        if (d.Zone == NavigatorZone.Inside) { var span = d.T1 - d.T0; SetReviewEnd(Math.Max(full.T0 + span, d.T1 + dt)); return true; }
        if (d.Zone == NavigatorZone.LeftEdge) { var t0 = Math.Min(d.T1 - 1e-6, Math.Max(full.T0, d.T0 + dt)); TimeSpan = d.T1 - t0; if (Paused) ReviewEnd = d.T1; return true; }
        var t1 = Math.Max(d.T0 + 1e-6, Math.Min(Now, d.T1 + dt)); TimeSpan = t1 - d.T0; SetReviewEnd(t1); return true;
    }
    /// <summary>Ends the navigator drag.</summary>
    public void EndNavigatorDrag() => NavDrag = null;
    /// <summary>Click in the navigator: the frame jumps so its centre sits under the pointer.</summary>
    public void NavigatorCenterAt(TrendLayout layout, double x)
    {
        if (layout.Navigator is null) return;
        var t = NavigatorScale(layout).Invert(x); var full = NavigatorFullRange();
        SetReviewEnd(Math.Min(Now, Math.Max(full.T0 + TimeSpan, t + TimeSpan / 2)));
    }
    /// <summary>Arrow keys step the frame by a tenth of its width.</summary>
    public bool NavigatorKey(string key)
    {
        if (key == "ArrowLeft") { ScrollBy(-TimeSpan / 10); return true; }
        if (key == "ArrowRight") { SetReviewEnd((ReviewEnd ?? Now) + TimeSpan / 10); return true; }
        return false;
    }

    /// <summary>Back to live with the configured span, no cursors and every axis autoscaled.</summary>
    public void Reset()
    {
        Resume(); TimeSpan = Config.TimeSpan; CursorA = CursorB = null;
        foreach (var a in Config.Axes) { a.Min = null; a.Max = null; }
    }

    /// <summary>Lane whose rectangle contains the point, or null.</summary>
    public LaneLayout? LaneAt(TrendLayout layout, double x, double y) => layout.Lanes.FirstOrDefault(l => l.Rect.Contains(x, y));

    /// <summary>Interpret a gesture effect; returns true when it changed the chart.</summary>
    public bool ApplyEffect(Effect e, TrendLayout layout)
    {
        var ts = TimeScale(layout);
        var secPerPx = TimeSpan / Math.Max(1, layout.Plot.W);
        switch (e)
        {
            case Effect.Pan p: ScrollBy(-p.Dx * secPerPx); return true;
            case Effect.Zoom z:
            {
                var (_, t1) = Window();
                var anchor = Paused ? ts.Invert(z.X) : t1;
                TimeSpan = Math.Max(1e-6, TimeSpan / z.Factor);
                if (Paused) ReviewEnd = Math.Min(Now, anchor + (t1 - anchor) / z.Factor);
                return true;
            }
            case Effect.BoxZoom bz:
            {
                var r = bz.Rect;
                var lane = LaneAt(layout, r.X + r.W / 2, r.Y + r.H / 2);
                double tA = ts.Invert(r.X), tB = ts.Invert(r.X + r.W);
                Pause(); TimeSpan = Math.Max(1e-6, tB - tA); ReviewEnd = tB;
                if (lane is not null)
                    foreach (var ax in AxesIn(lane.LaneId))
                    {
                        var ys = YScale(ax.Id, lane);
                        var cfg = Config.Axes.FirstOrDefault(a => a.Id == ax.Id);
                        if (cfg is null) { cfg = new AxisConfig(ax.Id); Config.Axes.Add(cfg); }
                        cfg.Min = ys.Invert(r.Y + r.H); cfg.Max = ys.Invert(r.Y);
                    }
                return true;
            }
            case Effect.Hover h: HoverTime = layout.Plot.Contains(h.X, h.Y) ? ts.Invert(h.X) : null; return true;
            case Effect.Click c:
                if (!layout.Plot.Contains(c.X, c.Y)) return false;
                SetCursor(c.Modifiers.Shift ? 'b' : 'a', ts.Invert(c.X)); return true;
            case Effect.Reset: Reset(); return true;
            default: return false;
        }
    }
}
