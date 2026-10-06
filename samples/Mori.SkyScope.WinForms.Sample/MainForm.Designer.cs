// Mori.SkyScope — Designer-generated layout of the Windows Forms dashboard, including the trend chart's lanes, axes and series as the designer serialises them.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.WinForms.Sample;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null!;
    private TrendChartControl chart = null!;
    private SignalTreeControl tree = null!;
    private ChartEditorControl editor = null!;
    private SceneControl3D scene3d = null!;
    private GaugeControl speed = null!, temp = null!, heading = null!, attitude = null!, bus = null!, level = null!, gain = null!, arm = null!, throttle = null!;
    private RecorderControl recorder = null!;
    private PlaybackControl transport = null!;
    private ToolStrip toolbar = null!;
    private ToolStripLabel status = null!;
    private ToolStripButton btnPan = null!, btnBoxZoom = null!, btnCursor = null!, btnPause = null!, btnReset = null!, btnSaveLayout = null!, btnLoadLayout = null!, btnExport = null!, btnGpu = null!, btnFit = null!, btnEditor = null!;
    private SplitContainer splitLeft = null!, splitRight = null!;
    private TableLayoutPanel gauges = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// Designer code. The chart's lanes, axes and series below are exactly what the Windows Forms designer writes after
    /// editing the Lanes, Axes and Series collections in the property grid.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        chart = new TrendChartControl();
        tree = new SignalTreeControl();
        editor = new ChartEditorControl();
        scene3d = new SceneControl3D();
        speed = new GaugeControl(); temp = new GaugeControl(); heading = new GaugeControl(); attitude = new GaugeControl(); bus = new GaugeControl(); level = new GaugeControl();
        gain = new GaugeControl(); arm = new GaugeControl(); throttle = new GaugeControl();
        recorder = new RecorderControl();
        transport = new PlaybackControl();
        toolbar = new ToolStrip();
        status = new ToolStripLabel();
        btnPan = new ToolStripButton(); btnBoxZoom = new ToolStripButton(); btnCursor = new ToolStripButton(); btnPause = new ToolStripButton(); btnReset = new ToolStripButton();
        btnSaveLayout = new ToolStripButton(); btnLoadLayout = new ToolStripButton(); btnExport = new ToolStripButton(); btnGpu = new ToolStripButton(); btnFit = new ToolStripButton(); btnEditor = new ToolStripButton();
        splitLeft = new SplitContainer(); splitRight = new SplitContainer();
        gauges = new TableLayoutPanel();
        SuspendLayout();

        // chart — lanes, axes and series as the designer serialises them
        chart.Dock = DockStyle.Fill;
        chart.Legend = Core.Charts.LegendPosition.TopLeft;
        chart.TimeFormat = TimeFormat.Utc;
        chart.TimeSpanSeconds = 30;
        chart.Navigator = true;
        var laneAnalog = new LaneDefinition { Id = "analog", Weight = 2 };
        var laneFast = new LaneDefinition { Id = "fast" };
        var laneRobot = new LaneDefinition { Id = "robot" };
        var laneIo = new LaneDefinition { Id = "io", Weight = 0.6 };
        chart.Lanes.Add(laneAnalog); chart.Lanes.Add(laneFast); chart.Lanes.Add(laneRobot); chart.Lanes.Add(laneIo);
        chart.Axes.Add(new AxisDefinition { Id = "axis:analog", Label = "sine / tri" });
        chart.Axes.Add(new AxisDefinition { Id = "axis:fast", Label = "saw" });
        chart.Axes.Add(new AxisDefinition { Id = "axis:robot", Label = "amr-1 pose", Unit = "m" });
        chart.Series.Add(new SeriesDefinition { Id = "s1", ChannelId = 1, LaneId = "analog" });
        chart.Series.Add(new SeriesDefinition { Id = "s2", ChannelId = 2, LaneId = "analog" });
        chart.Series.Add(new SeriesDefinition { Id = "s3", ChannelId = 3, LaneId = "fast" });
        chart.Series.Add(new SeriesDefinition { Id = "s5", ChannelId = 5, LaneId = "fast", AxisId = "noise", Width = 1 });
        chart.Series.Add(new SeriesDefinition { Id = "px", ChannelId = 100, LaneId = "robot", Name = "x" });
        chart.Series.Add(new SeriesDefinition { Id = "py", ChannelId = 101, LaneId = "robot", Name = "y" });
        chart.Series.Add(new SeriesDefinition { Id = "pv", ChannelId = 103, LaneId = "robot", Name = "speed", AxisId = "speed" });
        chart.Series.Add(new SeriesDefinition { Id = "s4", ChannelId = 4, LaneId = "io", Digital = true });
        chart.Series.Add(new SeriesDefinition { Id = "s9", ChannelId = 9, LaneId = "io", Digital = true });
        chart.Series.Add(new SeriesDefinition { Id = "pump", ChannelId = 200, LaneId = "io", Digital = true });
        chart.Series.Add(new SeriesDefinition { Id = "valve", ChannelId = 201, LaneId = "io", Digital = true });
        chart.Thresholds.Add(new ThresholdDefinition { Id = "hi", AxisId = "axis:analog", From = 1.5, To = 3, Color = "#dc2626", Label = "high" });

        // toolbar
        btnPan.Text = "Pan"; btnBoxZoom.Text = "Box zoom"; btnCursor.Text = "Cursor"; btnPause.Text = "Pause"; btnReset.Text = "Reset";
        btnSaveLayout.Text = "Save layout"; btnLoadLayout.Text = "Load layout"; btnExport.Text = "Export A→B"; btnGpu.Text = "GPU"; btnGpu.CheckOnClick = true; btnFit.Text = "Fit 3D"; btnEditor.Text = "Editor"; btnEditor.CheckOnClick = true;
        status.Text = "connecting…"; status.ForeColor = SystemColors.GrayText;
        toolbar.Items.AddRange([btnPan, btnBoxZoom, btnCursor, new ToolStripSeparator(), btnPause, btnReset, new ToolStripSeparator(), btnSaveLayout, btnLoadLayout, btnExport, btnEditor, new ToolStripSeparator(), btnGpu, btnFit, new ToolStripSeparator(), status]);
        toolbar.GripStyle = ToolStripGripStyle.Hidden;
        toolbar.Dock = DockStyle.Top;

        // tree | chart | 3D
        tree.Dock = DockStyle.Fill;
        scene3d.Dock = DockStyle.Fill; scene3d.Animate = true;
        splitLeft.Dock = DockStyle.Fill; splitLeft.SplitterDistance = 200; splitLeft.FixedPanel = FixedPanel.Panel1;
        splitRight.Dock = DockStyle.Fill; splitRight.SplitterDistance = 640;
        editor.Dock = DockStyle.Fill; editor.Visible = false;
        splitLeft.Panel1.Controls.Add(editor);
        splitLeft.Panel1.Controls.Add(tree);
        splitLeft.Panel2.Controls.Add(splitRight);
        splitRight.Panel1.Controls.Add(chart);
        splitRight.Panel2.Controls.Add(scene3d);

        // gauges row
        gauges.Dock = DockStyle.Bottom; gauges.Height = 190; gauges.ColumnCount = 9; gauges.RowCount = 1;
        for (var i = 0; i < 9; i++) gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 9));
        foreach (var g in new[] { speed, temp, heading, attitude, bus, level, gain, arm, throttle }) { g.Dock = DockStyle.Fill; gauges.Controls.Add(g); }

        // recorder + transport
        recorder.Dock = DockStyle.Bottom;
        transport.Dock = DockStyle.Bottom; transport.Visible = false;

        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 800);
        Text = "Mori.SkyScope — Windows Forms sample";
        Controls.Add(splitLeft);
        Controls.Add(gauges);
        Controls.Add(transport);
        Controls.Add(recorder);
        Controls.Add(toolbar);
        ResumeLayout(false);
        PerformLayout();
    }
}
