// Mori.SkyScope — WebSocket frame source: binary frames into a sink, catalog and layer messages relayed, reconnect; plus the channel catalog JSON.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Sources;

/// <summary>Configuration object for <see cref="WebSocketFrameSource.StartAsync"/>.</summary>
/// <param name="Url">The <c>ws://</c> or <c>wss://</c> endpoint.</param>
public sealed record WebSocketSourceConfig(Uri Url)
{
    /// <summary>Reconnect delay after a drop; zero disables.</summary>
    public TimeSpan Reconnect { get; init; } = TimeSpan.FromSeconds(1);
    /// <summary>Optional marshalling: run sink calls through this (e.g. a UI dispatcher). Default: the receive thread.</summary>
    public Action<Action>? Dispatch { get; init; }
}

/// <summary>Binary SkyScopeFrames over a WebSocket into a signal sink; text messages carry the channel catalog. Mirrors <c>websocket-source.ts</c>.</summary>
public sealed class WebSocketFrameSource : ISource
{
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary><c>"websocket"</c>.</summary>
    public string Type => "websocket";
    /// <summary>Binary frames decoded successfully since start.</summary>
    public long FramesReceived { get; private set; }
    /// <summary>Binary payload bytes received since start (text messages excluded).</summary>
    public long BytesReceived { get; private set; }
    /// <summary>True while the socket is open; false between reconnect attempts.</summary>
    public bool Connected { get; private set; }

    /// <summary>Start the receive loop on a background task: connect, decode binary frames into the signal sink, route text control messages (<c>channels</c>, <c>layer</c>, <c>push</c>) to the sinks, and reconnect after <see cref="WebSocketSourceConfig.Reconnect"/> on a drop. Returns immediately.</summary>
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        var cfg = config as WebSocketSourceConfig ?? throw new ArgumentException("WebSocketSourceConfig required", nameof(config));
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = Task.Run(() => RunAsync(ctx, cfg, _cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    private async Task RunAsync(SourceContext ctx, WebSocketSourceConfig cfg, CancellationToken ct)
    {
        var dispatch = cfg.Dispatch ?? (a => a());
        var buffer = new byte[1 << 16];
        while (!ct.IsCancellationRequested)
        {
            using var ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(cfg.Url, ct).ConfigureAwait(false);
                Connected = true;
                ctx.Log(LogLevel.Info, $"websocket: connected {cfg.Url}");
                using var ms = new MemoryStream();
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    var payload = ms.ToArray();
                    if (r.MessageType == WebSocketMessageType.Text) HandleText(ctx, dispatch, Encoding.UTF8.GetString(payload));
                    else HandleFrame(ctx, dispatch, payload);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { ctx.Log(LogLevel.Warn, $"websocket: {e.Message}"); }
            finally { Connected = false; }
            if (cfg.Reconnect <= TimeSpan.Zero) break;
            try { await Task.Delay(cfg.Reconnect, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    private void HandleFrame(SourceContext ctx, Action<Action> dispatch, byte[] payload)
    {
        BytesReceived += payload.Length;
        SkyScopeFrame frame;
        try { frame = FrameCodec.Decode(payload); }
        catch (FormatException e) { ctx.Log(LogLevel.Warn, $"websocket: bad frame ({e.Message})"); return; }
        FramesReceived++;
        dispatch(() => ctx.Signals.PushFrame(frame));
    }

    private static void HandleText(SourceContext ctx, Action<Action> dispatch, string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "channels":
                    {
                        var channels = root.GetProperty("channels").EnumerateArray().Select(ChannelCatalog.Parse).ToList();
                        dispatch(() => { foreach (var c in channels) ctx.Signals.DeclareChannel(c); });
                        break;
                    }
                case "layer":
                    {
                        string id = root.GetProperty("id").GetString()!, kind = root.GetProperty("kind").GetString()!;
                        var meta = root.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Object ? Scene.LayerJson.ToMeta(m.Clone()) : null;
                        dispatch(() => ctx.Layers.DeclareLayer(id, kind, meta));
                        break;
                    }
                case "push":
                    {
                        var id = root.GetProperty("id").GetString()!; var payload = root.TryGetProperty("payload", out var p) ? p.Clone() : default;
                        dispatch(() => ctx.Layers.Push(id, payload));
                        break;
                    }
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException) { ctx.Log(LogLevel.Warn, "websocket: unparseable text message"); }
    }

    /// <summary>Cancel the receive loop and wait for it to exit; safe to call when not started.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        try { if (_loop is not null) await _loop.ConfigureAwait(false); } catch (OperationCanceledException) { }
        _cts.Dispose(); _cts = null; _loop = null;
    }
}

/// <summary>JSON form of <see cref="ChannelInfo"/> used by the "channels" control message (camelCase, same as TS).</summary>
public static class ChannelCatalog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Serialize a <c>{ type: "channels", channels: [...] }</c> control message; null fields are omitted, kind and timing are lower-case strings.</summary>
    public static string ToJson(IEnumerable<ChannelInfo> channels) => JsonSerializer.Serialize(new
    {
        type = "channels",
        channels = channels.Select(c => new
        {
            id = c.Id, name = c.Name, unit = c.Unit, kind = c.Kind?.ToString().ToLowerInvariant(),
            timing = c.Timing is { } t ? (t == ChannelTiming.Timestamped ? "timestamped" : "regular") : null, rate = c.Rate, color = c.Color,
        }),
    }, Options);

    /// <summary>Parse one entry of the <c>channels</c> array; optional fields of the wrong JSON type are treated as absent.</summary>
    public static ChannelInfo Parse(JsonElement e) => new(e.GetProperty("id").GetInt32(), e.GetProperty("name").GetString() ?? "")
    {
        Unit = e.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null,
        Kind = e.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? Enum.Parse<ChannelKind>(k.GetString()!, true) : null,
        Timing = e.TryGetProperty("timing", out var t) && t.ValueKind == JsonValueKind.String ? (t.GetString() == "timestamped" ? ChannelTiming.Timestamped : ChannelTiming.Regular) : null,
        Rate = e.TryGetProperty("rate", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDouble() : null,
        Color = e.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null,
    };
}
