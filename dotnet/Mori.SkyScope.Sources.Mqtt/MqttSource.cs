// Mori.SkyScope — MQTT source on MQTTnet: subscribes to topic filters and maps payloads to samples through the shared mapping.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text;
using MQTTnet;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Sources.Mqtt;

/// <summary>Connection settings and topic-to-channel rules for <see cref="MqttSource"/>.</summary>
/// <param name="Host">Broker host name, or the WebSocket URI when <see cref="WebSocket"/> is set.</param>
/// <param name="Rules">Topic filters and how their payloads map onto channels.</param>
public sealed record MqttSourceConfig(string Host, IReadOnlyList<MqttRule> Rules)
{
    /// <summary>Broker TCP port; ignored for WebSocket connections.</summary>
    public int Port { get; init; } = 1883;
    /// <summary>Connect over WebSocket (URI like <c>host:9001/mqtt</c>) instead of TCP.</summary>
    public bool WebSocket { get; init; }
    /// <summary>Wrap the connection in TLS.</summary>
    public bool Tls { get; init; }
    /// <summary>User name sent on connect; null for anonymous access.</summary>
    public string? Username { get; init; }
    /// <summary>Password paired with <see cref="Username"/>.</summary>
    public string? Password { get; init; }
    /// <summary>MQTT client identifier; a random <c>skyscope-</c> id is generated when null.</summary>
    public string? ClientId { get; init; }
    /// <summary>Samples are batched into one frame every this many milliseconds.</summary>
    public int BatchMs { get; init; } = 50;
    /// <summary>Marshals sink calls onto a UI thread; null = call on a timer thread.</summary>
    public Action<Action>? Dispatch { get; init; }
}

/// <summary>MQTT source (MQTTnet): subscribes to every rule's topic filter and turns payloads into timestamped frames through <see cref="MqttMapping"/>.</summary>
public sealed class MqttSource : ISource, IAsyncDisposable
{
    /// <summary>Source type key (<c>mqtt</c>).</summary>
    public string Type => "mqtt";
    private IMqttClient? _client;
    private SourceContext? _ctx;
    private MqttSourceConfig? _cfg;
    private readonly List<MqttSample> _pending = [];
    private readonly Lock _lock = new();
    private Timer? _timer;
    private uint _seq;
    /// <summary>True while the MQTT client reports an open connection.</summary>
    public bool Connected => _client?.IsConnected ?? false;
    /// <summary>Count of application messages received since start; incremented on the client receive thread.</summary>
    public long MessagesReceived;

    /// <summary>Declares the channels of every rule, connects to the broker (reconnecting after a two-second pause on disconnect), subscribes to every distinct topic filter and starts the batching timer. The config must be an <see cref="MqttSourceConfig"/>.</summary>
    public async Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        _ctx = ctx; _cfg = config as MqttSourceConfig ?? throw new ArgumentException("MqttSource needs an MqttSourceConfig", nameof(config));
        foreach (var c in MqttMapping.Channels(_cfg.Rules)) ctx.Signals.DeclareChannel(c);
        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        var ob = new MqttClientOptionsBuilder().WithClientId(_cfg.ClientId ?? $"skyscope-{Guid.NewGuid():N}"[..20]);
        ob = _cfg.WebSocket ? ob.WithWebSocketServer(o => o.WithUri(_cfg.Host)) : ob.WithTcpServer(_cfg.Host, _cfg.Port);
        if (_cfg.Tls) ob = ob.WithTlsOptions(o => o.UseTls());
        if (_cfg.Username is not null) ob = ob.WithCredentials(_cfg.Username, _cfg.Password);
        var options = ob.Build();
        _client.ApplicationMessageReceivedAsync += e =>
        {
            Interlocked.Increment(ref MessagesReceived);
            var payload = e.ApplicationMessage.Payload.Length > 0 ? Encoding.UTF8.GetString(e.ApplicationMessage.Payload) : "";
            var samples = MqttMapping.Map(_cfg.Rules, e.ApplicationMessage.Topic, payload, ctx.Clock.Now());
            if (samples.Count > 0) lock (_lock) _pending.AddRange(samples);
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += async e =>
        {
            ctx.Log(LogLevel.Warn, $"mqtt: disconnected ({e.Reason}); reconnecting");
            await Task.Delay(2000, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            try { if (_client is { IsConnected: false } c && !ct.IsCancellationRequested) await c.ConnectAsync(options, ct); } catch (Exception ex) { ctx.Log(LogLevel.Warn, $"mqtt: reconnect failed: {ex.Message}"); }
        };
        await _client.ConnectAsync(options, ct);
        var sb = factory.CreateSubscribeOptionsBuilder();
        foreach (var topic in _cfg.Rules.Select(r => r.Topic).Distinct()) sb = sb.WithTopicFilter(topic);
        await _client.SubscribeAsync(sb.Build(), ct);
        ctx.Log(LogLevel.Info, $"mqtt: connected to {_cfg.Host}, {_cfg.Rules.Count} rules");
        _timer = new Timer(_ => Flush(), null, _cfg.BatchMs, _cfg.BatchMs);
    }

    private void Flush()
    {
        List<MqttSample> batch;
        lock (_lock) { if (_pending.Count == 0) return; batch = [.. _pending]; _pending.Clear(); }
        var frame = MqttMapping.ToFrame(_seq++, batch);
        if (frame is null || _ctx is null) return;
        if (_cfg?.Dispatch is { } d) d(() => _ctx.Signals.PushFrame(frame)); else _ctx.Signals.PushFrame(frame);
    }

    /// <summary>Stops the timer, flushes the pending samples as a last frame and disconnects.</summary>
    public async Task StopAsync()
    {
        _timer?.Dispose(); _timer = null;
        Flush();
        if (_client is not null) { try { await _client.DisconnectAsync(); } catch (Exception) { } _client.Dispose(); _client = null; }
        _ctx = null;
    }
    /// <summary>Equivalent to <see cref="StopAsync"/>.</summary>
    public async ValueTask DisposeAsync() => await StopAsync();
}

/// <summary>Registers the MQTT source with the source registry; found by assembly scanning through <c>[SkyScopeSource]</c>.</summary>
[SkyScopeSource]
public sealed class MqttSourceFactory : ISourceFactory
{
    /// <summary>Source type key, the same as <see cref="MqttSource.Type"/>.</summary>
    public string Type => "mqtt";
    /// <summary>Human-readable name shown in source pickers.</summary>
    public string DisplayName => "MQTT";
    /// <summary>The source produces signals only.</summary>
    public SourceCapabilities Capabilities => SourceCapabilities.Signals;
    /// <summary>No JSON schema; configure with an <see cref="MqttSourceConfig"/> instance.</summary>
    public string? ConfigSchema => null;
    /// <summary>Creates a new, unstarted <see cref="MqttSource"/>.</summary>
    public ISource Create() => new MqttSource();
}
