// Mori.SkyScope — The Windows Forms dashboard: trend chart with the signal tree, 3D scene, gauges, recording and playback.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Core.Mcap;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.WinForms.Sample;

/// <summary>
/// The dashboard form: the chart is configured entirely by the designer code in <c>MainForm.Designer.cs</c>; this file
/// wires the live socket (with the MCAP recorder in between), the gauges, the 3D scene and the toolbar.
/// </summary>
public partial class MainForm : Form
{
    private readonly WebSocketFrameSource _source = new();
    private readonly RadialGauge _speed = new(new RadialGaugeConfig { Min = -4, Max = 4, Unit = "m/s", Label = "sine", Decimals = 2 });
    private readonly LinearGauge _temp = new(new LinearGaugeConfig { Min = -4, Max = 4, Unit = "°C", Label = "triangle", Orientation = Core.Gauges.Orientation.Vertical, Decimals = 1 });
    private readonly Compass _heading = new(new CompassConfig { Label = "HDG" });
    private readonly AttitudeIndicator _attitude = new();
    private readonly NumericDisplay _bus = new(new NumericDisplayConfig { Digits = 6, Decimals = 3, Unit = "V", Label = "sawtooth" });
    private readonly LedArray _level = new(new LedArrayConfig { Count = 12, Min = -1.5, Max = 1.5, Label = "noise", Orientation = Core.Gauges.Orientation.Vertical });
    private readonly Knob _gain = new(new KnobConfig { Min = 0, Max = 10, Step = 0.5, Label = "gain", Unit = "x" }, 2.5);
    private readonly Switch _arm = new(new SwitchConfig { Label = "arm" });
    private readonly Slider _throttle = new(new SliderConfig { Min = 0, Max = 100, Label = "throttle", Unit = "%" }, 30);
    private readonly PlaybackSource _playback = new();
    private bool _paused;

    /// <summary>Builds the dashboard and starts the live source, or a recording when <paramref name="recordingPath"/> is given.</summary>
    public MainForm(string wsUrl, RenderingMode rendering = RenderingMode.Cpu, string? recordingPath = null)
    {
        InitializeComponent();
        foreach (var c in new SkiaHostControl[] { chart, speed, temp, heading, attitude, bus, level, gain, arm, throttle }) c.Rendering = rendering;
        btnGpu.Checked = rendering == RenderingMode.Gpu;
        tree.Chart = chart; tree.RefreshRows();
        editor.Chart = chart;
        btnEditor.Click += (_, _) => { editor.Visible = btnEditor.Checked; tree.Visible = !btnEditor.Checked; splitLeft.SplitterDistance = btnEditor.Checked ? 330 : 200; if (editor.Visible) editor.Refresh(); };

        speed.Gauge = _speed; temp.Gauge = _temp; heading.Gauge = _heading; attitude.Gauge = _attitude; bus.Gauge = _bus; level.Gauge = _level;
        gain.Gauge = _gain; arm.Gauge = _arm; throttle.Gauge = _throttle;

        // Latest samples drive the gauges; the store notifies on the UI thread because the source dispatches there.
        chart.Store.Subscribe((ids, _) =>
        {
            double? V(int ch) { var b = chart.Store.Get(ch)?.Buffer; return b is { IsEmpty: false } ? b.ValueAt(b.HeadSeq - 1) : null; }
            if (V(1) is { } s1) { _speed.SetValue(s1); speed.Refresh(); _heading.SetHeading(s1 * 45 + 180); heading.Refresh(); }
            if (V(2) is { } s2) { _temp.SetValue(s2); temp.Refresh(); }
            if (V(3) is { } s3) { _bus.SetValue(s3); bus.Refresh(); }
            if (V(5) is { } s5) { _level.SetValue(s5); level.Refresh(); }
            if (V(1) is { } p && V(2) is { } r) { _attitude.Set(p * 8, r * 15); attitude.Refresh(); }
        });

        // Relayed 3D layers land in the scene view; the socket dispatches onto the UI thread, so the sink needs no marshalling.
        var layers3d = new SceneLayerSink(scene3d.Scene, null, () => scene3d.Refresh(), new LayerJson.LayerEnv(scene3d.Frames, "map"));
        var fitted = false;
        scene3d.Scene.Add(new Core.Scene3D.Grid3DLayer("grid"));

        btnPan.Click += (_, _) => chart.Tool = Tool.Pan;
        btnBoxZoom.Click += (_, _) => chart.Tool = Tool.BoxZoom;
        btnCursor.Click += (_, _) => chart.Tool = Tool.Cursor;
        btnPause.Click += (_, _) => { _paused = !_paused; if (_paused) chart.Model.Pause(); else chart.Model.Resume(); btnPause.Text = _paused ? "Live" : "Pause"; };
        btnReset.Click += (_, _) => { chart.Model.Reset(); _paused = false; btnPause.Text = "Pause"; };
        btnSaveLayout.Click += (_, _) => { using var dlg = new SaveFileDialog { Filter = "Layout (*.json)|*.json", FileName = "skyscope-layout.json" }; if (dlg.ShowDialog(this) == DialogResult.OK) chart.SaveLayout(dlg.FileName); };
        btnLoadLayout.Click += (_, _) => { using var dlg = new OpenFileDialog { Filter = "Layout (*.json)|*.json" }; if (dlg.ShowDialog(this) == DialogResult.OK) { try { chart.LoadLayout(dlg.FileName); tree.RefreshRows(); } catch (Exception ex) { status.Text = $"layout: {ex.Message}"; } } };
        // the span between cursors A and B to a CSV or MCAP file; the extension picks the format
        btnExport.Click += (_, _) =>
        {
            if (chart.Model.CursorRange() is null) { status.Text = "export: set cursors A and B first (Cursor tool: click, Shift + click)"; return; }
            using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv|MCAP recording (*.mcap)|*.mcap", FileName = "skyscope-range.csv" };
            if (dlg.ShowDialog(this) == DialogResult.OK) { try { chart.SaveCursorsRange(dlg.FileName); status.Text = $"exported {Path.GetFileName(dlg.FileName)}"; } catch (Exception ex) { status.Text = $"export: {ex.Message}"; } }
        };
        btnGpu.Click += (_, _) => { var mode = btnGpu.Checked ? RenderingMode.Gpu : RenderingMode.Cpu; foreach (var c in new SkiaHostControl[] { chart, speed, temp, heading, attitude, bus, level, gain, arm, throttle }) c.Rendering = mode; status.Text = $"rendering: {chart.EffectiveRendering}"; };
        btnFit.Click += (_, _) => scene3d.FitAll();
        chart.ConfigChanged += () => tree.RefreshRows();

        recorder.OpenRequested += path => new MainForm(wsUrl, rendering, path).Show();
        if (recordingPath is not null)
        {
            var rec = recordingPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? CsvRecording.Parse(File.ReadAllText(recordingPath)) : SkyScopeMcap.ReadRecording(recordingPath);
            chart.SetStore(new SignalStore(retentionSeconds: 3600));
            tree.Chart = chart;
            chart.Clock = _playback.Clock;
            chart.Model.NavigatorRange = (rec.Start, rec.End);
            _ = _playback.StartAsync(new SourceContext(chart.Store, layers3d, _playback.Clock, (_, _) => { }), new PlaybackConfig(rec) { Autoplay = true, Loop = true });
            transport.Source = _playback; transport.Visible = true; recorder.Visible = false;
            status.Text = $"playback: {Path.GetFileName(recordingPath)} ({rec.Channels.Count} channels, {rec.End - rec.Start:0.0} s)";
            return;
        }

        // Live: the recorder sits between the socket and the chart/scene sinks.
        var mcap = new McapRecorder(chart.Store, layers3d, chart.Clock);
        recorder.Recorder = mcap;
        recorder.Saved += path => Ui(() => status.Text = $"saved {path}");
        var ctx = new SourceContext(mcap, mcap, chart.Clock, (level, msg) => Ui(() => status.Text = $"{level}: {msg}"));
        _ = _source.StartAsync(ctx, new WebSocketSourceConfig(new Uri(wsUrl)) { Dispatch = Ui });
        var poll = new System.Windows.Forms.Timer { Interval = 1000 };
        poll.Tick += (_, _) =>
        {
            var text = status.Text ?? ""; if (text.StartsWith("saved", StringComparison.Ordinal) || text.StartsWith("layout", StringComparison.Ordinal)) return;
            status.Text = _source.Connected ? $"live {wsUrl} · {chart.Store.Channels.Count} channels · {chart.EffectiveRendering}" : $"connecting {wsUrl}…";
            if (!fitted && scene3d.Scene.Layers.Count > 1) { fitted = true; scene3d.FitAll(); }
        };
        poll.Start();
    }

    /// <summary>Opens the chart editor in place of the signal tree (the toolbar's Editor button).</summary>
    internal void ShowEditor() { if (!btnEditor.Checked) btnEditor.PerformClick(); }

    /// <summary>Runs <paramref name="a"/> on the UI thread once the window exists; drops it otherwise (early connection errors).</summary>
    private void Ui(Action a) { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _ = _source.StopAsync();
        _ = _playback.StopAsync();
    }
}
