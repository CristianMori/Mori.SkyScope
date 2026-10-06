// Mori.SkyScope — WPF transport bar for a playback source: play, pause, rewind, scrubber, speed and loop.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// Transport bar for a <see cref="PlaybackSource"/>: play/pause, rewind, scrubber, elapsed / total, speed buttons, loop.
/// Ticks the source from a dispatcher timer (50 ms), so charts bound to <see cref="PlaybackSource.Clock"/> follow the recording.
/// </summary>
public class PlaybackControl : UserControl
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Button _play = new() { Content = "▶", Width = 32, Margin = new Thickness(0, 0, 4, 0) };
    private readonly Button _rewind = new() { Content = "⏮", Width = 32, Margin = new Thickness(0, 0, 8, 0) };
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 1000, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    private readonly TextBlock _time = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 110, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _speeds = new() { Orientation = Orientation.Horizontal };
    private readonly ToggleButton _loop = new() { Content = "loop", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0) };
    private DateTime _last = DateTime.UtcNow;
    private bool _scrubbing;
    private PlaybackSource? _source;

    /// <summary>Builds the bar (play, rewind, scrubber, elapsed time, speed buttons, loop) and wires each control to the source.</summary>
    public PlaybackControl()
    {
        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_play, Dock.Left); DockPanel.SetDock(_rewind, Dock.Left); DockPanel.SetDock(_loop, Dock.Right); DockPanel.SetDock(_speeds, Dock.Right); DockPanel.SetDock(_time, Dock.Right);
        root.Children.Add(_play); root.Children.Add(_rewind); root.Children.Add(_loop); root.Children.Add(_speeds); root.Children.Add(_time); root.Children.Add(_slider);
        Content = root;
        foreach (var s in new[] { 0.25, 0.5, 1, 2, 5, 10 })
        {
            var b = new Button { Content = $"{s}×", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(2, 0, 0, 0), Tag = s };
            b.Click += (_, _) => { if (_source is not null) _source.Speed = s; Refresh(); };
            _speeds.Children.Add(b);
        }
        _play.Click += (_, _) => { if (_source is null) return; if (_source.Playing) _source.Pause(); else _source.Play(); Refresh(); };
        _rewind.Click += (_, _) => { SeekProgress(0); };
        _loop.Click += (_, _) => { if (_source is not null) _source.Loop = _loop.IsChecked == true; };
        _slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _scrubbing = true));
        _slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _scrubbing = false; SeekProgress(_slider.Value / 1000); }));
        _slider.PreviewMouseLeftButtonUp += (_, _) => { if (!_scrubbing) SeekProgress(_slider.Value / 1000); };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The source to drive; the control starts ticking it as soon as it is set.</summary>
    public PlaybackSource? Source
    {
        get => _source;
        set { _source = value; _last = DateTime.UtcNow; if (value is null) _timer.Stop(); else _timer.Start(); Refresh(); }
    }

    /// <summary>Raised after every tick that pushed frames or changed state — hosts repaint charts here.</summary>
    public event Action? Advanced;

    private void SeekProgress(double p)
    {
        if (_source?.Recording is not { } r) return;
        _source.Seek(r.Start + p * _source.Duration);
        Refresh(); Advanced?.Invoke();
    }

    private void Tick()
    {
        if (_source is null) return;
        var now = DateTime.UtcNow; var dt = Math.Min(0.5, (now - _last).TotalSeconds); _last = now;
        var pushed = _source.Tick(dt);
        if (pushed > 0 || _source.Playing) { Refresh(); Advanced?.Invoke(); }
    }

    private void Refresh()
    {
        if (_source is null) { _time.Text = "—"; return; }
        _play.Content = _source.Playing ? "⏸" : "▶";
        if (!_scrubbing) _slider.Value = _source.Progress * 1000;
        _time.Text = $"{Fmt(_source.Progress * _source.Duration)} / {Fmt(_source.Duration)}";
        _loop.IsChecked = _source.Loop;
        foreach (Button b in _speeds.Children) b.FontWeight = (double)b.Tag! == _source.Speed ? FontWeights.Bold : FontWeights.Normal;
    }

    private static string Fmt(double t) { var m = (int)Math.Floor(t / 60); var s = t - m * 60; return $"{m}:{(s < 10 ? "0" : "")}{s:0.00}"; }
}
