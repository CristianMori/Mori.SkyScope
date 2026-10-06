// Mori.SkyScope — The TrendChart on Windows Forms: designer-editable configuration, CPU or OpenGL painting, and the same gestures as the web and WPF hosts.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Render.Skia;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// The TrendChart on Windows Forms: the same headless model as the web and WPF hosts, painted with SkiaSharp (CPU bitmap
/// or OpenGL, see <see cref="SkiaHostControl.Rendering"/>) on a 30 fps timer. Lanes, axes, series, thresholds and markers
/// can be laid out in the designer through the collection properties; <see cref="Configure"/> replaces them from code.
/// Signals move ibaAnalyzer-style (labels and legend rows are drag handles, Ctrl/Shift groups), lane headers reorder, fold
/// and remove lanes, lane gaps resize them, Y-axes shift, stretch, zoom and autoscale, cursors drag with a measurement
/// table, a right-click on a signal opens its menu, and the arrangement saves to and loads from a layout file.
/// </summary>
[ToolboxItem(true), DefaultProperty(nameof(Series)), Description("Real-time strip chart over a SignalStore.")]
public class TrendChartControl : SkiaHostControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private InteractionState _interaction = InteractionState.Initial(Tool.Pan);
    private TrendLayout? _layout;
    private bool _nativeDrag;
    private bool _configured;
    private Point _pressAt;
    private string? _pressedSeries;

    /// <summary>Creates the control with an empty store and a default configuration; the designer collections apply on first paint unless <see cref="Configure"/> was called.</summary>
    public TrendChartControl()
    {
        Store = new SignalStore();
        Model = new TrendChartModel(Store);
        Clock = new LiveClock();
        AllowDrop = true;
        _timer.Tick += (_, _) => { if (!DesignMode) Redraw(); };
    }

    // ---- designer properties --------------------------------------------------
    /// <summary>Lanes (stacked strips) in top-to-bottom order; empty = one lane.</summary>
    [Category("Chart"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content), Description("Lanes (stacked strips) in top-to-bottom order; empty = one lane.")]
    public DefinitionCollection<LaneDefinition> Lanes { get; } = [];
    /// <summary>Axes with labels, units, fixed bounds and side.</summary>
    [Category("Chart"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content), Description("Axes with labels, units, fixed bounds and side.")]
    public DefinitionCollection<AxisDefinition> Axes { get; } = [];
    /// <summary>Series bound to store channels.</summary>
    [Category("Chart"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content), Description("Series bound to store channels.")]
    public DefinitionCollection<SeriesDefinition> Series { get; } = [];
    /// <summary>Threshold lines and bands.</summary>
    [Category("Chart"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content), Description("Threshold lines and bands.")]
    public DefinitionCollection<ThresholdDefinition> Thresholds { get; } = [];
    /// <summary>Event markers.</summary>
    [Category("Chart"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content), Description("Event markers.")]
    public DefinitionCollection<MarkerDefinition> Markers { get; } = [];
    /// <summary>Visible time span in seconds.</summary>
    [Category("Chart"), DefaultValue(30.0), Description("Visible time span in seconds.")]
    public double TimeSpanSeconds { get; set; } = 30;
    /// <summary>Time axis labels: wall clock (UTC) or seconds back from the live edge.</summary>
    [Category("Chart"), DefaultValue(TimeFormat.Utc), Description("Time axis labels: wall clock (UTC) or seconds back from the live edge.")]
    public TimeFormat TimeFormat { get; set; } = TimeFormat.Utc;
    /// <summary>Legend placement.</summary>
    [Category("Chart"), DefaultValue(LegendPosition.TopLeft), Description("Legend placement.")]
    public LegendPosition Legend { get; set; } = LegendPosition.TopLeft;
    /// <summary>Light or dark theme.</summary>
    [Category("Chart"), DefaultValue(ChartThemeChoice.Light), Description("Light or dark theme.")]
    public ChartThemeChoice Theme { get; set; } = ChartThemeChoice.Light;
    /// <summary>Series names inside the lanes (the drag handles).</summary>
    [Category("Chart"), DefaultValue(true), Description("Series names inside the lanes (the drag handles).")]
    public bool PlotLabels { get; set; } = true;
    /// <summary>Header bars left of the lanes: drag to reorder, fold, remove.</summary>
    [Category("Chart"), DefaultValue(true), Description("Header bars left of the lanes: drag to reorder, fold, remove.")]
    public bool LaneHeaders { get; set; } = true;
    /// <summary>Overview strip under the time axis with the visible window framed.</summary>
    [Category("Chart"), DefaultValue(false), Description("Overview strip under the time axis with the visible window framed.")]
    public bool Navigator { get; set; }
    /// <summary>Measurement table while both cursors are set.</summary>
    [Category("Chart"), DefaultValue(true), Description("Measurement table while both cursors are set.")]
    public bool MeasurePanel { get; set; } = true;
    /// <summary>Lane header bar width in pixels.</summary>
    [Category("Chart"), DefaultValue(14.0)]
    public double LaneHeaderWidth { get; set; } = 14;

    /// <summary>Builds a configuration from the designer properties (the collections above and the scalar settings).</summary>
    public TrendChartConfig BuildDesignerConfig()
    {
        var cfg = new TrendChartConfig
        {
            TimeSpan = TimeSpanSeconds, TimeFormat = TimeFormat, Legend = Legend, PlotLabels = PlotLabels, LaneHeaders = LaneHeaders, Navigator = Navigator, MeasurePanel = MeasurePanel, LaneHeaderWidth = LaneHeaderWidth,
            Theme = Theme == ChartThemeChoice.Dark ? ChartTheme.Dark : ChartTheme.Light,
        };
        cfg.Lanes.AddRange(Lanes.Select(l => l.ToConfig()));
        cfg.Axes.AddRange(Axes.Select(a => a.ToConfig()));
        cfg.Series.AddRange(Series.Select(s => s.ToConfig()));
        cfg.Thresholds.AddRange(Thresholds.Select(t => t.ToConfig()));
        cfg.Markers.AddRange(Markers.Select(m => m.ToConfig()));
        return cfg;
    }

    /// <summary>Apply the designer properties now (also done automatically on first paint when <see cref="Configure"/> was not called).</summary>
    public void ApplyDesignerConfig() => Configure(BuildDesignerConfig());

    // ---- runtime surface ---------------------------------------------------------
    /// <summary>The store the chart reads; replace it with <see cref="SetStore"/>.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public SignalStore Store { get; private set; }
    /// <summary>The headless model: window, cursors, lanes, series, drags, measurements.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public TrendChartModel Model { get; private set; }
    /// <summary>Chart time; a playback source's clock during playback.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public ITimeSource Clock { get; set; }
    /// <summary>Pan, box zoom or cursor tool for drags in the plot.</summary>
    [Category("Behavior"), DefaultValue(Tool.Pan)]
    public Tool Tool { get => _interaction.Tool; set => _interaction = InteractionState.Initial(value); }
    /// <summary>Series picked up together with the next one (Ctrl/Shift click on labels or legend rows).</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public HashSet<string> Selection { get; } = [];
    /// <summary>Raised when a gesture changed the configuration (series moved, lanes reordered or resized, axis ranges, series menu).</summary>
    public event Action? ConfigChanged;
    /// <summary>
    /// Raised when channels are dropped on the chart (Windows Forms drag and drop with <see cref="ChannelDragData.Format"/>, or
    /// plain text ids), before they are added: cancel, redirect or handle the drop. Otherwise the chart calls <see cref="AddChannels"/>.
    /// </summary>
    [Description("Channels dropped on the chart, before they are added: cancel, redirect or handle the drop.")]
    public event EventHandler<ChannelDropEventArgs>? ChannelDrop;

    /// <summary>Replace the configuration (lanes, series, theme, style…). The store and time state are kept.</summary>
    public void Configure(TrendChartConfig config) { Model = new TrendChartModel(Store, config) { Now = Model.Now }; _configured = true; _layout = null; Redraw(); }
    /// <summary>Replace the store (e.g. one with a long retention for playback) and rebuild the model on it. Call before subscribing listeners.</summary>
    public void SetStore(SignalStore store) { Store = store; Model = new TrendChartModel(Store, Model.Config) { Now = Model.Now }; _layout = null; Redraw(); }

    // ---- layout files ------------------------------------------------------------
    /// <summary>The arrangement as a JSON layout file (theme and style excluded).</summary>
    public string ExportLayout() => Model.ExportLayout();
    /// <summary>Replace the arrangement with a layout file; theme and style are kept.</summary>
    public void ImportLayout(string json) { Model.ImportLayout(json); Selection.Clear(); Changed(); }
    /// <summary>Write the layout file to <paramref name="path"/>.</summary>
    public void SaveLayout(string path) => File.WriteAllText(path, ExportLayout());
    /// <summary>Load a layout file from <paramref name="path"/>.</summary>
    public void LoadLayout(string path) => ImportLayout(File.ReadAllText(path));

    // ---- range export ------------------------------------------------------------
    /// <summary>CSV of the visible signals between cursors A and B; null unless both cursors are set.</summary>
    public string? ExportCursorsCsv() => Model.ExportCursorsCsv();
    /// <summary>MCAP recording of the visible signals between cursors A and B; null unless both cursors are set.</summary>
    public byte[]? ExportCursorsMcap() => Model.ExportCursorsMcap();
    /// <summary>Write the cursor span to <paramref name="path"/>: CSV for a <c>.csv</c> extension, MCAP otherwise. Returns false, writing nothing, unless both cursors are set.</summary>
    public bool SaveCursorsRange(string path)
    {
        if (Model.CursorRange() is null) return false;
        if (string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase)) File.WriteAllText(path, ExportCursorsCsv());
        else File.WriteAllBytes(path, ExportCursorsMcap()!);
        return true;
    }

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
    public DropTarget DropTargetAt(Point screen, bool digital = false) { var p = Local(PointToClient(screen)); return Model.DropTargetAt(CurrentLayout(), p.X, p.Y, digital); }

    private static ChannelDragPayload? ReadPayload(IDataObject? data)
    {
        if (data is null) return null;
        if (data.GetDataPresent(ChannelDragData.Format) && ChannelDragData.TryParse(data.GetData(ChannelDragData.Format) as string, out var p)) return p;
        if (data.GetDataPresent(DataFormats.UnicodeText) && ChannelDragData.TryParse(data.GetData(DataFormats.UnicodeText) as string, out p)) return p;
        if (data.GetDataPresent(DataFormats.Text) && ChannelDragData.TryParse(data.GetData(DataFormats.Text) as string, out p)) return p;
        return null;
    }

    protected override void OnDragEnter(DragEventArgs e) { base.OnDragEnter(e); DragOverChart(e); }
    protected override void OnDragOver(DragEventArgs e) { base.OnDragOver(e); DragOverChart(e); }
    protected override void OnDragLeave(EventArgs e) { base.OnDragLeave(e); if (_nativeDrag) { _nativeDrag = false; Model.CancelDrag(); Redraw(); } }

    private void DragOverChart(DragEventArgs e)
    {
        var payload = ReadPayload(e.Data);
        if (payload is null) { e.Effect = DragDropEffects.None; return; }
        var p = Local(PointToClient(new Point(e.X, e.Y)));
        if (!_nativeDrag || Model.Drag is null) { _nativeDrag = true; Model.BeginDrag([], p.X, p.Y, payload.Group, payload.Ids, ChannelDragData.IsDigital(payload, Store)); }
        Model.UpdateDrag(CurrentLayout(), p.X, p.Y);
        e.Effect = Model.Drag?.Target is DropTarget.None ? DragDropEffects.None : DragDropEffects.Copy;
        Redraw();
    }

    protected override void OnDragDrop(DragEventArgs e)
    {
        base.OnDragDrop(e);
        _nativeDrag = false; Model.CancelDrag();
        var payload = ReadPayload(e.Data);
        if (payload is null) { Redraw(); return; }
        var p = Local(PointToClient(new Point(e.X, e.Y)));
        var args = new ChannelDropEventArgs(payload, Model.DropTargetAt(CurrentLayout(), p.X, p.Y, ChannelDragData.IsDigital(payload, Store)), p.X, p.Y);
        ChannelDrop?.Invoke(this, args);
        if (args.Cancel) { Redraw(); return; }
        if (args.Handled) { Changed(); return; }
        if (args.Target is DropTarget.None) { Redraw(); return; }
        AddChannels(args.ChannelIds, args.Target, args.Group);
    }

    // ---- helpers -----------------------------------------------------------------
    private (double X, double Y) Local(Point client) => (client.X / PixelRatio, client.Y / PixelRatio);
    private static int Button(MouseButtons b) => b switch { MouseButtons.Middle => 1, MouseButtons.Right => 2, _ => 0 };
    private static Modifiers Mods() => new(ModifierKeys.HasFlag(Keys.Shift), ModifierKeys.HasFlag(Keys.Control), ModifierKeys.HasFlag(Keys.Alt));
    private static bool GroupKey() => ModifierKeys.HasFlag(Keys.Control) || ModifierKeys.HasFlag(Keys.Shift);
    private TrendLayout CurrentLayout() => _layout ??= Model.Layout(LogicalWidth, LogicalHeight);
    private void Changed() { _layout = null; ConfigChanged?.Invoke(); Redraw(); }
    /// <summary>Tell the control the configuration was changed from outside (an editor calling model commands): relayouts, repaints and raises <see cref="ConfigChanged"/>.</summary>
    public void NotifyConfigChanged() => Changed();

    private static Cursor CursorFor(HitRegion hit) => hit switch
    {
        HitRegion.Label or HitRegion.LegendRow => Cursors.Hand,
        HitRegion.Header h => h.Part == HeaderPart.Grip ? Cursors.SizeAll : Cursors.Hand,
        HitRegion.Axis => Cursors.SizeNS,
        HitRegion.Navigator n => n.Zone is NavigatorZone.LeftEdge or NavigatorZone.RightEdge ? Cursors.SizeWE : Cursors.Hand,
        HitRegion.LaneGap => Cursors.SizeNS,
        HitRegion.Cursor => Cursors.SizeWE,
        _ => Cursors.Default,
    };

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (!_configured && !DesignMode && (Lanes.Count > 0 || Series.Count > 0 || Axes.Count > 0)) ApplyDesignerConfig();
        if (!DesignMode) _timer.Start();
    }
    protected override void OnHandleDestroyed(EventArgs e) { _timer.Stop(); base.OnHandleDestroyed(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var p = Local(e.Location);
        var layout = CurrentLayout(); var hit = Model.HitTest(layout, p.X, p.Y); var group = GroupKey();
        if (e.Clicks == 2 && e.Button == MouseButtons.Left)
        {
            if (hit is HitRegion.Axis ax) { Model.AxisAutoscale(ax.AxisId); Changed(); return; }
            if (hit is HitRegion.Navigator or HitRegion.Header or HitRegion.Label or HitRegion.LaneGap or HitRegion.Cursor or HitRegion.Measure) return;
            Dispatch(new InputEvent.DoubleClick(p.X, p.Y)); return;
        }
        if (e.Button == MouseButtons.Left)
        {
            Capture = true;
            switch (hit)
            {
                case HitRegion.Label or HitRegion.LegendRow:
                {
                    var id = hit is HitRegion.Label l ? l.SeriesId : ((HitRegion.LegendRow)hit).SeriesId;
                    if (group) Selection.Add(id);
                    var ids = Selection.Contains(id) ? Selection.ToList() : [id];
                    _pressAt = e.Location; _pressedSeries = group ? null : id;
                    Model.BeginDrag(ids, p.X, p.Y, group); Redraw(); return;
                }
                case HitRegion.Header h:
                    if (h.Part == HeaderPart.Collapse) { var lane = Model.Lanes().First(l => l.Id == h.LaneId); Model.SetLaneCollapsed(h.LaneId, !lane.Collapsed); Changed(); return; }
                    if (h.Part == HeaderPart.Remove) { Model.RemoveLane(h.LaneId); Changed(); return; }
                    Model.BeginLaneDrag(h.LaneId, p.X, p.Y); return;
                case HitRegion.Axis a: Model.BeginAxisDrag(layout, a.AxisId, a.LaneId, a.Zone, p.Y); return;
                case HitRegion.Navigator: Model.BeginNavigatorDrag(layout, p.X); Redraw(); return;
                case HitRegion.LaneGap gap: Model.BeginLaneResize(layout, gap.AboveLaneId, gap.BelowLaneId, p.Y); return;
                case HitRegion.Cursor cu: Model.BeginCursorDrag(cu.Which); return;
                case HitRegion.Measure: return;
            }
            if (!group) Selection.Clear();
        }
        if (e.Button == MouseButtons.Right && hit is HitRegion.Label or HitRegion.LegendRow)
        {
            var id = hit is HitRegion.Label l ? l.SeriesId : ((HitRegion.LegendRow)hit).SeriesId;
            BuildSeriesMenu(id).Show(this, e.Location);
            return;
        }
        Dispatch(new InputEvent.PointerDown(p.X, p.Y, Button(e.Button), Mods()));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = Local(e.Location);
        if (Model.Drag is not null) { Model.UpdateDrag(CurrentLayout(), p.X, p.Y); Redraw(); return; }
        if (Model.LaneDrag is not null) { Model.UpdateLaneDrag(CurrentLayout(), p.X, p.Y); Redraw(); return; }
        if (Model.AxisDrag is not null) { Model.UpdateAxisDrag(CurrentLayout(), p.Y); Redraw(); return; }
        if (Model.NavDrag is not null) { Model.UpdateNavigatorDrag(CurrentLayout(), p.X); Redraw(); return; }
        if (Model.LaneResize is not null) { if (Model.UpdateLaneResize(CurrentLayout(), p.Y)) _layout = null; Redraw(); return; }
        if (Model.CursorDrag is not null) { Model.UpdateCursorDrag(CurrentLayout(), p.X); _layout = null; Redraw(); return; }
        Cursor = CursorFor(Model.HitTest(CurrentLayout(), p.X, p.Y));
        Dispatch(new InputEvent.PointerMove(p.X, p.Y, Mods()));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Capture = false;
        var p = Local(e.Location);
        if (Model.Drag is not null)
        {
            // a press without movement on a label or legend row is a click: hide or show that signal
            var pressed = _pressedSeries; _pressedSeries = null;
            if (pressed is not null && Model.Drag.ChannelIds.Count == 0 && Math.Abs(e.X - _pressAt.X) < 4 * PixelRatio && Math.Abs(e.Y - _pressAt.Y) < 4 * PixelRatio) { Model.CancelDrag(); Model.ToggleSeries(pressed); Changed(); return; }
            if (Model.EndDrag(CurrentLayout(), p.X, p.Y)) Changed(); else Redraw();
            return;
        }
        if (Model.LaneDrag is not null) { if (Model.EndLaneDrag(CurrentLayout(), p.X, p.Y)) Changed(); else Redraw(); return; }
        if (Model.AxisDrag is not null) { Model.EndAxisDrag(); ConfigChanged?.Invoke(); return; }
        if (Model.NavDrag is not null) { Model.EndNavigatorDrag(); return; }
        if (Model.LaneResize is not null) { Model.EndLaneResize(); Changed(); return; }
        if (Model.CursorDrag is not null) { Model.EndCursorDrag(); return; }
        Dispatch(new InputEvent.PointerUp(p.X, p.Y, Button(e.Button), Mods()));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var p = Local(e.Location); var layout = CurrentLayout();
        var factor = Math.Pow(2, e.Delta / 400.0);
        switch (Model.HitTest(layout, p.X, p.Y))
        {
            case HitRegion.Axis a: Model.AxisZoomAt(layout, a.AxisId, a.LaneId, p.Y, factor); Redraw(); return;
            case HitRegion.Navigator: Model.ApplyEffect(new Effect.Zoom(layout.Plot.X + layout.Plot.W, p.Y, factor), layout); Redraw(); return;
        }
        Dispatch(new InputEvent.Wheel(p.X, p.Y, -e.Delta, Mods()));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) { Model.CancelDrag(); Model.CancelLaneDrag(); Selection.Clear(); Redraw(); }
        if (e.KeyCode is Keys.Left or Keys.Right && Model.Config.Navigator) { Model.NavigatorKey(e.KeyCode == Keys.Left ? "ArrowLeft" : "ArrowRight"); e.Handled = true; Redraw(); return; }
        Dispatch(new InputEvent.KeyDown(e.KeyCode == Keys.Space ? " " : e.KeyCode.ToString()));
    }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); Dispatch(new InputEvent.KeyUp(e.KeyCode == Keys.Space ? " " : e.KeyCode.ToString())); }

    private void Dispatch(InputEvent ev)
    {
        var layout = CurrentLayout();
        var (state, effects) = Interaction.Reduce(_interaction, ev);
        _interaction = state;
        foreach (var fx in effects) Model.ApplyEffect(fx, layout);
        Redraw();
    }

    /// <summary>The series menu: show or hide, rename, colour, line width, remove.</summary>
    private ContextMenuStrip BuildSeriesMenu(string seriesId)
    {
        var s = Model.Config.Series.First(x => x.Id == seriesId);
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(Model.SeriesName(s)) { Enabled = false, Font = new Font(Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(s.Visible ? "Hide" : "Show", null, (_, _) => { Model.ToggleSeries(seriesId); Changed(); });
        menu.Items.Add("Rename…", null, (_, _) => { if (Prompt("Rename signal", s.Name ?? "") is { } name) { Model.RenameSeries(seriesId, name); Changed(); } });
        var colours = new ToolStripMenuItem("Colour");
        foreach (var c in TrendChartConfig.SeriesPalette)
        {
            var item = new ToolStripMenuItem(c) { Checked = string.Equals(Model.SeriesColor(s), c, StringComparison.OrdinalIgnoreCase), Image = Swatch(c) };
            item.Click += (_, _) => { Model.SetSeriesColor(seriesId, c); Changed(); };
            colours.DropDownItems.Add(item);
        }
        colours.DropDownItems.Add(new ToolStripSeparator());
        colours.DropDownItems.Add("Palette default", null, (_, _) => { Model.SetSeriesColor(seriesId, null); Changed(); });
        menu.Items.Add(colours);
        var widths = new ToolStripMenuItem("Line width");
        foreach (var w in new[] { 1.0, 1.5, 2, 3 })
        {
            var item = new ToolStripMenuItem(w.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Checked = (s.Width ?? Model.Config.Style.SeriesWidth) == w };
            item.Click += (_, _) => { Model.SetSeriesWidth(seriesId, w); Changed(); };
            widths.DropDownItems.Add(item);
        }
        menu.Items.Add(widths);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Remove from chart", null, (_, _) => { Model.RemoveSeries(seriesId); Changed(); });
        return menu;
    }

    private static Image Swatch(string css)
    {
        var bmp = new Bitmap(14, 14);
        using var g = Graphics.FromImage(bmp);
        var c = Core.Paint.CssColor.Parse(css);
        g.Clear(System.Drawing.Color.FromArgb(255, c.R, c.G, c.B));
        return bmp;
    }

    private string? Prompt(string title, string initial)
    {
        using var dlg = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(300, 70), MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false };
        var box = new TextBox { Text = initial, Left = 10, Top = 10, Width = 280 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 130, Top = 38, Width = 75 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 215, Top = 38, Width = 75 };
        dlg.Controls.AddRange([box, ok, cancel]); dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK ? box.Text : null;
    }

    protected override void OnPaintSurface(SkiaPaintEventArgs e)
    {
        if (DesignMode && !_configured && (Lanes.Count > 0 || Series.Count > 0 || Axes.Count > 0)) { Model = new TrendChartModel(Store, BuildDesignerConfig()); }
        lock (Store.SyncRoot)
        {
            Model.Now = DesignMode ? 0 : Clock.Now();
            _layout = Model.Layout(e.Width, e.Height);
            Model.Update(_layout);
            using var painter = new SkiaPainter(e.Canvas, e.Width, e.Height, e.Scale);
            TrendChartRenderer.Draw(Model, painter, _layout);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
