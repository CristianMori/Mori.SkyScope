// Mori.SkyScope — Template source plugin: declares one regular-rate channel and pushes a counting ramp from a timer.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Sources.Template;

/// <summary>Settings for <see cref="CounterSource"/>.</summary>
public sealed record CounterConfig
{
    /// <summary>Id of the single channel the source declares.</summary>
    public int ChannelId { get; init; } = 1;
    /// <summary>Samples per second.</summary>
    public double Rate { get; init; } = 100;
    /// <summary>Milliseconds of samples per frame.</summary>
    public int BatchMs { get; init; } = 50;
    /// <summary>Run the internal timer; false when a host (or a test) calls <see cref="CounterSource.Push"/> itself.</summary>
    public bool UseTimer { get; init; } = true;
    /// <summary>Marshals sink calls onto a UI thread; null = timer thread.</summary>
    public Action<Action>? Dispatch { get; init; }
}

/// <summary>
/// Template source plugin: declares one regular-rate channel and pushes a counting ramp from a timer.
/// Copy this project, rename it, and replace the timer with your protocol. See docs/PLUGINS.md.
/// </summary>
public sealed class CounterSource : ISource, IDisposable
{
    /// <summary>Source type key (<c>counter</c>).</summary>
    public string Type => "counter";
    private SourceContext? _ctx;
    private CounterConfig _cfg = new();
    private Timer? _timer;
    private long _n;
    private uint _seq;

    /// <summary>Declares the counter channel as a regular-rate signal and, when <see cref="CounterConfig.UseTimer"/> is set, starts the timer that calls <see cref="Push"/> once per batch. A missing config uses the defaults.</summary>
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        _ctx = ctx; _cfg = config as CounterConfig ?? new CounterConfig();
        ctx.Signals.DeclareChannel(new ChannelInfo(_cfg.ChannelId, "counter") { Unit = "count", Timing = ChannelTiming.Regular, Rate = _cfg.Rate });
        if (_cfg.UseTimer) _timer = new Timer(_ => Push(), null, _cfg.BatchMs, _cfg.BatchMs);
        ctx.Log(LogLevel.Info, $"counter: {_cfg.Rate} Hz on channel {_cfg.ChannelId}");
        return Task.CompletedTask;
    }

    /// <summary>Push one frame (called by the timer, or by a host that drives the source itself).</summary>
    public void Push()
    {
        if (_ctx is null) return;
        var count = Math.Max(1, (int)Math.Round(_cfg.Rate * _cfg.BatchMs / 1000.0));
        var tStart = _ctx.Clock.Now();
        var values = new float[count];
        for (var i = 0; i < count; i++) values[i] = _n++;
        var frame = new SkyScopeFrame(_seq++, tStart, [FrameChannel.Regular((ushort)_cfg.ChannelId, tStart, 1 / _cfg.Rate, values)]);
        if (_cfg.Dispatch is { } d) d(() => _ctx.Signals.PushFrame(frame)); else _ctx.Signals.PushFrame(frame);
    }

    /// <summary>Stops the timer and forgets the context, so later <see cref="Push"/> calls are ignored.</summary>
    public Task StopAsync() { _timer?.Dispose(); _timer = null; _ctx = null; return Task.CompletedTask; }
    /// <summary>Stops the timer.</summary>
    public void Dispose() => _timer?.Dispose();
}

/// <summary>Registers the template source with the source registry; found by assembly scanning through <c>[SkyScopeSource]</c>.</summary>
[SkyScopeSource]
public sealed class CounterSourceFactory : ISourceFactory
{
    /// <summary>Source type key, the same as <see cref="CounterSource.Type"/>.</summary>
    public string Type => "counter";
    /// <summary>Human-readable name shown in source pickers.</summary>
    public string DisplayName => "Counter (template)";
    /// <summary>The source produces signals only.</summary>
    public SourceCapabilities Capabilities => SourceCapabilities.Signals;
    /// <summary>JSON schema of the configurable fields: channel id, rate and batch size.</summary>
    public string? ConfigSchema => """{"type":"object","properties":{"channelId":{"type":"integer"},"rate":{"type":"number"},"batchMs":{"type":"number"}}}""";
    /// <summary>Creates a new, unstarted <see cref="CounterSource"/>.</summary>
    public ISource Create() => new CounterSource();
}
