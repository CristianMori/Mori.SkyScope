// Mori.SkyScope — Windows Forms transport bar for a playback source: play, pause, rewind, scrubber, speed and loop.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// Transport bar for a <see cref="PlaybackSource"/>: play/pause, rewind, scrubber, elapsed / total, speed buttons, loop.
/// Ticks the source from a timer (50 ms), so charts bound to <see cref="PlaybackSource.Clock"/> follow the recording.
/// </summary>
[ToolboxItem(true), Description("Transport bar for a recording: play, pause, scrub, speed, loop.")]
public class PlaybackControl : UserControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    private readonly Button _play = new() { Text = "▶", Width = 32 };
    private readonly Button _rewind = new() { Text = "⏮", Width = 32 };
    private readonly TrackBar _slider = new() { Minimum = 0, Maximum = 1000, TickStyle = TickStyle.None, AutoSize = false, Height = 24 };
    private readonly Label _time = new() { AutoSize = false, Width = 120, TextAlign = ContentAlignment.MiddleRight };
    private readonly FlowLayoutPanel _speeds = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
    private readonly CheckBox _loop = new() { Text = "loop", Appearance = Appearance.Button, AutoSize = true };
    private readonly TableLayoutPanel _root = new() { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1 };
    private DateTime _last = DateTime.UtcNow;
    private bool _scrubbing;
    private PlaybackSource? _source;

    /// <summary>Builds the bar and wires each control to the source.</summary>
    public PlaybackControl()
    {
        Height = 32;
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); _root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _slider.Dock = DockStyle.Fill;
        _root.Controls.Add(_play, 0, 0); _root.Controls.Add(_rewind, 1, 0); _root.Controls.Add(_slider, 2, 0); _root.Controls.Add(_time, 3, 0); _root.Controls.Add(_speeds, 4, 0); _root.Controls.Add(_loop, 5, 0);
        Controls.Add(_root);
        foreach (var s in new[] { 0.25, 0.5, 1, 2, 5, 10 })
        {
            var b = new Button { Text = $"{s}×", AutoSize = true, Tag = s, Margin = new Padding(2, 0, 0, 0) };
            b.Click += (_, _) => { if (_source is not null) _source.Speed = s; RefreshBar(); };
            _speeds.Controls.Add(b);
        }
        _play.Click += (_, _) => { if (_source is null) return; if (_source.Playing) _source.Pause(); else _source.Play(); RefreshBar(); };
        _rewind.Click += (_, _) => SeekProgress(0);
        _loop.CheckedChanged += (_, _) => { if (_source is not null) _source.Loop = _loop.Checked; };
        _slider.MouseDown += (_, _) => _scrubbing = true;
        _slider.MouseUp += (_, _) => { _scrubbing = false; SeekProgress(_slider.Value / 1000.0); };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>The source to drive; the control starts ticking it as soon as it is set.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PlaybackSource? Source
    {
        get => _source;
        set { _source = value; _last = DateTime.UtcNow; if (value is null) _timer.Stop(); else _timer.Start(); RefreshBar(); }
    }

    /// <summary>Raised after every tick that pushed frames or changed state — hosts repaint charts here.</summary>
    public event Action? Advanced;

    private void SeekProgress(double p)
    {
        if (_source?.Recording is not { } r) return;
        _source.Seek(r.Start + p * _source.Duration);
        RefreshBar(); Advanced?.Invoke();
    }

    private void Tick()
    {
        if (_source is null) return;
        var now = DateTime.UtcNow; var dt = Math.Min(0.5, (now - _last).TotalSeconds); _last = now;
        var pushed = _source.Tick(dt);
        if (pushed > 0 || _source.Playing) { RefreshBar(); Advanced?.Invoke(); }
    }

    private void RefreshBar()
    {
        if (_source is null) { _time.Text = "—"; return; }
        _play.Text = _source.Playing ? "⏸" : "▶";
        if (!_scrubbing) _slider.Value = Math.Clamp((int)(_source.Progress * 1000), 0, 1000);
        _time.Text = $"{Fmt(_source.Progress * _source.Duration)} / {Fmt(_source.Duration)}";
        _loop.Checked = _source.Loop;
        foreach (Button b in _speeds.Controls) b.Font = (double)b.Tag! == _source.Speed ? new Font(Font, FontStyle.Bold) : Font;
    }

    private static string Fmt(double t) { var m = (int)Math.Floor(t / 60); var s = t - m * 60; return $"{m}:{(s < 10 ? "0" : "")}{s:0.00}"; }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
