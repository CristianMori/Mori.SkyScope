// Mori.SkyScope — WPF runtime editor for a trend chart: lanes, axes, series, thresholds and markers as a list, add/remove/move buttons, and a property form for the selected item.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// A runtime editor for a <see cref="TrendChartControl"/>: the structure as an indented list (lanes → axes → series,
/// the logic stack, unused axes, thresholds, markers), buttons to add and remove items and to move lanes, and a
/// property form for the selected item (or the chart's own settings when nothing is selected). Every change goes
/// through the model's editor commands and raises the chart's <c>ConfigChanged</c>.
/// </summary>
public class ChartEditorControl : DockPanel
{
    /// <summary>A list item: wraps an <see cref="EditorRow"/> with the display values the template reads.</summary>
    public sealed class Item(EditorRow row)
    {
        /// <summary>The underlying row.</summary>
        public EditorRow Row { get; } = row;
        /// <summary>A glyph per kind.</summary>
        public string Glyph => Row.Kind switch { "lane" => "▤", "axis" => "┃", "stack" => "▦", "series" => "〜", "threshold" => "―", _ => "│" };
        /// <summary>Display name.</summary>
        public string Label => Row.Label;
        /// <summary>Secondary text.</summary>
        public string Detail => Row.Detail;
        /// <summary>Indentation from the row's depth.</summary>
        public Thickness Indent => new(6 + Row.Depth * 14, 0, 0, 0);
        /// <summary>Lanes in semi-bold.</summary>
        public FontWeight Weight => Row.Kind == "lane" ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private readonly WrapPanel _bar = new() { Margin = new Thickness(0, 0, 0, 4) };
    private readonly ListBox _list = new() { BorderThickness = new Thickness(1) };
    private readonly Grid _form = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly Button _up = new() { Content = "▲", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3) };
    private readonly Button _down = new() { Content = "▼", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3) };
    private readonly Button _remove = new() { Content = "remove", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3) };
    private readonly Button _undo = new() { Content = "undo", ToolTip = "undo the last change (Ctrl+Z)", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3) };
    private readonly Button _redo = new() { Content = "redo", ToolTip = "redo (Ctrl+Y)", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3) };
    private readonly Stack<string> _undoStack = new(), _redoStack = new();
    private string? _last;
    private TrendChartControl? _chart;
    private (string Kind, string Id)? _selected;
    private bool _refreshing;

    /// <summary>Builds the toolbar, the list and the form.</summary>
    public ChartEditorControl()
    {
        SetDock(_bar, Dock.Top); SetDock(_form, Dock.Bottom);
        Children.Add(_bar); Children.Add(_form); Children.Add(_list);
        _list.ItemTemplate = RowTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Button Add(string label, string tip, Action click) { var b = new Button { Content = label, ToolTip = tip, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 3, 3) }; b.Click += (_, _) => click(); _bar.Children.Add(b); return b; }
        Add("+ lane", "add a lane at the end", () => Run(m => { var id = m.AddLane(); _selected = ("lane", id); }));
        Add("+ signal", "add a channel of the store to the selected lane (or the first)", PickChannel);
        Add("+ axis", "add an axis definition (assign it to a series afterwards)", () => Run(m => { var id = m.AddAxis(); _selected = ("axis", id); }));
        Add("+ threshold", "add a threshold on the selected axis (or the first axis)", () => Run(m =>
        {
            var axisId = _selected is { Kind: "axis" } a ? a.Id : _selected is { Kind: "series" } s ? m.AxisIdOf(m.Config.Series.First(x => x.Id == s.Id)) : m.AxesIn(m.Lanes()[0].Id).FirstOrDefault()?.Id ?? $"axis:{m.Lanes()[0].Id}";
            _selected = ("threshold", m.AddThreshold(axisId.StartsWith("stack:") ? $"axis:{m.Lanes()[0].Id}" : axisId, 0));
        }));
        Add("+ marker", "add an event marker at the right edge of the window", () => Run(m => { _selected = ("marker", m.AddMarker(m.Window().T1)); }));
        _up.ToolTip = "move the lane or the signal up"; _up.Click += (_, _) => Run(m => { if (_selected is { Kind: "series" }) { Reorder(m, -1); return; } var i = LaneIndex(m); if (i > 0) m.MoveLane(_selected!.Value.Id, i - 1); }); _bar.Children.Add(_up);
        _down.ToolTip = "move the lane or the signal down"; _down.Click += (_, _) => Run(m => { if (_selected is { Kind: "series" }) { Reorder(m, 1); return; } var i = LaneIndex(m); if (i >= 0 && i < m.Lanes().Count - 1) m.MoveLane(_selected!.Value.Id, i + 2); }); _bar.Children.Add(_down);
        _remove.ToolTip = "remove the selected item (Delete)"; _remove.Click += (_, _) => RemoveSelected(); _bar.Children.Add(_remove);
        _undo.Click += (_, _) => Undo(); _bar.Children.Add(_undo);
        _redo.Click += (_, _) => Redo(); _bar.Children.Add(_redo);
        PreviewKeyDown += OnKey;
        _list.SelectionChanged += (_, _) => { if (_refreshing) return; _selected = _list.SelectedItem is Item it ? (it.Row.Kind, it.Row.Id) : null; RenderBar(); RenderForm(); };
        _list.PreviewMouseLeftButtonDown += (_, e) => { if (_list.SelectedItem is Item it && RowAt(e) == it) { _list.UnselectAll(); e.Handled = true; } };
        Refresh();
    }

    /// <summary>The chart to edit; the editor follows its <c>ConfigChanged</c>.</summary>
    public TrendChartControl? Chart
    {
        get => _chart;
        set { if (_chart is not null) _chart.ConfigChanged -= OnChartChanged; _chart = value; if (_chart is not null) _chart.ConfigChanged += OnChartChanged; _selected = null; _undoStack.Clear(); _redoStack.Clear(); _last = _chart?.Model.SnapshotConfig(); Refresh(); }
    }
    /// <summary>Hide the chart-level settings (time span, legend, panels) shown when nothing is selected.</summary>
    public bool HideChartSettings { get; set; }

    private void OnChartChanged() => Dispatcher.BeginInvoke(() => { RecordChange(); Refresh(); });

    /// <summary>After any configuration change: the previous state goes on the undo stack (a restore lands on the state it restored, so nothing is pushed then).</summary>
    private void RecordChange()
    {
        if (_chart is null) return;
        var cur = _chart.Model.SnapshotConfig();
        if (cur == _last) return;
        if (_last is not null) { _undoStack.Push(_last); while (_undoStack.Count > 100) { var keep = _undoStack.ToArray(); _undoStack.Clear(); foreach (var x in keep.Take(100).Reverse()) _undoStack.Push(x); } }
        _redoStack.Clear();
        _last = cur;
    }
    /// <summary>Undo the last change (editor or gesture); no-op without history.</summary>
    public void Undo() { if (_chart is null || _undoStack.Count == 0) return; var prev = _undoStack.Pop(); if (_last is not null) _redoStack.Push(_last); _last = prev; _chart.Model.RestoreConfig(prev); _chart.NotifyConfigChanged(); }
    /// <summary>Redo the last undone change.</summary>
    public void Redo() { if (_chart is null || _redoStack.Count == 0) return; var next = _redoStack.Pop(); if (_last is not null) _undoStack.Push(_last); _last = next; _chart.Model.RestoreConfig(next); _chart.NotifyConfigChanged(); }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var inText = Keyboard.FocusedElement is TextBox;
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && !inText) { Undo(); e.Handled = true; }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control && !inText) { Redo(); e.Handled = true; }
        else if (e.Key == Key.Delete && _list.IsKeyboardFocusWithin) { RemoveSelected(); e.Handled = true; }
    }

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

    private Item? RowAt(MouseButtonEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d is not null and not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as Item;
    }

    private int LaneIndex(TrendChartModel m) => _selected is { Kind: "lane" } s ? m.Lanes().FindIndex(l => l.Id == s.Id) : -1;

    /// <summary>Runs a command on the model, notifies the chart and re-renders.</summary>
    private void Run(Action<TrendChartModel> command)
    {
        if (_chart is null) return;
        command(_chart.Model);
        _chart.NotifyConfigChanged();
        Refresh();
    }

    /// <summary>Rebuilds the list, the toolbar state and the form from the model.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            var rows = _chart?.Model.EditorRows() ?? [];
            if (_selected is { } sel && !rows.Any(r => r.Kind == sel.Kind && r.Id == sel.Id)) _selected = null;
            var items = rows.Select(r => new Item(r)).ToList();
            _list.ItemsSource = items;
            _list.SelectedItem = items.FirstOrDefault(i => _selected is { } s && i.Row.Kind == s.Kind && i.Row.Id == s.Id);
        }
        finally { _refreshing = false; }
        RenderBar(); RenderForm();
    }

    private void RenderBar()
    {
        var m = _chart?.Model;
        var i = m is null ? -1 : LaneIndex(m);
        var series = _selected is { Kind: "series" };
        _up.IsEnabled = i > 0 || series; _down.IsEnabled = (m is not null && i >= 0 && i < m.Lanes().Count - 1) || series;
        _remove.IsEnabled = _selected is { } s && s.Kind != "stack";
        _undo.IsEnabled = _undoStack.Count > 0; _redo.IsEnabled = _redoStack.Count > 0;
        foreach (var c in _bar.Children.OfType<Button>()) if (c != _up && c != _down && c != _remove && c != _undo && c != _redo) c.IsEnabled = m is not null;
    }

    // ---- the property form ----------------------------------------------------------
    private void Clear() { _form.Children.Clear(); _form.RowDefinitions.Clear(); }
    private void Heading(string text) { var t = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), Margin = new Thickness(0, 0, 0, 2) }; Place(t, 0, 2); }
    private void Place(UIElement e, int col, int span = 1) { if (col == 0) _form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); Grid.SetRow(e, _form.RowDefinitions.Count - 1); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span); _form.Children.Add(e); }
    private void Field(string label, FrameworkElement input) { var l = new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)), Margin = new Thickness(0, 2, 8, 2), VerticalAlignment = VerticalAlignment.Center }; Place(l, 0); input.Margin = new Thickness(0, 1, 0, 1); Place(input, 1); }
    private TextBox Text(string? value, Action<string?> apply, string placeholder = "")
    {
        var t = new TextBox { Text = value ?? "", ToolTip = placeholder == "" ? null : placeholder, Padding = new Thickness(3, 1, 3, 1) };
        void Commit() { var v = t.Text.Trim(); apply(v == "" ? null : t.Text); }
        t.LostFocus += (_, _) => Commit(); t.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        return t;
    }
    private TextBox Number(double? value, Action<double?> apply, string placeholder = "")
        => Text(value is { } v ? v.ToString(CultureInfo.InvariantCulture) : null, s => apply(s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null), placeholder);
    private CheckBox Check(bool value, Action<bool> apply) { var c = new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center }; c.Click += (_, _) => apply(c.IsChecked == true); return c; }
    private ComboBox Select(string value, IEnumerable<(string Value, string Label)> options, Action<string> apply)
    {
        var c = new ComboBox { Padding = new Thickness(3, 1, 3, 1) };
        foreach (var (v, l) in options) c.Items.Add(new ComboBoxItem { Content = l, Tag = v, IsSelected = v == value });
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is ComboBoxItem it && it.Tag is string v && v != value) apply(v); };
        return c;
    }

    private void RenderForm()
    {
        Clear();
        var chart = _chart; var m = chart?.Model;
        if (chart is null || m is null) return;
        var sel = _selected;
        void Apply(bool ok) { if (ok) { chart.NotifyConfigChanged(); Refresh(); } }
        if (sel is null)
        {
            if (HideChartSettings) return;
            var c = m.Config;
            Heading("chart");
            Field("time span (s)", Number(c.TimeSpan, v => { if (v is > 0) { c.TimeSpan = v.Value; m.SetTimeSpan(v.Value); Apply(true); } }));
            Field("time format", Select(c.TimeFormat == TimeFormat.Utc ? "utc" : "relative", [("utc", "wall clock (UTC)"), ("relative", "seconds back")], v => { c.TimeFormat = v == "utc" ? TimeFormat.Utc : TimeFormat.Relative; Apply(true); }));
            Field("legend", Select(LegendName(c.Legend), Enum.GetValues<LegendPosition>().Select(l => (LegendName(l), LegendName(l))), v => { c.Legend = Enum.GetValues<LegendPosition>().First(l => LegendName(l) == v); Apply(true); }));
            Field("signal labels", Check(c.PlotLabels, v => { c.PlotLabels = v; Apply(true); }));
            Field("lane headers", Check(c.LaneHeaders, v => { c.LaneHeaders = v; Apply(true); }));
            Field("navigator", Check(c.Navigator, v => { c.Navigator = v; Apply(true); }));
            Field("measurements", Check(c.MeasurePanel, v => { c.MeasurePanel = v; Apply(true); }));
            Heading("look");
            var preset = c.Theme == ChartTheme.Dark ? "dark" : c.Theme == ChartTheme.Light ? "light" : "custom";
            var presets = new List<(string, string)> { ("light", "light"), ("dark", "dark") }; if (preset == "custom") presets.Add(("custom", "custom"));
            Field("theme", Select(preset, presets, v => { if (v == "light") c.Theme = ChartTheme.Light; else if (v == "dark") c.Theme = ChartTheme.Dark; else return; Apply(true); }));
            Field("signal width", Number(c.Style.SeriesWidth, v => { if (v is > 0) { c.Style = c.Style with { SeriesWidth = v.Value }; Apply(true); } }, "1.5"));
            Field("font size", Number(c.Theme.FontSize, v => { if (v is >= 6) { c.Theme = c.Theme with { FontSize = v.Value }; Apply(true); } }, "12"));
            Field("value grid", Check(c.Style.ShowValueGrid, v => { c.Style = c.Style with { ShowValueGrid = v }; Apply(true); }));
            Field("time grid", Check(c.Style.ShowTimeGrid, v => { c.Style = c.Style with { ShowTimeGrid = v }; Apply(true); }));
            return;
        }
        var (kind, id) = sel.Value;
        switch (kind)
        {
            case "lane":
                {
                    var lane = m.Lanes().FirstOrDefault(l => l.Id == id); if (lane is null) return;
                    Heading($"lane {lane.Id}");
                    Field("label", Text(lane.Label, v => Apply(m.UpdateLane(id, new LanePatch { Label = v })), lane.Id));
                    Field("weight", Number(lane.Weight, v => Apply(m.UpdateLane(id, new LanePatch { Weight = v })), "1"));
                    Field("folded", Check(lane.Collapsed, v => Apply(m.UpdateLane(id, new LanePatch { Collapsed = v }))));
                    Field("keep when empty", Check(lane.Keep, v => Apply(m.UpdateLane(id, new LanePatch { Keep = v }))));
                    return;
                }
            case "axis":
                {
                    var a = m.Axis(id);
                    Heading($"axis {a.Id}");
                    Field("label", Text(a.Label, v => Apply(m.UpdateAxis(id, new AxisPatch { Label = v })), a.Id));
                    Field("unit", Text(a.Unit, v => Apply(m.UpdateAxis(id, new AxisPatch { Unit = v }))));
                    Field("min", Number(a.Min, v => Apply(m.UpdateAxis(id, new AxisPatch { Min = v })), "auto"));
                    Field("max", Number(a.Max, v => Apply(m.UpdateAxis(id, new AxisPatch { Max = v })), "auto"));
                    Field("side", Select(a.Side == AxisSide.Right ? "right" : "left", [("left", "left"), ("right", "right")], v => Apply(m.UpdateAxis(id, new AxisPatch { Side = v == "right" ? AxisSide.Right : AxisSide.Left }))));
                    Field("colour", Text(a.Color, v => Apply(m.UpdateAxis(id, new AxisPatch { Color = v })), "CSS colour, empty = theme"));
                    return;
                }
            case "series":
                {
                    var s = m.Config.Series.FirstOrDefault(x => x.Id == id); if (s is null) return;
                    Heading($"signal {s.Id} (channel {s.ChannelId})");
                    Field("name", Text(s.Name, v => Apply(m.UpdateSeries(id, new SeriesPatch { Name = v })), m.Store.Get(s.ChannelId)?.Info.Name ?? s.Id));
                    Field("lane", Select(m.LaneIdOf(s), m.Lanes().Select(l => (l.Id, l.Label ?? l.Id)), v => Apply(m.UpdateSeries(id, new SeriesPatch { LaneId = v }))));
                    if (s.Kind != SeriesKind.Digital)
                    {
                        var def = $"axis:{m.LaneIdOf(s)}";
                        var axes = new List<(string, string)> { (def, "lane default") };
                        axes.AddRange(m.Config.Axes.Where(a => a.Id != def).Select(a => (a.Id, a.Label ?? a.Id)));
                        axes.Add(("__new__", "new axis…"));
                        Field("axis", Select(m.AxisIdOf(s), axes, v => { if (v == "__new__") { var nid = m.AddAxis(); m.UpdateSeries(id, new SeriesPatch { AxisId = nid }); _selected = ("axis", nid); Apply(true); } else Apply(m.UpdateSeries(id, new SeriesPatch { AxisId = v })); }));
                    }
                    Field("digital", Check(s.Kind == SeriesKind.Digital, v => Apply(m.UpdateSeries(id, new SeriesPatch { Kind = v ? SeriesKind.Digital : SeriesKind.Analog }))));
                    Field("visible", Check(s.Visible, v => Apply(m.UpdateSeries(id, new SeriesPatch { Visible = v }))));
                    Field("colour", Text(s.Color, v => Apply(m.UpdateSeries(id, new SeriesPatch { Color = v })), $"CSS colour, empty = palette ({m.SeriesColor(s)})"));
                    Field("width", Number(s.Width, v => Apply(m.UpdateSeries(id, new SeriesPatch { Width = v })), m.Config.Style.SeriesWidth.ToString(CultureInfo.InvariantCulture)));
                    return;
                }
            case "stack": Heading("logic stack"); Place(new TextBlock { Text = "digital signals of this lane; select one to edit it" }, 0, 2); return;
            case "threshold":
                {
                    var t = m.Config.Thresholds.FirstOrDefault(x => x.Id == id); if (t is null) return;
                    Heading($"threshold {t.Id}");
                    var axes = m.Lanes().Select(l => $"axis:{l.Id}").Concat(m.Config.Axes.Select(a => a.Id)).Distinct().ToList();
                    if (!axes.Contains(t.AxisId)) axes.Insert(0, t.AxisId);
                    Field("axis", Select(t.AxisId, axes.Select(a => (a, m.Axis(a).Label ?? a)), v => Apply(m.UpdateThreshold(id, new ThresholdPatch { AxisId = v }))));
                    Field("from", Number(t.From, v => Apply(m.UpdateThreshold(id, new ThresholdPatch { From = v }))));
                    Field("to", Number(t.To, v => Apply(m.UpdateThreshold(id, new ThresholdPatch { To = v })), "empty = line"));
                    Field("label", Text(t.Label, v => Apply(m.UpdateThreshold(id, new ThresholdPatch { Label = v }))));
                    Field("colour", Text(t.Color, v => Apply(m.UpdateThreshold(id, new ThresholdPatch { Color = v ?? "#dc2626" }))));
                    return;
                }
            case "marker":
                {
                    var mk = m.Config.Markers.FirstOrDefault(x => x.Id == id); if (mk is null) return;
                    Heading($"marker {mk.Id}");
                    Field("time (s)", Number(mk.Time, v => Apply(m.UpdateMarker(id, new MarkerPatch { Time = v }))));
                    Field("label", Text(mk.Label, v => Apply(m.UpdateMarker(id, new MarkerPatch { Label = v }))));
                    Field("colour", Text(mk.Color, v => Apply(m.UpdateMarker(id, new MarkerPatch { Color = v })), "CSS colour, empty = theme"));
                    return;
                }
        }
    }

    /// <summary>A chooser of store channels not yet in the chart; the pick lands in the selected lane (own axis, or the logic stack for digital).</summary>
    private void PickChannel()
    {
        var chart = _chart; var m = chart?.Model;
        if (chart is null || m is null) return;
        var rows = m.EditorRows();
        string? LaneOf(string? rid) { if (rid is null) return null; var r = rows.FirstOrDefault(x => x.Id == rid); return r is null ? null : r.Kind == "lane" ? r.Id : LaneOf(r.ParentId); }
        var laneId = _selected is { } s ? (s.Kind == "lane" ? s.Id : LaneOf(s.Id)) : null;
        laneId ??= m.Lanes()[0].Id;
        var inChart = m.Config.Series.Select(x => x.ChannelId).ToHashSet();
        List<ChannelInfo> options;
        lock (chart.Store.SyncRoot) options = chart.Store.Channels.Values.Select(c => c.Info).Where(i => !inChart.Contains(i.Id)).OrderBy(i => i.Name, StringComparer.Ordinal).ToList();
        Clear();
        Heading($"add a signal to {laneId}");
        var combo = new ComboBox { Padding = new Thickness(3, 1, 3, 1) };
        foreach (var i in options) combo.Items.Add(new ComboBoxItem { Content = $"{i.Name}{(i.Unit is not null ? $" [{i.Unit}]" : "")}{(i.Kind == ChannelKind.Digital ? " (digital)" : "")}", Tag = i.Id });
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        Field("channel", combo);
        var add = new Button { Content = "add", Padding = new Thickness(6, 1, 6, 1), IsEnabled = options.Count > 0, HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => { if (combo.SelectedItem is ComboBoxItem it && it.Tag is int ch) { var ids = chart.AddChannels([ch], new DropTarget.OwnAxis(laneId)); if (ids.Count > 0) _selected = ("series", ids[0]); Refresh(); } };
        var cancel = new Button { Content = "cancel", Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Left };
        cancel.Click += (_, _) => Refresh();
        Field("", add); Field("", cancel);
    }

    private static string LegendName(LegendPosition l) => l switch { LegendPosition.TopLeft => "top-left", LegendPosition.TopRight => "top-right", LegendPosition.BottomLeft => "bottom-left", LegendPosition.BottomRight => "bottom-right", LegendPosition.Right => "right", LegendPosition.Top => "top", _ => "none" };

    private static DataTemplate RowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(DockPanel));
        panel.SetBinding(MarginProperty, new System.Windows.Data.Binding(nameof(Item.Indent)));
        panel.SetValue(LastChildFillProperty, true);
        var muted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        FrameworkElementFactory T(string path, Brush? brush = null, Dock? dock = null, double? width = null)
        {
            var t = new FrameworkElementFactory(typeof(TextBlock));
            t.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
            t.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            t.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            if (brush is not null) t.SetValue(TextBlock.ForegroundProperty, brush);
            if (dock is { } d) t.SetValue(DockProperty, d);
            if (width is { } w) t.SetValue(WidthProperty, w);
            return t;
        }
        panel.AppendChild(T(nameof(Item.Glyph), muted, Dock.Left, 14));
        var detail = T(nameof(Item.Detail), muted, Dock.Right); detail.SetValue(TextBlock.FontSizeProperty, 11.0); detail.SetValue(TextBlock.MaxWidthProperty, 140.0);
        panel.AppendChild(detail);
        var label = T(nameof(Item.Label)); label.SetBinding(TextBlock.FontWeightProperty, new System.Windows.Data.Binding(nameof(Item.Weight)));
        panel.AppendChild(label);
        return new DataTemplate { VisualTree = panel };
    }
}
