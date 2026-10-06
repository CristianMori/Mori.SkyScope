// Mori.SkyScope — Tests the frame broadcaster, the WebSocket endpoint and the client source end to end.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;
using Mori.SkyScope.Streaming;

namespace Mori.SkyScope.Core.Tests;

/// <summary>Tests the frame broadcaster, the WebSocket endpoint and the client source end to end on a loopback Kestrel host.</summary>
public class StreamingTests
{
    /// <summary>A client source connected to the endpoint receives the catalog, a later channel declaration and every frame into its store, and the broadcaster sees it connect and disconnect.</summary>
    [Fact]
    public async Task Broadcaster_streams_catalog_and_frames_to_a_WebSocketFrameSource()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var broadcaster = new FrameBroadcaster();
        broadcaster.DeclareChannel(new ChannelInfo(1, "speed") { Unit = "m/s", Rate = 4 });
        app.UseWebSockets();
        app.MapSkyScopeStream("/ws", broadcaster);
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            var url = new Uri(address.Replace("http://", "ws://") + "/ws");

            var store = new SignalStore(retentionSeconds: 10);
            var logs = new List<string>();
            var ctx = new SourceContext(store, new NullLayerSink(), new ManualClock(), (l, m) => { lock (logs) logs.Add($"{l}:{m}"); });
            var source = new WebSocketFrameSource();
            await source.StartAsync(ctx, new WebSocketSourceConfig(url) { Reconnect = TimeSpan.Zero });

            await Until(() => broadcaster.ClientCount == 1 && store.Channels.ContainsKey(1));
            broadcaster.PushFrame(new SkyScopeFrame(1, 0, [FrameChannel.Regular(1, 0, 0.25, [1, 2, 3])]));
            broadcaster.DeclareChannel(new ChannelInfo(7, "event") { Timing = ChannelTiming.Timestamped });
            broadcaster.PushFrame(new SkyScopeFrame(2, 0, [FrameChannel.Timestamped(7, [5], [9])]));
            await Until(() => source.FramesReceived == 2 && store.Channels.ContainsKey(7) && store.Get(7)!.Buffer.Length == 1);

            Assert.Equal("m/s", store.Get(1)!.Info.Unit);
            Assert.Equal(3, store.Get(1)!.Buffer.Length);
            Assert.Equal(0, store.Dropped);
            Assert.True(source.Connected);

            await source.StopAsync();
            await Until(() => broadcaster.ClientCount == 0);
        }
        finally { await app.StopAsync(); }
    }

    private static async Task Until(Func<bool> pred, int ms = 5000)
    {
        var t0 = Environment.TickCount64;
        while (!pred()) { if (Environment.TickCount64 - t0 > ms) throw new TimeoutException(); await Task.Delay(10); }
    }
}
