// Mori.SkyScope — Designer-generated layout of the Windows Forms dashboard, in the statement shapes the Windows Forms designer reads and writes, including the trend chart's lanes, axes and series.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

#nullable disable

namespace Mori.SkyScope.WinForms.Sample;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private TrendChartControl chart;
    private SignalTreeControl tree;
    private ChartEditorControl editor;
    private SceneControl3D scene3d;
    private GaugeControl speed;
    private GaugeControl temp;
    private GaugeControl heading;
    private GaugeControl attitude;
    private GaugeControl bus;
    private GaugeControl level;
    private GaugeControl gain;
    private GaugeControl arm;
    private GaugeControl throttle;
    private RecorderControl recorder;
    private PlaybackControl transport;
    private ToolStrip toolbar;
    private ToolStripLabel status;
    private ToolStripButton btnPan;
    private ToolStripButton btnBoxZoom;
    private ToolStripButton btnCursor;
    private ToolStripSeparator sep1;
    private ToolStripButton btnPause;
    private ToolStripButton btnReset;
    private ToolStripSeparator sep2;
    private ToolStripButton btnSaveLayout;
    private ToolStripButton btnLoadLayout;
    private ToolStripButton btnExport;
    private ToolStripButton btnEditor;
    private ToolStripSeparator sep3;
    private ToolStripButton btnGpu;
    private ToolStripButton btnFit;
    private ToolStripSeparator sep4;
    private SplitContainer splitLeft;
    private SplitContainer splitRight;
    private TableLayoutPanel gauges;

    /// <summary>Clean up any resources being used.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify the contents of this method with the code editor.
    /// The chart's lanes, axes and series below are exactly what the designer writes after editing the Lanes, Axes and Series collections in the property grid.
    /// </summary>
    private void InitializeComponent()
    {
        AxisDefinition axisDefinition1 = new AxisDefinition();
        AxisDefinition axisDefinition2 = new AxisDefinition();
        AxisDefinition axisDefinition3 = new AxisDefinition();
        LaneDefinition laneDefinition1 = new LaneDefinition();
        LaneDefinition laneDefinition2 = new LaneDefinition();
        LaneDefinition laneDefinition3 = new LaneDefinition();
        LaneDefinition laneDefinition4 = new LaneDefinition();
        SeriesDefinition seriesDefinition1 = new SeriesDefinition();
        SeriesDefinition seriesDefinition2 = new SeriesDefinition();
        SeriesDefinition seriesDefinition3 = new SeriesDefinition();
        SeriesDefinition seriesDefinition4 = new SeriesDefinition();
        SeriesDefinition seriesDefinition5 = new SeriesDefinition();
        SeriesDefinition seriesDefinition6 = new SeriesDefinition();
        SeriesDefinition seriesDefinition7 = new SeriesDefinition();
        SeriesDefinition seriesDefinition8 = new SeriesDefinition();
        SeriesDefinition seriesDefinition9 = new SeriesDefinition();
        SeriesDefinition seriesDefinition10 = new SeriesDefinition();
        SeriesDefinition seriesDefinition11 = new SeriesDefinition();
        ThresholdDefinition thresholdDefinition1 = new ThresholdDefinition();
        toolbar = new ToolStrip();
        btnPan = new ToolStripButton();
        btnBoxZoom = new ToolStripButton();
        btnCursor = new ToolStripButton();
        sep1 = new ToolStripSeparator();
        btnPause = new ToolStripButton();
        btnReset = new ToolStripButton();
        sep2 = new ToolStripSeparator();
        btnSaveLayout = new ToolStripButton();
        btnLoadLayout = new ToolStripButton();
        btnExport = new ToolStripButton();
        btnEditor = new ToolStripButton();
        sep3 = new ToolStripSeparator();
        btnGpu = new ToolStripButton();
        btnFit = new ToolStripButton();
        sep4 = new ToolStripSeparator();
        status = new ToolStripLabel();
        splitLeft = new SplitContainer();
        editor = new ChartEditorControl();
        tree = new SignalTreeControl();
        splitRight = new SplitContainer();
        chart = new TrendChartControl();
        scene3d = new SceneControl3D();
        gauges = new TableLayoutPanel();
        speed = new GaugeControl();
        temp = new GaugeControl();
        heading = new GaugeControl();
        attitude = new GaugeControl();
        bus = new GaugeControl();
        level = new GaugeControl();
        gain = new GaugeControl();
        arm = new GaugeControl();
        throttle = new GaugeControl();
        recorder = new RecorderControl();
        transport = new PlaybackControl();
        toolbar.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitLeft).BeginInit();
        splitLeft.Panel1.SuspendLayout();
        splitLeft.Panel2.SuspendLayout();
        splitLeft.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)splitRight).BeginInit();
        splitRight.Panel1.SuspendLayout();
        splitRight.Panel2.SuspendLayout();
        splitRight.SuspendLayout();
        gauges.SuspendLayout();
        SuspendLayout();
        // 
        // toolbar
        // 
        toolbar.GripStyle = ToolStripGripStyle.Hidden;
        toolbar.ImageScalingSize = new Size(24, 24);
        toolbar.Items.AddRange(new ToolStripItem[] { btnPan, btnBoxZoom, btnCursor, sep1, btnPause, btnReset, sep2, btnSaveLayout, btnLoadLayout, btnExport, btnEditor, sep3, btnGpu, btnFit, sep4, status });
        toolbar.Location = new Point(0, 0);
        toolbar.Name = "toolbar";
        toolbar.Size = new Size(1280, 38);
        toolbar.TabIndex = 0;
        // 
        // btnPan
        // 
        btnPan.Name = "btnPan";
        btnPan.Size = new Size(44, 33);
        btnPan.Text = "Pan";
        // 
        // btnBoxZoom
        // 
        btnBoxZoom.Name = "btnBoxZoom";
        btnBoxZoom.Size = new Size(96, 33);
        btnBoxZoom.Text = "Box zoom";
        // 
        // btnCursor
        // 
        btnCursor.Name = "btnCursor";
        btnCursor.Size = new Size(68, 33);
        btnCursor.Text = "Cursor";
        // 
        // sep1
        // 
        sep1.Name = "sep1";
        sep1.Size = new Size(6, 38);
        // 
        // btnPause
        // 
        btnPause.Name = "btnPause";
        btnPause.Size = new Size(61, 33);
        btnPause.Text = "Pause";
        // 
        // btnReset
        // 
        btnReset.Name = "btnReset";
        btnReset.Size = new Size(58, 33);
        btnReset.Text = "Reset";
        // 
        // sep2
        // 
        sep2.Name = "sep2";
        sep2.Size = new Size(6, 38);
        // 
        // btnSaveLayout
        // 
        btnSaveLayout.Name = "btnSaveLayout";
        btnSaveLayout.Size = new Size(107, 33);
        btnSaveLayout.Text = "Save layout";
        // 
        // btnLoadLayout
        // 
        btnLoadLayout.Name = "btnLoadLayout";
        btnLoadLayout.Size = new Size(109, 33);
        btnLoadLayout.Text = "Load layout";
        // 
        // btnExport
        // 
        btnExport.Name = "btnExport";
        btnExport.Size = new Size(110, 33);
        btnExport.Text = "Export A→B";
        // 
        // btnEditor
        // 
        btnEditor.CheckOnClick = true;
        btnEditor.Name = "btnEditor";
        btnEditor.Size = new Size(63, 33);
        btnEditor.Text = "Editor";
        // 
        // sep3
        // 
        sep3.Name = "sep3";
        sep3.Size = new Size(6, 38);
        // 
        // btnGpu
        // 
        btnGpu.CheckOnClick = true;
        btnGpu.Name = "btnGpu";
        btnGpu.Size = new Size(50, 33);
        btnGpu.Text = "GPU";
        // 
        // btnFit
        // 
        btnFit.Name = "btnFit";
        btnFit.Size = new Size(63, 33);
        btnFit.Text = "Fit 3D";
        // 
        // sep4
        // 
        sep4.Name = "sep4";
        sep4.Size = new Size(6, 38);
        // 
        // status
        // 
        status.ForeColor = SystemColors.GrayText;
        status.Name = "status";
        status.Size = new Size(112, 33);
        status.Text = "connecting…";
        // 
        // splitLeft
        // 
        splitLeft.Dock = DockStyle.Fill;
        splitLeft.FixedPanel = FixedPanel.Panel1;
        splitLeft.Location = new Point(0, 38);
        splitLeft.Name = "splitLeft";
        // 
        // splitLeft.Panel1
        // 
        splitLeft.Panel1.Controls.Add(editor);
        splitLeft.Panel1.Controls.Add(tree);
        // 
        // splitLeft.Panel2
        // 
        splitLeft.Panel2.Controls.Add(splitRight);
        splitLeft.Size = new Size(1280, 508);
        splitLeft.SplitterDistance = 200;
        splitLeft.TabIndex = 1;
        // 
        // editor
        // 
        editor.Dock = DockStyle.Fill;
        editor.GridHeight = 153;
        editor.Location = new Point(0, 0);
        editor.Name = "editor";
        editor.Size = new Size(200, 508);
        editor.TabIndex = 1;
        editor.Visible = false;
        // 
        // tree
        // 
        tree.Dock = DockStyle.Fill;
        tree.Location = new Point(0, 0);
        tree.Name = "tree";
        tree.Size = new Size(200, 508);
        tree.TabIndex = 0;
        // 
        // splitRight
        // 
        splitRight.Dock = DockStyle.Fill;
        splitRight.Location = new Point(0, 0);
        splitRight.Name = "splitRight";
        // 
        // splitRight.Panel1
        // 
        splitRight.Panel1.Controls.Add(chart);
        // 
        // splitRight.Panel2
        // 
        splitRight.Panel2.Controls.Add(scene3d);
        splitRight.Size = new Size(1076, 508);
        splitRight.SplitterDistance = 640;
        splitRight.TabIndex = 0;
        // 
        // chart
        // 
        chart.AllowDrop = true;
        axisDefinition1.Id = "axis:analog";
        axisDefinition1.Label = "sine / tri";
        axisDefinition2.Id = "axis:fast";
        axisDefinition2.Label = "saw";
        axisDefinition3.Id = "axis:robot";
        axisDefinition3.Label = "amr-1 pose";
        axisDefinition3.Unit = "m";
        chart.Axes.Add(axisDefinition1);
        chart.Axes.Add(axisDefinition2);
        chart.Axes.Add(axisDefinition3);
        chart.Dock = DockStyle.Fill;
        laneDefinition1.Id = "analog";
        laneDefinition1.Weight = 2D;
        laneDefinition2.Id = "fast";
        laneDefinition3.Id = "robot";
        laneDefinition4.Id = "io";
        laneDefinition4.Weight = 0.6D;
        chart.Lanes.Add(laneDefinition1);
        chart.Lanes.Add(laneDefinition2);
        chart.Lanes.Add(laneDefinition3);
        chart.Lanes.Add(laneDefinition4);
        chart.Location = new Point(0, 0);
        chart.Name = "chart";
        chart.Navigator = true;
        seriesDefinition1.Id = "s1";
        seriesDefinition1.LaneId = "analog";
        seriesDefinition2.ChannelId = 2;
        seriesDefinition2.Id = "s2";
        seriesDefinition2.LaneId = "analog";
        seriesDefinition3.ChannelId = 3;
        seriesDefinition3.Id = "s3";
        seriesDefinition3.LaneId = "fast";
        seriesDefinition4.AxisId = "noise";
        seriesDefinition4.ChannelId = 5;
        seriesDefinition4.Id = "s5";
        seriesDefinition4.LaneId = "fast";
        seriesDefinition4.Width = 1D;
        seriesDefinition5.ChannelId = 100;
        seriesDefinition5.Id = "px";
        seriesDefinition5.LaneId = "robot";
        seriesDefinition5.Name = "x";
        seriesDefinition6.ChannelId = 101;
        seriesDefinition6.Id = "py";
        seriesDefinition6.LaneId = "robot";
        seriesDefinition6.Name = "y";
        seriesDefinition7.AxisId = "speed";
        seriesDefinition7.ChannelId = 103;
        seriesDefinition7.Id = "pv";
        seriesDefinition7.LaneId = "robot";
        seriesDefinition7.Name = "speed";
        seriesDefinition8.ChannelId = 4;
        seriesDefinition8.Digital = true;
        seriesDefinition8.Id = "s4";
        seriesDefinition8.LaneId = "io";
        seriesDefinition9.ChannelId = 9;
        seriesDefinition9.Digital = true;
        seriesDefinition9.Id = "s9";
        seriesDefinition9.LaneId = "io";
        seriesDefinition10.ChannelId = 200;
        seriesDefinition10.Digital = true;
        seriesDefinition10.Id = "pump";
        seriesDefinition10.LaneId = "io";
        seriesDefinition11.ChannelId = 201;
        seriesDefinition11.Digital = true;
        seriesDefinition11.Id = "valve";
        seriesDefinition11.LaneId = "io";
        chart.Series.Add(seriesDefinition1);
        chart.Series.Add(seriesDefinition2);
        chart.Series.Add(seriesDefinition3);
        chart.Series.Add(seriesDefinition4);
        chart.Series.Add(seriesDefinition5);
        chart.Series.Add(seriesDefinition6);
        chart.Series.Add(seriesDefinition7);
        chart.Series.Add(seriesDefinition8);
        chart.Series.Add(seriesDefinition9);
        chart.Series.Add(seriesDefinition10);
        chart.Series.Add(seriesDefinition11);
        chart.Size = new Size(640, 508);
        chart.TabIndex = 0;
        thresholdDefinition1.AxisId = "axis:analog";
        thresholdDefinition1.Color = "#dc2626";
        thresholdDefinition1.From = 1.5D;
        thresholdDefinition1.Id = "hi";
        thresholdDefinition1.Label = "high";
        thresholdDefinition1.To = 3D;
        chart.Thresholds.Add(thresholdDefinition1);
        // 
        // scene3d
        // 
        scene3d.Animate = true;
        scene3d.Dock = DockStyle.Fill;
        scene3d.Location = new Point(0, 0);
        scene3d.Name = "scene3d";
        scene3d.Rendering = RenderingMode.Gpu;
        scene3d.Size = new Size(432, 508);
        scene3d.TabIndex = 0;
        // 
        // gauges
        // 
        gauges.ColumnCount = 9;
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 11.1111116F));
        gauges.Controls.Add(speed, 0, 0);
        gauges.Controls.Add(temp, 1, 0);
        gauges.Controls.Add(heading, 2, 0);
        gauges.Controls.Add(attitude, 3, 0);
        gauges.Controls.Add(bus, 4, 0);
        gauges.Controls.Add(level, 5, 0);
        gauges.Controls.Add(gain, 6, 0);
        gauges.Controls.Add(arm, 7, 0);
        gauges.Controls.Add(throttle, 8, 0);
        gauges.Dock = DockStyle.Bottom;
        gauges.Location = new Point(0, 546);
        gauges.Name = "gauges";
        gauges.RowCount = 1;
        gauges.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        gauges.Size = new Size(1280, 190);
        gauges.TabIndex = 2;
        // 
        // speed
        // 
        speed.Dock = DockStyle.Fill;
        speed.Location = new Point(3, 3);
        speed.Name = "speed";
        speed.Size = new Size(136, 184);
        speed.TabIndex = 0;
        // 
        // temp
        // 
        temp.Dock = DockStyle.Fill;
        temp.Location = new Point(145, 3);
        temp.Name = "temp";
        temp.Size = new Size(136, 184);
        temp.TabIndex = 1;
        // 
        // heading
        // 
        heading.Dock = DockStyle.Fill;
        heading.Location = new Point(287, 3);
        heading.Name = "heading";
        heading.Size = new Size(136, 184);
        heading.TabIndex = 2;
        // 
        // attitude
        // 
        attitude.Dock = DockStyle.Fill;
        attitude.Location = new Point(429, 3);
        attitude.Name = "attitude";
        attitude.Size = new Size(136, 184);
        attitude.TabIndex = 3;
        // 
        // bus
        // 
        bus.Dock = DockStyle.Fill;
        bus.Location = new Point(571, 3);
        bus.Name = "bus";
        bus.Size = new Size(136, 184);
        bus.TabIndex = 4;
        // 
        // level
        // 
        level.Dock = DockStyle.Fill;
        level.Location = new Point(713, 3);
        level.Name = "level";
        level.Size = new Size(136, 184);
        level.TabIndex = 5;
        // 
        // gain
        // 
        gain.Dock = DockStyle.Fill;
        gain.Location = new Point(855, 3);
        gain.Name = "gain";
        gain.Size = new Size(136, 184);
        gain.TabIndex = 6;
        // 
        // arm
        // 
        arm.Dock = DockStyle.Fill;
        arm.Location = new Point(997, 3);
        arm.Name = "arm";
        arm.Size = new Size(136, 184);
        arm.TabIndex = 7;
        // 
        // throttle
        // 
        throttle.Dock = DockStyle.Fill;
        throttle.Location = new Point(1139, 3);
        throttle.Name = "throttle";
        throttle.Size = new Size(138, 184);
        throttle.TabIndex = 8;
        // 
        // recorder
        // 
        recorder.Dock = DockStyle.Bottom;
        recorder.Location = new Point(0, 768);
        recorder.Name = "recorder";
        recorder.Size = new Size(1280, 32);
        recorder.TabIndex = 3;
        // 
        // transport
        // 
        transport.Dock = DockStyle.Bottom;
        transport.Location = new Point(0, 736);
        transport.Name = "transport";
        transport.Size = new Size(1280, 32);
        transport.TabIndex = 4;
        transport.Visible = false;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(144F, 144F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1280, 800);
        Controls.Add(splitLeft);
        Controls.Add(gauges);
        Controls.Add(transport);
        Controls.Add(recorder);
        Controls.Add(toolbar);
        Name = "MainForm";
        Text = "Mori.SkyScope — Windows Forms sample";
        toolbar.ResumeLayout(false);
        toolbar.PerformLayout();
        splitLeft.Panel1.ResumeLayout(false);
        splitLeft.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitLeft).EndInit();
        splitLeft.ResumeLayout(false);
        splitRight.Panel1.ResumeLayout(false);
        splitRight.Panel2.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)splitRight).EndInit();
        splitRight.ResumeLayout(false);
        gauges.ResumeLayout(false);
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
