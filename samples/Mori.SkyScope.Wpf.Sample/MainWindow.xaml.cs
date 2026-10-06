// Mori.SkyScope — The WPF dashboard window: trend chart with the signal tree, 2D and 3D scenes, gauges, analytic charts, recording and playback.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Windows;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Mcap;
using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>
/// The dashboard window: trend chart with the signal tree, 2D and 3D scene views, gauges and analytic charts, fed either by
/// the live socket (with the MCAP recorder in between) or by a CSV/MCAP recording through the transport bar.
/// </summary>
public partial class MainWindow : Window
{
    private readonly WebSocketFrameSource _source = new();
    private readonly RadialGauge _speed = new(new RadialGaugeConfig { Min = -4, Max = 4, Unit = "m/s", Label = "sine", Decimals = 2, Bands = { } });
    private readonly LinearGauge _temp = new(new LinearGaugeConfig { Min = -4, Max = 4, Unit = "°C", Label = "triangle", Orientation = Orientation.Vertical, Decimals = 1 });
    private readonly Compass _heading = new(new CompassConfig { Label = "HDG" });
    private readonly AttitudeIndicator _attitude = new();
    private readonly NumericDisplay _bus = new(new NumericDisplayConfig { Digits = 6, Decimals = 3, Unit = "V", Label = "sawtooth" });
    private readonly LedArray _level = new(new LedArrayConfig { Count = 12, Min = -1.5, Max = 1.5, Label = "noise", Orientation = Orientation.Vertical });
    private readonly Knob _gain = new(new KnobConfig { Min = 0, Max = 10, Step = 0.5, Label = "gain", Unit = "x" }, 2.5);
    private readonly Switch _arm = new(new SwitchConfig { Label = "arm" });
    private readonly Slider _throttle = new(new SliderConfig { Min = 0, Max = 100, Label = "throttle", Unit = "%" }, 30);
    private bool _paused;
    private readonly Heatmap _spectrogram = new(new HeatmapConfig { Title = "Noise spectrogram (live)", Cols = 100, Rows = 32, XMin = -20, XMax = 0, YMin = 0, YMax = 500, XLabel = "s", YLabel = "Hz", ValueLabel = "dB", Colormap = "inferno", Rolling = true, Min = -60, Max = -10 });
    private int _specTick;

    private readonly PlaybackSource _playback = new();

    /// <summary>Builds the dashboard, configures the chart, gauges and scene sinks, then starts either playback of the recording or the live source.</summary>
    /// <param name="wsUrl">Stream to connect to; ignored when a recording is given.</param>
    /// <param name="csvPath">A CSV recording to play back instead of the live stream.</param>
    /// <param name="mcapPath">An MCAP recording (signals and scene) to play back instead of the live stream.</param>
    public MainWindow(string wsUrl, string? csvPath = null, string? mcapPath = null)
    {
        InitializeComponent();
        if (csvPath is not null || mcapPath is not null) Chart.SetStore(new SignalStore(retentionSeconds: 3600));   // review the whole recording
        _speed.Config.Bands.Add(new Band(3, 4, "#dc2626"));
        _speed.Config.Bands.Add(new Band(-4, -3, "#dc2626"));

        var cfg = new TrendChartConfig { TimeSpan = 30, TimeFormat = TimeFormat.Utc };
        cfg.Lanes.AddRange([new LaneConfig("analog") { Weight = 2 }, new LaneConfig("fast"), new LaneConfig("robot"), new LaneConfig("digital") { Weight = 0.5 }]);
        cfg.Axes.AddRange([new AxisConfig("axis:analog") { Label = "sine / tri" }, new AxisConfig("axis:fast") { Label = "saw" }, new AxisConfig("axis:robot") { Label = "amr-1 pose", Unit = "m" }]);
        cfg.Series.AddRange([
            new SeriesConfig("s1", 1) { LaneId = "analog" }, new SeriesConfig("s2", 2) { LaneId = "analog" },
            new SeriesConfig("s3", 3) { LaneId = "fast" }, new SeriesConfig("s5", 5) { LaneId = "fast", AxisId = "noise", Width = 1 },
            new SeriesConfig("s4", 4) { LaneId = "digital", Kind = SeriesKind.Digital }, new SeriesConfig("s9", 9) { LaneId = "digital", Kind = SeriesKind.Digital },
            new SeriesConfig("pump", 200) { LaneId = "digital", Kind = SeriesKind.Digital }, new SeriesConfig("valve", 201) { LaneId = "digital", Kind = SeriesKind.Digital },
            new SeriesConfig("px", 100) { LaneId = "robot", Name = "x" }, new SeriesConfig("py", 101) { LaneId = "robot", Name = "y" }, new SeriesConfig("pv", 103) { LaneId = "robot", Name = "speed", AxisId = "speed" },
        ]);
        cfg.Thresholds.Add(new ThresholdConfig("hi", "axis:analog", 1.5, "#dc2626") { To = 3 });
        cfg.Navigator = true;
        Chart.Configure(cfg);
        Tree.Chart = Chart; Tree.Refresh();

        Speed.Gauge = _speed; Temp.Gauge = _temp; Heading.Gauge = _heading; Attitude.Gauge = _attitude; Bus.Gauge = _bus; Level.Gauge = _level;
        Gain.Gauge = _gain; Arm.Gauge = _arm; Throttle.Gauge = _throttle;
        Bars.Chart = SampleCharts.Bars(); Pie.Chart = SampleCharts.Pie(); Radar.Chart = SampleCharts.Radar(); Spectrogram.Chart = _spectrogram;

        // Latest samples drive the gauges; the store notifies on the UI thread because the source dispatches there.
        Chart.Store.Subscribe((ids, _) =>
        {
            double? V(int ch) { var b = Chart.Store.Get(ch)?.Buffer; return b is { IsEmpty: false } ? b.ValueAt(b.HeadSeq - 1) : null; }
            if (V(1) is { } s1) { _speed.SetValue(s1); Speed.Invalidate(); _heading.SetHeading(s1 * 45 + 180); Heading.Invalidate(); }
            if (V(2) is { } s2) { _temp.SetValue(s2); Temp.Invalidate(); }
            if (V(3) is { } s3) { _bus.SetValue(s3); Bus.Invalidate(); }
            if (V(5) is { } s5) { _level.SetValue(s5); Level.Invalidate(); }
            if (V(1) is { } p && V(2) is { } r) { _attitude.Set(p * 8, r * 15); Attitude.Invalidate(); }
            // Live spectrogram of the noise channel: a 32-bin DFT of the last 128 samples, about 5× per second.
            if (++_specTick % 10 == 0 && Chart.Store.Get(5)?.Buffer is { IsEmpty: false } nb)
            {
                var n = Math.Min(128, nb.Length); var x = new double[n];
                for (var i = 0; i < n; i++) x[i] = nb.ValueAt(nb.HeadSeq - n + i);
                var col = new double[32];
                for (var k = 0; k < 32; k++) { double re = 0, im = 0; for (var i = 0; i < n; i++) { var w = 2 * Math.PI * k * i / n; re += x[i] * Math.Cos(w); im -= x[i] * Math.Sin(w); } col[k] = 20 * Math.Log10(Math.Sqrt(re * re + im * im) / n + 1e-6); }
                _spectrogram.PushColumn(col); Spectrogram.Invalidate();
            }
        });

        // Relayed scene layers land in both map views; the socket dispatches onto the UI thread, so the sinks need no marshalling.
        Map.Scene.Add(new GridLayer("grid"));
        var fitted = false;
        var layers2d = new SceneLayerSink(Map.Scene, null, () => { if (!fitted && Map.Scene.Layers.Count > 1) { fitted = true; Map.FitAll(); } else Map.Invalidate(); });
        var layers3d = new SceneLayerSink(Map3D.Scene, null, () => Map3D.Invalidate(), new LayerJson.LayerEnv(Map3D.Frames, "map"));
        var layers = new FanoutLayerSink(); layers.Add(layers2d); layers.Add(layers3d);
        StartSynthetic3D();
        Recorder.OpenRequested += path => new MainWindow(wsUrl, path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? path : null, path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? null : path).Show();
        if (csvPath is not null || mcapPath is not null)
        {
            // Playback: the chart follows the recording's clock; the transport bar ticks the source; layers replay into the map.
            var path = csvPath ?? mcapPath!;
            var rec = csvPath is not null ? CsvRecording.Parse(File.ReadAllText(csvPath)) : SkyScopeMcap.ReadRecording(mcapPath!);
            Chart.Clock = _playback.Clock;
            _ = _playback.StartAsync(new SourceContext(Chart.Store, layers, _playback.Clock, (_, _) => { }), new PlaybackConfig(rec) { Autoplay = true, Loop = true });
            Transport.Source = _playback; Transport.Visibility = Visibility.Visible; Recorder.Visibility = Visibility.Collapsed;
            Status.Text = $"playback: {System.IO.Path.GetFileName(path)} ({rec.Channels.Count} channels, {rec.LayerEvents.Count} layer events, {rec.End - rec.Start:0.0} s)";
            return;
        }
        // Live: the recorder sits between the socket and the chart/map sinks.
        var recorder = new McapRecorder(Chart.Store, layers, Chart.Clock);
        Recorder.Recorder = recorder;
        Recorder.Saved += path => Dispatcher.BeginInvoke(() => Status.Text = $"saved {path}");
        var ctx = new SourceContext(recorder, recorder, Chart.Clock, (level, msg) => Dispatcher.BeginInvoke(() => Status.Text = $"{level}: {msg}"));
        _ = _source.StartAsync(ctx, new WebSocketSourceConfig(new Uri(wsUrl)) { Dispatch = a => Dispatcher.BeginInvoke(a) });
        Closed += async (_, _) => await _source.StopAsync();
    }

    /// <summary>Shows the 3D view and its key hint.</summary>
    private void OnScene3D(object sender, RoutedEventArgs e) { Map3D.Visibility = Visibility.Visible; Map.Visibility = Visibility.Collapsed; Btn3D.FontWeight = FontWeights.Bold; Btn2D.FontWeight = FontWeights.Normal; SceneHint.Text = "drag orbit · middle/shift pan · wheel dolly · F fit · T top · O ortho"; }
    /// <summary>Shows the 2D view and its key hint.</summary>
    private void OnScene2D(object sender, RoutedEventArgs e) { Map.Visibility = Visibility.Visible; Map3D.Visibility = Visibility.Collapsed; Btn2D.FontWeight = FontWeights.Bold; Btn3D.FontWeight = FontWeights.Normal; SceneHint.Text = "Q/E rotate · F fit · wheel zoom"; }
    /// <summary>True while the 3D view is the visible one.</summary>
    private bool Is3D => Map3D.Visibility == Visibility.Visible;

    /// <summary>A 3D scene generated in-process (no server needed): a robot circling on a metric grid with a spinning lidar cloud, trail, labelled pose and markers.</summary>
    private void StartSynthetic3D()
    {
        var c = Map3D.Controller; var frames = c.Frames;
        frames.Set("laser", "base_link", new Transform3(new Vec3(0.2, 0, 0.35), Quat.Identity));
        c.Camera.SetDistance(14); c.Camera.SetOrbit(-2.2, 0.7);
        c.Theme = c.Theme with { Background = "#e2e8f0", Overlay = "#0f172a", OverlayText = "#0f172a" };
        c.Scene.Add(new Grid3DLayer("grid") { Size = 8, Color = "#94a3b8", MajorColor = "#64748b" });
        c.Scene.Add(new AxesLayer("axes", frames, "map") { Length = 0.6 });
        var cloud = new PointCloud3DLayer("lidar") { Frame = "laser", Frames = frames, FixedFrame = "map", PointSize = 3 };
        var trail = new Path3DLayer("trail") { Frame = "map", Frames = frames, FixedFrame = "map", MaxPoints = 600 };
        var pose = new Pose3DLayer("robot") { Frame = "base_link", Frames = frames, FixedFrame = "map", Label = "robot", AxisLength = 0.8 };
        var markers = new MarkerLayer("markers") { Frames = frames, FixedFrame = "map" };
        markers.SetMarkers([
            new Marker("dock", MarkerType.Cube) { Position = new(4, 4, 0.25), Scale = new(1, 0.6, 0.5), Color = "#3b82f6" },
            new Marker("dock-label", MarkerType.Text) { Position = new(4, 4, 0.9), Text = "dock", Color = "#0f172a" },
            new Marker("goal", MarkerType.Sphere) { Position = new(-3, 2, 0.3), Scale = new(0.6, 0.6, 0.6), Color = "#22c55e", Opacity = 0.7 },
            new Marker("pillar", MarkerType.Cylinder) { Position = new(0, -4, 1), Scale = new(0.5, 0.5, 2), Color = "#a855f7" },
            new Marker("heading", MarkerType.Arrow) { Position = new(-3, 2, 0.3), Orientation = new Quat(0, 0, 0.3826834, 0.9238795), Scale = new(1.2, 0.15, 0.15), Color = "#eab308" },
            new Marker("fence", MarkerType.LineStrip) { Points = [-5, -5, 0, 5, -5, 0, 5, 5, 0, -5, 5, 0, -5, -5, 0], Scale = new(1, 0, 0), Color = "#f87171" },
        ]);
        c.Scene.Add(cloud); c.Scene.Add(trail); c.Scene.Add(pose); c.Scene.Add(markers);
        const int n = 720; var pos = new float[n * 3]; var inten = new float[n]; var t0 = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - t0).TotalSeconds;
            double a = t * 0.4, x = 3 * Math.Cos(a), y = 3 * Math.Sin(a);
            frames.Set("base_link", "map", new Transform3(new Vec3(x, y, 0), Quat.FromEuler(0, 0, a + Math.PI / 2)), t);
            for (var i = 0; i < n; i++)
            {
                var ang = (double)i / n * Math.PI * 2 + t;
                var r = 2 + 0.6 * Math.Sin(ang * 3 + t) + (Math.Cos(ang) > 0.8 ? -1.2 : 0);
                pos[3 * i] = (float)(r * Math.Cos(ang)); pos[3 * i + 1] = (float)(r * Math.Sin(ang)); pos[3 * i + 2] = (float)(0.1 * Math.Sin(ang * 5 + t * 2));
                inten[i] = (float)r;
            }
            cloud.SetPoints((float[])pos.Clone(), (float[])inten.Clone(), null, t);
            trail.Append(x, y, 0.02);
            pose.SetPose(Pose3D.Identity, t);
        };
        timer.Start();
        Closed += (_, _) => timer.Stop();
    }
    /// <summary>Selects the orbit (3D) or pan (2D) tool of the visible scene view.</summary>
    private void OnScenePan(object sender, RoutedEventArgs e) { if (Is3D) Map3D.Tool = Scene3DTool.Orbit; else Map.Tool = SceneTool.Pan; }
    /// <summary>Selects the measure tool of the visible scene view.</summary>
    private void OnSceneMeasure(object sender, RoutedEventArgs e) { if (Is3D) Map3D.Tool = Scene3DTool.Measure; else Map.Tool = SceneTool.Measure; }
    /// <summary>Selects the select tool of the visible scene view.</summary>
    private void OnSceneSelect(object sender, RoutedEventArgs e) { if (Is3D) Map3D.Tool = Scene3DTool.Select; else Map.Tool = SceneTool.Select; }
    /// <summary>Fits the visible scene view to its layers.</summary>
    private void OnSceneFit(object sender, RoutedEventArgs e) { if (Is3D) Map3D.FitAll(); else Map.FitAll(); }
    /// <summary>Switches the trend chart to the pan tool.</summary>
    private void OnPan(object sender, RoutedEventArgs e) => Chart.Tool = Tool.Pan;
    /// <summary>Switches the trend chart to the box zoom tool.</summary>
    private void OnBoxZoom(object sender, RoutedEventArgs e) => Chart.Tool = Tool.BoxZoom;
    /// <summary>Switches the trend chart to the cursor tool.</summary>
    private void OnCursor(object sender, RoutedEventArgs e) => Chart.Tool = Tool.Cursor;
    /// <summary>Toggles the trend chart between frozen and live time and relabels the button.</summary>
    private void OnPause(object sender, RoutedEventArgs e) { _paused = !_paused; if (_paused) Chart.Model.Pause(); else Chart.Model.Resume(); PauseButton.Content = _paused ? "Live" : "Pause"; }
    /// <summary>Resets the trend chart view and returns to live time.</summary>
    private void OnReset(object sender, RoutedEventArgs e) { Chart.Model.Reset(); _paused = false; PauseButton.Content = "Pause"; }
}
