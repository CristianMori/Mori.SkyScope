// Mori.SkyScope — WPF record and open controls for MCAP recordings.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Mori.SkyScope.Core.Mcap;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// Record / stop button with elapsed time and size for an <see cref="McapRecorder"/>, an Open… button that raises
/// <see cref="OpenRequested"/> with the chosen MCAP path, and a save dialog when a recording stops.
/// </summary>
public class RecorderControl : UserControl
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Button _record = new() { Content = "● Record", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button _open = new() { Content = "Open…", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.Gray, MinWidth = 140 };
    private McapRecorder? _recorder;
    private string? _path;

    /// <summary>Builds the record, open and status row and wires the buttons and the half-second status refresh.</summary>
    public RecorderControl()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(_record); panel.Children.Add(_open); panel.Children.Add(_status);
        Content = panel;
        _record.Click += (_, _) => Toggle();
        _open.Click += (_, _) =>
        {
            var dlg = new OpenFileDialog { Filter = "MCAP recordings (*.mcap)|*.mcap|CSV (*.csv)|*.csv|All files|*.*" };
            if (dlg.ShowDialog() == true) OpenRequested?.Invoke(dlg.FileName);
        };
        _timer.Tick += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>The recorder sitting between the source and the sinks.</summary>
    public McapRecorder? Recorder { get => _recorder; set { _recorder = value; if (value is null) _timer.Stop(); else _timer.Start(); Refresh(); } }
    /// <summary>Suggested file name prefix.</summary>
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
            var dlg = new SaveFileDialog { Filter = "MCAP recordings (*.mcap)|*.mcap", FileName = $"{FilePrefix}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}.mcap" };
            if (dlg.ShowDialog() != true) return;
            _path = dlg.FileName; _recorder.StartFile(_path); Refresh(); return;
        }
        _recorder.Stop();
        Refresh();
        if (_path is not null) Saved?.Invoke(_path);
    }

    private void Refresh()
    {
        if (_recorder is null) { _record.IsEnabled = false; _status.Text = ""; return; }
        _record.IsEnabled = true;
        var s = _recorder.Stats();
        _record.Content = s.Recording ? "■ Stop" : "● Record";
        _status.Text = s.Recording ? $"REC {s.Duration:0.0} s · {s.Messages} msgs · {s.Bytes / 1e6:0.0} MB" : s.Bytes > 0 ? $"last: {s.Messages} msgs · {s.Bytes / 1e6:0.0} MB" : "";
    }
}
