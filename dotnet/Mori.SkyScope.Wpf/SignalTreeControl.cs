// Mori.SkyScope — WPF signal tree: a searchable list of channels grouped by name prefix, dragged onto a trend chart control.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// A searchable tree of every channel in a store (grouped by name prefix). Rows are ordinary WPF drag sources
/// (<see cref="DragDrop.DoDragDrop"/> with a <see cref="ChannelDragData.Format"/> data object plus plain-text ids), so any
/// <see cref="TrendChartControl"/> accepts them and the drop obeys the chart's rules (axis strip → shared scale, lane →
/// own scale, time axis → new lane, logic stack for digital). The list's own Ctrl/Shift selection picks several; a drag
/// on a selected row carries the selection as a group. Double-click on a group folds it; double-click on a channel adds
/// it to the attached chart's first lane. The chart is optional: without it the tree lists <see cref="Store"/> and drags.
/// </summary>
public class SignalTreeControl : DockPanel
{
    /// <summary>A list item bound by the row template: wraps a <see cref="SignalTreeRow"/> and exposes the display values the template reads.</summary>
    public sealed class Row(SignalTreeRow row, bool inChart)
    {
        /// <summary>The underlying model row.</summary>
        public SignalTreeRow Source { get; } = row;
        /// <summary>Row identifier from the model (a kind prefix followed by the group path or channel id).</summary>
        public string Id => Source.Id;
        /// <summary>True for a group header, false for a channel.</summary>
        public bool IsGroup => Source.Kind == SignalTreeRowKind.Group;
        /// <summary>Expand/collapse glyph for groups; empty for channels.</summary>
        public string Caret => IsGroup ? (Source.Expanded == true ? "▾" : "▸") : "";
        /// <summary>Display name: the leaf part of a channel name or the prefix of a group.</summary>
        public string Name => Source.Name;
        /// <summary>Member count for groups, unit for channels.</summary>
        public string Detail => IsGroup ? Source.Count?.ToString() ?? "" : Source.Unit ?? "";
        /// <summary>A dot when the channel is already plotted on the chart; empty otherwise.</summary>
        public string InChart => inChart ? "●" : "";
        /// <summary>Left margin derived from the row's depth in the tree.</summary>
        public Thickness Indent => new(6 + Source.Depth * 14, 0, 0, 0);
        /// <summary>Semi-bold for groups, normal for channels.</summary>
        public FontWeight Weight => IsGroup ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(4, 2, 4, 2) };
    private readonly ListBox _list = new() { SelectionMode = SelectionMode.Extended, BorderThickness = new Thickness(1) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private SignalTreeModel? _model;
    private SignalStore? _store;
    private string _signature = "";
    private Point _pressAt;
    private Row? _pressed;
    private bool _dragging;

    /// <summary>Builds the search box and the list, wires drag, double-click and Escape handling, and polls the store for new channels while the control is loaded.</summary>
    public SignalTreeControl()
    {
        SetDock(_search, Dock.Top);
        Children.Add(_search);
        Children.Add(_list);
        _list.ItemTemplate = RowTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _search.TextChanged += (_, _) => { if (_model is not null) { _model.SetQuery(_search.Text); Refresh(); } };
        _list.PreviewMouseLeftButtonDown += OnPress;
        _list.PreviewMouseMove += OnMove;
        _list.PreviewMouseLeftButtonUp += OnRelease;
        _list.MouseDoubleClick += OnDoubleClick;
        _list.KeyDown += (_, e) => { if (e.Key == Key.Escape) _list.UnselectAll(); };
        _timer.Tick += (_, _) => { if (Signature() != _signature) Refresh(); };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>Optional: a chart whose series are marked in the tree and that double-click adds to; its store is listed unless <see cref="Store"/> is set. Drops work on any chart without it.</summary>
    public TrendChartControl? Chart { get; set; }
    /// <summary>Store to list; defaults to the chart's.</summary>
    public SignalStore? Store { get => _store; set { _store = value; _model = null; Refresh(); } }
    /// <summary>Characters that split a channel name into group and leaf.</summary>
    public string Separators { get; set; } = "/.:";
    /// <summary>The headless tree model behind the list; null until a store is available.</summary>
    public SignalTreeModel? Model => _model;

    private SignalTreeModel? EnsureModel()
    {
        var store = _store ?? Chart?.Store;
        if (store is null) return null;
        if (_model is null || _model.Store != store) { _model = new SignalTreeModel(store, Separators); _model.SetQuery(_search.Text); }
        return _model;
    }

    private string Signature()
    {
        var store = _store ?? Chart?.Store;
        return $"{store?.Channels.Count ?? 0}:{string.Join(",", Chart?.Model.Config.Series.Select(s => s.ChannelId) ?? [])}";
    }

    /// <summary>Rebuild the list from the model: rows, in-chart marks and the selection.</summary>
    public void Refresh()
    {
        var m = EnsureModel();
        _signature = Signature();
        if (m is null) { _list.ItemsSource = null; return; }
        var inChart = new HashSet<int>(Chart?.Model.Config.Series.Select(s => s.ChannelId) ?? []);
        List<SignalTreeRow> rows;
        lock (m.Store.SyncRoot) rows = m.Rows();
        var items = rows.Select(r => new Row(r, r.ChannelId is { } id && inChart.Contains(id))).ToList();
        _list.SelectionChanged -= OnSelectionChanged;
        _list.ItemsSource = items;
        foreach (var it in items) if (it.Source.Selected) _list.SelectedItems.Add(it);
        _list.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => SyncSelection();
    private void SyncSelection()
    {
        if (_model is null) return;
        _model.Selection.Clear();
        foreach (var it in _list.SelectedItems.OfType<Row>()) if (it.Source.ChannelId is { } id) _model.Selection.Add(id);
    }

    private Row? RowAt(MouseEventArgs e)
    {
        var d = e.OriginalSource as DependencyObject;
        while (d is not null and not ListBoxItem) d = VisualTreeHelper.GetParent(d);
        return (d as ListBoxItem)?.DataContext as Row;
    }

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        _pressed = RowAt(e); _pressAt = e.GetPosition(_list); _dragging = false;
        if (_pressed?.IsGroup == true && Keyboard.Modifiers == ModifierKeys.None) { _model?.ToggleGroup(_pressed.Id[2..]); Refresh(); e.Handled = true; }
    }

    /// <summary>The payload a drag of <paramref name="channelId"/> carries: the selection when the row is selected, the row alone otherwise.</summary>
    public ChannelDragPayload DragPayload(int channelId, bool group = false)
    {
        var m = EnsureModel();
        var ids = m?.DragIds(channelId) ?? [channelId];
        var store = _store ?? Chart?.Store;
        return new ChannelDragPayload(ids.Select(id => { var info = store?.Get(id)?.Info; return new ChannelDragItem(id, info?.Name, info?.Unit, info?.Kind == ChannelKind.Digital ? ChannelKind.Digital : ChannelKind.Analog); }).ToList(), group || ids.Count > 1);
    }

    /// <summary>The data object a drag carries: the payload under <see cref="ChannelDragData.Format"/> and the ids as text.</summary>
    public static DataObject ToDataObject(ChannelDragPayload payload)
    {
        var data = new DataObject();
        data.SetData(ChannelDragData.Format, ChannelDragData.Encode(payload));
        data.SetData(DataFormats.UnicodeText, string.Join(",", payload.Ids));
        return data;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_pressed is null || e.LeftButton != MouseButtonState.Pressed || _dragging) return;
        if (_pressed.IsGroup || _pressed.Source.ChannelId is not { } id) return;
        var p = e.GetPosition(_list);
        if (Math.Abs(p.X - _pressAt.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(p.Y - _pressAt.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        SyncSelection();
        var payload = DragPayload(id, Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        _dragging = true;
        try { DragDrop.DoDragDrop(_list, ToDataObject(payload), DragDropEffects.Copy); }
        finally { _dragging = false; _pressed = null; Refresh(); }
    }

    private void OnRelease(object sender, MouseButtonEventArgs e) => _pressed = null;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var row = RowAt(e);
        if (row is null || row.IsGroup || Chart is null || row.Source.ChannelId is not { } id) return;
        Chart.AddChannels([id]);
        Refresh();
    }

    private static DataTemplate RowTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetBinding(MarginProperty, new Binding(nameof(Row.Indent)));
        FrameworkElementFactory Text(string path, double width = double.NaN, Brush? brush = null)
        {
            var t = new FrameworkElementFactory(typeof(TextBlock));
            t.SetBinding(TextBlock.TextProperty, new Binding(path));
            t.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            if (!double.IsNaN(width)) t.SetValue(WidthProperty, width);
            if (brush is not null) t.SetValue(TextBlock.ForegroundProperty, brush);
            return t;
        }
        var muted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
        panel.AppendChild(Text(nameof(Row.Caret), 10, muted));
        var name = Text(nameof(Row.Name));
        name.SetBinding(TextBlock.FontWeightProperty, new Binding(nameof(Row.Weight)));
        panel.AppendChild(name);
        panel.AppendChild(Text(nameof(Row.Detail), double.NaN, muted));
        panel.AppendChild(Text(nameof(Row.InChart), double.NaN, new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB))));
        return new DataTemplate { VisualTree = panel };
    }
}
