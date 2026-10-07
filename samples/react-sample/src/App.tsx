// Mori.SkyScope — The React dashboard: signal tree, trend chart, scene views, gauges, analytic charts, recording and playback.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useMemo, useState } from "react";
import { Scene3DDemo } from "./Scene3DDemo.js";
import { DARK_THEME, LIGHT_THEME, type Tool, type TrendChartOptions } from "@cmori/skyscope-core";
import { saveFile, type TrendChartView } from "@cmori/skyscope-render";
import { AttitudeView, Button, ChartEditor, CompassView, HeatmapView, KnobView, LedArrayView, LinearGaugeView, NumericDisplayView, PieChartView, PlaybackControls, PolarChartView, RadialGaugeView, RecordButton, SceneView, SignalTree, SliderView, SwitchView, TrendChart, XYChart, usePlayback, useSignalStore, useWebSocketSource } from "@cmori/skyscope-react";
import { McapRecorder, parseCsv, readRecording, type Recording } from "@cmori/skyscope-core";
import { pickFile } from "@cmori/skyscope-render";

/** A 20 s recording generated in code (the same shape as samples/demo-recording.csv) so the playback mode needs no file. */
function demoCsv(): string {
  const rate = 100, lines = ["t,sine,triangle,sawtooth,noise"];
  let seed = 1; const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
  for (let i = 0; i <= rate * 20; i++) { const t = i / rate; lines.push([t.toFixed(3), (2.5 * Math.sin(t * 0.7 * 2 * Math.PI / 3)).toFixed(4), (3 * (2 * Math.abs((t * 0.47) % 1 - 0.5) - 0.5)).toFixed(4), (2 * ((t * 0.84) % 1) - 1).toFixed(4), ((rnd() - 0.5) * 2).toFixed(4)].join(",")); }
  return lines.join("\n");
}
import { FanoutLayerSink, GridLayer, SceneLayerSink, SCENE_DARK, SCENE_LIGHT, type SceneTool } from "@cmori/skyscope-core";
import type { Heatmap } from "@cmori/skyscope-core";
import { useRef } from "react";
import { GAUGE_DARK, GAUGE_LIGHT } from "@cmori/skyscope-core";
import { useEffect } from "react";

// the stream follows the page host, so a dashboard opened from another machine talks to the same server
const WS_URL = new URLSearchParams(location.search).get("ws") ?? `ws://${location.hostname}:5055/ws`;
/** `?focus=trend` shows only the signal tree and the trend chart, full height. */
const FOCUS = new URLSearchParams(location.search).get("focus") === "trend";
/** Time-window lengths offered in the toolbar, in seconds. */
const SPANS = [5, 10, 30, 60, 300];
/** Trend-chart pointer tools offered in the toolbar. */
const TOOLS: Tool[] = ["pan", "boxZoom", "cursor"];

/**
 * The whole dashboard. Two data paths share the page: the live WebSocket stream (through the recorder into `store`)
 * and a playback store fed by `usePlayback`; `mode` selects which one the tree and the trend chart show.
 * Scene layers from either path reach the 2D/3D scene through the `layers` fan-out.
 */
export function App() {
  // ---- data paths: live socket -> recorder -> store; playback -> playbackStore ----
  const store = useSignalStore({ retentionSeconds: 600 });
  const layers = useMemo(() => new FanoutLayerSink(), []);
  // The recorder sits between the socket and the store/scene: what is recorded is exactly what is shown.
  const recorder = useMemo(() => new McapRecorder({ signals: store, layers }, undefined, { maxBytes: 256 * 1024 * 1024 }), [store, layers]);
  const [mode, setMode] = useState<"live" | "playback">("live");
  const { connected } = useWebSocketSource(recorder, { url: WS_URL }, recorder, mode === "live");
  const [sceneTool, setSceneTool] = useState<SceneTool>("pan");
  const [scene3d, setScene3d] = useState(true);
  const playbackStore = useSignalStore({ retentionSeconds: 3600 });
  const [opened, setOpened] = useState<{ name: string; recording: Recording } | null>(null);
  const recording = useMemo(() => (mode === "playback" ? opened?.recording ?? parseCsv(demoCsv()) : null), [mode, opened]);
  const playback = usePlayback(playbackStore, recording, { autoplay: true, loop: true }, layers);
  // ?mcap=<url>: open a served recording straight into playback mode.
  useEffect(() => {
    const url = new URLSearchParams(location.search).get("mcap");
    if (!url) return;
    void fetch(url).then((r) => r.arrayBuffer()).then((buf) => { setOpened({ name: url, recording: readRecording(new Uint8Array(buf)) }); setMode("playback"); });
  }, []);
  /** "open…" button: pick an .mcap or .csv file, parse it and switch to playback. */
  const openFile = async () => {
    const file = await pickFile(".mcap,.csv");
    if (!file) return;
    const rec = file.name.toLowerCase().endsWith(".csv") ? parseCsv(await file.text()) : readRecording(new Uint8Array(await file.arrayBuffer()));
    setOpened({ name: file.name, recording: rec }); setMode("playback");
  };
  // ---- UI state: theme, trend-chart tool/span/pause, the chart view handle, gauge values and input-gauge state ----
  const [dark, setDark] = useState(false);
  const [tool, setTool] = useState<Tool>("pan");
  const [span, setSpan] = useState(30);
  const [paused, setPaused] = useState(false);
  const [editor, setEditor] = useState(false);
  const [view, setView] = useState<TrendChartView | null>(null);
  const [note, setNote] = useState<string | null>(null);
  /** Downloads the visible signals between cursors A and B; the chart view declines without both cursors. */
  const exportCursors = (format: "csv" | "mcap"): void => setNote(view?.exportCursors(format) ? null : "export: set cursors A and B first (cursor tool: click, Shift + click)");
  const [latest, setLatest] = useState<Record<number, number>>({});
  const [gain, setGain] = useState(2.5);
  const [armed, setArmed] = useState(false);
  const [throttle, setThrottle] = useState(30);
  // Latest sample of every live channel, refreshed on each store notification; drives the gauge row.
  useEffect(() => store.subscribe((ids) => {
    const next: Record<number, number> = {};
    for (const id of ids) { const b = store.get(id)?.buffer; if (b && !b.isEmpty) next[id] = b.valueAt(b.headSeq - 1); }
    setLatest((prev) => ({ ...prev, ...next }));
  }), [store]);
  const gtheme = dark ? GAUGE_DARK : GAUGE_LIGHT;
  const ctheme = dark ? DARK_THEME : LIGHT_THEME;

  // Live spectrogram of the noise channel: a 32-bin DFT of the last 128 samples, pushed 5× per second.
  const spectrogram = useRef<{ chart: Heatmap; invalidate: () => void } | null>(null);
  useEffect(() => {
    const id = setInterval(() => {
      const s = spectrogram.current, b = store.get(5)?.buffer;
      if (!s || !b || b.isEmpty) return;
      const n = Math.min(128, b.length), x = new Float64Array(n);
      for (let i = 0; i < n; i++) x[i] = b.valueAt(b.headSeq - n + i);
      const col: number[] = [];
      for (let k = 0; k < 32; k++) { let re = 0, im = 0; for (let i = 0; i < n; i++) { const w = 2 * Math.PI * k * i / n; re += x[i]! * Math.cos(w); im -= x[i]! * Math.sin(w); } col.push(20 * Math.log10(Math.hypot(re, im) / n + 1e-6)); }
      s.chart.pushColumn(col); s.invalidate();
    }, 200);
    return () => clearInterval(id);
  }, [store]);
  // Static option objects of the four analytic charts, rebuilt only when the theme flips (their JSON keys the models).
  const charts = useMemo(() => ({
    bars: { title: "Throughput by hour", theme: ctheme, legend: "top-left" as const, xAxis: { label: "hour" }, yAxis: { unit: "msg/s" }, y2Axis: { unit: "% err" },
      series: [
        { id: "thr", name: "throughput", kind: "bar" as const, y: [420, 380, 350, 330, 360, 410, 520, 640, 700, 690, 660, 640, 610, 600, 620, 650, 680, 720, 700, 650, 590, 540, 490, 450], fillOpacity: 0.55 },
        { id: "avg", name: "3 h average", y: [420, 400, 383, 353, 347, 367, 430, 523, 620, 677, 683, 663, 637, 617, 610, 623, 650, 683, 700, 690, 647, 593, 540, 493], width: 2, color: "#dc2626" },
        { id: "err", name: "error rate", kind: "scatter" as const, axis: "y2" as const, marker: "diamond" as const, color: "#7c3aed", y: [1.5, 1.4, 1.6, 1.5, 1.7, 1.9, 2.4, 2.8, 2.6, 2.2, 2.0, 1.9, 2.3, 2.9, 3.1, 2.7, 2.4, 2.1, 1.9, 1.8, 1.7, 1.6, 1.5, 1.5] },
      ] },
    pie: { title: "CPU time", theme: ctheme, donut: 0.55, padAngle: 1.5, sort: true, legend: "right" as const, slices: [{ id: "perception", value: 38 }, { id: "planning", value: 22 }, { id: "control", value: 17 }, { id: "telemetry", value: 12 }, { id: "logging", value: 7 }, { id: "other", value: 4 }] },
    radar: { title: "Robot comparison", theme: ctheme, categories: ["speed", "payload", "range", "accuracy", "autonomy", "cost"], gridShape: "polygon" as const, max: 10, legend: "bottom-right" as const,
      series: [{ id: "a", name: "AMR-200", values: [8, 6, 7, 9, 5, 6] }, { id: "b", name: "Go2", values: [6, 3, 5, 6, 8, 9], color: "#d97706" }] },
    spec: { title: "Noise spectrogram (live)", theme: ctheme, cols: 100, rows: 32, xMin: -20, xMax: 0, yMin: 0, yMax: 500, xLabel: "s", yLabel: "Hz", valueLabel: "dB", colormap: "inferno" as const, rolling: true, min: -60, max: -10 },
  }), [ctheme]);

  // Trend chart layout: four lanes (analog, fast, robot, digital), three named axes, series bound to the demo channel ids.
  const config = useMemo<TrendChartOptions>(() => ({
    timeSpan: span,
    timeFormat: "utc",
    theme: dark ? DARK_THEME : LIGHT_THEME,
    lanes: [{ id: "analog", weight: 2 }, { id: "fast" }, { id: "robot" }, { id: "digital", weight: 0.5 }],
    axes: [{ id: "axis:analog", label: "sine / tri" }, { id: "axis:fast", label: "saw" }, { id: "axis:robot", label: "amr-1 pose", unit: "m" }],
    series: [
      { id: "s1", channelId: 1, laneId: "analog" }, { id: "s2", channelId: 2, laneId: "analog" },
      { id: "s3", channelId: 3, laneId: "fast" }, { id: "s5", channelId: 5, laneId: "fast", axisId: "noise", width: 1 },
      { id: "s4", channelId: 4, laneId: "digital", kind: "digital" }, { id: "s9", channelId: 9, laneId: "digital", kind: "digital" },
      { id: "pump", channelId: 200, laneId: "digital", kind: "digital" }, { id: "valve", channelId: 201, laneId: "digital", kind: "digital" },
      { id: "px", channelId: 100, laneId: "robot", name: "x" }, { id: "py", channelId: 101, laneId: "robot", name: "y" }, { id: "pv", channelId: 103, laneId: "robot", name: "speed", axisId: "speed" },
    ],
    thresholds: [{ id: "hi", axisId: "axis:analog", from: 1.5, to: 3, color: "#dc2626", label: "high" }],
    markers: [],
    navigator: true,
  }), [dark, span]);

  // ---- layout: toolbar, optional transport bar, main row (tree | trend | scene), gauge row, analytic charts, help line ----
  return (
    <div className="skyscope-root" data-theme={dark ? "dark" : undefined} style={{ height: "100vh", display: "flex", flexDirection: "column", gap: 8, padding: 12, boxSizing: "border-box" }}>
      {/* toolbar: connection state, trend tools and spans, pause/reset, theme, live/playback mode, open and record */}
      <div style={{ display: "flex", gap: 8, alignItems: "center", flexWrap: "wrap" }}>
        <strong>Mori.SkyScope</strong>
        <span style={{ color: connected ? "#16a34a" : "#dc2626" }}>{connected ? "● live" : "○ connecting…"} {WS_URL}</span>
        <span style={{ flex: 1 }} />
        {TOOLS.map((t) => <Button key={t} size="sm" active={tool === t} onClick={() => setTool(t)}>{t}</Button>)}
        <span style={{ width: 8 }} />
        {SPANS.map((s) => <Button key={s} size="sm" active={span === s} onClick={() => { setSpan(s); view?.model.setTimeSpan(s); }}>{s} s</Button>)}
        <span style={{ width: 8 }} />
        <Button size="sm" variant={paused ? "primary" : "default"} onClick={() => { if (paused) view?.model.resume(); else view?.model.pause(); setPaused(!paused); }}>{paused ? "▶ live" : "⏸ pause"}</Button>
        <Button size="sm" onClick={() => { view?.model.reset(); setPaused(false); }}>reset</Button>
        <Button size="sm" variant="ghost" onClick={() => setDark(!dark)}>{dark ? "light" : "dark"}</Button>
        <span style={{ width: 8 }} />
        <Button size="sm" active={mode === "live"} onClick={() => setMode("live")}>live</Button>
        <Button size="sm" active={mode === "playback"} onClick={() => setMode("playback")}>playback</Button>
        <Button size="sm" onClick={() => void openFile()}>open…</Button>
        <span style={{ width: 8 }} />
        <Button size="sm" variant="ghost" onClick={() => { if (view) saveFile(new Blob([view.exportLayout()], { type: "application/json" }), "skyscope-layout.json", "application/json"); }}>save layout</Button>
        <Button size="sm" variant="ghost" onClick={() => { void pickFile(".json").then(async (f) => { if (f && view) view.importLayout(await f.text()); }); }}>load layout</Button>
        <Button size="sm" variant="ghost" onClick={() => exportCursors("csv")}>export A→B</Button>
        <Button size="sm" variant="ghost" onClick={() => exportCursors("mcap")}>export A→B mcap</Button>
        {note && <span style={{ fontSize: 12, color: "#dc2626" }}>{note}</span>}
        <Button size="sm" active={editor} onClick={() => setEditor(!editor)}>editor</Button>
        {mode === "live" && <RecordButton recorder={recorder} streamToFile />}
      </div>
      {/* transport bar, playback mode only */}
      {mode === "playback" && <div style={{ display: "flex", alignItems: "center", gap: 8 }}><PlaybackControls playback={playback} style={{ flex: 1 }} /><span style={{ fontSize: 12, color: "var(--skyscope-color-text-secondary)" }}>{opened?.name ?? "demo recording (generated)"}</span></div>}
      {/* main row: signal tree, trend chart and (unless focused) the 2D or 3D scene; tree and chart follow the active mode's store */}
      <div style={{ flex: 1, minHeight: 0, display: "grid", gridTemplateColumns: FOCUS ? "220px 1fr" : "200px 2fr 1fr", gap: 8 }}>
        {editor ? <ChartEditor chart={view} /> : <SignalTree store={mode === "playback" ? playbackStore : store} chart={view} />}
        <TrendChart store={mode === "playback" ? playbackStore : store} clock={mode === "playback" ? playback.source.clock : undefined} config={config} tool={tool} onReady={setView} />
        {!FOCUS && (scene3d ? <div style={{ display: "flex", flexDirection: "column", gap: 4, minHeight: 0 }}>
          <div style={{ display: "flex", gap: 4 }}><Button size="sm" active onClick={() => setScene3d(false)}>3D</Button><Button size="sm" onClick={() => setScene3d(false)}>2D</Button></div>
          <div style={{ flex: 1, minHeight: 0 }}><Scene3DDemo layers={layers} dark={dark} /></div>
        </div> : <div style={{ display: "flex", flexDirection: "column", gap: 4, minHeight: 0 }}>
          <div style={{ display: "flex", gap: 4 }}>
            <Button size="sm" onClick={() => setScene3d(true)}>3D</Button>
            {(["pan", "boxZoom", "measure", "select"] as SceneTool[]).map((t) => <Button key={t} size="sm" active={sceneTool === t} onClick={() => setSceneTool(t)}>{t}</Button>)}
            <span style={{ fontSize: 11, alignSelf: "center", color: "var(--skyscope-color-text-secondary)" }}>Q/E rotate · F fit · wheel zoom</span>
          </div>
          <div style={{ flex: 1, minHeight: 0, border: "1px solid var(--skyscope-color-border)", borderRadius: 4, overflow: "hidden" }}>
            <SceneView key={dark ? "d" : "l"} tool={sceneTool} options={{ theme: dark ? SCENE_DARK : SCENE_LIGHT, unit: "m" }} onReady={(c, v) => {
              c.scene.add(new GridLayer("grid", { stroke: { color: dark ? "#1e293b" : "#e2e8f0", width: 1 }, majorStroke: { color: dark ? "#334155" : "#cbd5e1", width: 1 } }));
              let fitted = false;
              layers.add(new SceneLayerSink(c.scene, () => { if (!fitted && c.scene.layers.length > 1) { fitted = true; c.fitAll(); } v.invalidate(); }));
            }} />
          </div>
        </div>)}
      </div>
      {/* gauge row: display gauges driven by the latest live samples, plus the three input gauges bound to local state */}
      {!FOCUS && <div style={{ display: "grid", gridTemplateColumns: "repeat(7, 1fr)", gap: 8, height: 200 }}>
        <RadialGaugeView value={latest[1] ?? 0} options={{ min: -4, max: 4, unit: "m/s", label: "sine", decimals: 2, bands: [{ from: 3, to: 4, color: "#dc2626" }, { from: -4, to: -3, color: "#dc2626" }], theme: gtheme }} />
        <LinearGaugeView value={latest[2] ?? 0} options={{ min: -4, max: 4, unit: "°C", label: "triangle", orientation: "vertical", decimals: 1, theme: gtheme }} />
        <CompassView heading={(latest[1] ?? 0) * 45 + 180} options={{ label: "HDG", theme: gtheme }} />
        <AttitudeView pitch={(latest[1] ?? 0) * 8} roll={(latest[2] ?? 0) * 15} options={{ theme: gtheme }} />
        <NumericDisplayView value={latest[3] ?? null} options={{ digits: 6, decimals: 3, unit: "V", label: "sawtooth", theme: gtheme }} />
        <LedArrayView value={latest[5] ?? 0} options={{ count: 12, min: -1.5, max: 1.5, label: "noise", orientation: "vertical", theme: gtheme }} />
        <div style={{ display: "flex", flexDirection: "column", gap: 4 }}>
          <div style={{ flex: 1, minHeight: 0 }}><KnobView value={gain} onChange={setGain} options={{ min: 0, max: 10, step: 0.5, label: "gain", unit: "x", theme: gtheme }} /></div>
          <div style={{ height: 44 }}><SwitchView on={armed} onChange={setArmed} options={{ label: "arm", theme: gtheme }} /></div>
          <div style={{ height: 44 }}><SliderView value={throttle} onChange={setThrottle} options={{ min: 0, max: 100, label: "throttle", unit: "%", theme: gtheme }} /></div>
        </div>
      </div>}
      {/* analytic charts: static bar/line, pie and radar, and the live spectrogram heatmap */}
      {!FOCUS && <div style={{ display: "grid", gridTemplateColumns: "1.4fr 1fr 1fr 1.2fr", gap: 8, height: 230 }}>
        <XYChart options={charts.bars} />
        <PieChartView options={charts.pie} />
        <PolarChartView options={charts.radar} />
        <HeatmapView options={charts.spec} onReady={(chart, view) => { spectrogram.current = { chart, invalidate: () => view.invalidate() }; }} />
      </div>}
      {/* gesture cheat sheet for the trend chart */}
      <div style={{ fontSize: 12, color: "var(--skyscope-color-text-secondary)" }}>
        drag = pan · wheel = zoom · shift+drag = box zoom · click = cursor A · shift+click = cursor B · double-click = reset · drag a signal label onto an axis (shared scale), into a lane (own scale) or onto the time axis (new lane) · Ctrl/Shift picks several · lane bar: drag to reorder, ▾ folds, ✕ removes · drag the Y-axis middle to shift, its ends to stretch, wheel zooms, double-click autoscales · navigator: drag the red frame, its edges, click to centre, ←/→ · drag the gap between lanes to resize · click a signal name to hide/show it, right-click for colour, width, rename, remove · drag cursor A/B; both set = measurement table
      </div>
    </div>
  );
}
