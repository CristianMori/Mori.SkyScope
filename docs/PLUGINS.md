# Writing a source plugin

Everything that produces data for Mori.SkyScope is a **source plugin**. The core never knows a protocol: a
source pushes into two sinks (signals and scene layers), reads chart time from a clock, and logs through the
context it was started with. The same contract exists in TypeScript (`@mori/skyscope-core`) and C#
(`Mori.SkyScope.Core`), and both are pinned by the shared fixtures in `spec/fixtures/`.

## The contract

```
SourceContext
  signals : SignalSink    declareChannel(info) · pushFrame(frame) · reset?()
  layers  : LayerSink     declareLayer(id, kind, meta?) · push(id, payload)
  clock   : TimeSource    now() — seconds; live sources use the wall clock, playback sources drive their own
  log     : (level, message) => void

Source
  type : string
  start(ctx, config) · stop()

SourceFactory
  type · displayName · capabilities ("signals" | "layers" | "playback") · configSchema? · create()
```

- **Channels** are declared once (`id`, `name`, `unit`, `kind`, `timing`, `rate`). Declaring before pushing lets
  the store size its ring buffer from `rate × retention`; undeclared channels are auto-declared with defaults.
- **Frames** are `SkyScopeFrame`s: one batch per channel, either *regular* (`tStart`, `dt`, values) or
  *timestamped* (times, values). Batch 10–50 ms of samples per frame; never push one sample at a time on a hot path.
- **Layers** use the JSON layer contract (`createLayer` / `applyLayerPayload`, C# `LayerJson`): declare a layer
  with a kind (`occupancyGrid`, `pointCloud`, `pose`, `shapes`, `trail`, `polyline`, `points`, `bitmap`, `grid`)
  and push updates such as `{ x, y, yaw }` for a pose or `{ scan, pose }` for a lidar. Whatever you push can be
  relayed unchanged over the WebSocket stream and lands in any SceneView.
- **Time**: use `ctx.clock.now()` for receive-time stamps so charts line up; use message timestamps when the
  protocol has them (ROS headers, MQTT JSON fields).
- **Threads (.NET)**: sinks are called from your thread. `SignalStore` locks internally; UI-bound sinks (WPF
  scene, gauges) expect a `Dispatch` option to marshal onto the UI thread — see `WebSocketSourceConfig.Dispatch`.

## The template

Two minimal, working plugins live next to the real ones and are the recommended starting point:

| Language | Location | Package name |
|---|---|---|
| TypeScript | `ts/packages/source-template` | `@mori/skyscope-source-template` |
| C# | `dotnet/Mori.SkyScope.Sources.Template` | `Mori.SkyScope.Sources.Template` |

Each implements a "counter" source: it declares one channel and pushes a regular-rate ramp from a timer, shows
how to expose a factory (`[SkyScopeSource]` in .NET, an exported `SourceFactory` in TS), and ships a test that runs
the source against a `SignalStore` and asserts what arrived. Copy the folder, rename the package, replace the
timer with your protocol.

### TypeScript walkthrough

```ts
import { type Source, type SourceContext, type SourceFactory } from "@mori/skyscope-core";

export interface CounterConfig { channelId?: number; rate?: number }

export class CounterSource implements Source {
  readonly type = "counter";
  private timer: ReturnType<typeof setInterval> | null = null;
  start(ctx: SourceContext, config: CounterConfig = {}): void {
    const id = config.channelId ?? 1, rate = config.rate ?? 100, dt = 1 / rate;
    ctx.signals.declareChannel({ id, name: "counter", timing: "regular", rate });
    let seq = 0, n = 0;
    this.timer = setInterval(() => {
      const count = Math.round(rate / 20);                       // 50 ms per frame
      const values = Float32Array.from({ length: count }, () => n++);
      ctx.signals.pushFrame({ seq: seq++, t0: ctx.clock.now(), channels: [{ id, encoding: "regular", tStart: ctx.clock.now(), dt, values }] });
    }, 50);
  }
  stop(): void { if (this.timer) clearInterval(this.timer); this.timer = null; }
}

export const counterFactory: SourceFactory = { type: "counter", displayName: "Counter", capabilities: ["signals"], create: () => new CounterSource() };
```

Register it where the app builds its registry: `registry.register(counterFactory)`; hosts that let users pick a
source list `registry.list()` and call `registry.create(type)`.

### C# walkthrough

```csharp
[SkyScopeSource]
public sealed class CounterSourceFactory : ISourceFactory { /* Type, DisplayName, Capabilities, Create */ }

public sealed class CounterSource : ISource
{
    public string Type => "counter";
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default) { /* declare, start a timer, push frames */ }
    public Task StopAsync() { /* stop the timer */ }
}
```

`SourceRegistry.Scan(assembly)` picks up every `[SkyScopeSource]` factory, so a plugin assembly dropped next to
the app is discovered without code changes.

## Testing a plugin

- Unit-test the *mapping* (bytes or text → samples) as a pure function, the way `MqttMapping` and the ROS 2 CDR
  decoders are written; keep I/O out of it.
- Run the source against a `SignalStore` with a `ManualClock` and assert on `store.get(id).buffer` (the template
  test does exactly this).
- If the plugin exists in both languages, add a fixture under `spec/fixtures/` and a driver in both fixture
  runners; the two suites then prove the implementations agree.

## Recording what a plugin produces

Wrap the sinks in an `McapRecorder` (TS: `new McapRecorder({ signals: store, layers })`; C#: `new McapRecorder(store, layers)`)
and hand the recorder to the plugin as both sinks. It forwards everything and, between `start()` and `stop()`, writes an
MCAP file with the frames, the channel catalog and every layer message. The file plays back through `PlaybackSource`
into a store and any layer sink — the same code path as live data.

## Shipping data to browsers

Browsers cannot open raw sockets, so desktop-side plugins (ROS 2, MQTT over TCP, serial…) run in a .NET process and
are relayed: point the plugin at a `FrameBroadcaster` (it is both an `ISignalSink` and an `ILayerSink`), map it
with `app.MapSkyScopeStream("/ws", broadcaster)`, and every web chart and scene connected to `/ws` receives the
channels, frames and layers. `samples/Mori.SkyScope.DemoServer` is the reference: a synthetic signal source and a
synthetic robot scene relayed to React, Blazor and WPF clients.

## 3D layers and binary payloads

The same `LayerSink` carries the 3D scene. Declare a layer with one of the 3D kinds (`pointCloud3d`, `laserScan3d`,
`path3d`, `pose3d`, `markers`, `occupancyGrid3d`, `axes`, `grid3d`) and give it a `frame`; push transforms into a
`frames` layer (`{ transforms: [{ child, parent, t: [x, y, z], q: [x, y, z, w], time? }] }`, omit `time` for static
ones) and the sink's `FrameTree` places every layer in the view's fixed frame at its `stamp`.

Payloads with typed arrays — `{ positions: Float32Array, intensities?: Float32Array, colors?: Uint8Array, stamp }` in
TypeScript, a `Dictionary<string, object?>` holding `float[]`/`byte[]` in C# — are sent as binary `SkyScopeLayer`
messages by the broadcaster and recorded on a second MCAP channel of the same `/layers/<id>` topic (message
encoding `skyscope-layer`); plain-array JSON payloads still work for small data. Put state that a late joiner needs
(static transforms, a marker set) into the declaration meta (`transforms`, `markers`): declarations are replayed to new
clients and written first into recordings, pushes are not. `SyntheticScene3DSource` in the core is the reference
producer; `Ros2Source` with `Scene3D = true` maps PointCloud2, TF, MarkerArray, Path, LaserScan, Odometry and
OccupancyGrid onto these kinds.

## Robot models (URDF)

`parseUrdf(xml)` (TS) / `Urdf.Parse(xml)` (C#) turn a URDF into links, joints and materials. `publishUrdf(sink, model, { prefix })`
declares the visuals as a marker set and the joints as static transforms, and returns a function that pushes the
joint transforms for a set of joint positions — call it from a JointState. Mesh visuals name a resource URI; load the
file (`parseMeshResource`) and push it to a `meshes` layer (`{ uri, positions, normals?, indices? }`, binary on the
wire) and every marker that references the URI draws it. In .NET, `Ros2SourceConfig.Urdf` plus `MeshRoots`
(package name → folder) does all of this for a ROS 2 robot; the demo server's synthetic scene carries a two-joint arm
the same way.
