# Mori.SkyScope — real-time data-viz component library (one-week plan)

## Context

Started 2026-09-04 as "our own Syncfusion/DevExpress" (working name *domos*). On 2026-09-05 the
scope pivoted and the name was fixed: **Mori.SkyScope**, joining the user's Mori.* family
(Mori.Ros2Sharp, Mori.SkyFrame, Mori.SkySong). Target apps are **robotics / automation / data-heavy** —
IOComp-class real-time trend charts (pause, zoom, rearrange signals), car-dashboard gauges, the
usual analytic charts, and a **composable layered scene** (bitmap background + lidar scan + paths +
overlays). Timebox: one week. User's call: **nothing slips**.

### Decisions taken in discussion
- **Topology: all of them** — backend→browser, same-process desktop, edge→remote viewers.
  ⇒ Web engine is TypeScript (Blazor Server can only render in-browser); desktop engine is
  C# + SkiaSharp. Both implement one headless-core spec and pass the same fixtures.
- **Peak: ~100–500 channels × 1 kHz.** ⇒ WebGL series renderer, worker-thread ingest, incremental
  M4 decimation, binary batched frames are mandatory, not stretch.
- **Timing: mix of regular-rate (implicit time) and explicitly timestamped channels.**
- **Data plane ≠ control plane**: samples never pass through Blazor interop or React props.
- **Scene is 2D this week with a 3D-ready API**: `Camera`/`Layer` interfaces are dimension-agnostic
  (`Camera2D` now, `Camera3D` later); layers declare supported camera kinds; hit-tests return
  world-space `Vector3` (z = 0 for 2D layers).
- **Adapters, all first-class**: raw binary WebSocket + `Mori.SkyScope.Streaming` server; file
  playback (CSV + MCAP) with time scrubber; MQTT over WebSocket; **ROS 2 via Mori.Ros2Sharp**
  (https://github.com/CristianMori/Mori.Ros2Sharp — pure-managed RTPS/CDR node with a `.msg` source
  generator; `sensor_msgs`/`nav_msgs`/`geometry_msgs` embedded).
  - ROS 2 adapter is **.NET-side only** (`Mori.SkyScope.Sources.Ros2`): `Ros2Node.CreateSubscription`
    → generated `LaserScan`/`Odometry`/`Imu`/`Float64MultiArray`/`OccupancyGrid` → sinks.
    Browsers get ROS data relayed by `Mori.SkyScope.Streaming` as `SkyScopeFrame`s / layer messages.
  - Known limit: Ros2Sharp has **no RTPS fragmentation yet (~60 KB payload cap)**. `LaserScan` (≈9 KB)
    and `Odometry` are fine; large `OccupancyGrid`/`PointCloud2` won't arrive until fragmentation
    lands in Ros2Sharp. The adapter logs and skips oversized topics.

### First consumers (drive the day-7 samples)
1. **A new robot dashboard** — web + desktop; live signals, lidar/map scene, gauges. The day-7
   "robot dashboard" sample is its seed.
2. **Unitree Go2 tooling** (`D:\DataDrive\go2`, empty so far) — Go2 speaks DDS (CycloneDDS);
   Ros2Sharp's RTPS should interoperate but is only validated against Fast DDS → a day-6 spike,
   not a promise.
3. **Open Duck Mini v2** (`D:\DataDrive\MiniDuck\Open_Duck_Mini`, Python) — **no provision now**
   (user's call). Its feed becomes a later source plugin; the plugin architecture below is what
   makes that possible without touching the core.

### Related user projects (context only, not modified)
- `D:\DataDrive\AMR Simulation` — .NET 9 API + React/Vite/Three.js/SignalR UI, coordinates in mm. Later consumer.
- `D:\DataDrive\RosTest` — native ROS 1 publisher in C# (XML-RPC + TCPROS).

### What carries over from the domos scaffold (all uncommitted)
Tokens pipeline (`design/`, `tools/build-design.mjs`), fixture harness (`spec/fixtures`, C# and TS
runners), `Field`/validation core, Button + TextField (become the "chrome" around instruments:
toolbars, legends, dialogs). All get renamed; nothing is thrown away.

## Naming

| Thing | Name |
|---|---|
| Repo folder | `D:\DataDrive\Mori.SkyScope` (rename `domos` on day 1; git history kept) |
| .NET packages | `Mori.SkyScope.Core`, `.Core.Tests`, `.Render.Skia`, `.Wpf`, `.WinForms`, `.Blazor`, `.Streaming`, `.Sources.Ros2`, `.Sources.Mqtt`, `.Sources.Playback` |
| npm packages | `@mori/skyscope-core`, `-tokens`, `-render`, `-react`, `-blazor` (JS bridge), `-sources` |
| Source plugins | `Mori.SkyScope.Sources.<Name>` (.NET), `@mori/skyscope-source-<name>` (TS) |
| CSS / tokens prefix | `--skyscope-*`, `.skyscope-*` |
| Wire format | `SkyScopeFrame` |
| Root namespace (C#) | `Mori.SkyScope` |

## Architecture

```
spec/fixtures/*.json + spec/frames/*.bin   behaviour fixtures + golden binary frames, run by BOTH cores
design/tokens, design/styles               tokens feed painters too (chart palette, gauge colours)

TS (web — React AND Blazor share it)
  @mori/skyscope-core      headless: ring buffers, M4 buckets, frame codec, scales/ticks, layout, camera,
                           scene/layer model, hit-testing, interaction state machines, gauge geometry
  @mori/skyscope-render    Painter impls: Canvas2D (axes/text/gauges/overlays), WebGL (line series),
                           layer surface cache; Worker ingest (WebSocket → SharedArrayBuffer ring buffers)
  @mori/skyscope-react     TrendChart, Gauge*, XYChart, BarChart, PieChart, PolarChart, Heatmap,
                           SceneView (+ Button/TextField chrome)
  @mori/skyscope-blazor    same engine behind mount(el, config) / update / dispose for JS interop
  @mori/skyscope-sources   built-in source plugins: WebSocketFrameSource, MqttSource, PlaybackSource, SyntheticSource

.NET
  Mori.SkyScope.Core             headless mirror of skyscope-core (same fixtures)
  Mori.SkyScope.Render.Skia      SkiaSharp Painter + layer surface cache
  Mori.SkyScope.Wpf              SKElement host (CPU raster); GL host is a stretch goal
  Mori.SkyScope.WinForms         SKGLControl host (GPU)
  Mori.SkyScope.Blazor           Razor components wrapping skyscope-blazor (control plane only)
  Mori.SkyScope.Streaming        frame encoder, ASP.NET Core WebSocket publisher, in-process feed,
                                 server-side display-rate decimation for remote viewers
  Mori.SkyScope.Sources.*        Ros2 (Mori.Ros2Sharp), Mqtt, Playback (CSV/MCAP) — each a plugin
```

Rule: the core emits geometry; a `Painter` with ~12 primitives (line, polyline, rect, arc, path,
text, image, surface, clip, transform, createLayerSurface, drawSurface) paints it.

### Plugin architecture (data sources)
Everything that produces data is a **source plugin**; the core never knows a specific protocol.
```
ISource            id, displayName, capabilities (signals | layers | playback), configSchema (JSON)
  start(ctx, config) / stop()
  ctx.signals : SignalSink   — declareChannel(id, name, unit, kind, rate?) ; push(frame | samples)
  ctx.layers  : LayerSink    — declareLayer(id, kind) ; push(id, payload)
  ctx.clock   : TimeSource   — maps source time → chart time; playback sources drive it
  ctx.log
ISourceRegistry    register(factory) ; list() ; create(id, config)
```
- Identical shape in TS (`@mori/skyscope-core`) and C# (`Mori.SkyScope.Core`); fixtures pin the
  sink semantics (channel declaration, out-of-order pushes, clock mapping).
- Built-in this week: `WebSocketFrameSource` (SkyScopeFrame over WS), `MqttSource`, `PlaybackSource`
  (CSV/MCAP), `Ros2Source` (.NET only), `SyntheticSource` (demo/bench generator).
- Discovery: explicit `registry.register(...)` plus, in .NET, assembly scanning for
  `[SkyScopeSource]`-attributed factories; in TS, packages export a `source` factory. Later sources
  (Open Duck Mini, Go2 SDK, PLC/EtherNet-IP) are separate packages that touch nothing here.
- Same pattern for the other extension points: `IPainter` (renderers), `ILayer` (scene layers),
  `IGaugeShape` — one registry each, so a new gauge or layer type is also a plugin.

### Data plane: `SkyScopeFrame` binary format
```
header:      u32 magic 'SKSF', u16 version, u32 seq, f64 t0, u16 channelCount
per channel: u16 id, u32 count, u8 encoding
  enc 0  f64 timestamps[count], f32 values[count]         irregular / event signals
  enc 1  f64 dt, f32 values[count]                        regular rate, implicit time
  enc 2  f64 dt, i16 values[count], f32 scale, f32 offset quantized (ADC-style)
```
Encoder/decoder in C#, TS and Python; `spec/frames/*.bin` + JSON expectations prove round-trips
in every direction. Frames every 10–20 ms. 500 ch × 1 kHz × f32 ≈ 2 MB/s raw.

### Ingest → display
Zero-copy typed-array views → per-channel SPSC ring buffer → incremental M4 buckets at
quantum = window / pixelWidth. Render loop at display rate reads ≤ 4×width vertices per channel.
Pause = stop advancing the window; ingest continues. Zoom = rebuild buckets from raw (one pass).
Remote viewers: client sends (window, width), server streams buckets instead of raw.

## Schedule

| Day | Deliverable |
|---|---|
| 1 | **Rename + core foundations**. Rename repo/packages/prefixes to Mori.SkyScope, Apache-2.0. Then, in both languages and all fixture-pinned: ring buffers (regular + timestamped), M4 incremental + rebuild, `SkyScopeFrame` codec with golden files, scales (linear/log/time), nice-tick + time-tick generation, `ISource`/`SignalSink`/`LayerSink`/registry contracts + `SyntheticSource`. `Painter` interface; Canvas2D painter; SkiaSharp painter. |
| 2 | **Scene engine**: layers, camera (pan/zoom/rotate), layer surface caching, hit-testing, interaction state machines (pan, wheel zoom, box zoom, cursor, drag). WebGL polyline renderer. Worker ingest over WebSocket (web); background-thread ingest (.NET). |
| 3 | **TrendChart** (flagship): multi-Y-axis, stacked lanes or overlay, drag signals between lanes/axes, live legend, pause/resume/review scroll-back, zoom modes, single/dual cursors with delta readout, threshold bands, event markers, digital/state track. React + Blazor wrappers. |
| 4 | **Gauges**: radial, linear, LED / LED array, numeric display, compass, attitude indicator; inputs: knob, switch, slider; needle damping. React + Blazor + **first WPF component** (proves the Skia path). |
| 5 | **Charts**: XY line/scatter, bar (grouped/stacked), area, pie/donut, polar, heatmap/spectrogram, histogram — sharing axis/legend/tooltip machinery with TrendChart. |
| 6 | **SceneView**: BitmapLayer (affine-georeferenced), OccupancyGridLayer, PointCloudLayer (LaserScan polar→XY), PathLayer, PoseLayer, ShapeLayer, GridLayer; tools (pan/zoom/rotate, measure, select). **Source plugins**: `WebSocketFrameSource`, `PlaybackSource` (CSV/MCAP, scrubber), `MqttSource`, `Ros2Source` via Mori.Ros2Sharp (+ Go2/CycloneDDS interop spike). |
| 7 | **Samples + ship**: "robot dashboard" in React, Blazor and WPF; `Mori.SkyScope.Streaming` demo server generating 200 ch × 1 kHz via `SyntheticSource`; a "write your own source plugin" doc + template package; NuGet + npm packaging; CI. |

## Verification
- `dotnet test` and `npx vitest run` — every fixture passes in both cores, including binary frame
  round-trips (C#→TS and TS→C#) and the source-sink contract fixtures.
- Throughput bench (TS + C#): ingest 500 ch × 1 kHz for 60 s; assert zero dropped frames, render
  loop ≥ 30 fps at 1920 px wide.
- Sample apps run: React (`npm run sample`), Blazor Server (`dotnet run`), WPF; each shows the same
  dashboard fed by the demo server.
- Browser check via Chrome tools: chart scrolls at 60 fps with 200 live channels; pause, zoom,
  cursors, lane drag all work.

## License hygiene
**Apache-2.0** (user's choice, 2026-09-05). Update `Directory.Build.props` (`PackageLicenseExpression`),
every `package.json`, and add `LICENSE` + `NOTICE` on day 1. No decompiled vendor code (IOComp,
SciChart, LightningChart). Inspiration from public APIs only. Deps: MIT/Apache/BSD only
(SkiaSharp MIT, Skia BSD, Mori.Ros2Sharp — user's own).

## Sprint 2 — 3D viewport (RViz-class), 2026-09-08 → 2026-09-12

Decisions (2026-09-08): scope is a viewer, not a simulator (RViz, not Gazebo). Web renderer is hand-written
WebGL2 with GLSL ES 300 shaders shared with the desktop; desktop host is OpenTK 4 on `GLWpfControl`.
RTPS fragmentation in Mori.Ros2Sharp is deferred: large point clouds reach browsers through the relay from a
machine running full ROS 2. Markers first; STL/glTF meshes and URDF only if day 5 has room.

Architecture: a second painter contract, `Painter3D` (points, lines, triangles with per-vertex colour, textured
quad, depth flag, view/projection matrices). Layers hand it versioned vertex buffers (`Mesh3D`), uploaded once
per version like `RasterImage`. The existing 2D `Painter` draws the HUD on top. `RecordingPainter3D` hashes the
buffers so fixtures prove both cores emit identical vertices. `Camera3D` implements `Projection` (kind `3d`);
the `Scene` class is reused, 2D layers are skipped by camera kind.

Wire: layer pushes whose payload carries typed arrays travel as a binary `SkyScopeLayer` message (`SKSL`) on the
WebSocket and as a second channel with message encoding `skyscope-layer` on the same `/layers/<id>` MCAP topic.

| Day | Deliverable |
|---|---|
| 1 | Core math (`Mat4`, quaternion, `Camera3D` orbit/first-person, perspective/orthographic, ray picking), `FrameTree` (tf2-style timed transforms), `Painter3D` + `RecordingPainter3D`, binary layer message on WebSocket and MCAP, Grid3D/Axes/PointCloud3D layers — both cores, fixture-pinned. |
| 2 | WebGL2 painter, `Scene3DView` in render/React/Blazor with 2D HUD overlay, `Scene3DController` (orbit, pan, dolly, fit, select, measure), Path3D/Pose3D/Marker/OccupancyGrid-plane/LaserScan3D layers. |
| 3 | .NET: OpenTK `GlPainter3D` with the same shaders, `SceneControl3D` on `GLWpfControl`, WPF sample page. |
| 4 | ROS 2 data: PointCloud2 decoder, TF/TFMessage into `FrameTree`, MarkerArray, Path, Odometry in 3D; relay and MCAP playback of binary layers; synthetic 3D scene source for the demo server. |
| 5 | Markers polish, mesh loading if time allows, bench (1 M points at 30 fps), docs, samples in all three hosts. |
