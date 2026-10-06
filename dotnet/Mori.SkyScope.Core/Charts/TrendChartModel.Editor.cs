// Mori.SkyScope — Editor commands of the trend chart model: add, update and remove lanes, axes, series, thresholds and markers, and the row list an editor panel renders. Mirrors the editor section of trend-model.ts.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Charts;

/// <summary>An optional patch value: unset leaves the field alone, <see cref="Clear"/> removes it, a value sets it. Implicitly converts from <typeparamref name="T"/>.</summary>
public readonly record struct Opt<T>(bool IsSet, T? Value)
{
    /// <summary>Removes the field (sets it back to its default).</summary>
    public static Opt<T> Clear => new(true, default);
    /// <summary>Sets the field to <paramref name="value"/>.</summary>
    public static implicit operator Opt<T>(T? value) => new(true, value);
    /// <summary>True when the patch sets a non-null value.</summary>
    public bool HasValue => IsSet && Value is not null;
}

/// <summary>Fields of a lane an editor may change; unset fields are left alone.</summary>
public sealed record LanePatch { public Opt<string> Label { get; init; } public Opt<double?> Weight { get; init; } public Opt<bool> Collapsed { get; init; } public Opt<bool> Keep { get; init; } }
/// <summary>Fields of an axis an editor may change; unset fields are left alone, cleared bounds mean autoscale.</summary>
public sealed record AxisPatch { public Opt<string> Label { get; init; } public Opt<string> Unit { get; init; } public Opt<double?> Min { get; init; } public Opt<double?> Max { get; init; } public Opt<AxisSide> Side { get; init; } public Opt<string> Color { get; init; } }
/// <summary>Fields of a series an editor may change; a cleared axis means the lane's default axis, a cleared lane the first lane.</summary>
public sealed record SeriesPatch { public Opt<string> Name { get; init; } public Opt<string> Color { get; init; } public Opt<double?> Width { get; init; } public Opt<bool> Visible { get; init; } public Opt<string> LaneId { get; init; } public Opt<string> AxisId { get; init; } public Opt<SeriesKind> Kind { get; init; } }
/// <summary>Fields of a threshold an editor may change; a cleared <c>To</c> turns a band into a line.</summary>
public sealed record ThresholdPatch { public Opt<string> AxisId { get; init; } public Opt<double?> From { get; init; } public Opt<double?> To { get; init; } public Opt<string> Color { get; init; } public Opt<string> Label { get; init; } }
/// <summary>Fields of a marker an editor may change.</summary>
public sealed record MarkerPatch { public Opt<double?> Time { get; init; } public Opt<string> Label { get; init; } public Opt<string> Color { get; init; } }

/// <summary>What an editor panel shows: one row per lane, axis, logic stack, series, threshold and marker, in display order, with its parent and depth.</summary>
public sealed record EditorRow(string Kind, string Id, string? ParentId, int Depth, string Label, string Detail);

public sealed partial class TrendChartModel
{
    private int _axisSeq, _thresholdSeq, _markerSeq;

    /// <summary>Add an empty lane at <paramref name="index"/> (the end by default) that stays while empty; returns its id.</summary>
    public string AddLane(int? index = null, string? label = null)
    {
        var id = InsertLane(index ?? Lanes().Count);
        var lane = Lanes().First(l => l.Id == id);
        lane.Keep = true;
        if (!string.IsNullOrWhiteSpace(label)) lane.Label = label;
        return id;
    }

    /// <summary>Change a lane's label, weight, fold state or keep flag.</summary>
    public bool UpdateLane(string laneId, LanePatch patch)
    {
        var lane = Lanes().FirstOrDefault(l => l.Id == laneId);
        if (lane is null) return false;
        if (patch.Label.IsSet) lane.Label = string.IsNullOrWhiteSpace(patch.Label.Value) ? null : patch.Label.Value!.Trim();
        if (patch.Weight.IsSet) lane.Weight = patch.Weight.Value is { } w && w > 0 ? w : 1;
        if (patch.Collapsed.IsSet) lane.Collapsed = patch.Collapsed.Value;
        if (patch.Keep.IsSet) lane.Keep = patch.Keep.Value;
        return true;
    }

    /// <summary>Add an axis definition (id <c>axis-N</c> unless given); it shows once a series uses it. Returns the id.</summary>
    public string AddAxis(AxisPatch? props = null, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(id)) do { id = $"axis-{++_axisSeq}"; } while (Config.Axes.Any(a => a.Id == id));
        if (Config.Axes.All(a => a.Id != id)) Config.Axes.Add(new AxisConfig(id));
        if (props is not null) UpdateAxis(id, props);
        return id;
    }

    /// <summary>Change an axis's label, unit, bounds, side or colour; an axis known only implicitly (<c>axis:&lt;lane&gt;</c>) gets a definition.</summary>
    public bool UpdateAxis(string axisId, AxisPatch patch)
    {
        var axis = Config.Axes.FirstOrDefault(a => a.Id == axisId);
        if (axis is null) { axis = new AxisConfig(axisId); Config.Axes.Add(axis); }
        if (patch.Label.IsSet) axis.Label = string.IsNullOrWhiteSpace(patch.Label.Value) ? null : patch.Label.Value!.Trim();
        if (patch.Unit.IsSet) axis.Unit = string.IsNullOrWhiteSpace(patch.Unit.Value) ? null : patch.Unit.Value!.Trim();
        if (patch.Min.IsSet) axis.Min = patch.Min.Value;
        if (patch.Max.IsSet) axis.Max = patch.Max.Value;
        if (patch.Side.IsSet) axis.Side = patch.Side.Value;
        if (patch.Color.IsSet) axis.Color = string.IsNullOrWhiteSpace(patch.Color.Value) ? null : patch.Color.Value;
        return true;
    }

    /// <summary>Remove an axis definition: series on it fall back to their lane's default axis, thresholds on it are removed.</summary>
    public bool RemoveAxis(string axisId)
    {
        var i = Config.Axes.FindIndex(a => a.Id == axisId);
        var used = Config.Series.Any(s => s.AxisId == axisId);
        if (i < 0 && !used) return false;
        if (i >= 0) Config.Axes.RemoveAt(i);
        foreach (var s in Config.Series) if (s.AxisId == axisId) s.AxisId = null;
        Config.Thresholds.RemoveAll(t => t.AxisId == axisId);
        return true;
    }

    /// <summary>Change a series's name, colour, width, visibility, lane, axis or kind; lanes left empty vanish unless kept.</summary>
    public bool UpdateSeries(string seriesId, SeriesPatch patch)
    {
        var s = Config.Series.FirstOrDefault(x => x.Id == seriesId);
        if (s is null) return false;
        if (patch.LaneId.IsSet && patch.LaneId.HasValue && Lanes().All(l => l.Id != patch.LaneId.Value)) return false;
        if (patch.Name.IsSet) s.Name = string.IsNullOrWhiteSpace(patch.Name.Value) ? null : patch.Name.Value!.Trim();
        if (patch.Color.IsSet) s.Color = string.IsNullOrWhiteSpace(patch.Color.Value) ? null : patch.Color.Value;
        if (patch.Width.IsSet) s.Width = patch.Width.Value is { } w ? Math.Max(0.5, w) : null;
        if (patch.Visible.IsSet) s.Visible = patch.Visible.Value;
        if (patch.Kind.IsSet) s.Kind = patch.Kind.Value;
        if (patch.LaneId.IsSet || patch.AxisId.IsSet)
        {
            PinLanes();
            if (patch.LaneId.IsSet) s.LaneId = patch.LaneId.HasValue ? patch.LaneId.Value : Lanes()[0].Id;
            if (patch.AxisId.IsSet) s.AxisId = patch.AxisId.HasValue && patch.AxisId.Value != $"axis:{LaneIdOf(s)}" ? patch.AxisId.Value : null;
            PruneEmptyLanes();
        }
        return true;
    }

    /// <summary>Add a threshold line (<paramref name="to"/> null) or band on an axis; returns its id (<c>threshold-N</c> unless given).</summary>
    public string AddThreshold(string axisId, double from, double? to = null, string? color = null, string? label = null, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(id)) do { id = $"threshold-{++_thresholdSeq}"; } while (Config.Thresholds.Any(t => t.Id == id));
        Config.Thresholds.RemoveAll(t => t.Id == id);
        Config.Thresholds.Add(new ThresholdConfig(id, axisId, from, string.IsNullOrWhiteSpace(color) ? "#dc2626" : color) { To = to, Label = string.IsNullOrWhiteSpace(label) ? null : label });
        return id;
    }

    /// <summary>Change a threshold's axis, values, colour or label.</summary>
    public bool UpdateThreshold(string id, ThresholdPatch patch)
    {
        var i = Config.Thresholds.FindIndex(t => t.Id == id);
        if (i < 0) return false;
        var t = Config.Thresholds[i];
        Config.Thresholds[i] = new ThresholdConfig(id, patch.AxisId.HasValue ? patch.AxisId.Value! : t.AxisId, patch.From.IsSet ? patch.From.Value ?? t.From : t.From, patch.Color.HasValue ? patch.Color.Value! : t.Color)
        {
            To = patch.To.IsSet ? patch.To.Value : t.To,
            Label = patch.Label.IsSet ? (string.IsNullOrWhiteSpace(patch.Label.Value) ? null : patch.Label.Value) : t.Label,
        };
        return true;
    }

    /// <summary>Remove a threshold.</summary>
    public bool RemoveThreshold(string id) => Config.Thresholds.RemoveAll(t => t.Id == id) > 0;

    /// <summary>Add an event marker at a chart time; returns its id (<c>marker-N</c> unless given).</summary>
    public string AddMarker(double time, string? label = null, string? color = null, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(id)) do { id = $"marker-{++_markerSeq}"; } while (Config.Markers.Any(m => m.Id == id));
        Config.Markers.RemoveAll(m => m.Id == id);
        Config.Markers.Add(new MarkerConfig(id, time) { Label = string.IsNullOrWhiteSpace(label) ? null : label, Color = string.IsNullOrWhiteSpace(color) ? null : color });
        return id;
    }

    /// <summary>Change a marker's time, label or colour.</summary>
    public bool UpdateMarker(string id, MarkerPatch patch)
    {
        var i = Config.Markers.FindIndex(m => m.Id == id);
        if (i < 0) return false;
        var m = Config.Markers[i];
        Config.Markers[i] = new MarkerConfig(id, patch.Time.IsSet ? patch.Time.Value ?? m.Time : m.Time)
        {
            Label = patch.Label.IsSet ? (string.IsNullOrWhiteSpace(patch.Label.Value) ? null : patch.Label.Value) : m.Label,
            Color = patch.Color.IsSet ? (string.IsNullOrWhiteSpace(patch.Color.Value) ? null : patch.Color.Value) : m.Color,
        };
        return true;
    }

    /// <summary>Remove a marker.</summary>
    public bool RemoveMarker(string id) => Config.Markers.RemoveAll(m => m.Id == id) > 0;

    /// <summary>Move a series to position <paramref name="index"/> in the configuration order (what the legend and the labels follow).</summary>
    public bool ReorderSeries(string seriesId, int index)
    {
        var list = Config.Series;
        var i = list.FindIndex(s => s.Id == seriesId);
        if (i < 0) return false;
        var at = Math.Clamp(index, 0, list.Count - 1);
        if (at == i) return false;
        var s = list[i]; list.RemoveAt(i); list.Insert(at, s);
        return true;
    }
    /// <summary>The whole configuration (theme and style included) as JSON, for undo history; <see cref="RestoreConfig"/> takes it back.</summary>
    public string SnapshotConfig() => System.Text.Json.JsonSerializer.Serialize(Config, TrendLayoutFile.Options);
    /// <summary>Replace the whole configuration with a snapshot; cursors and the time window are kept, the measurement cache is dropped.</summary>
    public void RestoreConfig(string json)
    {
        var c = System.Text.Json.JsonSerializer.Deserialize<TrendChartConfig>(json, TrendLayoutFile.Options) ?? new TrendChartConfig();
        TrendLayoutFile.Apply(Config, c);
        Config.Theme = c.Theme; Config.Style = c.Style;
        TimeSpan = Config.TimeSpan;
        _measureCache.Clear();
    }

    /// <summary>
    /// The rows an editor panel renders, in display order: each lane, under it its axes (left ones first) with their
    /// series (hidden ones included), then the logic stack with the digital series; then axis definitions no series
    /// uses, the thresholds and the markers. Both cores produce the same list.
    /// </summary>
    public List<EditorRow> EditorRows()
    {
        var rows = new List<EditorRow>();
        var usedAxes = new HashSet<string>();
        foreach (var lane in Lanes())
        {
            var all = AllSeriesIn(lane.Id).ToList();
            rows.Add(new EditorRow("lane", lane.Id, null, 0, lane.Label ?? lane.Id, $"weight {lane.Weight}{(lane.Collapsed ? ", folded" : "")}"));
            var ids = new List<string>();
            foreach (var s in all) { if (s.Kind == SeriesKind.Digital) continue; var id = AxisIdOf(s); if (!ids.Contains(id)) ids.Add(id); }
            var axes = ids.Select(Axis).ToList();
            foreach (var a in axes.Where(a => a.Side == AxisSide.Left).Concat(axes.Where(a => a.Side == AxisSide.Right)))
            {
                usedAxes.Add(a.Id);
                var bounds = a.Min is { } mn || a.Max is { } mx ? $"{(a.Min is { } lo ? Round(lo) : "auto")} … {(a.Max is { } hi ? Round(hi) : "auto")}" : "autoscale";
                rows.Add(new EditorRow("axis", a.Id, lane.Id, 1, a.Label ?? a.Id, $"{(a.Unit is not null ? a.Unit + " · " : "")}{bounds}{(a.Side == AxisSide.Right ? " · right" : "")}"));
                foreach (var s in all.Where(s => s.Kind != SeriesKind.Digital && AxisIdOf(s) == a.Id))
                    rows.Add(new EditorRow("series", s.Id, a.Id, 2, SeriesName(s), $"ch {s.ChannelId}{(s.Visible ? "" : " · hidden")}"));
            }
            var digital = all.Where(s => s.Kind == SeriesKind.Digital).ToList();
            if (digital.Count > 0)
            {
                rows.Add(new EditorRow("stack", $"stack:{lane.Id}", lane.Id, 1, "logic stack", $"{digital.Count} track{(digital.Count == 1 ? "" : "s")}"));
                foreach (var s in digital) rows.Add(new EditorRow("series", s.Id, $"stack:{lane.Id}", 2, SeriesName(s), $"ch {s.ChannelId}{(s.Visible ? "" : " · hidden")}"));
            }
        }
        foreach (var a in Config.Axes.Where(a => !usedAxes.Contains(a.Id)))
            rows.Add(new EditorRow("axis", a.Id, null, 0, a.Label ?? a.Id, "unused"));
        foreach (var t in Config.Thresholds)
            rows.Add(new EditorRow("threshold", t.Id, null, 0, t.Label ?? t.Id, $"{t.AxisId} · {(t.To is { } to ? $"{Round(t.From)} … {Round(to)}" : Round(t.From))}"));
        foreach (var m in Config.Markers)
            rows.Add(new EditorRow("marker", m.Id, null, 0, m.Label ?? m.Id, $"t = {Round(m.Time)}"));
        return rows;
    }

    private static string Round(double v) => (Math.Round(v * 1000) / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
