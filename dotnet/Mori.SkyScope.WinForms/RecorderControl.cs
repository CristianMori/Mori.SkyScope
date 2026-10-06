// Mori.SkyScope — Windows Forms record and open controls for MCAP recordings.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Mcap;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// Record / stop button with elapsed time and size for an <see cref="McapRecorder"/>, an Open… button that raises
/// <see cref="OpenRequested"/> with the chosen MCAP or CSV path, and a save dialog when a recording starts.
/// </summary>
[ToolboxItem(true), Description("Record the stream to MCAP and open recordings.")]
public class RecorderControl : UserControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly Button _record = new() { Text = "● Record", AutoSize = true };
    private readonly Button _open = new() { Text = "Open…", AutoSize = true };
    private readonly Label _status = new() { AutoSize = true, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 6, 0, 0) };
    private McapRecorder? _recorder;
    private string? _path;

    /// <summary>Builds the record, open and status row and wires the buttons and the half-second status refresh.</summary>
    public RecorderControl()
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true };
        panel.Controls.Add(_record); panel.Controls.Add(_open); panel.Controls.Add(_status);
        Controls.Add(panel);
        Height = 32;
        _record.Click += (_, _) => Toggle();
        _open.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "MCAP recordings (*.mcap)|*.mcap|CSV (*.csv)|*.csv|All files|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) OpenRequested?.Invoke(dlg.FileName);
        };
        _timer.Tick += (_, _) => RefreshStatus();
        RefreshStatus();
    }

    /// <summary>The recorder sitting between the source and the sinks.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public McapRecorder? Recorder { get => _recorder; set { _recorder = value; if (value is null) _timer.Stop(); else _timer.Start(); RefreshStatus(); } }
    /// <summary>Suggested file name prefix.</summary>
    [Category("Behavior"), DefaultValue("skyscope")]
    public string FilePrefix { get; set; } = "skyscope";
    /// <summary>Raised with the path the user picked in Open….</summary>
    public event Action<string>? OpenRequested;
    /// <summary>Raised after a recording was saved.</summary>
    public event Action<string>? Saved;

    private void Toggle()
    {
        if (_recorder is null) return;
        if (!_recorder.IsRecording)
        {
            // The file is chosen up front and written through, so recordings can run for hours without growing in memory.
            using var dlg = new SaveFileDialog { Filter = "MCAP recordings (*.mcap)|*.mcap", FileName = $"{FilePrefix}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.mcap" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            _path = dlg.FileName; _recorder.StartFile(_path); RefreshStatus(); return;
        }
        _recorder.Stop();
        RefreshStatus();
        if (_path is not null) Saved?.Invoke(_path);
    }

    private void RefreshStatus()
    {
        if (_recorder is null) { _record.Enabled = false; _status.Text = ""; return; }
        _record.Enabled = true;
        var s = _recorder.Stats();
        _record.Text = s.Recording ? "■ Stop" : "● Record";
        _status.Text = s.Recording ? $"REC {s.Duration:0.0} s · {s.Messages} msgs · {s.Bytes / 1e6:0.0} MB" : s.Bytes > 0 ? $"last: {s.Messages} msgs · {s.Bytes / 1e6:0.0} MB" : "";
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
