// Mori.SkyScope — Fixture driver for the trend chart: setup, commands, layout, hit-test, drop, navigator and drawing queries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SignalStore } from "../sources/signal-store.js";
import { frameFromJson, type FrameJson } from "../streaming/frame-json.js";
import { TrendChartModel } from "../charts/trend-model.js";
import { encodeChannelDrag, parseChannelDrag, type ChannelDragPayload } from "../charts/channel-drag.js";
import { drawTrendChart } from "../charts/trend-draw.js";
import { RecordingPainter } from "../paint/recording-painter.js";
import { exportRangeCsv, exportRangeMcap } from "../recording/export.js";
import { readRecording } from "../recording/recorder.js";
import { channelCount } from "../streaming/frame.js";
import { fnv1a } from "../charts/colormaps.js";
import type { ChannelInfo } from "../sources/contracts.js";
import type { DropTarget, TrendChartConfig, TrendLayout } from "../charts/trend-config.js";
import type { Effect } from "../scene/interaction.js";
import { r9, rRect } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const opt = (v: unknown): number | null => (v === null || v === undefined ? null : r9(v as number));
/** Canonical form of a drag payload for parity queries: every field present, null when absent. */
const dragJson = (p: ChannelDragPayload) => ({ channels: p.channels.map((c) => ({ id: c.id, name: c.name ?? null, unit: c.unit ?? null, kind: c.kind ?? null })), group: p.group });

interface State { model: TrendChartModel; width: number; height: number; layout: TrendLayout | null; queries: unknown[]; snapshot?: string | undefined }

/**
 * Trend chart model: `setup` gives channels, frames, config, chart time and size; the layout is cached until a step
 * invalidates it.
 */
export const trendDriver: FixtureDriver<State> = {
  component: "trend-chart",
  create(setup) {
    const store = new SignalStore({ retentionSeconds: 100, defaultCapacity: 4096 });
    for (const c of (setup.channels as ChannelInfo[] | undefined) ?? []) store.declareChannel(c);
    for (const f of (setup.frames as FrameJson[] | undefined) ?? []) store.pushFrame(frameFromJson(f));
    const model = new TrendChartModel(store, structuredClone((setup.config as Partial<TrendChartConfig> | undefined) ?? {}));
    model.now = num(setup.now ?? 0);
    return { model, width: num(setup.width), height: num(setup.height), layout: null, queries: [] };
  },
  /**
   * Steps mirror the model's commands (time control, cursors, series, lane and axis drag and drop, navigator,
   * effects); queries expose layout, hit tests, drag state, configuration, window, domains, polylines, readouts,
   * cursors, legend values, drop targets and recorded draw ops.
   */
  step(s, step: Step) {
    const { model, queries } = s;
    const layout = (): TrendLayout => s.layout ?? (s.layout = model.layout(s.width, s.height));
    switch (step.type) {
      case "update": s.layout = model.layout(s.width, s.height); model.update(s.layout); break;
      case "setNow": model.now = num(step.now); break;
      case "pause": model.pause(); break;
      case "resume": model.resume(); break;
      case "scrollBy": model.scrollBy(num(step.seconds)); break;
      case "setTimeSpan": model.setTimeSpan(num(step.seconds)); break;
      case "setCursor": model.setCursor(step.which as "a" | "b", step.time === null ? null : num(step.time)); break;
      case "moveSeries": model.moveSeries(step.seriesId as string, step.laneId as string, step.axisId as string | undefined); break;
      case "reset": model.reset(); break;
      case "setLegend": model.config.legend = step.position as TrendChartConfig["legend"]; s.layout = null; break;
      case "applyDrop": model.applyDrop(step.seriesId as string, step.target as DropTarget); s.layout = null; break;
      case "beginDrag": model.beginDrag((step.seriesIds as string[] | undefined) ?? (step.seriesId as string), num(step.x), num(step.y), step.group === true, (step.channelIds as number[] | undefined) ?? [], step.digital as boolean | undefined); break;
      case "addLane": model.addLane(step.index === undefined ? undefined : num(step.index), step.label as string | undefined); s.layout = null; break;
      case "updateLane": model.updateLane(step.laneId as string, step.patch as Record<string, never>); s.layout = null; break;
      case "addAxis": model.addAxis(step.props as Record<string, never> | undefined, step.id as string | undefined); s.layout = null; break;
      case "updateAxis": model.updateAxis(step.axisId as string, step.patch as Record<string, never>); s.layout = null; break;
      case "removeAxis": model.removeAxis(step.axisId as string); s.layout = null; break;
      case "updateSeries": model.updateSeries(step.seriesId as string, step.patch as Record<string, never>); s.layout = null; break;
      case "addThreshold": model.addThreshold(step.axisId as string, num(step.from), step.to as number | null | undefined, step.color as string | null | undefined, step.label as string | null | undefined, step.id as string | undefined); s.layout = null; break;
      case "updateThreshold": model.updateThreshold(step.id as string, step.patch as Record<string, never>); break;
      case "removeThreshold": model.removeThreshold(step.id as string); break;
      case "addMarker": model.addMarker(num(step.time), step.label as string | null | undefined, step.color as string | null | undefined, step.id as string | undefined); break;
      case "updateMarker": model.updateMarker(step.id as string, step.patch as Record<string, never>); break;
      case "removeMarker": model.removeMarker(step.id as string); break;
      case "reorderSeries": model.reorderSeries(step.seriesId as string, num(step.index)); s.layout = null; break;
      case "snapshotConfig": s.snapshot = model.snapshotConfig(); break;
      case "restoreConfig": if (s.snapshot !== undefined) model.restoreConfig(s.snapshot); s.layout = null; break;
      case "addChannels": model.addChannels(step.channelIds as number[], step.target as DropTarget | undefined, step.group === true); s.layout = null; break;
      case "applyGroupDrop": model.applyGroupDrop(step.seriesIds as string[], step.target as DropTarget, step.group === true); s.layout = null; break;
      case "setSeriesVisible": model.setSeriesVisible(step.seriesId as string, step.visible !== false); s.layout = null; break;
      case "moveLane": model.moveLane(step.laneId as string, num(step.index)); s.layout = null; break;
      case "setLaneCollapsed": model.setLaneCollapsed(step.laneId as string, step.collapsed !== false); s.layout = null; break;
      case "removeLane": model.removeLane(step.laneId as string); s.layout = null; break;
      case "beginLaneDrag": model.beginLaneDrag(step.laneId as string, num(step.x), num(step.y)); break;
      case "beginLaneResize": model.beginLaneResize(layout(), step.aboveLaneId as string, step.belowLaneId as string, num(step.y)); break;
      case "updateLaneResize": model.updateLaneResize(layout(), num(step.y)); s.layout = null; break;
      case "endLaneResize": model.endLaneResize(); break;
      case "beginCursorDrag": model.beginCursorDrag(step.which as "a" | "b"); break;
      case "updateCursorDrag": model.updateCursorDrag(layout(), num(step.x)); s.layout = null; break;
      case "endCursorDrag": model.endCursorDrag(); break;
      case "toggleSeries": model.toggleSeries(step.seriesId as string); s.layout = null; break;
      case "renameSeries": model.renameSeries(step.seriesId as string, (step.name as string | null | undefined) ?? null); s.layout = null; break;
      case "setSeriesColor": model.setSeriesColor(step.seriesId as string, (step.color as string | null | undefined) ?? null); break;
      case "setSeriesWidth": model.setSeriesWidth(step.seriesId as string, step.width === null || step.width === undefined ? null : num(step.width)); break;
      case "removeSeries": model.removeSeries(step.seriesId as string); s.layout = null; break;
      case "importLayout": model.importLayout(step.json as string); s.layout = null; break;
      case "updateLaneDrag": model.updateLaneDrag(layout(), num(step.x), num(step.y)); break;
      case "endLaneDrag": model.endLaneDrag(layout(), num(step.x), num(step.y)); s.layout = null; break;
      case "beginAxisDrag": model.beginAxisDrag(layout(), step.axisId as string, step.laneId as string, step.zone as "top" | "middle" | "bottom", num(step.y)); break;
      case "updateAxisDrag": model.updateAxisDrag(layout(), num(step.y)); break;
      case "endAxisDrag": model.endAxisDrag(); break;
      case "axisZoomAt": model.axisZoomAt(layout(), step.axisId as string, step.laneId as string, num(step.y), num(step.factor)); break;
      case "axisAutoscale": model.axisAutoscale(step.axisId as string); break;
      case "setNavigatorRange": model.navigatorRange = step.range === null ? null : (step.range as [number, number]); break;
      case "beginNavigatorDrag": model.beginNavigatorDrag(layout(), num(step.x)); break;
      case "updateNavigatorDrag": model.updateNavigatorDrag(layout(), num(step.x)); break;
      case "endNavigatorDrag": model.endNavigatorDrag(); break;
      case "navigatorCenterAt": model.navigatorCenterAt(layout(), num(step.x)); break;
      case "navigatorKey": model.navigatorKey(step.key as string); break;
      case "setReviewEnd": model.setReviewEnd(num(step.t)); break;
      case "updateDrag": model.updateDrag(layout(), num(step.x), num(step.y)); break;
      case "endDrag": model.endDrag(layout(), num(step.x), num(step.y)); s.layout = null; break;
      case "cancelDrag": model.cancelDrag(); break;
      case "effect": model.applyEffect(step.effect as Effect, layout()); break;
      case "query":
        if ("layout" in step) { const l = layout(); queries.push({ plot: rRect(l.plot), timeAxis: rRect(l.timeAxis), legend: rRect(l.legend), navigator: rRect(l.navigator), measure: rRect(l.measure), lanes: l.lanes.map((ln) => ({ laneId: ln.laneId, rect: rRect(ln.rect), analog: rRect(ln.analog), stack: rRect(ln.stack), collapsed: ln.collapsed, header: rRect(ln.header), axes: ln.axes.map((a) => ({ axisId: a.axisId, side: a.side, rect: rRect(a.rect) })), labels: ln.labels.map((lb) => ({ seriesId: lb.seriesId, rect: rRect(lb.rect) })) })) }); }
        else if ("hitTest" in step) { const [x, y] = step.hitTest as [number, number]; queries.push(model.hitTest(layout(), x, y)); }
        else if ("laneDrag" in step) queries.push(model.laneDrag ? { laneId: model.laneDrag.laneId, index: model.laneDrag.index } : null);
        else if ("laneWeights" in step) queries.push(model.lanes().map((l) => ({ id: l.id, weight: r9(l.weight ?? 1) })));
        else if ("measurements" in step) { const ms = model.measurements(); queries.push(ms ? { t0: r9(ms.t0), t1: r9(ms.t1), dt: r9(ms.dt), hz: ms.hz === null ? null : r9(ms.hz), rows: ms.rows.map((x) => ({ seriesId: x.seriesId, a: x.a === null ? null : r9(x.a), b: x.b === null ? null : r9(x.b), delta: x.delta === null ? null : r9(x.delta), min: x.min === null ? null : r9(x.min), max: x.max === null ? null : r9(x.max), mean: x.mean === null ? null : r9(x.mean), count: x.count })) } : null); }
        else if ("seriesStyle" in step) { const sc = model.config.series.find((x) => x.id === step.seriesStyle); queries.push(sc ? { name: sc.name ?? null, color: sc.color ?? null, width: sc.width ?? null, visible: sc.visible !== false } : null); }
        else if ("layoutJson" in step) {
          // canonical projection of the file so both cores compare the same facts regardless of which default keys they write
          const f = JSON.parse(model.exportLayout()) as Record<string, unknown>;
          const lanes = (f.lanes as { id: string; weight?: number; collapsed?: boolean }[] | undefined) ?? [];
          const axes = (f.axes as { id: string; label?: string; unit?: string; min?: number; max?: number; side?: string }[] | undefined) ?? [];
          const series = (f.series as { id: string; channelId: number; laneId?: string; axisId?: string; name?: string; color?: string; width?: number; visible?: boolean; kind?: string }[] | undefined) ?? [];
          queries.push({ version: f.version, timeSpan: f.timeSpan, timeFormat: f.timeFormat ?? null, legend: f.legend ?? null, theme: "theme" in f, style: "style" in f,
            lanes: lanes.map((l) => ({ id: l.id, weight: r9(l.weight ?? 1), collapsed: l.collapsed === true })),
            axes: axes.map((a) => ({ id: a.id, label: a.label ?? null, unit: a.unit ?? null, min: a.min === undefined ? null : r9(a.min), max: a.max === undefined ? null : r9(a.max), side: a.side ?? "left" })),
            series: series.map((x) => ({ id: x.id, channelId: x.channelId, laneId: x.laneId ?? null, axisId: x.axisId ?? null, name: x.name ?? null, color: x.color ?? null, width: x.width === undefined ? null : r9(x.width), visible: x.visible !== false, kind: x.kind ?? "analog" })),
            thresholds: ((f.thresholds as unknown[] | undefined) ?? []).length, markers: ((f.markers as unknown[] | undefined) ?? []).length });
        }
        else if ("axisDrag" in step) queries.push(model.axisDrag ? { axisId: model.axisDrag.axisId, zone: model.axisDrag.zone, min0: r9(model.axisDrag.min0), max0: r9(model.axisDrag.max0) } : null);
        else if ("laneConfig" in step) queries.push(model.lanes().map((l) => ({ id: l.id, collapsed: l.collapsed === true })));
        else if ("seriesIds" in step) queries.push(model.config.series.map((x) => ({ id: x.id, channelId: x.channelId, lane: model.laneIdOf(x), axis: model.axisIdOf(x), visible: x.visible !== false })));
        else if ("navigatorRange" in step) { const r = model.navigatorFullRange(); queries.push({ t0: r9(r.t0), t1: r9(r.t1) }); }
        else if ("navigatorFrame" in step) queries.push(rRect(model.navigatorFrame(layout())));
        else if ("navigatorZone" in step) queries.push(model.navigatorZone(layout(), num(step.navigatorZone)));
        else if ("navigatorSilhouettes" in step) queries.push(model.navigatorSilhouettes(layout()).map((x) => ({ seriesId: x.seriesId, color: x.color, count: x.points.length / 3, first: Array.from(x.points.subarray(0, 3), r9), last: Array.from(x.points.subarray(Math.max(0, x.points.length - 3)), r9) })));
        else if ("window" in step) { const w = model.window(); queries.push({ t0: r9(w.t0), t1: r9(w.t1) }); }
        else if ("yDomain" in step) queries.push(model.yDomain(step.yDomain as string).map(r9));
        else if ("polyline" in step) queries.push(Array.from(model.seriesPolyline(step.polyline as string), r9));
        else if ("readout" in step) { const r = model.readout(num(step.readout)); queries.push({ time: r9(r.time), values: r.values.map((v) => ({ seriesId: v.seriesId, time: r9(v.time), value: r9(v.value) })) }); }
        else if ("cursorReadouts" in step) {
          const c = model.cursorReadouts();
          const ro = (r: { time: number; values: { seriesId: string; time: number; value: number }[] } | null) => (r ? { time: r9(r.time), values: r.values.map((v) => ({ seriesId: v.seriesId, time: r9(v.time), value: r9(v.value) })) } : null);
          queries.push({ a: ro(c.a), b: ro(c.b), delta: c.delta ? { dt: r9(c.delta.dt), values: c.delta.values.map((d) => ({ seriesId: d.seriesId, delta: r9(d.delta) })) } : null });
        }
        else if ("legendValue" in step) { const sc = model.config.series.find((x) => x.id === step.legendValue)!; queries.push(opt(model.legendValue(sc))); }
        else if ("state" in step) queries.push({ paused: model.paused, reviewEnd: opt(model.reviewEnd), timeSpan: r9(model.timeSpan), cursorA: opt(model.cursorA), cursorB: opt(model.cursorB), hoverTime: opt(model.hoverTime) });
        else if ("axis" in step) { const a = model.config.axes.find((x) => x.id === step.axis); queries.push({ min: opt(a?.min), max: opt(a?.max) }); }
        else if ("laneAxes" in step) queries.push(model.axesIn(step.laneAxes as string).map((a) => a.id));
        else if ("tracks" in step) queries.push(model.digitalTracks(step.tracks as string).map((x) => x.id));
        else if ("seriesRange" in step) { const sc = model.config.series.find((x) => x.id === step.seriesRange)!; const lane = layout().lanes.find((l) => l.laneId === model.laneIdOf(sc))!; queries.push([...model.seriesScale(sc, lane).range].map(r9)); }
        else if ("legendRect" in step) queries.push(rRect(layout().legend));
        else if ("dropTarget" in step) { const [x, y] = step.dropTarget as [number, number]; queries.push(model.dropTarget(layout(), x, y)); }
        else if ("dropTargetDigital" in step) { const [x, y] = step.dropTargetDigital as [number, number]; queries.push(model.dropTarget(layout(), x, y, true)); }
        else if ("legendRowAt" in step) { const [x, y] = step.legendRowAt as [number, number]; queries.push(model.legendRowAt(layout(), x, y)); }
        else if ("lanes" in step) queries.push(model.lanes().map((l) => l.id));
        else if ("seriesLane" in step) queries.push(model.laneIdOf(model.config.series.find((x) => x.id === step.seriesLane)!));
        else if ("seriesAxis" in step) queries.push(model.config.series.find((x) => x.id === step.seriesAxis)!.axisId ?? null);
        else if ("editorRows" in step) queries.push(model.editorRows());
        else if ("thresholds" in step) queries.push(model.config.thresholds.map((t) => ({ id: t.id, axisId: t.axisId, from: r9(t.from), to: t.to === undefined ? null : r9(t.to), color: t.color, label: t.label ?? null })));
        else if ("markers" in step) queries.push(model.config.markers.map((m) => ({ id: m.id, time: r9(m.time), label: m.label ?? null, color: m.color ?? null })));
        else if ("axesConfig" in step) queries.push(model.config.axes.map((a) => ({ id: a.id, label: a.label ?? null, unit: a.unit ?? null, min: opt(a.min), max: opt(a.max), side: a.side ?? "left", color: a.color ?? null })));
        else if ("lanesConfig" in step) queries.push(model.lanes().map((l) => ({ id: l.id, label: l.label ?? null, weight: r9(l.weight ?? 1), collapsed: l.collapsed === true, keep: l.keep === true })));
        else if ("parseChannelDrag" in step) { const p = parseChannelDrag(step.parseChannelDrag as string); queries.push(p ? dragJson(p) : null); }
        else if ("channelDragRoundTrip" in step) { const p = parseChannelDrag(encodeChannelDrag(step.channelDragRoundTrip as ChannelDragPayload)); queries.push(p ? dragJson(p) : null); }
        else if ("drag" in step) queries.push(model.drag ? { seriesId: model.drag.seriesId, x: r9(model.drag.x), y: r9(model.drag.y), target: model.drag.target } : null);
        else if ("draw" in step) { const p = new RecordingPainter(s.width, s.height); drawTrendChart(model, p, layout()); queries.push(p.ops); }
        else if ("cursorRange" in step) { const r = model.cursorRange(); queries.push(r ? { t0: r9(r.t0), t1: r9(r.t1) } : null); }
        else if ("exportCsv" in step) {
          // explicit channels and range → the store function; otherwise the chart's cursor wrapper (null without both cursors)
          const o = step.exportCsv as { channelIds?: number[]; t0?: number; t1?: number; decimals?: number; valueDecimals?: number };
          const opts = { decimals: o.decimals, valueDecimals: o.valueDecimals };
          queries.push(o.channelIds ? exportRangeCsv(model.store, o.channelIds, num(o.t0), num(o.t1), opts) : model.exportCursorsCsv(opts));
        }
        else if ("exportMcapSummary" in step) {
          const o = step.exportMcapSummary as { channelIds?: number[]; t0?: number; t1?: number };
          const bytes = o.channelIds ? exportRangeMcap(model.store, o.channelIds, num(o.t0), num(o.t1)) : model.exportCursorsMcap();
          if (!bytes) queries.push(null);
          else { const rec = readRecording(bytes); queries.push({ length: bytes.length, hash: fnv1a(bytes), channels: rec.channels.length, frames: rec.frames.length, samples: rec.frames.reduce((n, f) => n + f.channels.reduce((m, c) => m + channelCount(c), 0), 0), start: r9(rec.start), end: r9(rec.end) }); }
        }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
