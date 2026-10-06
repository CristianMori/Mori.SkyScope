// Mori.SkyScope — A signal sink that fans frames out to every connected WebSocket as binary SkyScopeFrames.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Streaming;

/// <summary>
/// A signal sink that fans frames out to every connected WebSocket as binary SkyScopeFrames. New clients
/// first receive the channel catalog as a text message. Each client has a bounded queue; when a slow client
/// falls behind, its oldest queued frames are dropped (counted in <see cref="Dropped"/>) rather than stalling
/// the producer.
/// </summary>
public sealed class FrameBroadcaster(int queueCapacity = 256) : ISignalSink, ILayerSink
{
    private readonly ConcurrentDictionary<int, ChannelInfo> _channels = new();
    private readonly ConcurrentDictionary<string, string> _layers = new();
    private static readonly System.Text.Json.JsonSerializerOptions Json = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Layer declarations are remembered and replayed to clients that connect later; pushes are live only.</summary>
    public void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null)
    {
        var text = System.Text.Json.JsonSerializer.Serialize(new { type = "layer", id, kind, meta = meta is null ? null : new Dictionary<string, object?>(meta) }, Json);
        _layers[id] = text;
        var bytes = Encoding.UTF8.GetBytes(text);
        foreach (var c in _clients.Values) Enqueue(c, bytes, isText: true);
    }

    /// <summary>Forwards one layer payload to every connected client: payloads carrying typed arrays go out as a binary SkyScopeLayer message, everything else as a JSON text message.</summary>
    public void Push(string id, object? payload)
    {
        if (_clients.IsEmpty) return;
        if (Mori.SkyScope.Core.Streaming.LayerMessage.HasBinary(payload))
        {
            var bin = Mori.SkyScope.Core.Streaming.LayerMessage.Encode(id, (IReadOnlyDictionary<string, object?>)payload!);
            foreach (var c in _clients.Values) Enqueue(c, bin, isText: false);
            return;
        }
        var bytes = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { type = "push", id, payload = Mori.SkyScope.Core.Scene.LayerJson.ToElement(payload) }, Json));
        foreach (var c in _clients.Values) Enqueue(c, bytes, isText: true);
    }
    private readonly ConcurrentDictionary<Guid, Client> _clients = new();
    private long _dropped;

    private sealed record Client(WebSocket Socket, Channel<byte[]> Queue);

    /// <summary>Number of WebSocket clients currently being served.</summary>
    public int ClientCount => _clients.Count;
    /// <summary>Total queued frames and messages discarded so far because a client fell behind its queue capacity.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);
    /// <summary>Every channel declared so far, ordered by id; this is the catalog a new client receives first.</summary>
    public IReadOnlyCollection<ChannelInfo> Channels => _channels.Values.OrderBy(c => c.Id).ToList();

    /// <summary>Remembers the channel for the catalog and sends a one-entry catalog update to the connected clients.</summary>
    public void DeclareChannel(ChannelInfo info)
    {
        _channels[info.Id] = info;
        var text = Encoding.UTF8.GetBytes(ChannelCatalog.ToJson([info]));
        foreach (var c in _clients.Values) Enqueue(c, text, isText: true);
    }

    /// <summary>Encodes the frame once and queues the bytes for every client; a no-op when nobody is connected.</summary>
    public void PushFrame(SkyScopeFrame frame)
    {
        if (_clients.IsEmpty) return;
        var bytes = FrameCodec.Encode(frame);
        foreach (var c in _clients.Values) Enqueue(c, bytes, isText: false);
    }

    private void Enqueue(Client c, byte[] payload, bool isText)
    {
        var item = isText ? Tag(payload) : payload;
        while (!c.Queue.Writer.TryWrite(item))
        {
            if (c.Queue.Reader.TryRead(out _)) Interlocked.Increment(ref _dropped); else break;
        }
    }

    // Text payloads carry a 0xFF prefix in the queue; binary frames always start with 0x53 (the "S" of the magic), so the two cannot collide.
    private static byte[] Tag(byte[] text) { var t = new byte[text.Length + 1]; t[0] = 0xFF; text.CopyTo(t, 1); return t; }
    private static bool IsTagged(byte[] b) => b.Length > 0 && b[0] == 0xFF;

    /// <summary>Serve one accepted socket until it closes. Call from the endpoint handler.</summary>
    public async Task ServeAsync(WebSocket socket, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var client = new Client(socket, Channel.CreateBounded<byte[]>(new BoundedChannelOptions(queueCapacity) { SingleReader = true }));
        _clients[id] = client;
        try
        {
            var catalog = Encoding.UTF8.GetBytes(ChannelCatalog.ToJson(Channels));
            await socket.SendAsync(catalog, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            foreach (var layer in _layers.Values) await socket.SendAsync(Encoding.UTF8.GetBytes(layer), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            var receive = Task.Run(async () =>
            {
                var buf = new byte[1024];
                try { while (socket.State == WebSocketState.Open) { var r = await socket.ReceiveAsync(buf, ct).ConfigureAwait(false); if (r.MessageType == WebSocketMessageType.Close) break; } }
                catch (Exception) { /* client went away */ }
                client.Queue.Writer.TryComplete();
            }, ct);
            await foreach (var payload in client.Queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                if (socket.State != WebSocketState.Open) break;
                if (IsTagged(payload)) await socket.SendAsync(payload.AsMemory(1), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
                else await socket.SendAsync(payload, WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
            }
            await receive.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _clients.TryRemove(id, out _);
            if (socket.State == WebSocketState.Open) { try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch (Exception) { } }
        }
    }
}
