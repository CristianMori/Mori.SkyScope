// Mori.SkyScope — Recorded data (CSV or MCAP) played back through a manual clock with play, pause, speed, seek and loop.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Sources;

/// <summary>Recorded data: channels plus frames sorted by time. Mirrors <c>Recording</c> in <c>sources/playback.ts</c>.</summary>
/// <param name="Channels">Channel declarations, pushed to the sink on start.</param>
/// <param name="Frames">Frames sorted by <c>T0</c>.</param>
/// <param name="Start">Time of the first frame, seconds.</param>
/// <param name="End">Time of the last sample, seconds.</param>
public sealed record Recording(IReadOnlyList<ChannelInfo> Channels, IReadOnlyList<SkyScopeFrame> Frames, double Start, double End)
{
    /// <summary>Scene-layer declarations and pushes sorted by time; empty for signal-only recordings such as CSV.</summary>
    public IReadOnlyList<LayerEvent> LayerEvents { get; init; } = [];
}

/// <summary>A scene-layer declaration (<see cref="Kind"/> present) or push, at recording time <see cref="T"/>.</summary>
/// <param name="T">Recording time in seconds.</param>
/// <param name="Id">Layer id.</param>
public sealed record LayerEvent(double T, string Id)
{
    /// <summary>Layer kind for a declaration; null marks a push.</summary>
    public string? Kind { get; init; }
    /// <summary>Declaration meta (declarations only).</summary>
    public IReadOnlyDictionary<string, object?>? Meta { get; init; }
    /// <summary>Push payload (pushes only).</summary>
    public object? Payload { get; init; }
}

/// <summary>How <see cref="CsvRecording.Parse"/> reads a file. By default the delimiter is sniffed, the time column is found by name (<c>t</c>, <c>time</c>, <c>timestamp</c>, <c>seconds</c>) or falls back to the first column.</summary>
public sealed record CsvOptions
{
    /// <summary>Column separator; when null, <c>;</c> is used if the text has semicolons and no commas, otherwise <c>,</c>.</summary>
    public string? Delimiter { get; init; }
    /// <summary>Name or index of the time column (seconds). Omit with <see cref="Rate"/> for regular data without a time column.</summary>
    public string? TimeColumn { get; init; }
    /// <summary>Zero-based index of the time column; takes precedence over <see cref="TimeColumn"/>.</summary>
    public int? TimeColumnIndex { get; init; }
    /// <summary>Samples per second for files without a time column; rows then become regular channels at <c>1 / Rate</c>.</summary>
    public double? Rate { get; init; }
    /// <summary>Rows per frame.</summary>
    public int ChunkRows { get; init; } = 256;
    /// <summary>Id of the first value column; the following columns count up from it.</summary>
    public int FirstChannelId { get; init; } = 1;
    /// <summary>Multiplier that brings the time column to seconds (0.001 for milliseconds).</summary>
    public double TimeScale { get; init; } = 1;
}

/// <summary>CSV → Recording. The header row names the channels; blank lines and non-numeric cells (→ NaN) are tolerated. Mirrors <c>parseCsv</c>.</summary>
public static class CsvRecording
{
    /// <summary>Parse CSV text into channels and frames; an empty file gives an empty recording. Frames are sorted by time, so an unsorted time column reorders chunks but not rows within a chunk.</summary>
    public static Recording Parse(string text, CsvOptions? o = null)
    {
        o ??= new CsvOptions();
        var delim = o.Delimiter ?? (text.Contains(';') && !text.Contains(',') ? ";" : ",");
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToArray();
        if (lines.Length == 0) return new Recording([], [], 0, 0);
        var header = lines[0].Split(delim).Select(h => h.Trim().Trim('"')).ToArray();
        var timeIdx = -1;
        if (o.TimeColumnIndex is { } ti) timeIdx = ti;
        else if (o.TimeColumn is { } tc) timeIdx = Array.IndexOf(header, tc);
        else if (o.Rate is null) timeIdx = Array.FindIndex(header, h => System.Text.RegularExpressions.Regex.IsMatch(h, "^(t|time|timestamp|seconds?)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        if (timeIdx < 0 && o.Rate is null) timeIdx = 0;
        var valueCols = Enumerable.Range(0, header.Length).Where(i => i != timeIdx).ToArray();
        var channels = valueCols.Select((c, k) => new ChannelInfo(o.FirstChannelId + k, header[c]) { Timing = timeIdx >= 0 ? ChannelTiming.Timestamped : ChannelTiming.Regular, Rate = timeIdx >= 0 ? null : o.Rate }).ToList();
        var rows = new List<double[]>();
        for (var i = 1; i < lines.Length; i++) rows.Add(lines[i].Split(delim).Select(c => double.TryParse(c.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : double.NaN).ToArray());
        var chunk = Math.Max(1, o.ChunkRows); var dt = o.Rate is { } rate ? 1 / rate : 0;
        var frames = new List<SkyScopeFrame>();
        for (int r0 = 0, seq = 0; r0 < rows.Count; r0 += chunk, seq++)
        {
            var slice = rows.GetRange(r0, Math.Min(chunk, rows.Count - r0));
            var times = timeIdx >= 0 ? slice.Select(row => (timeIdx < row.Length ? row[timeIdx] : double.NaN) * o.TimeScale).ToArray() : null;
            var t0 = times is not null ? times[0] : r0 * dt;
            var fchannels = valueCols.Select((c, k) =>
            {
                var values = slice.Select(row => (float)(c < row.Length ? row[c] : double.NaN)).ToArray();
                return times is not null ? FrameChannel.Timestamped((ushort)(o.FirstChannelId + k), times, values) : FrameChannel.Regular((ushort)(o.FirstChannelId + k), t0, dt, values);
            }).ToList();
            frames.Add(new SkyScopeFrame((uint)seq, t0, fchannels));
        }
        frames.Sort((a, b) => a.T0.CompareTo(b.T0));
        var start = frames.Count > 0 ? frames[0].T0 : 0; var end = start;
        if (frames.Count > 0)
            foreach (var ch in frames[^1].Channels)
                end = Math.Max(end, ch.Encoding == FrameEncoding.Timestamped ? (ch.Times.Length > 0 ? ch.Times[^1] : frames[^1].T0) : ch.TStart + ch.Dt * Math.Max(0, ch.Count - 1));
        return new Recording(channels, frames, start, end);
    }
}

/// <summary>Configuration object for <see cref="PlaybackSource.StartAsync"/>.</summary>
/// <param name="Recording">What to play.</param>
public sealed record PlaybackConfig(Recording Recording)
{
    /// <summary>Playback rate relative to wall time.</summary>
    public double Speed { get; init; } = 1;
    /// <summary>Restart from the beginning when the end is reached.</summary>
    public bool Loop { get; init; }
    /// <summary>Start playing immediately instead of paused.</summary>
    public bool Autoplay { get; init; }
}

/// <summary>
/// Plays a Recording: <see cref="Tick"/> advances the manual clock by <c>wallDt × Speed</c> and pushes every frame whose time was
/// crossed. Seeking backwards resets the sink and replays from the start up to the target. Mirrors <c>PlaybackSource</c> in TS.
/// </summary>
public sealed class PlaybackSource : ISource
{
    /// <summary><c>"playback"</c>.</summary>
    public string Type => "playback";
    /// <summary>The clock this source drives; hand it to the <see cref="SourceContext"/> as the chart time.</summary>
    public ManualClock Clock { get; } = new();
    private SourceContext? _ctx;
    private Recording? _rec;
    private int _cursor, _layerCursor;
    /// <summary>True while <see cref="Tick"/> advances; cleared at the end of a non-looping recording.</summary>
    public bool Playing { get; private set; }
    /// <summary>Playback rate relative to wall time; may be changed while playing.</summary>
    public double Speed { get; set; } = 1;
    /// <summary>Restart from the beginning when the end is reached.</summary>
    public bool Loop { get; set; }

    /// <summary>The recording loaded by <see cref="StartAsync"/>, or null.</summary>
    public Recording? Recording => _rec;
    /// <summary>Current recording time in seconds.</summary>
    public double Position => Clock.Now();
    /// <summary>Recording length in seconds; 0 when nothing is loaded.</summary>
    public double Duration => _rec is null ? 0 : _rec.End - _rec.Start;
    /// <summary>Position as a fraction of the duration, 0–1.</summary>
    public double Progress => _rec is not null && Duration > 0 ? Math.Clamp((Position - _rec.Start) / Duration, 0, 1) : 0;
    /// <summary>Frames delivered since the start or the last backwards seek.</summary>
    public int FramesPushed => _cursor;
    /// <summary>True once every frame and layer event has been delivered.</summary>
    public bool Finished => _rec is not null && _cursor >= _rec.Frames.Count && _layerCursor >= _rec.LayerEvents.Count;

    /// <summary>Load the <see cref="PlaybackConfig"/>, declare its channels, rewind the clock to the start and apply speed, loop and autoplay. Nothing is pushed until <see cref="Tick"/> or <see cref="Seek"/>.</summary>
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        var cfg = config as PlaybackConfig ?? throw new ArgumentException("PlaybackSource needs a PlaybackConfig", nameof(config));
        _ctx = ctx; _rec = cfg.Recording; Speed = cfg.Speed; Loop = cfg.Loop;
        foreach (var c in cfg.Recording.Channels) ctx.Signals.DeclareChannel(c);
        Clock.Set(cfg.Recording.Start); _cursor = 0; _layerCursor = 0; Playing = cfg.Autoplay;
        return Task.CompletedTask;
    }
    /// <summary>Pause and detach from the context; the recording stays loaded.</summary>
    public Task StopAsync() { Playing = false; _ctx = null; return Task.CompletedTask; }
    /// <summary>Resume; a finished, non-looping recording restarts from the beginning.</summary>
    public void Play() { if (Finished && !Loop) Seek(_rec?.Start ?? 0); Playing = true; }
    /// <summary>Stop advancing; the position is kept.</summary>
    public void Pause() => Playing = false;

    /// <summary>Advance by <paramref name="wallDt"/> seconds of wall time; returns the number of frames pushed.</summary>
    public int Tick(double wallDt)
    {
        if (!Playing || _rec is null || _ctx is null) return 0;
        var target = Clock.Now() + wallDt * Speed;
        if (target > _rec.End)
        {
            if (Loop && _rec.Frames.Count > 0) { var pushed = PushUntil(_rec.End); Seek(_rec.Start); return pushed + Tick(Math.Max(0, (target - _rec.End) / Speed)); }
            target = _rec.End; Playing = false;
        }
        return PushUntil(target);
    }

    /// <summary>Jump to time <paramref name="t"/>: forwards pushes the skipped frames, backwards resets the sink and replays.</summary>
    public void Seek(double t)
    {
        if (_rec is null || _ctx is null) return;
        var target = Math.Clamp(t, _rec.Start, _rec.End);
        if (target < Clock.Now()) { _ctx.Signals.Reset(); _ctx.Layers.Reset(); _cursor = 0; _layerCursor = 0; }
        PushUntil(target);
    }

    /// <summary>Frames and layer events are applied in time order (frames first on ties). Returns the number of frames pushed.</summary>
    private int PushUntil(double t)
    {
        var r = _rec!; var n = 0;
        while (true)
        {
            var f = _cursor < r.Frames.Count ? r.Frames[_cursor] : null;
            var e = _layerCursor < r.LayerEvents.Count ? r.LayerEvents[_layerCursor] : null;
            var ft = f is not null && f.T0 <= t ? f.T0 : double.PositiveInfinity;
            var et = e is not null && e.T <= t ? e.T : double.PositiveInfinity;
            if (double.IsPositiveInfinity(ft) && double.IsPositiveInfinity(et)) break;
            if (ft <= et) { _ctx!.Signals.PushFrame(f!); _cursor++; n++; }
            else { if (e!.Kind is { } kind) _ctx!.Layers.DeclareLayer(e.Id, kind, e.Meta); else _ctx!.Layers.Push(e.Id, e.Payload); _layerCursor++; }
        }
        Clock.Set(t);
        return n;
    }
}

/// <summary>Factory for <see cref="PlaybackSource"/>.</summary>
[SkyScopeSource]
public sealed class PlaybackSourceFactory : ISourceFactory
{
    /// <summary><c>"playback"</c>.</summary>
    public string Type => "playback";
    /// <summary>Name for pickers.</summary>
    public string DisplayName => "Playback (CSV, recordings)";
    /// <summary>Signals with playback control; layer events are replayed too when the recording has them.</summary>
    public SourceCapabilities Capabilities => SourceCapabilities.Signals | SourceCapabilities.Playback;
    /// <summary>None: the configuration is a <see cref="PlaybackConfig"/> object, not JSON.</summary>
    public string? ConfigSchema => null;
    /// <summary>A new, paused playback source.</summary>
    public ISource Create() => new PlaybackSource();
}
