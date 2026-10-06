// Mori.SkyScope — The TrendChart on WPF: the same headless model as the web, painted with SkiaSharp on a 30 fps dispatcher timer.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
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
    private bool _externalDrag;

    /// <summary>Creates the control with a fresh store, model and live clock, wires the input handlers and runs the 30 fps repaint timer while loaded.</summary>
    public TrendChartControl()
    {
        Focusable = true;
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

    /// <summary>Replace the configuration (lanes, series, theme, style…). The store and time state are kept.</summary>
    public void Configure(TrendChartConfig config) { Model = new TrendChartModel(Store, config) { Now = Model.Now }; _layout = null; InvalidateVisual(); }
    /// <summary>Replace the store (e.g. one with a long retention for playback) and rebuild the model on it. Call before subscribing listeners.</summary>
    public void SetStore(SignalStore store) { Store = store; Model = new TrendChartModel(Store, Model.Config) { Now = Model.Now }; _layout = null; InvalidateVisual(); }

    // ---- drags that start outside the chart (signal tree) ---------------------
    /// <summary>Start dragging channels from elsewhere (a signal tree); follow with <see cref="ExternalDragMove"/> and <see cref="ExternalDrop"/> in screen coordinates.</summary>
    public void ExternalDragStart(IReadOnlyList<int> channelIds, bool group = false) { _externalDrag = true; Model.BeginDrag([], -1000, -1000, group, channelIds); InvalidateVisual(); }
    /// <summary>Move an external drag to a screen position; updates the drop-target highlight.</summary>
    public void ExternalDragMove(Point screen) { if (!_externalDrag) return; var p = PointFromScreen(screen); Model.UpdateDrag(CurrentLayout(), p.X, p.Y); InvalidateVisual(); }
    /// <summary>Finish an external drag at a screen position. Returns true when the drop changed the configuration.</summary>
    public bool ExternalDrop(Point screen)
    {
        if (!_externalDrag) return false;
        _externalDrag = false;
        var p = PointFromScreen(screen);
        var changed = Model.EndDrag(CurrentLayout(), p.X, p.Y);
        if (changed) Changed(); else InvalidateVisual();
        return changed;
    }
    /// <summary>Abort an external drag without changing the chart.</summary>
    public void ExternalDragCancel() { _externalDrag = false; Model.CancelDrag(); InvalidateVisual(); }

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
        _ => null,
    };

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus(); var p = e.GetPosition(this);
        var layout = CurrentLayout(); var hit = Model.HitTest(layout, p.X, p.Y); var group = GroupKey();
        if (e.ClickCount == 2)
        {
            if (hit is HitRegion.Axis ax) { Model.AxisAutoscale(ax.AxisId); Changed(); return; }
            if (hit is HitRegion.Navigator or HitRegion.Header or HitRegion.Label) return;
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
                    Model.BeginDrag(ids, p.X, p.Y, group); InvalidateVisual(); return;
                }
                case HitRegion.Header h:
                    if (h.Part == HeaderPart.Collapse) { var lane = Model.Lanes().First(l => l.Id == h.LaneId); Model.SetLaneCollapsed(h.LaneId, !lane.Collapsed); Changed(); return; }
                    if (h.Part == HeaderPart.Remove) { Model.RemoveLane(h.LaneId); Changed(); return; }
                    Model.BeginLaneDrag(h.LaneId, p.X, p.Y); return;
                case HitRegion.Axis a: Model.BeginAxisDrag(layout, a.AxisId, a.LaneId, a.Zone, p.Y); return;
                case HitRegion.Navigator: Model.BeginNavigatorDrag(layout, p.X); InvalidateVisual(); return;
            }
            if (!group) Selection.Clear();
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
        Cursor = CursorFor(Model.HitTest(CurrentLayout(), p.X, p.Y));
        Dispatch(new InputEvent.PointerMove(p.X, p.Y, Mods()));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        ReleaseMouseCapture(); var p = e.GetPosition(this);
        if (Model.Drag is not null) { if (Model.EndDrag(CurrentLayout(), p.X, p.Y)) Changed(); else InvalidateVisual(); return; }
        if (Model.LaneDrag is not null) { if (Model.EndLaneDrag(CurrentLayout(), p.X, p.Y)) Changed(); else InvalidateVisual(); return; }
        if (Model.AxisDrag is not null) { Model.EndAxisDrag(); ConfigChanged?.Invoke(); return; }
        if (Model.NavDrag is not null) { Model.EndNavigatorDrag(); return; }
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
