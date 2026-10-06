// Mori.SkyScope — Windows Forms runtime editor for a trend chart: a tree of lanes, axes, series, thresholds and markers, add/remove/move buttons, and a PropertyGrid for the selected item.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// A runtime editor for a <see cref="TrendChartControl"/>: the structure as a tree (lanes → axes → series, the logic
/// stack, unused axes, thresholds, markers), a toolbar to add and remove items and to move lanes, and a
/// <see cref="PropertyGrid"/> for the selected item (or the chart's own settings when nothing is selected). Every
/// change goes through the model's editor commands and raises the chart's <c>ConfigChanged</c>.
/// </summary>
[ToolboxItem(true), Description("Runtime editor for a trend chart: lanes, axes, signals, thresholds, markers.")]
public class ChartEditorControl : UserControl
{
    private readonly ToolStrip _bar = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top, LayoutStyle = ToolStripLayoutStyle.Flow, AutoSize = true };
    private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = true, BorderStyle = BorderStyle.FixedSingle };
    private readonly PropertyGrid _grid = new() { Dock = DockStyle.Bottom, Height = 220, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.NoSort };
    private readonly ToolStripComboBox _channels = new() { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 140 };
    private readonly ToolStripButton _addSignal = new("+ signal"), _up = new("▲"), _down = new("▼"), _remove = new("remove"), _undo = new("undo"), _redo = new("redo");
    private readonly Stack<string> _undoStack = new(), _redoStack = new();
    private string? _last;
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private TrendChartControl? _chart;
    private (string Kind, string Id)? _selected;
    private bool _refreshing;

    /// <summary>Builds the toolbar, the tree and the property grid.</summary>
    public ChartEditorControl()
    {
        _split.Panel1.Controls.Add(_tree); _split.Panel2.Controls.Add(_grid); _grid.Dock = DockStyle.Fill;
        Controls.Add(_split); Controls.Add(_bar);
        ToolStripButton Add(string label, string tip, Action click) { var b = new ToolStripButton(label) { ToolTipText = tip }; b.Click += (_, _) => click(); _bar.Items.Add(b); return b; }
        Add("+ lane", "add a lane at the end", () => Run(m => { var id = m.AddLane(); _selected = ("lane", id); }));
        _channels.ToolTipText = "a channel of the store not in the chart yet"; _bar.Items.Add(_channels);
        _addSignal.ToolTipText = "add the chosen channel to the selected lane (or the first)"; _addSignal.Click += (_, _) => AddPickedChannel(); _bar.Items.Add(_addSignal);
        Add("+ axis", "add an axis definition (assign it to a signal afterwards)", () => Run(m => { var id = m.AddAxis(); _selected = ("axis", id); }));
        Add("+ threshold", "add a threshold on the selected axis (or the first axis)", () => Run(m =>
        {
            var axisId = _selected is { Kind: "axis" } a ? a.Id : _selected is { Kind: "series" } s ? m.AxisIdOf(m.Config.Series.First(x => x.Id == s.Id)) : m.AxesIn(m.Lanes()[0].Id).FirstOrDefault()?.Id ?? $"axis:{m.Lanes()[0].Id}";
            _selected = ("threshold", m.AddThreshold(axisId.StartsWith("stack:") ? $"axis:{m.Lanes()[0].Id}" : axisId, 0));
        }));
        Add("+ marker", "add an event marker at the right edge of the window", () => Run(m => { _selected = ("marker", m.AddMarker(m.Window().T1)); }));
        _bar.Items.Add(new ToolStripSeparator());
        _up.ToolTipText = "move the lane or the signal up"; _up.Click += (_, _) => Run(m => { if (_selected is { Kind: "series" }) { Reorder(m, -1); return; } var i = LaneIndex(m); if (i > 0) m.MoveLane(_selected!.Value.Id, i - 1); }); _bar.Items.Add(_up);
        _down.ToolTipText = "move the lane or the signal down"; _down.Click += (_, _) => Run(m => { if (_selected is { Kind: "series" }) { Reorder(m, 1); return; } var i = LaneIndex(m); if (i >= 0 && i < m.Lanes().Count - 1) m.MoveLane(_selected!.Value.Id, i + 2); }); _bar.Items.Add(_down);
        _remove.ToolTipText = "remove the selected item (Delete)"; _remove.Click += (_, _) => RemoveSelected(); _bar.Items.Add(_remove);
        _undo.ToolTipText = "undo the last change (Ctrl+Z)"; _undo.Click += (_, _) => Undo(); _bar.Items.Add(_undo);
        _redo.ToolTipText = "redo (Ctrl+Y)"; _redo.Click += (_, _) => Redo(); _bar.Items.Add(_redo);
        _tree.KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z) { Undo(); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.Y) { Redo(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
        };
        _tree.AfterSelect += (_, e) => { if (_refreshing) return; _selected = e.Node?.Tag is EditorRow r ? (r.Kind, r.Id) : null; RenderBar(); RenderGrid(); };
        _tree.MouseDown += (_, e) => { var n = _tree.GetNodeAt(e.Location); if (n is null || (n == _tree.SelectedNode && e.Button == MouseButtons.Left)) { _refreshing = true; _tree.SelectedNode = null; _refreshing = false; _selected = null; RenderBar(); RenderGrid(); } };
        _grid.PropertyValueChanged += (_, _) => { _chart?.NotifyConfigChanged(); RefreshTree(); };
        Refresh();
    }

    /// <summary>The chart to edit; the editor follows its <c>ConfigChanged</c>.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TrendChartControl? Chart
    {
        get => _chart;
        set { if (_chart is not null) _chart.ConfigChanged -= OnChartChanged; _chart = value; if (_chart is not null) _chart.ConfigChanged += OnChartChanged; _selected = null; _undoStack.Clear(); _redoStack.Clear(); _last = _chart?.Model.SnapshotConfig(); Refresh(); }
    }
    /// <summary>Hide the chart-level settings (time span, legend, panels) shown when nothing is selected.</summary>
    [Category("Behavior"), DefaultValue(false)]
    public bool HideChartSettings { get; set; }
    /// <summary>Height of the property grid under the tree.</summary>
    [Category("Layout"), DefaultValue(220)]
    public int GridHeight { get => _split.Height - _split.SplitterDistance; set { if (_split.Height > value + 40) _split.SplitterDistance = _split.Height - value; } }

    private void OnChartChanged() { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => { RecordChange(); Refresh(); }); }

    /// <summary>After any configuration change: the previous state goes on the undo stack (a restore lands on the state it restored, so nothing is pushed then).</summary>
    private void RecordChange()
    {
        if (_chart is null) return;
        var cur = _chart.Model.SnapshotConfig();
        if (cur == _last) return;
        if (_last is not null) { _undoStack.Push(_last); if (_undoStack.Count > 100) { var keep = _undoStack.Take(100).Reverse().ToList(); _undoStack.Clear(); foreach (var x in keep) _undoStack.Push(x); } }
        _redoStack.Clear();
        _last = cur;
    }
    /// <summary>Undo the last change (editor or gesture); no-op without history.</summary>
    public void Undo() { if (_chart is null || _undoStack.Count == 0) return; var prev = _undoStack.Pop(); if (_last is not null) _redoStack.Push(_last); _last = prev; _chart.Model.RestoreConfig(prev); _chart.NotifyConfigChanged(); }
    /// <summary>Redo the last undone change.</summary>
    public void Redo() { if (_chart is null || _redoStack.Count == 0) return; var next = _redoStack.Pop(); if (_last is not null) _undoStack.Push(_last); _last = next; _chart.Model.RestoreConfig(next); _chart.NotifyConfigChanged(); }

    private void RemoveSelected() => Run(m =>
    {
        if (_selected is not { } sel || sel.Kind == "stack") return;
        switch (sel.Kind) { case "lane": m.RemoveLane(sel.Id); break; case "axis": m.RemoveAxis(sel.Id); break; case "series": m.RemoveSeries(sel.Id); break; case "threshold": m.RemoveThreshold(sel.Id); break; case "marker": m.RemoveMarker(sel.Id); break; }
        _selected = null;
    });

    /// <summary>Moves the selected series before or after its neighbour under the same axis or stack.</summary>
    private void Reorder(TrendChartModel m, int delta)
    {
        if (_selected is not { Kind: "series" } sel) return;
        var rows = m.EditorRows();
        var me = rows.FirstOrDefault(r => r.Kind == "series" && r.Id == sel.Id); if (me is null) return;
        var siblings = rows.Where(r => r.Kind == "series" && r.ParentId == me.ParentId).ToList();
        var i = siblings.IndexOf(me) + delta;
        if (i < 0 || i >= siblings.Count) return;
        m.ReorderSeries(sel.Id, m.Config.Series.FindIndex(s => s.Id == siblings[i].Id));
    }
    private int LaneIndex(TrendChartModel m) => _selected is { Kind: "lane" } s ? m.Lanes().FindIndex(l => l.Id == s.Id) : -1;

    private void Run(Action<TrendChartModel> command)
    {
        if (_chart is null) return;
        command(_chart.Model);
        _chart.NotifyConfigChanged();
        Refresh();
    }

    /// <summary>Rebuilds the tree, the toolbar state and the grid from the model.</summary>
    public new void Refresh() { RefreshTree(); RenderBar(); RenderGrid(); }

    private void RefreshTree()
    {
        _refreshing = true;
        try
        {
            var rows = _chart?.Model.EditorRows() ?? [];
            if (_selected is { } sel && !rows.Any(r => r.Kind == sel.Kind && r.Id == sel.Id)) _selected = null;
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var byId = new Dictionary<string, TreeNode>();
            TreeNode? selectedNode = null;
            foreach (var r in rows)
            {
                var node = new TreeNode($"{Glyph(r.Kind)} {r.Label}   {r.Detail}") { Tag = r, ToolTipText = $"{r.Kind} {r.Id}" };
                if (r.Kind == "lane") node.NodeFont = new Font(_tree.Font, FontStyle.Bold);
                if (r.ParentId is not null && byId.TryGetValue(r.ParentId, out var parent)) parent.Nodes.Add(node); else _tree.Nodes.Add(node);
                byId[r.Id] = node;
                if (_selected is { } s && r.Kind == s.Kind && r.Id == s.Id) selectedNode = node;
            }
            _tree.ExpandAll();
            _tree.SelectedNode = selectedNode;
            _tree.EndUpdate();
            // the channel picker lists what is not in the chart yet
            var chart = _chart;
            _channels.Items.Clear();
            if (chart is not null)
            {
                var inChart = chart.Model.Config.Series.Select(x => x.ChannelId).ToHashSet();
                List<ChannelInfo> options;
                lock (chart.Store.SyncRoot) options = chart.Store.Channels.Values.Select(c => c.Info).Where(i => !inChart.Contains(i.Id)).OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
                foreach (var i in options) _channels.Items.Add(new ChannelChoice(i));
                if (_channels.Items.Count > 0) _channels.SelectedIndex = 0;
            }
        }
        finally { _refreshing = false; }
    }

    private sealed record ChannelChoice(ChannelInfo Info) { public override string ToString() => $"{Info.Name}{(Info.Unit is not null ? $" [{Info.Unit}]" : "")}{(Info.Kind == ChannelKind.Digital ? " (digital)" : "")}"; }

    private void AddPickedChannel()
    {
        var chart = _chart; if (chart is null || _channels.SelectedItem is not ChannelChoice c) return;
        var rows = chart.Model.EditorRows();
        string? LaneOf(string? rid) { if (rid is null) return null; var r = rows.FirstOrDefault(x => x.Id == rid); return r is null ? null : r.Kind == "lane" ? r.Id : LaneOf(r.ParentId); }
        var laneId = (_selected is { } s ? (s.Kind == "lane" ? s.Id : LaneOf(s.Id)) : null) ?? chart.Model.Lanes()[0].Id;
        var ids = chart.AddChannels([c.Info.Id], new DropTarget.OwnAxis(laneId));
        if (ids.Count > 0) _selected = ("series", ids[0]);
        Refresh();
    }

    private void RenderBar()
    {
        var m = _chart?.Model;
        var i = m is null ? -1 : LaneIndex(m);
        var series = _selected is { Kind: "series" };
        _up.Enabled = i > 0 || series; _down.Enabled = (m is not null && i >= 0 && i < m.Lanes().Count - 1) || series;
        _remove.Enabled = _selected is { } s && s.Kind != "stack";
        _undo.Enabled = _undoStack.Count > 0; _redo.Enabled = _redoStack.Count > 0;
        _addSignal.Enabled = m is not null && _channels.Items.Count > 0;
        foreach (var item in _bar.Items.OfType<ToolStripButton>()) if (item != _up && item != _down && item != _remove && item != _addSignal && item != _undo && item != _redo) item.Enabled = m is not null;
    }

    private void RenderGrid()
    {
        var chart = _chart;
        if (chart is null) { _grid.SelectedObject = null; return; }
        var m = chart.Model;
        _grid.SelectedObject = _selected switch
        {
            null => HideChartSettings ? null : new ChartSettings(chart),
            { Kind: "lane" } s => m.Lanes().Any(l => l.Id == s.Id) ? new LaneProperties(m, s.Id) : null,
            { Kind: "axis" } s => new AxisProperties(m, s.Id),
            { Kind: "series" } s => m.Config.Series.Any(x => x.Id == s.Id) ? new SeriesProperties(m, s.Id) : null,
            { Kind: "threshold" } s => m.Config.Thresholds.Any(x => x.Id == s.Id) ? new ThresholdProperties(m, s.Id) : null,
            { Kind: "marker" } s => m.Config.Markers.Any(x => x.Id == s.Id) ? new MarkerProperties(m, s.Id) : null,
            _ => null,
        };
    }

    private static string Glyph(string kind) => kind switch { "lane" => "▤", "axis" => "┃", "stack" => "▦", "series" => "〜", "threshold" => "―", _ => "│" };

    // ---- property grid wrappers: every setter runs a model command; the grid's PropertyValueChanged notifies the chart ----

    /// <summary>Chart-level settings shown when nothing is selected.</summary>
    public sealed class ChartSettings(TrendChartControl chart)
    {
        private TrendChartConfig C => chart.Model.Config;
        /// <summary>Visible time span in seconds.</summary>
        [Category("Chart"), Description("Visible time span in seconds.")] public double TimeSpan { get => C.TimeSpan; set { if (value > 0) { C.TimeSpan = value; chart.Model.SetTimeSpan(value); } } }
        /// <summary>Time axis labels: wall clock (UTC) or seconds back from the live edge.</summary>
        [Category("Chart")] public TimeFormat TimeFormat { get => C.TimeFormat; set => C.TimeFormat = value; }
        /// <summary>Legend placement.</summary>
        [Category("Chart")] public LegendPosition Legend { get => C.Legend; set => C.Legend = value; }
        /// <summary>Series names inside the lanes.</summary>
        [Category("Panels")] public bool PlotLabels { get => C.PlotLabels; set => C.PlotLabels = value; }
        /// <summary>Header bars left of the lanes.</summary>
        [Category("Panels")] public bool LaneHeaders { get => C.LaneHeaders; set => C.LaneHeaders = value; }
        /// <summary>Overview strip under the time axis.</summary>
        [Category("Panels")] public bool Navigator { get => C.Navigator; set => C.Navigator = value; }
        /// <summary>Measurement table while both cursors are set.</summary>
        [Category("Panels")] public bool MeasurePanel { get => C.MeasurePanel; set => C.MeasurePanel = value; }
        /// <summary>Light or dark preset (a customised theme reads as Light).</summary>
        [Category("Look")] public ChartThemeChoice Theme { get => C.Theme == ChartTheme.Dark ? ChartThemeChoice.Dark : ChartThemeChoice.Light; set => C.Theme = value == ChartThemeChoice.Dark ? ChartTheme.Dark : ChartTheme.Light; }
        /// <summary>Default line width of the signals in pixels.</summary>
        [Category("Look")] public double SeriesWidth { get => C.Style.SeriesWidth; set { if (value > 0) C.Style = C.Style with { SeriesWidth = value }; } }
        /// <summary>Base font size in pixels.</summary>
        [Category("Look")] public double FontSize { get => C.Theme.FontSize; set { if (value >= 6) C.Theme = C.Theme with { FontSize = value }; } }
        /// <summary>Horizontal grid lines at value ticks.</summary>
        [Category("Look")] public bool ValueGrid { get => C.Style.ShowValueGrid; set => C.Style = C.Style with { ShowValueGrid = value }; }
        /// <summary>Vertical grid lines at time ticks.</summary>
        [Category("Look")] public bool TimeGrid { get => C.Style.ShowTimeGrid; set => C.Style = C.Style with { ShowTimeGrid = value }; }
        /// <inheritdoc/>
        public override string ToString() => "chart";
    }

    /// <summary>A lane's editable fields.</summary>
    public sealed class LaneProperties(TrendChartModel m, string id)
    {
        private LaneConfig L => m.Lanes().First(l => l.Id == id);
        /// <summary>Lane id (read-only).</summary>
        [Category("Lane")] public string Id => id;
        /// <summary>Caption; empty shows the id.</summary>
        [Category("Lane")] public string? Label { get => L.Label; set => m.UpdateLane(id, new LanePatch { Label = value }); }
        /// <summary>Share of the plot height relative to the other lanes.</summary>
        [Category("Lane")] public double Weight { get => L.Weight; set => m.UpdateLane(id, new LanePatch { Weight = value }); }
        /// <summary>Folded to a summary bar.</summary>
        [Category("Lane")] public bool Folded { get => L.Collapsed; set => m.UpdateLane(id, new LanePatch { Collapsed = value }); }
        /// <summary>Stays when it has no signals.</summary>
        [Category("Lane")] public bool KeepWhenEmpty { get => L.Keep; set => m.UpdateLane(id, new LanePatch { Keep = value }); }
        /// <inheritdoc/>
        public override string ToString() => $"lane {id}";
    }

    /// <summary>An axis's editable fields.</summary>
    public sealed class AxisProperties(TrendChartModel m, string id)
    {
        private AxisConfig A => m.Axis(id);
        /// <summary>Axis id (read-only).</summary>
        [Category("Axis")] public string Id => id;
        /// <summary>Caption at the top of the strip.</summary>
        [Category("Axis")] public string? Label { get => A.Label; set => m.UpdateAxis(id, new AxisPatch { Label = value }); }
        /// <summary>Unit shown in the legend.</summary>
        [Category("Axis")] public string? Unit { get => A.Unit; set => m.UpdateAxis(id, new AxisPatch { Unit = value }); }
        /// <summary>Fixed lower bound; empty autoscales.</summary>
        [Category("Range")] public double? Min { get => A.Min; set => m.UpdateAxis(id, new AxisPatch { Min = value }); }
        /// <summary>Fixed upper bound; empty autoscales.</summary>
        [Category("Range")] public double? Max { get => A.Max; set => m.UpdateAxis(id, new AxisPatch { Max = value }); }
        /// <summary>Left or right column.</summary>
        [Category("Axis")] public AxisSide Side { get => A.Side; set => m.UpdateAxis(id, new AxisPatch { Side = value }); }
        /// <summary>CSS colour; empty = theme.</summary>
        [Category("Axis")] public string? Color { get => A.Color; set => m.UpdateAxis(id, new AxisPatch { Color = value }); }
        /// <inheritdoc/>
        public override string ToString() => $"axis {id}";
    }

    /// <summary>A signal's editable fields.</summary>
    public sealed class SeriesProperties(TrendChartModel m, string id)
    {
        private SeriesConfig S => m.Config.Series.First(x => x.Id == id);
        /// <summary>Series id (read-only).</summary>
        [Category("Signal")] public string Id => id;
        /// <summary>Channel of the store (read-only; remove and add to change).</summary>
        [Category("Signal")] public int ChannelId => S.ChannelId;
        /// <summary>Display name; empty = the channel name.</summary>
        [Category("Signal")] public string? Name { get => S.Name; set => m.UpdateSeries(id, new SeriesPatch { Name = value }); }
        /// <summary>Lane id.</summary>
        [Category("Placement"), TypeConverter(typeof(LaneIdConverter))] public string LaneId { get => m.LaneIdOf(S); set => m.UpdateSeries(id, new SeriesPatch { LaneId = value }); }
        /// <summary>Axis id; empty = the lane's default axis (digital signals ignore it).</summary>
        [Category("Placement"), TypeConverter(typeof(AxisIdConverter))] public string? AxisId { get => S.Kind == SeriesKind.Digital ? null : m.AxisIdOf(S); set => m.UpdateSeries(id, new SeriesPatch { AxisId = value }); }
        /// <summary>Logic-analyzer track instead of an analog line.</summary>
        [Category("Placement")] public bool Digital { get => S.Kind == SeriesKind.Digital; set => m.UpdateSeries(id, new SeriesPatch { Kind = value ? SeriesKind.Digital : SeriesKind.Analog }); }
        /// <summary>Shown or hidden.</summary>
        [Category("Look")] public bool Visible { get => S.Visible; set => m.UpdateSeries(id, new SeriesPatch { Visible = value }); }
        /// <summary>CSS colour; empty = palette.</summary>
        [Category("Look")] public string? Color { get => S.Color; set => m.UpdateSeries(id, new SeriesPatch { Color = value }); }
        /// <summary>Line width in pixels; empty = style default.</summary>
        [Category("Look")] public double? Width { get => S.Width; set => m.UpdateSeries(id, new SeriesPatch { Width = value }); }
        /// <inheritdoc/>
        public override string ToString() => $"signal {id}";
        internal TrendChartModel Model => m;
    }

    /// <summary>A threshold's editable fields.</summary>
    public sealed class ThresholdProperties(TrendChartModel m, string id)
    {
        private ThresholdConfig T => m.Config.Thresholds.First(x => x.Id == id);
        /// <summary>Threshold id (read-only).</summary>
        [Category("Threshold")] public string Id => id;
        /// <summary>Axis the values refer to.</summary>
        [Category("Threshold"), TypeConverter(typeof(ThresholdAxisConverter))] public string AxisId { get => T.AxisId; set => m.UpdateThreshold(id, new ThresholdPatch { AxisId = value }); }
        /// <summary>Line value, or the band's lower bound.</summary>
        [Category("Threshold")] public double From { get => T.From; set => m.UpdateThreshold(id, new ThresholdPatch { From = value }); }
        /// <summary>Band's upper bound; empty = a line.</summary>
        [Category("Threshold")] public double? To { get => T.To; set => m.UpdateThreshold(id, new ThresholdPatch { To = value }); }
        /// <summary>CSS colour.</summary>
        [Category("Threshold")] public string Color { get => T.Color; set => m.UpdateThreshold(id, new ThresholdPatch { Color = string.IsNullOrWhiteSpace(value) ? "#dc2626" : value }); }
        /// <summary>Caption.</summary>
        [Category("Threshold")] public string? Label { get => T.Label; set => m.UpdateThreshold(id, new ThresholdPatch { Label = value }); }
        /// <inheritdoc/>
        public override string ToString() => $"threshold {id}";
        internal TrendChartModel Model => m;
    }

    /// <summary>A marker's editable fields.</summary>
    public sealed class MarkerProperties(TrendChartModel m, string id)
    {
        private MarkerConfig M => m.Config.Markers.First(x => x.Id == id);
        /// <summary>Marker id (read-only).</summary>
        [Category("Marker")] public string Id => id;
        /// <summary>Chart time in seconds.</summary>
        [Category("Marker")] public double Time { get => M.Time; set => m.UpdateMarker(id, new MarkerPatch { Time = value }); }
        /// <summary>Caption.</summary>
        [Category("Marker")] public string? Label { get => M.Label; set => m.UpdateMarker(id, new MarkerPatch { Label = value }); }
        /// <summary>CSS colour; empty = theme.</summary>
        [Category("Marker")] public string? Color { get => M.Color; set => m.UpdateMarker(id, new MarkerPatch { Color = value }); }
        /// <inheritdoc/>
        public override string ToString() => $"marker {id}";
    }

    /// <summary>Offers the chart's lane ids in the property grid's dropdown.</summary>
    private sealed class LaneIdConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new((context?.Instance as SeriesProperties)?.Model.Lanes().Select(l => l.Id).ToList() ?? []);
    }
    /// <summary>Offers the lane default axis and every defined axis in the property grid's dropdown.</summary>
    private sealed class AxisIdConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            if (context?.Instance is not SeriesProperties p) return new(Array.Empty<string>());
            var ids = new List<string> { $"axis:{p.LaneId}" };
            ids.AddRange(p.Model.Config.Axes.Select(a => a.Id).Where(a => !ids.Contains(a)));
            return new(ids);
        }
    }
    /// <summary>Offers every lane default axis and defined axis for a threshold.</summary>
    private sealed class ThresholdAxisConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            if (context?.Instance is not ThresholdProperties p) return new(Array.Empty<string>());
            var ids = p.Model.Lanes().Select(l => $"axis:{l.Id}").ToList();
            ids.AddRange(p.Model.Config.Axes.Select(a => a.Id).Where(a => !ids.Contains(a)));
            return new(ids);
        }
    }
}
