// Mori.SkyScope — Demo server: synthetic signals, digital I/O and robot scenes streamed over WebSocket, with remote-controlled MCAP recording.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Streaming;

// Streams synthetic signals as SkyScopeFrames: ws://localhost:5055/ws
//   dotnet run --project samples/Mori.SkyScope.DemoServer -- --channels 200 --rate 1000 --batch-ms 20
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["urls"] ?? "http://localhost:5055");
var app = builder.Build();

int Arg(string name, int dflt) => int.TryParse(app.Configuration[name], out var v) ? v : dflt;
var config = new SyntheticSourceConfig
{
    ChannelCount = Arg("channels", 16),
    Rate = Arg("rate", 1000),
    BatchMs = Arg("batch-ms", 20),
    Seed = Arg("seed", 1),
    Quantized = app.Configuration["quantized"] is "true" or "1",
};

var broadcaster = new FrameBroadcaster();
var source = new SyntheticSource();
// Digital I/O (0/1 square waves at staggered periods) on channels 200–204, declared as digital so the signal tree and
// the chart treat them as logic tracks; a slower source of its own (100 Hz, 50 ms frames).
var digital = new SyntheticSource();
var digitalConfig = new SyntheticSourceConfig
{
    Rate = 100, BatchMs = 50, Seed = 7,
    Channels = new (int Id, string Name, double Hz, double Phase)[] { (200, "io/pump", 0.2, 0), (201, "io/valve", 0.37, 1.1), (202, "io/estop", 0.05, 2.0), (203, "io/alarm", 0.11, 0.4), (204, "io/door", 0.8, 3.0) }
        .Select(d => new SyntheticChannel(d.Id, new SynthSpec(Waveform.Square) { Frequency = d.Hz, Amplitude = 0.5, Offset = 0.5, Phase = d.Phase, Noise = 0 }, d.Name, null, ChannelKind.Digital)).ToList(),
};
// --record out.mcap: also record everything the server streams (signals and scene layers) to an MCAP file until shutdown.
// --record-compression zstd: write the recording as zstd-compressed chunks (default none, unchunked).
var recordPath = app.Configuration["record"];
var recordCompression = app.Configuration["record-compression"]?.ToLowerInvariant() switch
{
    "zstd" => Mori.SkyScope.Core.Mcap.McapCompression.Zstd,
    null or "" or "none" => Mori.SkyScope.Core.Mcap.McapCompression.None,
    var other => throw new ArgumentException($"--record-compression {other}: expected none or zstd"),
};
var recorder = new Mori.SkyScope.Core.Mcap.McapRecorder(broadcaster, broadcaster, options: new Mori.SkyScope.Core.Mcap.McapRecorderOptions { Compression = recordCompression });
var ctx = new SourceContext(recorder, recorder, new LiveClock(), (level, msg) => app.Logger.LogInformation("[{Level}] {Message}", level, msg));
// A synthetic robot (map, lidar, pose, trail) relayed as layer messages; its pose is also streamed as channels 100–103.
var scene = new SyntheticSceneSource();
// A synthetic 3D scene (frame tree, spinning lidar cloud as binary layer messages, trail, pose, markers); --scene3d false disables it.
var scene3d = app.Configuration["scene3d"] is not "false" ? new SyntheticScene3DSource() : null;

app.UseWebSockets();
app.MapSkyScopeStream("/ws", broadcaster);
app.MapGet("/", () => Results.Text(
    $"Mori.SkyScope demo server\n  stream: /ws\n  channels: {config.ChannelCount} × {config.Rate} Hz, frames every {config.BatchMs} ms, plus 5 digital I/O channels (200–204) at 100 Hz" +
    $"\n  clients: {broadcaster.ClientCount}, dropped: {broadcaster.Dropped}\n", "text/plain"));
app.MapGet("/channels", () => Results.Text(ChannelCatalog.ToJson(broadcaster.Channels), "application/json"));
// Remote-controlled recording: GET /record → stats; POST /record/start?path=out.mcap; POST /record/stop → writes the file.
var recordTarget = recordPath;
app.MapGet("/record", () => Results.Json(recorder.Stats()));
app.MapPost("/record/start", (string? path) => { recordTarget = path ?? recordTarget ?? "recording.mcap"; recorder.StartFile(recordTarget); return Results.Json(new { recording = true, path = recordTarget }); });
app.MapPost("/record/stop", () => { var was = recorder.IsRecording; recorder.Stop(); var ok = was && recordTarget is not null; return Results.Json(new { saved = ok, path = recordTarget, bytes = ok ? new FileInfo(recordTarget!).Length : 0 }); });

app.Lifetime.ApplicationStarted.Register(() => { if (recordPath is not null) { recorder.StartFile(recordPath); app.Logger.LogInformation("recording to {Path}", recordPath); } source.StartAsync(ctx, config); digital.StartAsync(ctx, digitalConfig); scene.StartAsync(ctx, new SyntheticSceneConfig()); });

if (scene3d is not null) app.Lifetime.ApplicationStarted.Register(() => scene3d.StartAsync(ctx, new SyntheticScene3DConfig()));
app.Lifetime.ApplicationStopping.Register(() =>
{
    source.StopAsync().GetAwaiter().GetResult(); scene.StopAsync().GetAwaiter().GetResult();
    if (recorder.IsRecording) { recorder.Stop(); app.Logger.LogInformation("wrote {Path} ({Bytes} bytes)", recordTarget, new FileInfo(recordTarget!).Length); }
});
app.Run();
