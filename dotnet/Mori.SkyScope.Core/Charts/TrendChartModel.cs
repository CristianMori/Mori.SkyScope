// Mori.SkyScope — Headless state of the trend chart over a signal store: configuration views, time window, layout inputs, decimation and autoscale.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Signals;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Sample of one series at a readout: series id, sample time in seconds and value.</summary>
public sealed record SeriesValue(string SeriesId, double Time, double Value);
/// <summary>Values of every visible series at or before <paramref name="Time"/>; series without data are left out.</summary>
public sealed record Readout(double Time, IReadOnlyList<SeriesValue> Values);
/// <summary>Change of one series' value between cursor A and cursor B.</summary>
public sealed record SeriesDelta(string SeriesId, double Delta);
/// <summary>Time difference <paramref name="Dt"/> (B − A, seconds) and the per-series value differences between the cursors.</summary>
public sealed record CursorDelta(double Dt, IReadOnlyList<SeriesDelta> Values);
/// <summary>Readouts at cursor A and B (null when a cursor is unset) and their delta (null unless both are set).</summary>
public sealed record CursorReadouts(Readout? A, Readout? B, CursorDelta? Delta);

/// <summary>
/// Headless state of a strip/trend chart over a <see cref="SignalStore"/>: time window (live, paused, review),
/// per-axis autoscale, cursors, hover, and how gestures change all of that. Drawing lives in <see cref="TrendChartRenderer"/>.
/// Mirrors <c>charts/trend-model.ts</c>; pinned by <c>spec/fixtures/trend-chart.json</c>.
/// </summary>
/// <param name="store">Store the series read from.</param>
/// <param name="config">Initial configuration; a default one when null.</param>
public sealed partial class TrendChartModel(SignalStore store, TrendChartConfig? config = null)
{
    private readonly Dictionary<string, BucketSeries> _buckets = [];
    private readonly Dictionary<string, (double Lo, double Hi)> _yDomains = [];
    private double _quantum;

    /// <summary>Store the series read from.</summary>
    public SignalStore Store { get; } = store;
    /// <summary>Live configuration; commands mutate it in place.</summary>
    public TrendChartConfig Config { get; } = config ?? new TrendChartConfig();
    /// <summary>Chart time (seconds); the live right edge.</summary>
    public double Now { get; set; }
    /// <summary>True while reviewing history; the right edge then stays at <see cref="ReviewEnd"/>.</summary>
    public bool Paused { get; private set; }
    /// <summary>Right edge while paused/reviewing.</summary>
    public double? ReviewEnd { get; private set; }
    /// <summary>Visible width in seconds; starts from the configuration and changes with zoom and navigator drags.</summary>
    public double TimeSpan { get; set; } = (config ?? new TrendChartConfig()).TimeSpan;
    /// <summary>Time of cursor A in seconds, or null when unset.</summary>
    public double? CursorA { get; set; }
    /// <summary>Time of cursor B in seconds, or null when unset.</summary>
    public double? CursorB { get; set; }
    /// <summary>Time under the pointer in seconds, or null when outside the plot.</summary>
    public double? HoverTime { get; set; }
    /// <summary>Series (or channels from a signal tree) being dragged onto a lane, axis or gap.</summary>
    public DragState? Drag { get; private set; }
    /// <summary>A lane header being dragged to reorder lanes.</summary>
    public LaneDragState? LaneDrag { get; private set; }
    /// <summary>A Y-axis being shifted or stretched by its scale ends.</summary>
    public AxisDragState? AxisDrag { get; private set; }
    /// <summary>The navigator frame being moved or resized.</summary>
    public NavigatorDragState? NavDrag { get; private set; }
    /// <summary>Full range the navigator shows; null = retained history up to now (playback hosts set the recording's span).</summary>
    public (double T0, double T1)? NavigatorRange { get; set; }
    private readonly Dictionary<string, BucketSeries> _navBuckets = [];
    private double _navQuantum;
    private int _laneSeq;

    // ---- configuration views -------------------------------------------------
    /// <summary>Lanes of the configuration; creates the default lane <c>main</c> when there is none.</summary>
    public List<LaneConfig> Lanes() { if (Config.Lanes.Count == 0) Config.Lanes.Add(new LaneConfig("main")); return Config.Lanes; }
    /// <summary>Lane a series lives in: its own, else the first lane.</summary>
    public string LaneIdOf(SeriesConfig s) => s.LaneId ?? Lanes()[0].Id;
    /// <summary>Digital series have no axis: they report their lane's logic stack (<c>stack:&lt;laneId&gt;</c>).</summary>
    public string AxisIdOf(SeriesConfig s) => s.Kind == SeriesKind.Digital ? $"stack:{LaneIdOf(s)}" : s.AxisId ?? $"axis:{LaneIdOf(s)}";
    /// <summary>Series not hidden, in configuration order.</summary>
    public IEnumerable<SeriesConfig> VisibleSeries() => Config.Series.Where(s => s.Visible);
    /// <summary>Visible series of a lane.</summary>
    public IEnumerable<SeriesConfig> SeriesIn(string laneId) => VisibleSeries().Where(s => LaneIdOf(s) == laneId);
    /// <summary>The explicit colour, or the palette entry for the series position.</summary>
    public string SeriesColor(SeriesConfig s) => s.Color ?? TrendChartConfig.SeriesPalette[Math.Max(0, Config.Series.IndexOf(s)) % TrendChartConfig.SeriesPalette.Length];
    /// <summary>The explicit name, else the channel name, else the series id.</summary>
    public string SeriesName(SeriesConfig s) => s.Name ?? Store.Get(s.ChannelId)?.Info.Name ?? s.Id;

    /// <summary>Axis configuration by id; an autoscaled left-side default when none is configured.</summary>
    public AxisConfig Axis(string axisId)
    {
        return Config.Axes.FirstOrDefault(a => a.Id == axisId) ?? new AxisConfig(axisId);
    }

    /// <summary>Axes used by the analog series of a lane, left-side ones first, each in first-appearance order.</summary>
    public IReadOnlyList<AxisConfig> AxesIn(string laneId)
    {
        var ids = new List<string>();
        foreach (var s in SeriesIn(laneId)) { if (s.Kind == SeriesKind.Digital) continue; var id = AxisIdOf(s); if (!ids.Contains(id)) ids.Add(id); }
        var axes = ids.Select(Axis).ToList();
        return [.. axes.Where(a => a.Side == AxisSide.Left), .. axes.Where(a => a.Side == AxisSide.Right)];
    }

    /// <summary>Digital series of a lane: always the logic-analyzer stack at the bottom, top to bottom in series order.</summary>
    public List<SeriesConfig> DigitalTracks(string laneId) => SeriesIn(laneId).Where(s => s.Kind == SeriesKind.Digital).ToList();

    /// <summary>Band for track <paramref name="index"/> of <paramref name="count"/> in the lane's stack.</summary>
    public static Rect TrackRect(LaneLayout lane, int index, int count)
    {
        var st = lane.Stack ?? lane.Rect;
        var h = st.H / Math.Max(1, count);
        return new Rect(st.X, st.Y + index * h, st.W, h);
    }

    /// <summary>The scale a series draws with: its own track when stacked, otherwise its axis. Low/high sit 15 % inside the band.</summary>
    public LinearScale SeriesScale(SeriesConfig s, LaneLayout lane)
    {
        var tracks = DigitalTracks(lane.LaneId);
        var i = tracks.IndexOf(s);
        if (i < 0) return YScale(AxisIdOf(s), lane);
        var r = TrackRect(lane, i, tracks.Count);
        var pad = r.H * Config.Style.DigitalTrackPadding;
        return new LinearScale(0, 1, r.Y + r.H - pad, r.Y + pad);
    }

    /// <summary>Unit shown next to a series value: the axis unit, else the channel unit.</summary>
    public string? UnitOf(SeriesConfig s) => Axis(AxisIdOf(s)).Unit ?? Store.Get(s.ChannelId)?.Info.Unit;

    // ---- time window ---------------------------------------------------------
    /// <summary>Visible time window in seconds: ending at now, or at the review end while paused.</summary>
    public (double T0, double T1) Window()
    {
        var t1 = Paused && ReviewEnd is { } r ? r : Now;
        return (t1 - TimeSpan, t1);
    }

    /// <summary>Pixel layout for a canvas size, derived from the lanes, their axes and labels, and the legend rows.</summary>
    public TrendLayout Layout(double width, double height) => TrendLayoutEngine.Layout(Config, width, height, Lanes().Select(l =>
    {
        var axes = AxesIn(l.Id);
        return new LaneLayoutInput(l.Id, l.Weight, axes.Where(a => a.Side == AxisSide.Left).Select(a => a.Id).ToList(), axes.Where(a => a.Side == AxisSide.Right).Select(a => a.Id).ToList()) { Collapsed = l.Collapsed, Tracks = DigitalTracks(l.Id).Count, Analog = SeriesIn(l.Id).Any(s => s.Kind != SeriesKind.Digital), Labels = LegendSeries(l.Id).Select(s => (s.Id, SeriesName(s), s.Kind == SeriesKind.Digital)).ToList() };
    }).ToList(), VisibleSeries().Count(), CursorA is not null && CursorB is not null ? VisibleSeries().Count(s => s.Kind != SeriesKind.Digital) : 0);

    /// <summary>Time-to-pixel scale across the plot; in relative mode labels count back from the right edge.</summary>
    public TimeScale TimeScale(TrendLayout layout)
    {
        var (t0, t1) = Window();
        return new TimeScale(t0, t1, layout.Plot.X, layout.Plot.X + layout.Plot.W, Config.TimeFormat, Config.TimeFormat == TimeFormat.Relative ? t1 : 0);
    }

    /// <summary>Value range of an axis as computed by the last <see cref="Update"/>; [0, 1] before the first.</summary>
    public (double Lo, double Hi) YDomain(string axisId) => _yDomains.TryGetValue(axisId, out var d) ? d : (0, 1);
    /// <summary>Value-to-pixel scale of an axis over a lane's analog area (top = maximum).</summary>
    public LinearScale YScale(string axisId, LaneLayout lane) { var (lo, hi) = YDomain(axisId); return new LinearScale(lo, hi, lane.Analog.Y + lane.Analog.H, lane.Analog.Y); }

    // ---- data ----------------------------------------------------------------
    /// <summary>Refresh decimation for the current window/size and recompute autoscaled axes. Call once per frame.</summary>
    public void Update(TrendLayout layout)
    {
        var q = Math.Max(1e-9, TimeSpan / Math.Max(1, layout.Plot.W));
        if (q != _quantum) { _quantum = q; foreach (var b in _buckets.Values) b.SetQuantum(q); }
        var (t0, t1) = Window();
        var ranges = new Dictionary<string, (double Lo, double Hi)>();
        foreach (var s in VisibleSeries())
        {
            var b = Bucket(s);
            if (b is null) continue;
            b.Update();
            if (s.Kind == SeriesKind.Digital) continue;
            var axisId = AxisIdOf(s);
            var ax = Axis(axisId);
            if (ax.Min is { } fixedMin && ax.Max is { } fixedMax) { ranges[axisId] = (fixedMin, fixedMax); continue; }
            var (lo, hi) = ranges.TryGetValue(axisId, out var r) ? r : (double.PositiveInfinity, double.NegativeInfinity);
            foreach (var k in b.BucketsInRange(t0, t1)) { if (k.Min < lo) lo = k.Min; if (k.Max > hi) hi = k.Max; }
            ranges[axisId] = (lo, hi);
        }
        _yDomains.Clear();
        foreach (var (axisId, (lo, hi)) in ranges)
        {
            var ax = Axis(axisId);
            if (ax.Min is { } fixedMin && ax.Max is { } fixedMax) { _yDomains[axisId] = (fixedMin, fixedMax); continue; }
            if (!double.IsFinite(lo) || !double.IsFinite(hi)) { _yDomains[axisId] = (ax.Min ?? 0, ax.Max ?? 1); continue; }
            var pad = hi == lo ? (Math.Abs(hi) == 0 ? 1 : Math.Abs(hi)) * 0.5 : (hi - lo) * 0.05;
            _yDomains[axisId] = (ax.Min ?? lo - pad, ax.Max ?? hi + pad);
        }
    }

    private BucketSeries? Bucket(SeriesConfig s)
    {
        var ch = Store.Get(s.ChannelId);
        if (ch is null) return null;
        if (!_buckets.TryGetValue(s.Id, out var b) || b.Buffer != ch.Buffer) { b = new BucketSeries(ch.Buffer, _quantum == 0 ? 1 : _quantum); _buckets[s.Id] = b; }
        return b;
    }
}
