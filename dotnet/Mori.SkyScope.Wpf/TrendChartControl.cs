// Mori.SkyScope — The TrendChart on WPF: the same headless model as the web, painted with SkiaSharp on a 30 fps dispatcher timer.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// The TrendChart on WPF: the same headless model as the web, painted with SkiaSharp on a 30 fps dispatcher timer.
/// Mouse/wheel/keys go through the shared interaction reducer. Bind a <see cref="SignalStore"/> and feed it from any source
/// (use <c>WebSocketSourceConfig.Dispatch = Dispatcher.Invoke</c> so frames arrive on the UI thread).
/// Signals move ibaAnalyzer-style: pick a series up by its in-plot label or legend row and drop it onto an axis (shared scale),
/// into a lane (own scale) or onto the time axis (new lane); Ctrl/Shift while picking up builds a group. Lane headers reorder,
/// fold and remove lanes; Y-axes shift (middle) and stretch (ends) by dragging, zoom with the wheel, autoscale on double-click;
/// the navigator frame moves, resizes, jumps on click and steps with the arrow keys.
/// </summary>
public class TrendChartControl : SkiaElement
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
    private InteractionState _interaction = InteractionState.Initial(Tool.Pan);
    private TrendLayout? _layout;
    private bool _nativeDrag;

    /// <summary>Creates the control with a fresh store, model and live clock, wires the input handlers and runs the 30 fps repaint timer while loaded.</summary>
    public TrendChartControl()
    {
        Focusable = true;
        AllowDrop = true;
        Store = new SignalStore();
        Model = new TrendChartModel(Store);
        Clock = new LiveClock();
        _timer.Tick += (_, _) => InvalidateVisual();
        PaintSurface += OnPaint;
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
        KeyUp += (s, e) => Dispatch(new InputEvent.KeyUp(e.Key == Key.Space ? " " : e.Key.ToString()));
        DragEnter += OnDragOverChart;
        DragOver += OnDragOverChart;
        DragLeave += (_, _) => { if (_nativeDrag) { _nativeDrag = false; Model.CancelDrag(); InvalidateVisual(); } };
        Drop += OnDropChart;
    }

    /// <summary>The store the model reads; feed it from a source, or swap it with <see cref="SetStore"/>.</summary>
    public SignalStore Store { get; private set; }
    /// <summary>The headless chart model: configuration, time state, drags and layout.</summary>
    public TrendChartModel Model { get; private set; }
    /// <summary>Supplies the current time for each frame: a <see cref="LiveClock"/> by default, a playback clock when reviewing a recording.</summary>
    public ITimeSource Clock { get; set; }
    /// <summary>The active pointer tool (pan, box zoom, cursor, select); setting it resets the interaction state.</summary>
    public Tool Tool { get => _interaction.Tool; set => _interaction = InteractionState.Initial(value); }
    /// <summary>Series picked up together with the next one (Ctrl/Shift click on labels or legend rows).</summary>
    public HashSet<string> Selection { get; } = [];
    /// <summary>Raised when a gesture changed the configuration (series moved, lanes reordered, axis ranges changed).</summary>
    public event Action? ConfigChanged;
    /// <summary>
    /// Raised when channels are dropped on the chart (WPF drag and drop with <see cref="ChannelDragData.Format"/>, or plain text
    /// ids), before they are added: cancel, redirect or handle the drop. Otherwise the chart calls <see cref="AddChannels"/>.
    /// </summary>
    public event EventHandler<ChannelDropEventArgs>? ChannelDrop;

    /// <summary>Replace the configuration (lanes, series, theme, style…). The store and time state are kept.</summary>
    public void Configure(TrendChartConfig config) { Model = new TrendChartModel(Store, config) { Now = Model.Now }; _layout = null; InvalidateVisual(); }
    /// <summary>Replace the store (e.g. one with a long retention for playback) and rebuild the model on it. Call before subscribing listeners.</summary>
    public void SetStore(SignalStore store) { Store = store; Model = new TrendChartModel(Store, Model.Config) { Now = Model.Now }; _layout = null; InvalidateVisual(); }

    // ---- adding channels: from code or from a native drag and drop --------------------
    /// <summary>
    /// Add channels from code: a series per channel not in the chart yet, placed at <paramref name="target"/> with the drop
    /// rules (default: the first lane, analog on an own axis, digital in the logic stack). Returns the series ids and raises
    /// <see cref="ConfigChanged"/>.
    /// </summary>
    public IReadOnlyList<string> AddChannels(IReadOnlyList<int> channelIds, DropTarget? target = null, bool group = false)
    {
        var ids = Model.AddChannels(channelIds, target, group);
        if (ids.Count > 0) Changed();
        return ids;
    }
    /// <summary>Where a payload would land if dropped at a screen position (for a custom preview); <see cref="DropTarget.None"/> outside the plot.</summary>
    public DropTarget DropTargetAt(Point screen, bool digital = false) { var p = PointFromScreen(screen); return Model.DropTargetAt(CurrentLayout(), p.X, p.Y, digital); }

    private static ChannelDragPayload? ReadPayload(IDataObject data)
    {
        if (data.GetDataPresent(ChannelDragData.Format) && ChannelDragData.TryParse(data.GetData(ChannelDragData.Format) as string, out var p)) return p;
        if (data.GetDataPresent(DataFormats.UnicodeText) && ChannelDragData.TryParse(data.GetData(DataFormats.UnicodeText) as string, out p)) return p;
        if (data.GetDataPresent(DataFormats.Text) && ChannelDragData.TryParse(data.GetData(DataFormats.Text) as string, out p)) return p;
        return null;
    }

    private void OnDragOverChart(object sender, DragEventArgs e)
    {
        var payload = ReadPayload(e.Data);
        if (payload is null) { e.Effects = DragDropEffects.None; e.Handled = true; return; }
        var p = e.GetPosition(this);
        if (!_nativeDrag || Model.Drag is null) { _nativeDrag = true; Model.BeginDrag([], p.X, p.Y, payload.Group, payload.Ids, ChannelDragData.IsDigital(payload, Store)); }
        Model.UpdateDrag(CurrentLayout(), p.X, p.Y);
        e.Effects = Model.Drag?.Target is DropTarget.None ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
        InvalidateVisual();
    }

    private void OnDropChart(object sender, DragEventArgs e)
    {
        _nativeDrag = false; Model.CancelDrag();
        var payload = ReadPayload(e.Data);
        if (payload is null) { InvalidateVisual(); return; }
        e.Handled = true;
        var p = e.GetPosition(this);
        var args = new ChannelDropEventArgs(payload, Model.DropTargetAt(CurrentLayout(), p.X, p.Y, ChannelDragData.IsDigital(payload, Store)), p.X, p.Y);
        ChannelDrop?.Invoke(this, args);
        if (args.Cancel) { InvalidateVisual(); return; }
        if (args.Handled) { Changed(); return; }
        if (args.Target is DropTarget.None) { InvalidateVisual(); return; }
        AddChannels(args.ChannelIds, args.Target, args.Group);
    }

    // ---- layout files ----------------------------------------------------------
    /// <summary>The arrangement as a JSON layout file (theme and style excluded).</summary>
    public string ExportLayout() => Model.ExportLayout();
    /// <summary>Replace the arrangement with a layout file; theme and style are kept.</summary>
    public void ImportLayout(string json) { Model.ImportLayout(json); Selection.Clear(); Changed(); }
    /// <summary>Write the layout file to <paramref name="path"/>.</summary>
    public void SaveLayout(string path) => System.IO.File.WriteAllText(path, ExportLayout());
    /// <summary>Load a layout file from <paramref name="path"/>.</summary>
    public void LoadLayout(string path) => ImportLayout(System.IO.File.ReadAllText(path));

    private Point _pressAt;
    private string? _pressedSeries;

    /// <summary>The series menu: show or hide, rename, colour, line width, remove.</summary>
    private ContextMenu BuildSeriesMenu(string seriesId)
    {
        var s = Model.Config.Series.First(x => x.Id == seriesId);
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = Model.SeriesName(s), IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        var toggle = new MenuItem { Header = s.Visible ? "Hide" : "Show" };
        toggle.Click += (_, _) => { Model.ToggleSeries(seriesId); Changed(); };
        menu.Items.Add(toggle);
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) =>
        {
            var box = new TextBox { Text = s.Name ?? "", MinWidth = 160 };
            var popup = new Window { Title = "Rename signal", Content = box, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize };
            box.KeyDown += (_, k) => { if (k.Key == Key.Enter) popup.DialogResult = true; if (k.Key == Key.Escape) popup.DialogResult = false; };
            if (popup.ShowDialog() == true) { Model.RenameSeries(seriesId, box.Text); Changed(); }
        };
        menu.Items.Add(rename);
        var colours = new MenuItem { Header = "Colour" };
        foreach (var c in TrendChartConfig.SeriesPalette)
        {
            var item = new MenuItem { Header = c, IsChecked = string.Equals(Model.SeriesColor(s), c, StringComparison.OrdinalIgnoreCase) };
            item.Icon = new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(c)! };
            item.Click += (_, _) => { Model.SetSeriesColor(seriesId, c); Changed(); };
            colours.Items.Add(item);
        }
        var reset = new MenuItem { Header = "Palette default" };
        reset.Click += (_, _) => { Model.SetSeriesColor(seriesId, null); Changed(); };
        colours.Items.Add(new Separator()); colours.Items.Add(reset);
        menu.Items.Add(colours);
        var widths = new MenuItem { Header = "Line width" };
        foreach (var w in new[] { 1.0, 1.5, 2, 3 })
        {
            var item = new MenuItem { Header = w.ToString(System.Globalization.CultureInfo.InvariantCulture), IsChecked = (s.Width ?? Model.Config.Style.SeriesWidth) == w };
            item.Click += (_, _) => { Model.SetSeriesWidth(seriesId, w); Changed(); };
            widths.Items.Add(item);
        }
        menu.Items.Add(widths);
        menu.Items.Add(new Separator());
        var remove = new MenuItem { Header = "Remove from chart" };
        remove.Click += (_, _) => { Model.RemoveSeries(seriesId); Changed(); };
        menu.Items.Add(remove);
        return menu;
    }

    private static int Button(MouseButton b) => b switch { MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 };
    private static Modifiers Mods() => new(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    private static bool GroupKey() => Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
    private TrendLayout CurrentLayout() => _layout ??= Model.Layout(ActualWidth, ActualHeight);
    private void Changed() { _layout = null; ConfigChanged?.Invoke(); InvalidateVisual(); }

    private static Cursor? CursorFor(HitRegion hit) => hit switch
    {
        HitRegion.Label or HitRegion.LegendRow => Cursors.Hand,
        HitRegion.Header h => h.Part == HeaderPart.Grip ? Cursors.SizeAll : Cursors.Hand,
        HitRegion.Axis => Cursors.SizeNS,
        HitRegion.Navigator n => n.Zone is NavigatorZone.LeftEdge or NavigatorZone.RightEdge ? Cursors.SizeWE : Cursors.Hand,
        HitRegion.LaneGap => Cursors.SizeNS,
        HitRegion.Cursor => Cursors.SizeWE,
        _ => null,
    };

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus(); var p = e.GetPosition(this);
        var layout = CurrentLayout(); var hit = Model.HitTest(layout, p.X, p.Y); var group = GroupKey();
        if (e.ClickCount == 2)
        {
            if (hit is HitRegion.Axis ax) { Model.AxisAutoscale(ax.AxisId); Changed(); return; }
            if (hit is HitRegion.Navigator or HitRegion.Header or HitRegion.Label or HitRegion.LaneGap or HitRegion.Cursor or HitRegion.Measure) return;
            Dispatch(new InputEvent.DoubleClick(p.X, p.Y)); return;
        }
        CaptureMouse();
        if (e.ChangedButton == MouseButton.Left)
        {
            switch (hit)
            {
                case HitRegion.Label or HitRegion.LegendRow:
                {
                    var id = hit is HitRegion.Label l ? l.SeriesId : ((HitRegion.LegendRow)hit).SeriesId;
                    if (group) Selection.Add(id);
                    var ids = Selection.Contains(id) ? Selection.ToList() : [id];
                    _pressAt = p; _pressedSeries = group ? null : id;
                    Model.BeginDrag(ids, p.X, p.Y, group); InvalidateVisual(); return;
                }
                case HitRegion.LaneGap gap: Model.BeginLaneResize(layout, gap.AboveLaneId, gap.BelowLaneId, p.Y); return;
                case HitRegion.Cursor cu: Model.BeginCursorDrag(cu.Which); return;
                case HitRegion.Measure: return;
                case HitRegion.Header h:
                    if (h.Part == HeaderPart.Collapse) { var lane = Model.Lanes().First(l => l.Id == h.LaneId); Model.SetLaneCollapsed(h.LaneId, !lane.Collapsed); Changed(); return; }
                    if (h.Part == HeaderPart.Remove) { Model.RemoveLane(h.LaneId); Changed(); return; }
                    Model.BeginLaneDrag(h.LaneId, p.X, p.Y); return;
                case HitRegion.Axis a: Model.BeginAxisDrag(layout, a.AxisId, a.LaneId, a.Zone, p.Y); return;
                case HitRegion.Navigator: Model.BeginNavigatorDrag(layout, p.X); InvalidateVisual(); return;
            }
            if (!group) Selection.Clear();
        }
        if (e.ChangedButton == MouseButton.Right && hit is HitRegion.Label or HitRegion.LegendRow)
        {
            ReleaseMouseCapture();
            var id = hit is HitRegion.Label l ? l.SeriesId : ((HitRegion.LegendRow)hit).SeriesId;
            var menu = BuildSeriesMenu(id); menu.PlacementTarget = this; menu.IsOpen = true; return;
        }
        Dispatch(new InputEvent.PointerDown(p.X, p.Y, Button(e.ChangedButton), Mods()));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (Model.Drag is not null) { Model.UpdateDrag(CurrentLayout(), p.X, p.Y); InvalidateVisual(); return; }
        if (Model.LaneDrag is not null) { Model.UpdateLaneDrag(CurrentLayout(), p.X, p.Y); InvalidateVisual(); return; }
        if (Model.AxisDrag is not null) { Model.UpdateAxisDrag(CurrentLayout(), p.Y); InvalidateVisual(); return; }
        if (Model.NavDrag is not null) { Model.UpdateNavigatorDrag(CurrentLayout(), p.X); InvalidateVisual(); return; }
        if (Model.LaneResize is not null) { if (Model.UpdateLaneResize(CurrentLayout(), p.Y)) _layout = null; InvalidateVisual(); return; }
        if (Model.CursorDrag is not null) { Model.UpdateCursorDrag(CurrentLayout(), p.X); _layout = null; InvalidateVisual(); return; }
        Cursor = CursorFor(Model.HitTest(CurrentLayout(), p.X, p.Y));
        Dispatch(new InputEvent.PointerMove(p.X, p.Y, Mods()));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture(); var p = e.GetPosition(this);
        if (Model.Drag is not null)
        {
            // a press without movement on a label or legend row is a click: hide or show that signal
            var pressed = _pressedSeries; _pressedSeries = null;
            if (pressed is not null && Model.Drag.ChannelIds.Count == 0 && Math.Abs(p.X - _pressAt.X) < 4 && Math.Abs(p.Y - _pressAt.Y) < 4) { Model.CancelDrag(); Model.ToggleSeries(pressed); Changed(); return; }
            if (Model.EndDrag(CurrentLayout(), p.X, p.Y)) Changed(); else InvalidateVisual();
            return;
        }
        if (Model.LaneDrag is not null) { if (Model.EndLaneDrag(CurrentLayout(), p.X, p.Y)) Changed(); else InvalidateVisual(); return; }
        if (Model.AxisDrag is not null) { Model.EndAxisDrag(); ConfigChanged?.Invoke(); return; }
        if (Model.NavDrag is not null) { Model.EndNavigatorDrag(); return; }
        if (Model.LaneResize is not null) { Model.EndLaneResize(); Changed(); return; }
        if (Model.CursorDrag is not null) { Model.EndCursorDrag(); return; }
        Dispatch(new InputEvent.PointerUp(p.X, p.Y, Button(e.ChangedButton), Mods()));
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(this); var layout = CurrentLayout();
        var factor = Math.Pow(2, e.Delta / 400.0);
        switch (Model.HitTest(layout, p.X, p.Y))
        {
            case HitRegion.Axis a: Model.AxisZoomAt(layout, a.AxisId, a.LaneId, p.Y, factor); InvalidateVisual(); return;
            case HitRegion.Navigator: Model.ApplyEffect(new Effect.Zoom(layout.Plot.X + layout.Plot.W, p.Y, factor), layout); InvalidateVisual(); return;
        }
        Dispatch(new InputEvent.Wheel(p.X, p.Y, -e.Delta, Mods()));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Model.CancelDrag(); Model.CancelLaneDrag(); Selection.Clear(); InvalidateVisual(); }
        if (e.Key is Key.Left or Key.Right && Model.Config.Navigator) { Model.NavigatorKey(e.Key == Key.Left ? "ArrowLeft" : "ArrowRight"); e.Handled = true; InvalidateVisual(); return; }
        Dispatch(new InputEvent.KeyDown(e.Key == Key.Space ? " " : e.Key.ToString()));
    }

    private void Dispatch(InputEvent ev)
    {
        var layout = CurrentLayout();
        var (state, effects) = Interaction.Reduce(_interaction, ev);
        _interaction = state;
        foreach (var e in effects) Model.ApplyEffect(e, layout);
        InvalidateVisual();
    }

    private void OnPaint(object? sender, SkiaPaintEventArgs e)
    {
        var canvas = e.Canvas;
        var scale = e.Scale;
        lock (Store.SyncRoot)
        {
            Model.Now = Clock.Now();
            _layout = Model.Layout(ActualWidth, ActualHeight);
            Model.Update(_layout);
            using var painter = new SkiaPainter(canvas, ActualWidth, ActualHeight, scale);
            TrendChartRenderer.Draw(Model, painter, _layout);
        }
    }
}
