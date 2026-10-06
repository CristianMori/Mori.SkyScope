# Mori.SkyScope — component inventory

Everything built during the one-week sprint (2026-09-04 → 2026-09-06) plus the first backlog item. Each row names the
TypeScript module (`ts/packages/core/src/…` unless stated) and the C# type (`dotnet/Mori.SkyScope.Core/…` unless stated),
and the fixture file under `spec/fixtures/` that pins both. Suites: TypeScript 233 tests, C# 212 tests.

## 1. Foundations

| Component | TypeScript | C# | Fixture | What it does |
|---|---|---|---|---|
| Design tokens | `@mori/skyscope-tokens`, `design/tokens/*.json`, `tools/build-design.mjs` | `Theming/Tokens.g.cs` | — | One token source generates CSS variables, TS constants and C# constants; `design/styles/skyscope.css` is the shared stylesheet. |
| Fixture harness | `fixtures/drivers.ts` + one driver per module, `fixtures.test.ts` | `Core.Tests/Fixtures/FixtureRunner.cs` + drivers | all | `create(setup) → step… → snapshot`; expectations are subsets; numbers compared numerically; geometry rounded to 9 decimals. Drawing-parity cases compare recorded painter calls op for op across the two cores. |
| Forms chrome | `forms/field.ts`, `forms/validation.ts`; React `Button`, `TextField`; Blazor `Button.razor`, `TextField.razor` | `Forms/*` | `field.json` | Field state reducer with validation rules; the buttons and inputs used around instruments. |
| SignalBuffer | `signals/signal-buffer.ts` | `Signals/SignalBuffer.cs` | `signal-buffer.json` | Ring buffer per channel; regular-rate "runs" (t0 + i·dt) or explicit timestamps; window lookups by time; out-of-order rejection. |
| BucketSeries (M4) | `signals/bucket-series.ts` | `Signals/BucketSeries.cs` | `decimation.json` | Incremental min/max/first/last buckets at `window / pixelWidth`; rebuild on zoom. |
| SkyScopeFrame codec | `streaming/frame.ts`, `frame-json.ts` | `Streaming/Frame.cs`, `FrameCodec.cs`, `FrameJson.cs` | `spec/frames/*.bin` + `.json` | Binary batches: header (magic "SKSF", seq, t0), per channel enc 0 timestamped / 1 regular / 2 quantized (i16 + scale/offset), 8-byte aligned. Golden files prove encode/decode in both directions. |
| Scales and ticks | `scales/ticks.ts`, `format.ts`, `scale.ts`, `clip.ts` | `Scales/Ticks.cs`, `Scale.cs`, `ClipTransform.cs` | `scales.json`, `clip-transform.json` | Linear / log / time scales, 1-2-5 tick steps computed identically in both languages, time ladder with UTC or relative labels (relative mode counts back from a live edge), nice domains, clip-space transform for the GPU line pass. |
| Source contracts | `sources/contracts.ts` | `Sources/Contracts.cs` | — | `Source`, `SourceFactory`, `SourceRegistry` (+ `[SkyScopeSource]` assembly scan in .NET), `SignalSink` (declare, push, reset), `LayerSink`, `LiveClock` / `ManualClock`, logging. |
| SignalStore | `sources/signal-store.ts` | `Sources/SignalStore.cs` | `signal-store.json` | Channel map of buffers sized from rate × retention, auto-declare of unknown channels, drop accounting, listeners, `reset()`; thread-safe in .NET (`SyncRoot`). |
| SyntheticSource | `sources/synth.ts`, `@mori/skyscope-sources` `synthetic-source.ts` | `Sources/Synth.cs`, `SyntheticSource.cs` | `synthetic.json` | Deterministic waveforms (sine, square, triangle, sawtooth, ramp, noise) for demos and benches; identical values in both cores. |
| WebSocketFrameSource | `@mori/skyscope-sources` `websocket-source.ts` (optional Web Worker) | `Sources/WebSocketFrameSource.cs` | — | Binary frames → store; text messages carry the channel catalog and relayed scene layers; reconnects; .NET side marshals through `Dispatch`. |
| FrameBroadcaster + endpoint | — | `Mori.SkyScope.Streaming`: `FrameBroadcaster.cs`, `StreamEndpoints.cs` | — | Server sink (`ISignalSink` + `ILayerSink`): bounded per-client queues, drop accounting, catalog and layer declarations replayed on connect; `app.MapSkyScopeStream("/ws", broadcaster)`. |
| Painter contract | `paint/painter.ts`, `paint/recording-painter.ts` | `Paint/IPainter.cs`, `RecordingPainter.cs`, `CssColor.cs` | `paint.json` | About fifteen primitives (line, polyline, polygon, rect, circle, arc, sector, text, measureText, image, clip, transform, layer cache); `RasterImage` for core-produced RGBA; the recording painter records resolved styles and a pixel hash for parity. |
| Renderers | `@mori/skyscope-render`: `canvas2d-painter.ts`, `webgl-lines.ts` | `Mori.SkyScope.Render.Skia`: `SkiaPainter.cs`, `RasterCache.cs` | — | Canvas2D painter with device-pixel ratio and layer surfaces; WebGL `LINE_STRIP` renderer with per-lane scissor; SkiaSharp painter with `SKSurface` layers and raster upload cache. |
| Scene engine | `scene/geometry.ts`, `camera.ts`, `scene.ts`, `layers.ts`, `interaction.ts` | `Scene/Geometry.cs`, `Camera2D.cs`, `Scene.cs`, `Layers.cs`, `Interaction.cs` | `geometry.json`, `camera.json`, `scene.json`, `interaction.json` | `Mat3` (DOMMatrix order), `Camera2D` behind a dimension-agnostic `Projection` interface (fit bounds, zoom about a point, rotation, y-up), `ScaleProjection` for chart plots, `Scene` with per-layer surface caching by projection version, `PolylineLayer` / `PointsLayer` / `GridLayer`, and a pure interaction reducer (pan, wheel zoom, box zoom, cursor, select, modifier overrides). |

## 2. TrendChart (the flagship)

| Component | TypeScript | C# | Fixture |
|---|---|---|---|
| Config, theme, style | `charts/trend-config.ts` | `Charts/TrendConfig.cs` | `trend-chart.json` |
| Layout | `charts/trend-layout.ts` | `Charts/TrendConfig.cs` (`TrendLayoutEngine`) | |
| Model | `charts/trend-model.ts` | `Charts/TrendChartModel.cs`, `TrendChartModel.Commands.cs` | |
| Drawing | `charts/trend-draw.ts` | `Charts/TrendChartRenderer.cs`, `TrendChartRenderer.Legend.cs` | |

Features: lanes (stacked strips sharing the time axis) with weights; several Y axes per lane, left or right; series
bound to channels with kind analog or digital; thresholds (bands or lines) and event markers; live / paused / review
window with zoom modes; autoscale; cursors A and B with per-series Δ and Δt; hover readout; legend in any corner or as a
column, grouped by lane and, inside a lane, by axis (series sharing a scale sit together with a bracket on the left;
the in-plot labels follow the same order); digital series as a logic analyzer: every digital series of a lane is a track
in the lane's **stack** at the bottom (values thresholded at 0.5 into true/false steps, solid fill while high, stepped outline, its label at the
left of the band, true/false in the legend), tracks never overlap each other or the analog signals, and there is no Y axis for them. A digital-only lane
is all stack; a mixed lane gives the stack `digitalTrackHeight` per track up to `digitalStackShare` of the lane and the
analog signals the rest (`LaneLayout.analog` / `.stack`; every analog drawing pass is clipped to the analog area); three drawing
passes (background, series, foreground) so WebGL can take the middle one; `ChartTheme` (every colour) and `ChartStyle`
(grid width and dash, toggles, lane border, tick lengths, series width, threshold/marker/cursor styles, legend metrics,
digital track padding) deep-merged over defaults. 20 fixture cases including three drawing-parity cases.

Signal handling follows ibaAnalyzer (manual part 2, chapter 6). Everything below is model state with a `hitTest`
(`HitRegion`) so every host routes gestures the same way:

- **In-plot labels** (`plotLabels`): each series' name sits at the top of its lane and is the drag handle (legend rows
  still work). Widths come from the painter-free estimate 0.6 × fontSize per character so both cores lay out alike;
  rows avoid an overlay legend.
- **Drop targets** (`DropTarget`): a Y-axis strip, a label or a legend row → `join` that axis (grey arrow cue); a lane's
  free area → `ownAxis` (a new axis `axis:<seriesId>` in that lane, dashed cue). A drag carrying only digital
  signals targets the lane's logic stack wherever it is dropped inside the lane (`stack`, solid cue); an analog signal
  dropped on a digital track or label gets its own axis, never a place in the stack. Mixed groups split the same way. the time axis, a gap between lanes or
  above the first → `newLane` (insertion line). Dropping a lone series next to its own lane is a no-op; empty lanes
  vanish (the last one stays). Ctrl/Shift while picking up builds a group: in free space the group shares one axis;
  on the time axis a Ctrl/Shift group lands in one new lane, a plain multi-selection gets one lane each.
- **Lane headers** (`laneHeaders`): a slim bar left of the axes per lane; drag it to reorder (a frame marks the
  insertion slot), the arrow folds the lane to a thin summary bar (`collapsed`), the cross removes the lane and its
  series. Series without an explicit lane are pinned before lanes move so they do not drift.
- **Resizable lanes**: the gap between two open lanes is a hit region (`laneGap`); dragging it moves height from one lane
  to the other by rewriting their two weights (their sum is kept, each lane keeps at least 24 px).
- **Legend as a control**: the legend lists hidden series too, dimmed with "hidden" in place of the value, so they can be
  brought back; a press without movement on a label or legend row toggles the series (`toggleSeries`); the model also has
  `renameSeries`, `setSeriesColor`, `setSeriesWidth` and `removeSeries`, which the web view exposes in a right-click menu
  (`showSeriesMenu` in the render package) and the WPF control in a context menu.
- **Measurements**: cursors A and B are hit regions (`cursor`) and drag (`beginCursorDrag`); their times are tagged at the
  top of the first lane and the span is shaded. While both are set, `measurements()` gives per analog signal the value at
  A and B, their difference and the minimum, maximum and mean of the raw samples in the span (cached until a cursor moves
  or samples enter or leave the span), and the layout reserves a band under the time axis (`TrendLayout.measure`,
  `measurePanel`) for the table with Δt and its frequency in the title row.
- **Layout files**: `exportLayout()` / `importLayout(json)` (C# `TrendLayoutFile`) write and read a versioned JSON of the
  arrangement without theme and style; the web view, the Blazor handle (`exportLayout`, `importLayout`, `downloadLayout`)
  and the WPF control (`SaveLayout`, `LoadLayout`) expose it and the three samples have save and load buttons.
- **Y-axis manipulation**: drag the middle of an axis strip to shift its range, drag its top or bottom fifth to stretch
  about the other end, wheel to zoom about the pointer, double-click to return to autoscale (ranges become explicit
  `min`/`max` on the axis config).
- **Navigator** (`navigator`): a strip under the time axis with min/max silhouettes of the topmost open lane's series
  over the full retained history (live) or an explicit range (`navigatorRange`, set by playback hosts to the
  recording's span); the visible window is a red frame: drag inside to move, drag an edge to resize (disabled by
  `navigatorFixedRange`), click outside to centre there, ←/→ step by a tenth, wheel zooms the time span. Moving the
  frame up to now resumes live.
- **Signal tree** (`charts/signal-tree.ts`, `Charts/SignalTreeModel.cs`, fixture `signal-tree.json`): every channel of
  a store grouped by name prefix (`amr-1/pose/x` → group `amr-1/pose`, separators `/.:` configurable), search over
  name and unit, Ctrl toggle / Shift range selection, `dragIds` for the payload. Panels: `SignalTreePanel` (DOM, in
  the render package), React `SignalTree`, Blazor `SignalTree.razor` (attaches to a `TrendChart` by `@ref`), WPF
  `SignalTreeControl` (a ListBox with native Ctrl/Shift selection), Windows Forms `SignalTreeControl` (a TreeView). Every
  panel is a plain drag source of the platform (HTML5 drag and drop, `DragDrop.DoDragDrop`, `Control.DoDragDrop`) carrying
  the channel payload (`charts/channel-drag.ts`, `Charts/ChannelDragData.cs`: MIME `application/x-skyscope-channels`,
  desktop format `Mori.SkyScope.Channels`, plain-text ids accepted; fixture `trend-chart.json` case 29), and every chart
  host is a drop target that previews the target on drag-over and raises `onChannelDrop` / `ChannelDrop` before applying
  it (cancel, redirect, handle). `addChannels` (model and hosts) adds channels from code with the same drop rules; channels
  not in the chart become series `ch:<id>`. The tree is optional: any control that produces the payload feeds a chart.

Hosts: `TrendChartView` (three canvases: 2D background, WebGL series, 2D foreground; pointer, wheel, keyboard,
hit-test routing, `onConfigChanged`), React `TrendChart` + hooks (`useSignalStore`, `useSource`, `useWebSocketSource`),
Blazor `TrendChart.razor` (JSON config over interop, data streams in the browser, shared socket per URL), WPF
`TrendChartControl` on the in-house `SkiaElement` (WriteableBitmap) with the same hit-test routing and a
`ConfigChanged` event, Windows Forms `TrendChartControl` (`Mori.SkyScope.WinForms`) on `SkiaHostControl`, a CPU bitmap or an
OpenGL surface chosen by `Rendering`, with designer collections (`LaneDefinition`, `AxisDefinition`, `SeriesDefinition`,
`ThresholdDefinition`, `MarkerDefinition` → `BuildDesignerConfig`), the same routing, context menu and layout files.

## 3. Gauges

| Gauge | TypeScript | C# | Notes |
|---|---|---|---|
| Common | `gauges/common.ts` | `Gauges/GaugeCommon.cs` (`GaugeMath`, `SmoothedValue`) | `GaugeTheme`, bands, 1-2-5 scale ticks with minors, polar helpers, exponential damping with wrap-around (compass) and settle threshold. |
| RadialGauge | `gauges/radial.ts` | `Gauges/RadialGauge.cs` | Sweep and start angle, bands, ticks, labels, needle or filled arc, value readout, decimals. |
| LinearGauge | `gauges/linear.ts` | `Gauges/LinearGauge.cs` | Horizontal or vertical track, bands, pointer, caption. |
| Led, LedArray | `gauges/led.ts` | `Gauges/Led.cs` | Round or square LED; arrays in level or bit mode. |
| NumericDisplay | `gauges/numeric.ts` | `Gauges/NumericDisplay.cs` | Seven-segment polygons with slant, off-segment ghosting, unit and label. |
| Compass | `gauges/compass.ts` | `Gauges/Compass.cs` | Needle or rotating card, damped across the 0/360 seam. |
| AttitudeIndicator | `gauges/attitude.ts` | `Gauges/AttitudeIndicator.cs` | Sky/ground horizon, pitch ladder, roll scale, aircraft symbol. |
| Knob, Switch, Slider | `gauges/knob.ts`, `gauges/switch-slider.ts` | `Gauges/Knob.cs`, `Gauges/SwitchSlider.cs` | Inputs with pointer, wheel and keyboard; step, min/max, angle normalisation. |

Fixture `gauges.json`: 21 cases, 10 of them drawing parity. Hosts: `GaugeView` (redraws only while a needle settles)
with `bindKnob` / `bindSlider` / `bindSwitch`, React views for each gauge, Blazor `Gauge.razor` (`Kind` + options +
value callbacks), WPF `GaugeControl`, Windows Forms `GaugeControl`.

## 4. Analytic charts

| Chart | TypeScript | C# | Fixture |
|---|---|---|---|
| CartesianChart | `charts/cartesian.ts` | `Charts/CartesianChart.cs` | `cartesian.json` (10 cases, 2 parity) |
| Histogram helper | `charts/histogram.ts` | `Charts/Histogram.cs` | in `cartesian.json` |
| PieChart | `charts/pie.ts` | `Charts/PieChart.cs` | `pie.json` (4 cases, 1 parity) |
| PolarChart | `charts/polar.ts` | `Charts/PolarChart.cs` | `polar.json` (4 cases, 1 parity) |
| Heatmap + colour maps | `charts/heatmap.ts`, `charts/colormaps.ts` | `Charts/Heatmap.cs`, `Charts/Colormaps.cs` | `heatmap.json` (4 cases, 1 parity) |
| Shared legend, tooltip, markers | `charts/legend.ts`, `drawTooltip` / `drawMarker` in `cartesian.ts` | `Charts/SimpleLegend.cs` (`SimpleLegend`, `ChartDrawing`, `IDrawable`) | through the charts |
| JSON config loader | — | `Charts/ChartJson.cs` | used by fixtures and hosts |

CartesianChart: line, step, scatter, area and bar series; grouped bars by default, stacked bars and areas through a
stack key with sign-aware stacking; category, linear and log axes; second Y axis; nice autoscale; legend toggles;
hover tooltip; box zoom, wheel zoom, reset. Histogram: 1-2-5 bin widths, closed last bin, density option.
PieChart: donut, pad angle, sorting, inside or outside labels, centre text, hover explode. PolarChart: polar or radar
(categories), circle or polygon grid, either rotation direction, closed areas. Heatmap: matrix to RGBA through seven
named maps or custom stops, colour bar, hover cell, rolling spectrogram mode (`pushColumn`).

Hosts: `ChartView` (hover, legend clicks, drag box zoom, wheel, double-click), React `XYChart`, `PieChartView`,
`PolarChartView`, `HeatmapView`, Blazor `Chart.razor` (`Kind` xy | pie | polar | heatmap, `PushColumnAsync`), WPF and Windows Forms
`ChartControl`.

## 5. SceneView

| Component | TypeScript | C# | Fixture |
|---|---|---|---|
| Robotics layers | `scene/robot-layers.ts` | `Scene/RobotLayers.cs` | `robot-layers.json` (8 cases, 1 parity) |
| SceneController | `scene/controller.ts` | `Scene/SceneController.cs` | `scene-controller.json` (5 cases, 1 parity) |
| Layer JSON contract | `scene/layer-json.ts` (`createLayer`, `applyLayerPayload`, `SceneLayerSink`, `FanoutLayerSink`) | `Scene/LayerJson.cs` (`LayerJson`, `SceneLayerSink`) | through the sources |

Layers: `BitmapLayer` (affine placement, ROS map origin convention, drawn through translate/rotate/scale decomposed
from the projection), `OccupancyGridLayer` (−1/0…100 cells, free/occupied/unknown colours, cell hit data),
`PointCloudLayer` (+ `laserScanToPoints` through the sensor pose, intensity colour maps), `PoseLayer` (heading arrow,
footprint, label), `ShapeLayer` (circle, rect, polygon, line, text; topmost hit), `TrailLayer`.
Controller: pan, wheel zoom, shift-drag box zoom, measure (two clicks, Δ in world units), select (hover + click hit
test), Q/E rotate, R reset, F or double-click fit-all, 1-2-5 scale bar, cursor readout, light and dark themes.

Hosts: web `SceneView`, React `SceneView`, Blazor `SceneView.razor` (JSON layer API, `WsUrl` for relayed layers), WPF
`SceneControl`.

## 5b. Scene3DView (sprint 2)

| Component | TypeScript | C# | Fixture |
|---|---|---|---|
| 3D math (Mat4, quaternions, boxes, rays) | `scene3d/math3.ts` | `Scene3D/Math3.cs` | `math3.json` (4 cases) |
| Camera3D (orbit, perspective/ortho, picking) | `scene3d/camera3d.ts` | `Scene3D/Camera3D.cs` | `camera3d.json` (5 cases) |
| FrameTree (tf-style timed transforms) | `scene3d/frame-tree.ts` | `Scene3D/FrameTree.cs` | `frame-tree.json` (4 cases) |
| Painter3D contract + RecordingPainter3D | `scene3d/painter3d.ts` | `Scene3D/IPainter3D.cs` | through scene3d |
| Layers: Grid3D, Axes, PointCloud3D, Frames | `scene3d/layers3d.ts` | `Scene3D/Layers3D.cs` | `scene3d.json` (7 cases, buffer hashes) |
| Layers: Path3D, Pose3D, Markers, LaserScan3D, OccupancyGridPlane | `scene3d/robot-layers3d.ts` (+ `primitives.ts`) | `Scene3D/RobotLayers3D.cs` (+ `Primitives.cs`) | `scene3d.json` |
| Scene3DController (orbit/measure/select, HUD) | `scene3d/controller3d.ts` | `Scene3D/Scene3DController.cs` | `scene3d-controller.json` (4 cases) |
| Binary layer message (`SKSL`) | `streaming/layer-message.ts` | `Streaming/LayerMessage.cs` | `layer-message.json` (3 cases), `spec/frames/layer-cloud.bin` |
| Mesh resources (STL binary/ASCII, GLB) + MeshRegistry, `meshes` layer, `mesh` markers | `scene3d/mesh-formats.ts` | `Scene3D/MeshFormats.cs` | `mesh-formats.json` (4 cases), `spec/meshes/*` |
| URDF → markers + joint transforms, `publishUrdf` | `scene3d/urdf.ts` (own XML reader) | `Scene3D/Urdf.cs` (XDocument) | `urdf.json` (3 cases), `spec/meshes/arm.urdf` |
| Renderers (thick lines as instanced quads) | `render/webgl-painter3d.ts` (WebGL2, GLSL ES 300) | `Render.OpenTK/GlPainter3D.cs` (OpenGL 3.3, same shader bodies) | — |
| Synthetic 3D scene | — | `Sources/SyntheticScene3DSource.cs` | C# test |

Layers draw through `Painter3D` (points, lines, triangles with per-vertex colour, textured quad) with versioned
`Mesh3D` buffers uploaded once per change; the 2D `Painter` draws labels and the HUD on top. Every 3D layer takes a
`frame` and an optional `stamp` and is placed through the sink's shared `FrameTree` into the fixed frame. JSON kinds:
`grid3d`, `axes`, `pointCloud3d`, `laserScan3d`, `path3d`, `pose3d`, `markers`, `occupancyGrid3d`, `frames`;
declarations may carry state (`frames.transforms`, `markers.markers`) so late joiners and recordings get it.
Controller: left drag orbits, middle/shift/space+left pans, right drag dollies, wheel dollies about the cursor,
double-click fits; F fit, R reset, T top-down, O orthographic; measure (two clicks on hits or the ground), select
(hover and selection rings); gizmo and cursor readout.

Hosts: web `Scene3DView` (GL canvas + HUD canvas), React `Scene3DView`, Blazor `SceneView3D.razor`, WPF
`SceneControl3D` (OpenTK `GLWpfControl` + Skia HUD; shows a notice when no OpenGL context is available), Windows Forms
`SceneControl3D` (own OpenGL 3.3 core context on the control window, `GlPainter3D` then Skia on the same framebuffer,
`Snapshot()` reads the pixels back; verified on a desktop against the demo server).

## 6. Source plugins

| Source | TypeScript | C# | Fixture / test |
|---|---|---|---|
| Playback (CSV) | `sources/playback.ts` (`parseCsv`, `Recording`, `PlaybackSource`) | `Sources/Playback.cs` (`CsvRecording`, `PlaybackSource`) | `playback.json` (5 cases) |
| MQTT mapping | `sources/mqtt-mapping.ts` | `Sources/MqttMapping.cs` | `mqtt-mapping.json` (4 cases) |
| MQTT client | `@mori/skyscope-sources` `mqtt-source.ts` (mqtt.js over WebSocket) | `Mori.SkyScope.Sources.Mqtt` (MQTTnet) | — |
| ROS 2 | — | `Mori.SkyScope.Sources.Ros2` on Mori.Ros2Sharp 0.2.0; hand-written CDR decoders (`Cdr`) | compiles; no live peer tested |
| Synthetic robot scene | — | `Sources/SyntheticSceneSource.cs` | used by the demo server and snapshots |
| Template | `ts/packages/source-template` | `Mori.SkyScope.Sources.Template` | `counter-source.test.ts`, `TemplateAndBenchTests.cs` |

Playback: play, pause, speed, seek, loop over a manual clock; seeking backwards resets the sink and replays. MQTT: topic
filters with `+` and `#`, JSON paths, message timestamps, scale/offset, batching into timestamped frames. ROS 2:
std_msgs scalars and arrays, Twist, Pose(Stamped), Odometry, Imu, BatteryState, JointState → signals; LaserScan,
Odometry, Pose, OccupancyGrid → scene layers. Synthetic scene: map with walls and obstacles, lidar ray-cast, a driving
robot, trail and zones; also publishes the pose as channels 100–103.

Recording (backlog 2): MCAP writer/reader (`recording/mcap.ts`, C# `Mcap/Mcap.cs`), `McapRecorder` tee and the SkyScope
topic layout (`recording/recorder.ts`, C# `Mcap/McapRecorder.cs`), layer events in `Recording` and `PlaybackSource`,
`LayerSink.reset`; fixture `mcap.json` (5 cases) and golden `spec/mcap/sample.mcap`. Hosts: React `RecordButton` /
`useRecorder`, WPF and Windows Forms `RecorderControl`, Blazor `Recorder`; demo server `--record` and `/record` endpoints.

Playback UI (backlog 1): React `usePlayback` + `PlaybackControls`, WPF and Windows Forms `PlaybackControl`, Blazor `Playback.razor`
(charts bind with `WsUrl="playback:<key>"`).

## 7. Samples, tools, packaging

- `samples/Mori.SkyScope.DemoServer` — `--channels --rate --batch-ms --quantized`; synthetic signals plus the synthetic
  robot scene relayed on `ws://localhost:5055/ws`.
- `samples/react-sample`, `samples/vanilla-ts` (DOM views without a framework, a custom drag source, the drop hook), `samples/Mori.SkyScope.Blazor.Sample`, `samples/Mori.SkyScope.Wpf.Sample`, `samples/Mori.SkyScope.WinForms.Sample` — the robot dashboard
  (trend chart with the robot pose lane, gauges, analytic charts, live scene; React also has the playback mode, WPF has
  `--csv`). WPF offscreen renders: `--snapshot`, `--logic`, `--charts`, `--scene` (`LogicSnapshot.cs`, `ChartsSnapshot.cs`,
  `SceneSnapshot.cs`) → `docs/images/*.png`.
- Tools: `tools/build-design.mjs`, `gen-frames.mjs`, `gen-demo-csv.mjs`, `pack-npm.mjs`, `bench-ingest.mjs`.
- Packaging: NuGet (Core, Render.Skia, Streaming, Wpf, Blazor with the bundled JS, Sources.Mqtt, Sources.Ros2) and npm
  (tokens, core, render, sources, react, source-template) into `artifacts/`; `.github/workflows/ci.yml`.
- Bench: 500 channels × 1 kHz × 60 s through encode → decode → store: TS 109 Msamples/s, C# 63 Msamples/s, zero drops.
- Docs: `README.md`, `docs/PLAN.md`, `docs/STATUS.md`, `docs/PLUGINS.md`, this file.
