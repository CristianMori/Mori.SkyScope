# Mori.SkyScope — build status

## Day 1 (2026-09-05) — rename + core foundations ✅

Everything below exists in **both** cores and is pinned by shared fixtures (`spec/fixtures/*.json`,
run by xUnit and Vitest) unless marked otherwise. TS: 96 tests, C#: 90 tests, all green.

| Area | TS (`@mori/skyscope-core`) | C# (`Mori.SkyScope.Core`) | Fixture |
|---|---|---|---|
| Rename to Mori.SkyScope, Apache-2.0, authorship | ✅ | ✅ | — |
| `SignalBuffer` ring buffer (regular runs + timestamped) | `signals/signal-buffer.ts` | `Signals/SignalBuffer.cs` | `signal-buffer.json` (12) |
| `BucketSeries` incremental M4 decimation + polyline | `signals/bucket-series.ts` | `Signals/BucketSeries.cs` | `decimation.json` (11) |
| `SkyScopeFrame` v1 codec (3 encodings, 8-byte aligned, zero-copy decode) | `streaming/frame.ts` | `Streaming/FrameCodec.cs` | `spec/frames/*.bin` golden (both directions) + random round-trips |
| Scales: linear / log / time, 1-2-5 ticks, time ladder, UTC + relative labels | `scales/*` | `Scales/*` | `scales.json` (20) |
| Source contracts: `Source`, `SourceFactory`, `SourceRegistry`, `SignalSink`, `LayerSink`, `TimeSource` | `sources/contracts.ts` | `Sources/Contracts.cs` (+ `[SkyScopeSource]` scanning) | — |
| `SignalStore` (rate×retention sizing, auto-declare, timing conversion, drop accounting, notifications) | `sources/signal-store.ts` | `Sources/SignalStore.cs` | `signal-store.json` (9) |
| Deterministic synth generator + `SyntheticSource` plugin | `sources/synth.ts`, `@mori/skyscope-sources` | `Sources/Synth.cs`, `Sources/SyntheticSource.cs` | `synthetic.json` (8) |
| `Painter` contract + `RecordingPainter` | `paint/*` | `Paint/*` | `paint.json` (2) |
| Real painters | `Canvas2DPainter` (`@mori/skyscope-render`) | `SkiaPainter` (`Mori.SkyScope.Render.Skia`, SkiaSharp 4.151) | manual for now |
| Design tokens → CSS vars / TS / C# consts; shared component stylesheet | ✅ | ✅ | — |
| Chrome (pre-pivot): `Field` validation core, Blazor `Button`/`TextField` | ✅ | ✅ | `field.json` (19) |

### Conventions established
- **Core emits geometry; painters paint.** ~15 primitives, interleaved xy arrays, layer cache by key.
- **Numbers are bit-identical across cores**: powers of ten by repeated multiplication, sub-unit ticks as
  `k / inv`, `floor(x + 0.5)` rounding, uint32 hash noise, f32 widened to double before JSON.
- Behaviour fixtures use `create(setup) → step… → snapshot`, expectations are subsets of the snapshot.
- Error codes shared across cores: `out-of-order-time`, `timing-mismatch`, `truncated-frame`, `bad-magic`.

### Not done / carried forward
- React `Button`/`TextField` and the sample apps are still empty shells (pre-pivot chrome; low priority).
- Blazor package still exports only chrome; the JS bridge (`@mori/skyscope-blazor`) starts day 3.
- WPF GL host, WinForms host: later days.

## Day 2 (2026-09-05) — scene engine + data plane ✅

TS: 140 tests, C#: 129 tests, all green. New fixtures: `geometry.json` (7), `camera.json` (12), `interaction.json` (12),
`scene.json` (4), `clip-transform.json` (3); plus WebSocket integration tests on both sides and a live
cross-language smoke run (C# demo server → TS client: 100 ch × 1 kHz, 198 000 samples in 2 s, zero drops).

| Area | TS | C# |
|---|---|---|
| Geometry: `Mat3` affine (DOMMatrix order), rects, segment distance, point-in-polygon | `scene/geometry.ts` | `Scene/Geometry.cs` |
| `Camera2D` (center/zoom/rotation/flipY, pan, zoomAt, fitBounds, fitScreenRect, worldBounds, version) + `ScaleProjection` for chart plot areas; `Projection` is dimension-agnostic (3D-ready) | `scene/camera.ts` | `Scene/Camera2D.cs` |
| `Scene` + `Layer` contracts: z-order, visibility, opacity, hit-testing (topmost within tolerance), bounds union, **layer surface caching** keyed by projection version and dirty flags | `scene/scene.ts` | `Scene/Scene.cs` |
| Built-in layers: `PolylineLayer`, `PointsLayer`, `GridLayer` (auto 1-2-5 spacing) | `scene/layers.ts` | `Scene/Layers.cs` |
| Interaction reducer: click vs drag slop, pan / box-zoom / cursor / select gestures, shift & middle-button & space overrides, wheel zoom, Escape/cancel, dblclick reset; `applyCameraEffect` | `scene/interaction.ts` | `Scene/Interaction.cs` |
| `clipTransform` (data → GL clip space with x-origin for epoch precision) | `scales/clip.ts` | `Scales/ClipTransform.cs` |
| **WebGL line renderer** (one GPU buffer per series, uniform transform, 1-px LINE_STRIP) | `@mori/skyscope-render/webgl-lines.ts` | — |
| **WebSocketFrameSource** (binary frames + JSON channel catalog, reconnect; optional Blob-worker socket with transferable frames) | `@mori/skyscope-sources` | `Core/Sources/WebSocketFrameSource.cs` (background receive task, optional `Dispatch` marshalling) |
| `SignalStore` thread-safety (`SyncRoot`) | n/a | ✅ |
| **Mori.SkyScope.Streaming**: `FrameBroadcaster` (ISignalSink → all sockets, bounded per-client queues, drop accounting) + `MapSkyScopeStream` | — | ✅ |
| **Demo server** `samples/Mori.SkyScope.DemoServer` (`--channels --rate --batch-ms --quantized`, `/`, `/channels`, `/ws`) | — | ✅ |

### Deviations from the plan
- Worker ingest uses **transferable frames** (socket + batching in a Blob worker, decode + append on the main
  thread) instead of SharedArrayBuffer ring buffers: no COOP/COEP requirement, and decode is microseconds.
  SAB remains an optimisation for later if profiling demands it.
- WebGL lines are 1 device pixel; thick strokes will come from expanded-quad geometry or the Canvas2D overlay (day 3).

## Day 3 (2026-09-05) — TrendChart ✅

TS: 150 tests, C#: 138 tests, all green. New fixture `trend-chart.json` (9 cases) including a **drawing-parity case:
76 painter ops identical in both cores** (backgrounds, threshold band + line, grid, series polylines, digital
steps, marker with label, hover/cursor lines, axis ticks + labels, time axis, legend with values and Δ readouts).
Verified live in Chrome: React sample and Blazor sample both streaming from the demo server.

| Area | TS | C# |
|---|---|---|
| Config model: lanes (stacked strips, weights), axes (left/right, fixed or auto), series (channel, lane, axis, digital), thresholds (band/line), markers, themes (light/dark), palette | `charts/trend-config.ts` | `Charts/TrendConfig.cs` |
| Pure layout: margins → legend → y-axis columns → stacked lanes → time axis | `charts/trend-layout.ts` | `TrendLayoutEngine` |
| `TrendChartModel`: live/paused/review window with clamping, per-axis autoscale (5 % pad, flat-series pad, fixed, digital), M4 decimation per series bound to the store, digital step expansion, readouts at/before time, cursor A/B + Δ, legend values, pan/zoom/box-zoom/hover/click/reset effects, move series between lanes | `charts/trend-model.ts` | `Charts/TrendChartModel*.cs` |
| Three-pass drawing (background / series / foreground) + GPU geometry export | `charts/trend-draw.ts` | `Charts/TrendChartRenderer*.cs` |
| `TrendChartView`: 3 stacked canvases (2D bg, WebGL series with per-lane scissor, 2D fg), RAF loop with fps cap, ResizeObserver, pointer/wheel/keyboard → reducer → model, box-zoom preview, legend-row drag onto a lane | `@mori/skyscope-render` | — |
| React: `<TrendChart>`, `<Button>`, `useSignalStore`, `useWebSocketSource`, `useSource` | `@mori/skyscope-react` | — |
| Blazor: `<TrendChart WsUrl Config Tool>` Razor component + JS bridge (`@mori/skyscope-blazor` → `wwwroot/skyscope.js`, 42 KB, esbuild); charts sharing a URL share one socket; C# config serialised to the TS JSON shape | `@mori/skyscope-blazor` | `Mori.SkyScope.Blazor` |
| Samples: React (`npm run sample`) and Blazor (`dotnet run --project samples/Mori.SkyScope.Blazor.Sample`) against `samples/Mori.SkyScope.DemoServer` | ✅ | ✅ |

### Fixed along the way
- **Clip-transform precision**: slope/offset were evaluated at x = 0 and x = 1 on a unix-epoch axis, losing digits that
  the ×1.79e9 origin amplified into a 54-clip-unit offset (series invisible). Now evaluated at the origin. Fixture-pinned.

### Deferred
- Enumerated *state* tracks (labelled bands) — digital 0/1 tracks are in; state tracks join the SceneView work.
- Thick GPU lines (currently 1 device pixel; Canvas2D path draws configured widths).
- Cursor dragging with the select tool; keyboard nudges.

### Day 3 addendum — corner legend + drag-to-rearrange ✅
- Legend is an overlay box in a plot corner (`legend: "top-left"` default, `top-right`, `bottom-left`, `bottom-right`; `right`/`top`/`none` kept),
  rows grouped by lane with separators; the plot keeps its full width.
- Drag a legend row: drop on the upper ¾ of a lane (or on another row) → the series joins that lane and shares its axis
  (`axisId` cleared → same scale). Drop on the lower ¼, a gap, above the first or below the last lane → a new lane is
  inserted there (own axis, parallel scale, no overlap). Emptied lanes are pruned. A live indicator shows the target.
- Model API (both cores): `dropTarget`, `applyDrop`, `moveSeriesToNewLane`, `pruneEmptyLanes`, `legendRowAt`,
  `beginDrag/updateDrag/endDrag/cancelDrag`; fixture cases for corners, drop targets, drops and drag state; the
  drawing-parity case now covers the overlay legend and the drop indicator (80 ops). TS 154 / C# 142 tests.
- Verified live in Chrome (join + new-lane drops).

### Day 3 addendum — logic-analyzer tracks ✅
- Digital series on a digital-only axis are **stacked as tracks** within their lane (first in legend on top), low/high 15 %
  inside each band, separators between tracks, and the signal names (in series colour) replace numeric ticks on the axis.
  A digital series sharing an axis with analog ones keeps the analog scale. Model API: `isDigitalAxis`, `digitalTracks`,
  `trackRect`, `seriesScale` (both cores); fixture case + parity regenerated (79 ops). TS 155 / C# 143.

### Day 3 addendum — full styling ✅
- `theme` (colours + type) gained `legendBorder`, `laneBorder`, `dropIndicator`; new `style` block holds every geometric
  constant that used to be hardcoded: grid width/dash, value/time grid toggles, track separators, lane border + width,
  axis tick length + label gap, default series width, threshold band opacity + line dash, marker width/dash, cursor
  width, hover dash, legend padding/row height/opacity/radius/swatch length, digital track padding.
- Partial `theme`/`style` objects deep-merge over defaults (`defaultTrendConfig`, `applyTrendOptions`); the Blazor
  component serialises the C# `ChartStyle`/`ChartTheme` to the same JSON. Fixture case drives non-default values
  through both renderers (68 ops identical). TS 156 / C# 144.

## Day 4 (2026-09-06) — Gauges + first WPF component ✅

TS: 178 tests, C#: 165 tests, all green. New fixture `gauges.json` (21 cases: dial angles/ticks/needle, damping incl. 360° wrap,
seven-segment masks/text, LED level+bit modes, knob pointer math with dead zone/snapping, slider mapping, attitude horizon
polygon, switch — plus **drawing parity for every gauge type**, 10 cases identical in both cores).

| Gauge | Model (TS `gauges/*`, C# `Gauges/*`) |
|---|---|
| `RadialGauge` | bands, 1-2-5 major/minor ticks, needle polygon or filled arc, value readout, half-sweep layout, damping |
| `LinearGauge` | horizontal/vertical bar or thermometer, bands, pointer, scale labels |
| `Led`, `LedArray` | lamp (round/square); strip in level (VU) or bit-mask mode, per-index colours |
| `NumericDisplay` | true seven-segment digits as polygons (masks a–g, faint "off" segments, slant, sign, overflow → EEEE) |
| `Compass` | needle or rotating-card mode, cardinals, wrap-aware damping |
| `AttitudeIndicator` | sky/ground horizon rotated by roll and shifted by pitch, pitch ladder, roll scale, fixed aircraft symbol |
| `Knob`, `Switch`, `Slider` (inputs) | pointer-angle / track mapping with snapping, wheel & arrow nudging, change events |
| shared | `GaugeTheme` (light/dark), `Band`, `SmoothedValue` damping, `scaleTicks`, JS-compatible half-up rounding on the C# side |

Hosts: `GaugeView` (Canvas2D; redraws only when invalidated or settling) + `bindKnob/bindSlider/bindSwitch`;
React `RadialGaugeView`…`SliderView`; Blazor `<Gauge Kind=… Options=… Value=… ValueChanged=…>` via the JS bridge
(`mountGauge`, change events through `DotNetObjectReference`); **WPF**: `Mori.SkyScope.Wpf` with an own `SkiaElement`
(WriteableBitmap-backed, no SkiaSharp.Views dependency), `GaugeControl` and `TrendChartControl` on SkiaSharp.
`samples/Mori.SkyScope.Wpf.Sample` shows chart + gauges live from the demo server and has `--snapshot out.png` which
renders the whole dashboard offscreen — see `docs/images/wpf-dashboard-snapshot.png`.

### Cross-language traps caught by the fixtures
- `Math.round(2.5)` is 3 in JS and 2 in .NET (banker's rounding) → C# gauges use half-up rounding.
- Knob angle normalisation for pointer positions more than 180° from the sweep start.
- Damping settle threshold (1e-4) so `animating` actually turns off.

## Day 5 — Charts (next)
XY line/scatter, bar (grouped/stacked), area, pie/donut, polar, heatmap/spectrogram, histogram — sharing axis/legend/tooltip
machinery with TrendChart.

## Logic-analyzer sample (2026-09-06)
- `samples/Mori.SkyScope.Wpf.Sample --logic <out.png> [--width --height]` renders an offscreen logic-analyzer view (`LogicSnapshot.cs`): ADC lane + 19 stacked digital tracks (CLK, CNT0-7, D0-7 bus, CS, RDY) at 10 kS/s, TRIG marker, cursors A/B with Δt readout. Output: `docs/images/logic-analyzer.png`.
- `TimeScale` gained an `origin` (both cores): relative mode places ticks and labels relative to the live edge, so a live window reads −0.20 … 0 instead of raw epoch seconds. Trend parity fixtures regenerated; TS 178/178, C# 165/165.

## Day 5 — analytic charts (2026-09-06)
- `CartesianChart` (line, step, scatter, area, bar; grouped bars by default, `stack` groups for stacked bars/areas with sign-aware stacking; category, linear and log axes; second y axis; autoscale with nice domains; legend toggles; hover tooltip; box zoom, wheel zoom, reset). `histogram()` / `Histogram.Compute` with 1-2-5 bin widths and a closed last bin.
- `PieChart` (pie/donut, pad angle, sort, inside/outside labels, centre text, hover explode, legend toggles), `PolarChart` (polar and radar, polygon or circle grid, clockwise/anticlockwise, closed areas), `Heatmap` (matrix → RGBA raster through named or custom colour maps, colour bar, hover cell, rolling spectrogram mode via `pushColumn`).
- Shared `SimpleLegend` (corner overlay, right column, top strip) and tooltip/marker helpers; all charts reuse `ChartTheme`/`ChartStyle`.
- `RasterImage` painter contract: core produces RGBA, Canvas2D and Skia painters upload once per version; `RecordingPainter` records an FNV-1a hash of the pixels for parity.
- Fixtures: `spec/fixtures/{cartesian,pie,polar,heatmap}.json` — 22 cases, 5 of them drawing parity. TS 204/204, C# 187/187.
- Hosts: web `ChartView`; React `XYChart`, `PieChartView`, `PolarChartView`, `HeatmapView`; Blazor `Chart` (`Kind` xy | pie | polar | heatmap, `PushColumnAsync`); WPF `ChartControl`. `ChartJson` builds C# configs from the TS option shape.
- Samples: a charts row (bars + average + error rate, donut, radar, live noise spectrogram from a 32-bin DFT) in React, Blazor and WPF. Page render: `Mori.SkyScope.Wpf.Sample --charts out.png [--dark]` → `docs/images/analytic-charts.png`.
- Verified live in Chrome (React sample against the demo server): tooltips, hover rings, legend, rolling spectrogram.
- Deferred: decimation for very large static series, data labels on bars, log colour scale for heatmaps, keyboard navigation.

## Day 6 — SceneView + source plugins (2026-09-06)
- **Robotics layers** (both cores, `spec/fixtures/robot-layers.json`, 8 cases incl. drawing parity): `BitmapLayer` (affine placement through the painter's translate/rotate/scale, decomposed from the projection), `OccupancyGridLayer` (ROS convention, raster with free/occupied/unknown colours, cell hit data), `PointCloudLayer` (+ `laserScanToPoints` through the sensor pose, intensity colour maps), `PoseLayer` (heading arrow, footprint, label), `ShapeLayer` (circle/rect/polygon/line/text, topmost hit), `TrailLayer`.
- **SceneController** (`spec/fixtures/scene-controller.json`, 5 cases incl. overlay parity): pan, wheel zoom, shift-drag box zoom, measure (two clicks, Δ in world units), select (hover + click hit test), Q/E rotate, R reset, F / double-click fit-all, scale bar (1-2-5), cursor readout. Hosts: web `SceneView`, React `SceneView`, Blazor `SceneView` (JSON layer API + `WsUrl`), WPF `SceneControl`.
- **Layer JSON contract** (`createLayer` / `applyLayerPayload`, C# `LayerJson` + `SceneLayerSink`, TS `SceneLayerSink` / `FanoutLayerSink`): one language for source plugins, the Blazor bridge and the WebSocket relay. `FrameBroadcaster` now implements `ILayerSink`; declarations are replayed to late clients; both WebSocket clients dispatch `layer` / `push` text messages.
- **Sources**: `PlaybackSource` (CSV → `Recording`, play/pause/seek/speed/loop over a manual clock; seek-back resets the sink and replays; `spec/fixtures/playback.json`), `MqttSource` (TS via `mqtt` over WebSocket, .NET `Mori.SkyScope.Sources.Mqtt` via MQTTnet; shared `MqttMapping` with topic wildcards, JSON paths, message time; `spec/fixtures/mqtt-mapping.json`), `Ros2Source` (.NET `Mori.SkyScope.Sources.Ros2` on Mori.Ros2Sharp 0.2.0 — hand-written CDR decoders for std_msgs scalars/arrays, Twist, Pose(Stamped), Odometry, Imu, BatteryState, JointState, LaserScan, OccupancyGrid; signals + scene layers), `SyntheticSceneSource` (map, lidar ray-cast, driving robot, trail, zones — demo server relays it to every client).
- Suites: TS 230/230, C# 209/209. Samples: scene beside the trend chart in React, Blazor and WPF, fed by the demo server relay; `Mori.SkyScope.Wpf.Sample --scene out.png` → `docs/images/scene-view.png`. Verified live in Chrome.
- Known limits / deferred: MCAP reader (ROS bags are usually zstd-chunked; needs a proper reader + CDR schemas — not started), Ros2Sharp has no RTPS fragmentation (payloads > ~60 KB such as large maps do not arrive), Go2/CycloneDDS interop untested (no robot on this network), playback scrubber UI in the samples, WebGL point-cloud rendering for very large clouds, ROS 2 adapter only exercised through the compiler (no live ROS 2 peer available).

## Day 7 — samples, docs, packaging, CI (2026-09-06)
- **Dashboards**: React, Blazor and WPF samples now show the same robot dashboard — trend chart (incl. the synthetic robot's pose lane on channels 100–103), gauges, analytic charts and the live scene relayed by the demo server.
- **Plugin template + doc**: `docs/PLUGINS.md` (contracts, sinks, clock, layer JSON, relaying to browsers, testing) with working templates `ts/packages/source-template` (`@mori/skyscope-source-template`) and `dotnet/Mori.SkyScope.Sources.Template`, each with tests (store-driven, registry discovery).
- **Packaging**: NuGet metadata in `Directory.Build.props` (readme, tags, docs XML); `dotnet pack dotnet/Mori.SkyScope.slnx -c Release -o artifacts/nuget` → Core, Render.Skia, Streaming, Wpf, Blazor (with `staticwebassets/skyscope.js`), Sources.Mqtt, Sources.Ros2. npm: `npm run pack` → tokens, core, render, sources, react, source-template tarballs (public scoped packages, per-package README). Samples, tests and the template project are not packable.
- **CI**: `.github/workflows/ci.yml` (windows-latest: npm build/test/pack, dotnet build/test/pack, artifacts uploaded).
- **Bench** (`npm run bench -- 60`, `BENCH_SECONDS=60 dotnet test --filter Category=Bench`): 500 ch × 1 kHz for 60 s through encode → decode → store: TS 30 M samples in 274 ms (109 Msamples/s), C# in 476 ms (63 Msamples/s), zero drops. A 2 s version runs in both suites.
- **README.md** at the root: features, images, quick start, package table, verification, limits.
- Suites: TS 233/233, C# 212/212. Release build clean.
- Not done in the week: MCAP reader, Go2/CycloneDDS interop spike, playback scrubber UI, GPU point clouds, folder rename to Mori.SkyScope (deferred by the user), first git commit (not requested).

## Backlog 1 — playback scrubber UI (2026-09-06)
- React: `usePlayback(store, recording)` + `PlaybackControls` (play/pause, rewind, scrubber, elapsed/total, speed, loop); the TrendChart takes `clock={playback.source.clock}`. Sample: a live / playback toggle plays a 20 s recording generated in code.
- WPF: `PlaybackControl` (transport bar ticking the source on a dispatcher timer); sample `--csv samples/demo-recording.csv` (generated by `tools/gen-demo-csv.mjs`).
- Blazor: `Playback` component parses CSV in the browser and registers a store as `playback:<key>`; charts bind with `WsUrl="playback:<key>"` (clock indirection so mount order does not matter). Not yet placed in the Blazor sample page.
- Verified live in Chrome (React). Suites unchanged: TS 233, C# 212.

## Backlog 2 — record and playback in MCAP (2026-09-07)
- **MCAP container** (`recording/mcap.ts`, C# `Mcap/Mcap.cs`): writer (magic, header, schemas, channels, messages, data end, summary with statistics and summary offsets, footer; no chunks, no CRCs) and reader (unchunked or uncompressed-chunk files; lz4/zstd chunks raise a clear error). Byte-identical output across the two cores (fixture hash), golden `spec/mcap/sample.mcap` + sidecar.
- **Recording layout**: `/skyscope/frames` (binary SkyScopeFrame, encoding `skyscope-frame`), `/skyscope/channels` (JSON catalog), `/layers/<id>` (JSON `{declare:{kind,meta}}` / `{push:payload}`, one topic per scene layer, schema per kind). Opens in Foxglove-style tools; the frames need our decoder, the layers are plain JSON.
- **McapRecorder** (TS + C#): a tee that implements both sinks; forwards to the sinks a source already feeds and, while recording, writes every message. Catalog and layer declarations seen before `start()` are written first, so recordings started mid-stream are self-contained. Size limit with auto-stop for browser memory; .NET `StopTo(path)`.
- **Playback of layers**: `Recording.layerEvents`; `PlaybackSource` applies frames and layer events in time order; seeking backwards resets the store and the layer sink (`LayerSink.reset`, `SceneLayerSink` removes the layers it created) and replays from the start, so trails, maps and poses are exactly what they were.
- **Hosts**: React `RecordButton`, `useRecorder`, `usePlayback(store, recording, opts, layers)`, `pickFile`/`saveFile`; sample has record / open… / live / playback and `?mcap=<url>`. WPF `RecorderControl` (record, stop + save dialog, open…) and `--mcap` playback into chart and map, chart store retention raised for playback. Blazor `Recorder` component, `Playback` gains `McapUrl` and `OpenFileAsync`, charts and scenes bind to `playback:<key>`. Demo server: `--record out.mcap` plus `GET /record`, `POST /record/start?path=`, `POST /record/stop`.
- Verified: fixtures `spec/fixtures/mcap.json` (5 cases) in both cores (TS 239, C# 218); an 8 s live recording written by the C# server read back by the TS reader (12 channels, 484 frames, 248 layer events) and played into a store and a scene from Node with a scrub back. The Chrome-side check could not run this session (the browser could not reach localhost while curl could), so the React record/open buttons are compiled and unit-covered but not clicked live.
- Not done: reading lz4/zstd chunks (needed for most ROS 2 bags), CDR decoding of foreign topics on playback, chunked/indexed writing for very long recordings (files are written in memory in the browser; the .NET recorder also buffers in memory until stop — streaming to disk is the next step for multi-hour recordings).

## Backlog 3 — streaming recordings and lz4 (2026-09-07)
- **Streaming output**: `McapWriter` takes a sink (TS) or a `Stream` (C#) and writes records through with a byte counter; byte-identical to the buffered path (tests in both suites). `McapRecorder.start(sink)` / `Start(Stream)` / `StartFile(path)`; the size limit applies only to in-memory recordings. WPF `RecorderControl` picks the file up front and streams to it; the demo server `--record` and `/record/start` write straight to disk; React `RecordButton streamToFile` uses the File System Access API where present (Chromium) and falls back to memory + download.
- **lz4 chunks**: `recording/lz4.ts` / `Mcap/Lz4.cs` decode LZ4 blocks and frames (skippable frames, uncompressed blocks, checksums skipped); MCAP readers accept chunks with compression "" or "lz4" and report zstd. Golden `spec/mcap/sample-lz4.mcap` (records of the sample inside one lz4 frame chunk, produced by a small compressor in the generator) plus block vectors in `spec/fixtures/mcap.json` (9 cases). ROS 2 bags recorded with lz4 now open; zstd bags still do not.
- Suites: TS 245, C# 224. Verified: 4 s streamed to disk through the demo server and read back.
- Remaining: zstd chunks, CDR decoding of foreign topics on playback (a bag's `/scan` would need the ROS 2 decoders wired to the playback path), and the browser click-through of record/open that Chrome blocked last session.

## Sprint 2 — 3D viewport, Day 1: core (2026-09-08)
- **Decisions**: RViz-class viewer (not a simulator); hand-written WebGL2 with GLSL ES 300 shared with the desktop; OpenTK 4 `GLWpfControl` on WPF; Ros2Sharp fragmentation deferred (big clouds come through the relay); markers first, meshes if day 5 allows. Plan in `docs/PLAN.md`, "Sprint 2".
- **Math** (`scene3d/math3.ts`, `Scene3D/Math3.cs`): column-major `Mat4` (translation/scaling/quaternion/pose/lookAt/perspective/ortho, multiply, invert, transpose, point/direction transforms), quaternions (axis-angle, ZYX Euler both ways, product, rotate, slerp on the shorter arc), `Box3`, rays against plane/box/sphere. Fixture `math3.json` (4 cases).
- **Camera3D** (`scene3d/camera3d.ts`, `Scene3D/Camera3D.cs`): orbit camera (target, distance, yaw, pitch, z up), perspective or orthographic with the same framing, `project3`/`unproject3`/`ray`/`groundPoint`, orbit/pan/dolly/`dollyAt` (the ground point under the cursor stays fixed), `fitSphere`/`fitBox`. Implements the 2D `Projection` on the ground plane so the existing cursor/measure tools keep working. Fixture `camera3d.json` (5 cases).
- **FrameTree** (`scene3d/frame-tree.ts`, `Scene3D/FrameTree.cs`): tf2-style timed transforms per child frame (sorted insert, `maxSamples`, static overrides), `lookup(target, source, time)` through the common root with lerp + slerp interpolation and clamping outside the range, reparenting resets, cycles do not hang. Fixture `frame-tree.json` (4 cases).
- **Painter3D contract** (`scene3d/painter3d.ts`, `Scene3D/IPainter3D.cs`): `Mesh3D` (key, version, positions f32, colours RGBA u8, normals, indices), `Material3D` (colour, opacity, point size, line width, depth test, lit, model matrix), primitives `begin/points/lines/triangles/image/end`. `RecordingPainter3D` records ops with FNV-1a hashes of every buffer, so fixtures prove both cores emit identical vertices. `LayerContext.painter3d` is optional; `Scene.draw` takes it as a fourth argument; 2D layers are skipped under a 3D camera by `cameraKinds`.
- **Layers** (`scene3d/layers3d.ts`, `Scene3D/Layers3D.cs`): `Grid3DLayer` (metric ground grid with major lines), `AxesLayer` (RGB triad at every frame of the tree, or a subset, at `ctx.now`), `PointCloud3DLayer` (xyz + intensity colormap or RGBA, frame-aware through the tree with the cloud's own stamp, ray-free pixel-distance picking, 3D bounds), `FramesLayer` (invisible; its pushes feed the sink's shared `FrameTree`). JSON contract: kinds `grid3d`, `axes`, `pointCloud3d`, `frames`; `SceneLayerSink` owns a `FrameTree` and a `fixedFrame` (`LayerEnv`); `reset()` clears the tree. Fixture `scene3d.json` (3 cases: draw recordings, bounds, hit test, fit, unknown frame skipped, reset).
- **Binary layer message** (`streaming/layer-message.ts`, `Streaming/LayerMessage.cs`): `SKSL` v1 — header, id, JSON for plain fields, 8-byte-aligned typed-array attachments (f32/f64/u8/u16/i16/u32/i32). Any layer push whose payload holds typed arrays takes this path: `FrameBroadcaster.Push` sends it as a binary WebSocket message, `WebSocketFrameSource` tells it apart from frames by magic, `McapRecorder` writes it on a second channel of the same `/layers/<id>` topic with message encoding `skyscope-layer`, and `recordingFromMcap` decodes it back. Fixture `layer-message.json` (3 cases) and golden `spec/frames/layer-cloud.{bin,json}` (byte-identical from both cores).
- Suites: TS 273, C# 244. Full `npm run build` and `dotnet build` clean.
- Caught by the fixtures: .NET array covariance made `short[]` match a `ushort[]` type pattern (dtype now chosen by element `TypeCode`); the TS payload handler ignored `frame` when `positions` were present while C# applied it (both now apply `frame` first).
- Next (day 2): WebGL2 painter, `Scene3DView` in render/React/Blazor with the 2D HUD, `Scene3DController`, Path3D/Pose3D/Marker/OccupancyGrid-plane/LaserScan3D layers.

## Sprint 2 — 3D viewport, Day 2: renderer, view, controller, robot layers (2026-09-08)
- **Scene3DController** (`scene3d/controller3d.ts`, `Scene3D/Scene3DController.cs`): orbit camera + scene + frame tree; left drag orbits, middle/shift/space+left pans, right drag dollies, wheel dollies about the cursor, double-click fits; F fit, R reset, T top-down, O orthographic, Escape clears. Tools orbit | measure (two clicks on hits or the ground, HUD label) | select (hover ring + selection ring). HUD: orientation gizmo, cursor ground readout, fixed frame and projection mode. Fixture `scene3d-controller.json` (4 cases, HUD ops recorded).
- **WebGL2 painter** (`render/src/webgl-painter3d.ts`): one VAO per mesh key re-uploaded on version change, GLSL ES 300 flat/lit program (per-vertex colour, head-light lambert) and a textured-quad program, premultiplied blending, depth test per material, texture cache for `RasterImage`s, idle eviction. Line width is 1 device pixel (WebGL limit) — a thick-line pass is polish for day 5.
- **Scene3DView** (`render/src/scene3d-view.ts`): GL canvas + transparent 2D HUD canvas, resize, pointer/wheel/keys → controller, on-demand or `animate` redraw, `clock` for `now`. Hosts: React `Scene3DView`, Blazor `SceneView3D` (`mountScene3D`: declare/push/setTransforms/setTool/fit/reset/topDown/setOrtho, relayed JSON + binary layers via `WsUrl`).
- **Layers** (`scene3d/robot-layers3d.ts`, `Scene3D/RobotLayers3D.cs`, unit meshes in `primitives.ts`/`Primitives.cs`): `Path3DLayer` (strip, append ring), `Pose3DLayer` (arrow + triad + label), `MarkerLayer` (cube/sphere/cylinder/arrow/lineList/lineStrip/points/text, per-marker frame, scale, colour, lifetime auto-expired on draw, ray-vs-sphere picking), `LaserScan3DLayer`, `OccupancyGridPlaneLayer` (textured quad from the 2D grid raster, ray-vs-plane pick returns the cell). JSON kinds `path3d`, `pose3d`, `markers`, `laserScan3d`, `occupancyGrid3d`; `frame` and `stamp` accepted by every 3D push. Fixture `scene3d.json` grew to 6 cases.
- **React sample**: a 2D/3D toggle beside the trend chart; the 3D page runs a browser-generated scene (robot circling on a metric grid, spinning turbo lidar cloud, trail, labelled pose, marker set) and joins relayed layers to the same frame tree. Verified in Chrome: renders, orbit drag, gizmo and cursor readout update — `docs/images/scene-3d.png`.
- Suites: TS 281, C# 251. Full `npm run build` (incl. Blazor bundle) and `dotnet build` clean.
- Caught by the fixtures: a corner list converted to float32 in C# shifted a plane's bounds by 1e-9 (double overload added).
- Next (day 3): OpenTK `GlPainter3D` with the same shaders, `SceneControl3D` on `GLWpfControl`, WPF sample page.

## Sprint 2 — 3D viewport, Day 3: .NET renderer and WPF host (2026-09-08)
- **Mori.SkyScope.Render.OpenTK** (new package, OpenTK 4.9.4): `GlPainter3D` implements `IPainter3D` on OpenGL 3.3 core with the same shader bodies as the WebGL2 painter (`#version 330 core` preamble instead of `300 es`): VAO per mesh key re-uploaded on version change, flat/lit program, textured quad for `RasterImage`s (premultiplied on upload), depth test per material, `ProgramPointSize`, idle eviction.
- **SceneControl3D** (`Mori.SkyScope.Wpf`, OpenTK.GLWpfControl 4.3.6): a `GLWpfControl` for the scene plus a transparent `SkiaElement` for the HUD; mouse/keys → `Scene3DController`; on-demand or `Animate` redraw; `Clock` for layer time. Layer text (pose labels, text markers) is drawn in the HUD pass through a `NullPainter3D` traversal so it lands on the Skia layer. Guarded against sessions without a monitor: GLFW dereferences the primary monitor while creating the context and would crash the process, so the control checks first and shows a notice instead.
- **WPF sample**: 3D/2D toggle beside the trend chart; the 3D page runs the same in-process synthetic scene as the React sample (robot circling, spinning lidar cloud, trail, labelled pose, markers) and relayed layers fan out to both views through the new C# `FanoutLayerSink`.
- **Not verified here**: this session has no display (GLFW reports no monitor), so the OpenGL path could not be rendered or screenshotted; the sample starts and stays up with the guard active. The GL painter needs a run on a desktop session (`dotnet run --project samples/Mori.SkyScope.Wpf.Sample`) to confirm what the WebGL path already shows.
- Suites unchanged: TS 281, C# 251; solution builds clean.
- Next (day 4): ROS 2 PointCloud2/TF/MarkerArray/Path/Odometry into the 3D layers, synthetic 3D scene on the demo server, end-to-end relay and MCAP playback of binary layers.

## Sprint 2 — 3D viewport, Day 4: ROS 2 data, synthetic 3D source, relay and MCAP (2026-09-08)
- **ROS 2 3D decoders** (`Mori.SkyScope.Sources.Ros2/Ros2Source3D.cs`): `sensor_msgs/PointCloud2` (any field layout: x/y/z by offset and datatype, `intensity` or packed `rgb`/`rgba`, non-finite points dropped, big-endian rejected), `tf2_msgs/TFMessage`, `visualization_msgs/Marker` and `MarkerArray` (arrow/cube/sphere/cylinder/line strip/line list/points/text; ADD/DELETE/DELETEALL; mesh resources reported as unsupported), `nav_msgs/Path`. `Ros2SourceConfig.Scene3D` switches LaserScan/Odometry/Pose/OccupancyGrid to their 3D layer kinds with the header `frame_id` as the layer frame. `/tf_static` transforms accumulate into the `frames` layer declaration; `/tf` transforms are pushes. Unit tests build payloads with Ros2Sharp's `CdrWriter` (encapsulation header prepended as on the wire).
- **Declarations carry state** (both cores): `frames` meta `transforms` and `markers` meta `markers` are applied on creation, so late WebSocket joiners and recordings started mid-stream get static transforms and marker sets — the relay and the recorder replay declarations, not pushes. Fixture `scene3d.json` case 7.
- **SyntheticScene3DSource** (`Core/Sources`): frame tree (map → base_link → laser), a 2000-point spinning lidar cloud pushed as a binary `pointCloud3d` payload (`float[]` in a dictionary), trail, labelled pose, marker set; the demo server starts it by default (`--scene3d false` disables). C# test steps it into a `SceneLayerSink` and round-trips a cloud through `LayerMessage`.
- **Verified end to end** (demo server → TypeScript client over the WebSocket): 12 layers, frames base_link/laser/map, 2000-point cloud in frame laser with turbo colours, 6 markers, trail, pose; 30 binary layer messages in 3 s; fit-all bounds cover the scene and the centre pick hits a lidar point. A 3 s server-side MCAP (1.17 MB) reads back in TypeScript with 30 binary cloud pushes, plays through `PlaybackSource` into a 3D scene, and a seek back resets the cloud and the frame tree as designed.
- Suites: TS 282, C# 256 (new: synthetic 3D source, ROS 2 decoders). Solution and packages build clean.
- Next (day 5): docs, README, Blazor sample page, a browser bench with a large cloud, thick lines if time allows.

## Sprint 2 — 3D viewport, Day 5: samples, bench, docs, packaging (2026-09-08)
- **Blazor sample**: 3D/2D toggle; `SceneView3D` with `WsUrl` shows the demo server's synthetic 3D scene (relayed binary layers) with orbit/measure/select tools.
- **React sample bench**: `?points=N` sizes the browser-generated cloud, `?rate=ms` slows its regeneration, the toolbar shows the frame rate. This session's Chrome has no GPU (ANGLE on the Microsoft Basic Render Driver, a software rasterizer): 100 000 points at 27 fps, 1 000 000 at 4 fps, scaling with fragment count as a CPU rasterizer does — the CPU side (frame tree, 16 MB upload once a second, HUD) is not the limit. The GPU number needs a desktop run.
- **Docs**: README (3D feature, package table with `Mori.SkyScope.Render.OpenTK`, ROS 2 message list, limits), `docs/COMPONENTS.md` section 5b (every 3D piece with its fixture), `docs/PLUGINS.md` "3D layers and binary payloads".
- **Packaging**: `dotnet pack` now produces 8 NuGet packages (new `Mori.SkyScope.Render.OpenTK`); npm tarballs unchanged in number. Release build and tests clean.
- Suites at the end of sprint 2: TS 282, C# 256 (sprint 1 ended at 245 / 224).
- Not done in sprint 2: thick lines in WebGL/OpenGL (1 device pixel), mesh resources (STL/glTF) and URDF, GPU-side verification of the OpenTK path and the desktop frame rate (no display in this session), Ros2Sharp RTPS fragmentation for large clouds, a fixture for the WebGL/OpenTK painters themselves (they are the only unfixtured code; the layer geometry they consume is).

## Sprint 2 follow-up — thick lines, mesh resources, URDF (2026-09-08)
- **Thick lines** in both renderers: lines wider than one device pixel are instanced screen-space quads (one per segment, endpoints read from the mesh buffers with an instance divisor, near-plane clipped); native lines otherwise.
- **Mesh resources** (`scene3d/mesh-formats.ts`, `Scene3D/MeshFormats.cs`): binary and ASCII STL, glTF binary (.glb: triangle primitives, u8/u16/u32 indices, node transforms, merged); flat or averaged normals computed identically in both cores. `MeshRegistry` shared through `LayerEnv`; a `meshes` layer kind whose pushes (binary over the wire) register resources; `mesh` markers reference them by URI and draw once loaded (bounds and picking through the mesh box). Fixture `mesh-formats.json` (4 cases) with golden files in `spec/meshes`; scene3d case 8.
- **URDF** (`scene3d/urdf.ts` with a dependency-free XML reader, `Scene3D/Urdf.cs`): links, visuals (box/cylinder/sphere/mesh, origins, materials by name or inline), joints (fixed/revolute/continuous/prismatic with axis and limits); `urdfMarkers`, `urdfTransforms(positions)`, `publishUrdf`. Fixture `urdf.json` (3 cases incl. forward kinematics through the frame tree). The demo server's synthetic scene and the React demo mount a two-joint arm on the robot; `Ros2SourceConfig.Urdf` + `MeshRoots` publish a real robot and drive it from JointState.
- Verified in Chrome: thick trail and axes, the arm on the robot (`?dist=3.5`). Note for future browser checks: a hidden tab reports 0 fps because animation frames pause; screenshots still force a frame.
- Suites: TS 292, C# 264.
- Remaining: Collada meshes, glTF external buffers/textures, GPU verification of the OpenTK path on a desktop, Ros2Sharp fragmentation, zstd MCAP chunks, remote access (pinned), folder rename and first commit.

## Signals the ibaAnalyzer way — labels, lane headers, navigator, signal tree, Y-axis manipulation (2026-10-05)
Prompted by ibaAnalyzer's manual (part 2, chapter 6: moving signals, the navigator). All five requested pieces, in both cores with fixtures, and in every host.
- **Model** (`trend-model.ts` / `TrendChartModel.Commands.cs`): `hitTest` → `HitRegion` (legend row, header part, label, axis zone, navigator zone, plot lane, time axis); `dropTarget` with the ibaAnalyzer rules (axis strip / label / legend row → join that axis; lane free area → own axis; time axis, gaps, above → new lane); `applyGroupDrop` (Ctrl/Shift group shares one axis in free space, one new lane with the key, one lane each without); drags carry series ids and channel ids (signal tree) and create series `ch:<id>` on drop; lane ops (`moveLane`, `setLaneCollapsed`, `removeLane`, header drag with `laneInsertIndexAt`, `pinLanes` so implicit-lane series do not drift); axis ops (`beginAxisDrag` middle shift / end stretch about the other end, `axisZoomAt`, `axisAutoscale`); navigator (`navigatorFullRange`, `navigatorFrame`, `navigatorZone`, silhouettes from a second BucketSeries per series at the strip's quantum, drag inside/edges, click to centre, arrow keys, `setReviewEnd` resumes live at now).
- **Layout**: lane header column, in-plot label boxes (0.6 × fontSize per character so both cores agree without a painter; rows avoid an overlay legend), collapsed lanes at a fixed height, navigator rect under the time axis. **Drawing**: headers (fold arrow, grip, cross), labels with the dragged one dimmed and wavy-underlined, folded-lane summary, join cue (axis highlight + grey arrow), own-axis cue (dashed lane), new-lane line, lane insertion frame, navigator (silhouette polygons, dimmed outside, red frame with edge grips, range labels).
- **Fixtures**: `trend-chart.json` regenerated (20 cases; 8 new `iba:` cases for hit testing, drop targets, single and group moves, lane ops, Y axes, navigator, drawing parity with all of the above); new `signal-tree.json` (6 cases: grouping and split, custom separators, search, Ctrl/Shift/group selection, drag payload, late channels).
- **Hosts**: `TrendChartView` routes pointer/wheel/dblclick/keys by hit region, keeps a Ctrl/Shift selection, exposes `externalDragStart/Move/Drop/Cancel` and `onConfigChanged`; WPF `TrendChartControl` the same (cursor feedback, `ConfigChanged` event, screen-coordinate external drag); Blazor serialises the new config fields as before and the `TrendChart` handle exposes its view for sibling mounts.
- **Signal tree**: `SignalTreeModel` (TS + C#), `SignalTreePanel` (DOM), React `SignalTree`, Blazor `SignalTree.razor` (attaches through `Chart="@ref"`), WPF `SignalTreeControl`. All three samples show it left of the chart with the navigator on.
- **Verified in Chrome** (React sample, demo server): label → axis strip joined the scale; a tree row dropped into a lane got its own axis; fold, lane reorder by header, navigator edge resize / click-centre / arrow keys, axis drag-shift, wheel zoom (through the chart element) and double-click autoscale all behave as specified.
- **Legend groups by axis** (follow-up the same day): within a lane the legend lists series by axis, shared-axis rows adjacent with a bracket on the left; `legendGroups`/`legendSeries` drive the legend, its hit test and the in-plot labels. Fixture case 21. Screenshots in `docs/screenshots/`.
- **Digital I/O in the demo**: the demo server runs a second synthetic source (100 Hz, 50 ms frames) with five 0/1 square waves `io/pump`, `io/valve`, `io/estop`, `io/alarm`, `io/door` on channels 200–204, declared `digital` (`SyntheticChannel.Kind`, TS `kind`), so the tree shows an `io` group and drops make logic tracks; pump and valve sit in the digital lane of all three samples. Dropping digital signals into a lane that already has a logic stack joins that stack instead of opening a second digital axis (fixture case 22).
- **Logic analyzer** (same day): digital series are tracks in a per-lane stack at the bottom of the lane (`LaneLayout.analog`/`.stack`, config `digitalTrackHeight` 18 / `digitalStackShare` 0.5), solid-filled while high (`style.digitalFillOpacity`), stepped outline, name at the left of the band, true/false in the legend, no Y axis (`axesIn` lists analog axes only; `axisIdOf` reports `stack:<lane>` for digital). Tracks never overlap each other or analog signals. Drops: a digital-only drag targets the stack anywhere in a lane (`DropTarget.stack`, `HitRegion.stack`); analog never joins a stack. Digital tracks are drawn in the foreground pass so the WebGL series pass stays analog-only. Digital values are thresholded at 0.5 into true/false steps (a ±4 square wave is still a clean logic track), and all analog content (series, WebGL scissor, thresholds, value grid) is clipped to the lane's analog area so nothing ever crosses into the stack. Fixture cases 23–24 plus regenerated expectations.
- Suites: TS 308, C# 279. Solution, samples and the Blazor bundle build clean.
- Open: the navigator's live range is bounded by buffer capacity (channels without a declared rate keep 65 536 samples), so at 1 kHz the strip shows about a minute even with a longer retention; the WPF/Blazor hosts were built but not exercised interactively in this session.

## Documentation pass — headers, doc comments, README (2026-10-06)
- Every source file (277: TypeScript, C#, Razor, tools) opens with a header naming its purpose, the author and the licence; the design generator emits the same header into its generated outputs.
- Documentation comments on every exported TypeScript symbol and every public C# type and member (about 3,900 added); file-level comments that had attached to the wrong declaration were moved; multi-property gauge configuration lines were split so each property carries its own summary. Not documented: private nested test state and secondary partial-class declarations.
- README rewritten end to end with nine screenshots (docs/screenshots) and the four refreshed offscreen renders (docs/images); the logic-analyzer render now shows the stacked true/false tracks.
- Legend: cursor deltas are no longer shown for digital signals and the legend height no longer reserves rows for them.
- Suites unchanged: TS 308, C# 279; every project and sample builds clean.
