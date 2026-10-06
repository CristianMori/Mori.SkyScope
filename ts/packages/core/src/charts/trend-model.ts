// Mori.SkyScope — Headless state of a strip/trend chart over a SignalStore: time window (live, paused, review), per-axis autoscale, cursors, hover, and how…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { BucketSeries } from "../signals/bucket-series.js";
import type { SignalStore } from "../sources/signal-store.js";
import { LinearScale, TimeScale } from "../scales/scale.js";
import type { Effect } from "../scene/interaction.js";
import { rectContains, type Rect } from "../scene/geometry.js";
import { defaultTrendConfig, legendRowHeight, SERIES_PALETTE, type TrendChartOptions, type AxisConfig, type AxisDragState, type DragState, type DropTarget, type HitRegion, type LaneConfig, type LaneDragState, type LaneLayout, type NavigatorDragState, type SeriesConfig, type TrendChartConfig, type TrendLayout } from "./trend-config.js";
import { layoutTrendChart } from "./trend-layout.js";

/** One sample of one series. */
export interface SeriesValue { /** Series id. */ seriesId: string; /** Sample time in chart seconds. */ time: number; /** Sample value. */ value: number }
/** Values of every visible series at a time; series without a sample at or before it are absent. */
export interface Readout { /** Requested time in chart seconds. */ time: number; /** One entry per series that had a sample. */ values: SeriesValue[] }
/** Readouts at cursors A and B and their differences; `delta` is null unless both cursors are set. */
export interface CursorReadouts { /** Readout at cursor A, or null. */ a: Readout | null; /** Readout at cursor B, or null. */ b: Readout | null; /** `dt` = B − A in seconds and the value differences for series present in both readouts. */ delta: { dt: number; values: { seriesId: string; delta: number }[] } | null }

/**
 * Headless state of a strip/trend chart over a SignalStore: time window (live, paused, review), per-axis
 * autoscale, cursors, hover, and how gestures change all of that. Drawing lives in `trend-draw.ts`.
 * Mirrors `Mori.SkyScope.Core.Charts.TrendChartModel`; pinned by `spec/fixtures/trend-chart.json`.
 */
export class TrendChartModel {
  /** Live configuration; commands mutate it in place (lanes, axes, series placement). */
  config: TrendChartConfig;
  /** Chart time (seconds); the live right edge. */
  now = 0;
  /** Right edge frozen at `reviewEnd` instead of following `now`. */
  paused = false;
  /** Right edge while paused/reviewing. */
  reviewEnd: number | null = null;
  /** Visible width in seconds; starts from the config and changes with zoom and navigator drags. */
  timeSpan: number;
  /** Cursor A time in chart seconds, or null. */
  cursorA: number | null = null;
  /** Cursor B time in chart seconds, or null. */
  cursorB: number | null = null;
  /** Time under the pointer in chart seconds, or null when outside the plot. */
  hoverTime: number | null = null;
  /** Series (or channels from a signal tree) being dragged onto a lane, axis or gap (see `beginDrag`). */
  drag: DragState | null = null;
  /** A lane header being dragged to reorder lanes. */
  laneDrag: LaneDragState | null = null;
  /** A Y-axis being shifted or stretched by its scale ends. */
  axisDrag: AxisDragState | null = null;
  /** The navigator frame being moved or resized. */
  navDrag: NavigatorDragState | null = null;
  /** Full range the navigator shows; null = retained history up to now (playback hosts set the recording's span). */
  navigatorRange: [number, number] | null = null;
  private readonly navBuckets = new Map<string, BucketSeries>();
  private navQuantum = 0;
  private laneSeq = 0;
  private readonly buckets = new Map<string, BucketSeries>();
  private readonly yDomains = new Map<string, [number, number]>();
  private quantum = 0;

  /** Binds the chart to a store (kept by reference) with the defaults merged under the options. */
  constructor(readonly store: SignalStore, config: TrendChartOptions = {}) {
    this.config = defaultTrendConfig(config);
    this.timeSpan = this.config.timeSpan;
  }

  // ---- configuration views -------------------------------------------------
  /** Lanes top to bottom; creates the default lane "main" in the config when it has none. */
  lanes(): LaneConfig[] { if (this.config.lanes.length === 0) this.config.lanes.push({ id: "main" }); return this.config.lanes; }
  /** Lane of a series: explicit, else the first lane. */
  laneIdOf(s: SeriesConfig): string { return s.laneId ?? this.lanes()[0]!.id; }
  /** Digital series have no axis: they report their lane's logic stack (`stack:<laneId>`). */
  axisIdOf(s: SeriesConfig): string { return s.kind === "digital" ? `stack:${this.laneIdOf(s)}` : s.axisId ?? `axis:${this.laneIdOf(s)}`; }
  /** Series not flagged invisible, in config order. */
  visibleSeries(): SeriesConfig[] { return this.config.series.filter((s) => s.visible !== false); }
  /** Visible series of a lane, in config order. */
  seriesIn(laneId: string): SeriesConfig[] { return this.visibleSeries().filter((s) => this.laneIdOf(s) === laneId); }
  /** Stroke colour: explicit, else from the palette by configured index. */
  seriesColor(s: SeriesConfig): string { return s.color ?? SERIES_PALETTE[Math.max(0, this.config.series.indexOf(s)) % SERIES_PALETTE.length]!; }
  /** Display name: `name`, else the channel name, else the id. */
  seriesName(s: SeriesConfig): string { return s.name ?? this.store.get(s.channelId)?.info.name ?? s.id; }
  /** Axis config by id; unknown ids get a detached default (autoscale, left side). */
  axis(axisId: string): AxisConfig {
    return this.config.axes.find((a) => a.id === axisId) ?? { id: axisId };
  }
  /** Axes used by a lane's analog series, in first-appearance order with left-side axes before right-side ones. */
  axesIn(laneId: string): AxisConfig[] {
    const ids: string[] = [];
    for (const s of this.seriesIn(laneId)) { if (s.kind === "digital") continue; const id = this.axisIdOf(s); if (!ids.includes(id)) ids.push(id); }
    const axes = ids.map((id) => this.axis(id));
    return [...axes.filter((a) => (a.side ?? "left") === "left"), ...axes.filter((a) => a.side === "right")];
  }
  /** Digital series of a lane: always the logic-analyzer stack at the bottom, top to bottom in series order. */
  digitalTracks(laneId: string): SeriesConfig[] { return this.seriesIn(laneId).filter((s) => s.kind === "digital"); }
  /** Band for track `index` of `count` in the lane's stack. */
  trackRect(lane: LaneLayout, index: number, count: number): Rect {
    const st = lane.stack ?? lane.rect;
    const h = st.h / Math.max(1, count);
    return { x: st.x, y: st.y + index * h, w: st.w, h };
  }
  /** The scale a series draws with: its own track when stacked, otherwise its axis. Low/high sit 15 % inside the band. */
  seriesScale(s: SeriesConfig, lane: LaneLayout): LinearScale {
    const tracks = this.digitalTracks(lane.laneId);
    const i = tracks.indexOf(s);
    if (i < 0) return this.yScale(this.axisIdOf(s), lane);
    const r = this.trackRect(lane, i, tracks.length);
    const pad = r.h * this.config.style.digitalTrackPadding;
    return new LinearScale([0, 1], [r.y + r.h - pad, r.y + pad]);
  }
  /** Unit shown after legend values: the axis unit, else the channel's. */
  unitOf(s: SeriesConfig): string | undefined { return this.axis(this.axisIdOf(s)).unit ?? this.store.get(s.channelId)?.info.unit; }

  // ---- time window ---------------------------------------------------------
  /** Visible time window [t0, t1] in chart seconds: ends at `reviewEnd` while paused, else at `now`. */
  window(): { t0: number; t1: number } {
    const t1 = this.paused && this.reviewEnd !== null ? this.reviewEnd : this.now;
    return { t0: t1 - this.timeSpan, t1 };
  }
  /** Layout for a canvas size from the current lanes, axes, tracks and labels. */
  layout(width: number, height: number): TrendLayout {
    return layoutTrendChart(this.config, width, height, this.lanes().map((l) => {
      const axes = this.axesIn(l.id);
      return { laneId: l.id, weight: l.weight ?? 1, collapsed: l.collapsed === true, tracks: this.digitalTracks(l.id).length, analog: this.seriesIn(l.id).some((s) => s.kind !== "digital"), leftAxes: axes.filter((a) => (a.side ?? "left") === "left").map((a) => a.id), rightAxes: axes.filter((a) => a.side === "right").map((a) => a.id), labels: this.legendSeries(l.id).map((s) => ({ seriesId: s.id, name: this.seriesName(s), digital: s.kind === "digital" })) };
    }), this.visibleSeries().length, this.cursorA !== null && this.cursorB !== null ? this.visibleSeries().filter((s) => s.kind !== "digital").length : 0);
  }
  /** Chart time to plot x over the visible window; relative labels count back from the right edge. */
  timeScale(layout: TrendLayout): TimeScale {
    const { t0, t1 } = this.window();
    return new TimeScale([t0, t1], [layout.plot.x, layout.plot.x + layout.plot.w], this.config.timeFormat, this.config.timeFormat === "relative" ? t1 : 0);
  }
  /** Domain of an axis as computed by the last `update`; [0, 1] before the first one. */
  yDomain(axisId: string): [number, number] { return this.yDomains.get(axisId) ?? [0, 1]; }
  /** Axis value to y within the lane's analog area, maximum at the top. */
  yScale(axisId: string, lane: LaneLayout): LinearScale {
    return new LinearScale(this.yDomain(axisId), [lane.analog.y + lane.analog.h, lane.analog.y]);
  }

  // ---- data ----------------------------------------------------------------
  /** Refresh decimation for the current window/size and recompute autoscaled axes. Call once per frame. */
  update(layout: TrendLayout): void {
    const q = Math.max(1e-9, this.timeSpan / Math.max(1, layout.plot.w));
    if (q !== this.quantum) { this.quantum = q; for (const b of this.buckets.values()) b.setQuantum(q); }
    const { t0, t1 } = this.window();
    const ranges = new Map<string, [number, number]>();
    for (const s of this.visibleSeries()) {
      const b = this.bucket(s);
      if (!b) continue;
      b.update();
      if (s.kind === "digital") continue;
      const axisId = this.axisIdOf(s);
      const ax = this.axis(axisId);
      if (ax.min !== undefined && ax.max !== undefined) { ranges.set(axisId, [ax.min, ax.max]); continue; }
      let lo = ranges.get(axisId)?.[0] ?? Infinity, hi = ranges.get(axisId)?.[1] ?? -Infinity;
      for (const k of b.bucketsInRange(t0, t1)) { if (k.min < lo) lo = k.min; if (k.max > hi) hi = k.max; }
      ranges.set(axisId, [lo, hi]);
    }
    this.yDomains.clear();
    for (const [axisId, [lo, hi]] of ranges) {
      const ax = this.axis(axisId);
      if (ax.min !== undefined && ax.max !== undefined) { this.yDomains.set(axisId, [ax.min, ax.max]); continue; }
      if (!Number.isFinite(lo) || !Number.isFinite(hi)) { this.yDomains.set(axisId, [ax.min ?? 0, ax.max ?? 1]); continue; }
      const pad = hi === lo ? (Math.abs(hi) || 1) * 0.5 : (hi - lo) * 0.05;
      this.yDomains.set(axisId, [ax.min ?? lo - pad, ax.max ?? hi + pad]);
    }
  }

  private bucket(s: SeriesConfig): BucketSeries | null {
    let b = this.buckets.get(s.id);
    const ch = this.store.get(s.channelId);
    if (!ch) return null;
    if (!b || b.buffer !== ch.buffer) { b = new BucketSeries(ch.buffer, this.quantum || 1); this.buckets.set(s.id, b); }
    return b;
  }

  /** Decimated [t, v, …] for the visible window; digital series become true/false steps (high when the value is ≥ 0.5). */
  seriesPolyline(seriesId: string): Float64Array {
    const s = this.config.series.find((x) => x.id === seriesId);
    const b = s ? this.bucket(s) : null;
    if (!s || !b) return new Float64Array(0);
    const { t0, t1 } = this.window();
    const pts = b.polyline(t0 - this.quantum, t1);
    if (s.kind !== "digital" || pts.length < 4) return pts;
    const bit = (v: number): number => (v >= 0.5 ? 1 : 0);
    const out = new Float64Array(pts.length * 2 - 2);
    out[0] = pts[0]!; out[1] = bit(pts[1]!);
    let j = 2;
    for (let i = 2; i < pts.length; i += 2) { out[j++] = pts[i]!; out[j++] = bit(pts[i - 1]!); out[j++] = pts[i]!; out[j++] = bit(pts[i + 1]!); }
    return out;
  }

  /** Value of each visible series at or before `time`. */
  readout(time: number): Readout {
    const values: SeriesValue[] = [];
    for (const s of this.visibleSeries()) {
      const buf = this.store.get(s.channelId)?.buffer;
      if (!buf || buf.isEmpty) continue;
      const seq = buf.indexAfterTime(time) - 1;
      if (seq < buf.firstSeq) continue;
      values.push({ seriesId: s.id, time: buf.timeAt(seq), value: buf.valueAt(seq) });
    }
    return { time, values };
  }
  /** Readouts at both cursors and their deltas (see `CursorReadouts`). */
  cursorReadouts(): CursorReadouts {
    const a = this.cursorA === null ? null : this.readout(this.cursorA);
    const b = this.cursorB === null ? null : this.readout(this.cursorB);
    let delta: CursorReadouts["delta"] = null;
    if (a && b) delta = { dt: b.time - a.time, values: a.values.flatMap((va) => { const vb = b.values.find((v) => v.seriesId === va.seriesId); return vb ? [{ seriesId: va.seriesId, delta: vb.value - va.value }] : []; }) };
    return { a, b, delta };
  }
  /** What the legend shows: cursor A's value when set, else the latest sample. */
  legendValue(s: SeriesConfig): number | null {
    const buf = this.store.get(s.channelId)?.buffer;
    if (!buf || buf.isEmpty) return null;
    if (this.cursorA !== null) { const v = this.readout(this.cursorA).values.find((x) => x.seriesId === s.id); return v ? v.value : null; }
    return buf.valueAt(buf.headSeq - 1);
  }

  // ---- commands ------------------------------------------------------------
  /** Freezes the right edge at `now`; no-op when already paused. */
  pause(): void { if (!this.paused) { this.paused = true; this.reviewEnd = this.now; } }
  /** Review right edge, clamped to now; reaching now goes back to live. */
  setReviewEnd(t: number): void { if (t >= this.now) { this.resume(); return; } this.paused = true; this.reviewEnd = t; }
  /** Back to live: the right edge follows `now` again. */
  resume(): void { this.paused = false; this.reviewEnd = null; }
  /** Sets the visible width in seconds (at least 1 µs). */
  setTimeSpan(seconds: number): void { this.timeSpan = Math.max(1e-6, seconds); }
  /** Shift the review window; pauses a live chart. */
  scrollBy(seconds: number): void { this.pause(); this.reviewEnd = Math.min(this.now, (this.reviewEnd ?? this.now) + seconds); }
  /** Hide or show a series without removing it. */
  setSeriesVisible(seriesId: string, visible: boolean): boolean { const s = this.config.series.find((x) => x.id === seriesId); if (!s) return false; s.visible = visible; return true; }
  /** Places cursor A or B at a chart time, or clears it with null. */
  setCursor(which: "a" | "b", time: number | null): void { if (which === "a") this.cursorA = time; else this.cursorB = time; }
  /** Move a series into an existing lane. `axisId` undefined = share the lane's default axis (same scale). */
  moveSeries(seriesId: string, laneId: string, axisId?: string): boolean {
    const s = this.config.series.find((x) => x.id === seriesId);
    if (!s || !this.lanes().some((l) => l.id === laneId)) return false;
    s.laneId = laneId; s.axisId = axisId === `axis:${laneId}` ? undefined : axisId;
    this.pruneEmptyLanes();
    return true;
  }
  /** Give a series its own new lane at `index` (parallel scale, no overlap). Returns the lane id. */
  moveSeriesToNewLane(seriesId: string, index: number): string | null {
    const s = this.config.series.find((x) => x.id === seriesId);
    if (!s) return null;
    const id = this.insertLane(index);
    s.laneId = id; s.axisId = undefined;
    this.pruneEmptyLanes();
    return id;
  }
  /** Series without an explicit lane live in the first lane; before lanes move, pin them there so they do not drift. */
  private pinLanes(): void { const first = this.lanes()[0]!.id; for (const s of this.config.series) if (s.laneId === undefined) s.laneId = first; }
  private insertLane(index: number): string {
    this.pinLanes();
    const lanes = this.lanes();
    let id: string;
    do { id = `lane-${++this.laneSeq}`; } while (lanes.some((l) => l.id === id));
    lanes.splice(Math.max(0, Math.min(index, lanes.length)), 0, { id });
    return id;
  }
  /** Lanes without series vanish (the last lane always stays). */
  pruneEmptyLanes(): void {
    const lanes = this.lanes();
    for (let i = lanes.length - 1; i >= 0 && lanes.length > 1; i--) {
      if (!this.config.series.some((x) => this.laneIdOf(x) === lanes[i]!.id)) lanes.splice(i, 1);
    }
  }
  /** Series of a lane grouped by axis (first-appearance order): shared-axis series sit together in the legend and the labels. */
  legendGroups(laneId: string): { axisId: string; series: SeriesConfig[] }[] {
    const groups: { axisId: string; series: SeriesConfig[] }[] = [];
    for (const s of this.seriesIn(laneId)) {
      const axisId = this.axisIdOf(s);
      const g = groups.find((x) => x.axisId === axisId);
      if (g) g.series.push(s); else groups.push({ axisId, series: [s] });
    }
    return groups;
  }
  /** Series of a lane in legend order (axis groups flattened). */
  legendSeries(laneId: string): SeriesConfig[] { return this.legendGroups(laneId).flatMap((g) => g.series); }

  /** Legend row under a point (overlay or side legend), grouped by lane in lane order. */
  legendRowAt(layout: TrendLayout, x: number, y: number): string | null {
    const lg = layout.legend;
    if (!lg || !rectContains(lg, x, y)) return null;
    const rowH = legendRowHeight(this.config);
    let yy = lg.y + this.config.style.legendPadding;
    for (const [i, lane] of this.lanes().entries()) {
      if (i > 0) yy += 4;
      for (const s of this.legendSeries(lane.id)) { if (y >= yy && y < yy + rowH) return s.id; yy += rowH; }
    }
    return null;
  }

  // ---- hit testing ---------------------------------------------------------
  /** What is under a point: legend row, lane header part, in-plot label, axis strip zone, navigator zone, plot lane, time axis. */
  hitTest(layout: TrendLayout, x: number, y: number): HitRegion {
    const row = this.legendRowAt(layout, x, y);
    if (row) return { kind: "legendRow", seriesId: row };
    for (const lane of layout.lanes) {
      const h = lane.header;
      if (h && rectContains(h, x, y)) {
        const part = y < h.y + 12 ? "collapse" : y > h.y + h.h - 12 && h.h >= 36 ? "remove" : "grip";
        return { kind: "header", laneId: lane.laneId, part };
      }
      for (const l of lane.labels) if (rectContains(l.rect, x, y)) return { kind: "label", seriesId: l.seriesId, laneId: lane.laneId };
      for (const a of lane.axes) if (rectContains(a.rect, x, y)) {
        const f = (y - a.rect.y) / Math.max(1, a.rect.h);
        return { kind: "axis", axisId: a.axisId, laneId: lane.laneId, zone: f < 0.2 ? "top" : f > 0.8 ? "bottom" : "middle" };
      }
    }
    if (layout.navigator && rectContains(layout.navigator, x, y)) return { kind: "navigator", zone: this.navigatorZone(layout, x) };
    for (const lane of layout.lanes) if (!lane.collapsed && rectContains(lane.rect, x, y)) return lane.stack && rectContains(lane.stack, x, y) ? { kind: "stack", laneId: lane.laneId } : { kind: "plot", laneId: lane.laneId };
    if (rectContains(layout.timeAxis, x, y)) return { kind: "timeAxis" };
    return { kind: "none" };
  }

  // ---- dragging signals ----------------------------------------------------
  /**
   * What dropping at (x, y) does (ibaAnalyzer rules): onto an axis strip, a series label or a legend row → join that
   * axis; into a lane's free area → that lane with an own axis; onto the time axis → a new lane at the end; above the
   * first lane, below the last one or in a gap → a new lane there.
   */
  dropTarget(layout: TrendLayout, x: number, y: number, digital = false): DropTarget {
    const hit = this.hitTest(layout, x, y);
    // digital signals only ever land in a lane's logic stack; analog signals never join a stack (own axis instead)
    if (hit.kind === "legendRow" || hit.kind === "label") {
      const s = this.config.series.find((q) => q.id === hit.seriesId)!;
      const laneId = this.laneIdOf(s);
      if (digital) return { kind: "stack", laneId };
      return s.kind === "digital" ? { kind: "ownAxis", laneId } : { kind: "join", laneId, axisId: this.axisIdOf(s) };
    }
    if (hit.kind === "axis") return digital ? { kind: "stack", laneId: hit.laneId } : { kind: "join", laneId: hit.laneId, axisId: hit.axisId };
    if (hit.kind === "plot" || hit.kind === "stack") return digital ? { kind: "stack", laneId: hit.laneId } : { kind: "ownAxis", laneId: hit.laneId };
    if (hit.kind === "timeAxis") return { kind: "newLane", index: layout.lanes.length, afterLaneId: layout.lanes[layout.lanes.length - 1]?.laneId ?? null };
    const p = layout.plot;
    if (x < p.x || x > p.x + p.w || layout.lanes.length === 0) return { kind: "none" };
    if (y < layout.lanes[0]!.rect.y) return { kind: "newLane", index: 0, afterLaneId: null };
    if (y > layout.timeAxis.y + layout.timeAxis.h) return { kind: "none" };
    for (const [i, lane] of layout.lanes.entries()) {
      const r = lane.rect;
      if (lane.collapsed && y >= r.y && y < r.y + r.h) return { kind: "none" };
      const next = layout.lanes[i + 1];
      if (y >= r.y + r.h && (!next || y < next.rect.y)) return { kind: "newLane", index: i + 1, afterLaneId: lane.laneId };
    }
    return { kind: "none" };
  }
  /** Apply a drop for one series. */
  applyDrop(seriesId: string, target: DropTarget): boolean { return this.applyGroupDrop([seriesId], target, false); }
  /**
   * Apply a drop for several series: `join` puts all on the axis; `ownAxis` gives the group one new axis in the lane;
   * `newLane` makes one lane for the group when `group` is set, else one lane per series.
   */
  applyGroupDrop(seriesIds: string[], target: DropTarget, group: boolean): boolean {
    const series = seriesIds.map((id) => this.config.series.find((x) => x.id === id)).filter((x): x is SeriesConfig => !!x);
    if (series.length === 0) return false;
    if (target.kind === "join" || target.kind === "ownAxis" || target.kind === "stack") {
      // digital members always go to the lane's logic stack; analog members to the axis (join) or a new own axis
      const laneId = target.laneId;
      const digital = series.filter((s) => s.kind === "digital"), analog = series.filter((s) => s.kind !== "digital");
      const axisId = target.kind === "join" && !target.axisId.startsWith("stack:") ? target.axisId : analog.length > 0 ? `axis:${analog[0]!.id}` : null;
      let changed = false;
      for (const s of digital) { if (this.laneIdOf(s) === laneId) continue; changed = this.moveSeries(s.id, laneId) || changed; }
      if (axisId !== null) for (const s of analog) { if (this.laneIdOf(s) === laneId && this.axisIdOf(s) === axisId) continue; changed = this.moveSeries(s.id, laneId, axisId) || changed; }
      return changed;
    }
    if (target.kind === "newLane") {
      if (series.length === 1) {
        const s = series[0]!;
        const from = this.lanes().findIndex((l) => l.id === this.laneIdOf(s));
        const alone = this.seriesIn(this.laneIdOf(s)).length === 1;
        if (alone && (target.index === from || target.index === from + 1)) return false;
        return this.moveSeriesToNewLane(s.id, target.index) !== null;
      }
      if (group) {
        const id = this.insertLane(target.index);
        for (const s of series) { s.laneId = id; s.axisId = undefined; }
        this.pruneEmptyLanes();
        return true;
      }
      let index = target.index;
      for (const s of series) { const lanesBefore = this.lanes().length; this.moveSeriesToNewLane(s.id, index); if (this.lanes().length >= lanesBefore) index++; }
      return true;
    }
    return false;
  }
  /** Pick up one series or several (ctrl/shift group); `channelIds` adds series for channels not in the chart yet (signal tree). */
  beginDrag(seriesIds: string | string[], x: number, y: number, group = false, channelIds: number[] = []): void {
    const ids = typeof seriesIds === "string" ? [seriesIds] : [...seriesIds];
    this.drag = { seriesId: ids[0] ?? (channelIds.length ? `ch:${channelIds[0]}` : ""), seriesIds: ids, channelIds, x, y, target: { kind: "none" }, group };
  }
  /** A drag carrying only digital signals (series or tree channels) targets logic stacks. */
  private dragIsDigital(d: DragState): boolean {
    const series = d.seriesIds.map((id) => this.config.series.find((s) => s.id === id)).filter((s): s is SeriesConfig => !!s);
    const kinds = d.channelIds.map((ch) => this.store.get(ch)?.info.kind);
    return series.length + kinds.length > 0 && series.every((s) => s.kind === "digital") && kinds.every((k) => k === "digital");
  }
  /** Moves the drag to the pointer and recomputes its drop target; no-op without a drag. */
  updateDrag(layout: TrendLayout, x: number, y: number): void { if (this.drag) this.drag = { ...this.drag, x, y, target: this.dropTarget(layout, x, y, this.dragIsDigital(this.drag)) }; }
  /** Drop: applies the target (if any) and clears the drag. Returns true when the layout changed. */
  endDrag(layout: TrendLayout, x: number, y: number): boolean {
    if (!this.drag) return false;
    const d = this.drag; this.drag = null;
    const target = this.dropTarget(layout, x, y, this.dragIsDigital(d));
    if (target.kind === "none") return false;
    const ids = [...d.seriesIds];
    for (const ch of d.channelIds) { const s = this.addSeriesForChannel(ch); if (s && !ids.includes(s.id)) ids.push(s.id); }
    if (ids.length === 0) return false;
    const changed = this.applyGroupDrop(ids, target, d.group || d.channelIds.length > 1);
    return changed || d.channelIds.length > 0;
  }
  /** Abandons the drag without applying it. */
  cancelDrag(): void { this.drag = null; }
  /** A series for a channel of the store (id `ch:<channelId>`), created in the first lane when missing. */
  addSeriesForChannel(channelId: number): SeriesConfig | null {
    const existing = this.config.series.find((s) => s.channelId === channelId);
    if (existing) return existing;
    const info = this.store.get(channelId)?.info;
    if (!info) return null;
    const s: SeriesConfig = { id: `ch:${channelId}`, channelId, laneId: this.lanes()[0]!.id };
    if (info.kind === "digital") s.kind = "digital";
    this.config.series.push(s);
    return s;
  }

  // ---- lanes ---------------------------------------------------------------
  /** Move a lane to position `index` (0 = top). */
  moveLane(laneId: string, index: number): boolean {
    this.pinLanes();
    const lanes = this.lanes();
    const i = lanes.findIndex((l) => l.id === laneId);
    if (i < 0) return false;
    const [lane] = lanes.splice(i, 1);
    const at = Math.max(0, Math.min(index > i ? index - 1 : index, lanes.length));
    lanes.splice(at, 0, lane!);
    return at !== i;
  }
  /** Folds or unfolds a lane; returns false when the lane is unknown or already in that state. */
  setLaneCollapsed(laneId: string, collapsed: boolean): boolean {
    const lane = this.lanes().find((l) => l.id === laneId);
    if (!lane || (lane.collapsed === true) === collapsed) return false;
    if (collapsed) lane.collapsed = true; else delete lane.collapsed;
    return true;
  }
  /** Remove a lane and the series in it (the last lane stays, emptied). */
  removeLane(laneId: string): boolean {
    this.pinLanes();
    const lanes = this.lanes();
    const i = lanes.findIndex((l) => l.id === laneId);
    if (i < 0) return false;
    this.config.series = this.config.series.filter((s) => this.laneIdOf(s) !== laneId);
    if (lanes.length > 1) lanes.splice(i, 1);
    return true;
  }
  /** Insertion slot for a lane dragged to `y`: above the lane whose middle is below the pointer. */
  laneInsertIndexAt(layout: TrendLayout, y: number): number {
    for (const [i, lane] of layout.lanes.entries()) if (y < lane.rect.y + lane.rect.h / 2) return i;
    return layout.lanes.length;
  }
  /** Picks up a lane header at the pointer; ignored for unknown lanes. */
  beginLaneDrag(laneId: string, x: number, y: number): void { const index = this.lanes().findIndex((l) => l.id === laneId); if (index >= 0) this.laneDrag = { laneId, x, y, index }; }
  /** Moves the lane drag to the pointer and recomputes its insertion slot. */
  updateLaneDrag(layout: TrendLayout, x: number, y: number): void { if (this.laneDrag) this.laneDrag = { ...this.laneDrag, x, y, index: this.laneInsertIndexAt(layout, y) }; }
  /** Drops the lane at the slot under `y` and clears the drag; returns true when the order changed. */
  endLaneDrag(layout: TrendLayout, x: number, y: number): boolean {
    if (!this.laneDrag) return false;
    const d = this.laneDrag; this.laneDrag = null;
    return this.moveLane(d.laneId, this.laneInsertIndexAt(layout, y));
  }
  /** Abandons the lane drag without reordering. */
  cancelLaneDrag(): void { this.laneDrag = null; }

  // ---- Y axes ------------------------------------------------------------------
  private fixedAxis(axisId: string): AxisConfig {
    let cfg = this.config.axes.find((a) => a.id === axisId);
    if (!cfg) { cfg = { id: axisId }; this.config.axes.push(cfg); }
    if (cfg.min === undefined || cfg.max === undefined) { const [lo, hi] = this.yDomain(axisId); cfg.min = lo; cfg.max = hi; }
    return cfg;
  }
  /** Pick up an axis: the middle shifts the scale, the ends stretch it about the other end. */
  beginAxisDrag(layout: TrendLayout, axisId: string, laneId: string, zone: "top" | "middle" | "bottom", y: number): void {
    const cfg = this.fixedAxis(axisId);
    this.axisDrag = { axisId, laneId, zone, y0: y, min0: cfg.min!, max0: cfg.max! };
  }
  /** Applies the pointer y to the dragged axis range (shift or stretch); returns false without a drag or a visible lane. */
  updateAxisDrag(layout: TrendLayout, y: number): boolean {
    const d = this.axisDrag;
    if (!d) return false;
    const lane = layout.lanes.find((l) => l.laneId === d.laneId);
    if (!lane || lane.rect.h <= 0) return false;
    const cfg = this.fixedAxis(d.axisId), h = lane.rect.h, span0 = d.max0 - d.min0 || 1;
    const vpp = span0 / h;
    if (d.zone === "middle") { const dv = (y - d.y0) * vpp; cfg.min = d.min0 + dv; cfg.max = d.max0 + dv; return true; }
    const v0 = d.max0 - (d.y0 - lane.rect.y) * vpp;
    const f = Math.min(0.95, Math.max(0.05, (y - lane.rect.y) / h));
    if (d.zone === "top") { const max = (v0 - f * d.min0) / (1 - f); if (max > d.min0) { cfg.min = d.min0; cfg.max = max; } }
    else { const min = d.max0 + (v0 - d.max0) / f; if (min < d.max0) { cfg.max = d.max0; cfg.min = min; } }
    return true;
  }
  /** Ends the axis drag; the range stays fixed until `axisAutoscale` releases it. */
  endAxisDrag(): void { this.axisDrag = null; }
  /** Wheel over an axis: scale the range by 1/factor about the value under the pointer. */
  axisZoomAt(layout: TrendLayout, axisId: string, laneId: string, y: number, factor: number): boolean {
    const lane = layout.lanes.find((l) => l.laneId === laneId);
    if (!lane) return false;
    const cfg = this.fixedAxis(axisId), v = this.yScale(axisId, lane).invert(y);
    cfg.min = v + (cfg.min! - v) / factor; cfg.max = v + (cfg.max! - v) / factor;
    return true;
  }
  /** Back to autoscale. */
  axisAutoscale(axisId: string): boolean {
    const cfg = this.config.axes.find((a) => a.id === axisId);
    if (!cfg || (cfg.min === undefined && cfg.max === undefined)) return false;
    delete cfg.min; delete cfg.max;
    return true;
  }

  // ---- navigator -----------------------------------------------------------------
  /** Full range the navigator shows: an explicit range (recordings) or the retained history up to now. */
  navigatorFullRange(): { t0: number; t1: number } {
    if (this.navigatorRange) return { t0: this.navigatorRange[0], t1: this.navigatorRange[1] };
    let earliest = Infinity;
    for (const s of this.visibleSeries()) { const b = this.store.get(s.channelId)?.buffer; if (b && !b.isEmpty) earliest = Math.min(earliest, b.timeAt(b.firstSeq)); }
    const t0 = Math.max(Number.isFinite(earliest) ? earliest : this.now - this.timeSpan, this.now - this.store.retentionSeconds);
    return { t0: Math.min(t0, this.now - this.timeSpan), t1: this.now };
  }
  /** Chart time to navigator x over the full range; requires a navigator in the layout. */
  navigatorScale(layout: TrendLayout): TimeScale {
    const r = this.navigatorFullRange(), nav = layout.navigator!;
    return new TimeScale([r.t0, r.t1], [nav.x, nav.x + nav.w], this.config.timeFormat, this.config.timeFormat === "relative" ? r.t1 : 0);
  }
  /** The visible window inside the navigator. */
  navigatorFrame(layout: TrendLayout): Rect | null {
    const nav = layout.navigator;
    if (!nav) return null;
    const ns = this.navigatorScale(layout), { t0, t1 } = this.window();
    const x0 = Math.max(nav.x, ns.scale(t0)), x1 = Math.min(nav.x + nav.w, ns.scale(t1));
    return { x: x0, y: nav.y, w: Math.max(2, x1 - x0), h: nav.h };
  }
  /** Part of the navigator under a pixel x; edges are 4 px wide and disabled with `navigatorFixedRange`. */
  navigatorZone(layout: TrendLayout, x: number): "leftEdge" | "rightEdge" | "inside" | "outside" {
    const f = this.navigatorFrame(layout);
    if (!f) return "outside";
    const tol = 4;
    if (!this.config.navigatorFixedRange) { if (Math.abs(x - f.x) <= tol) return "leftEdge"; if (Math.abs(x - (f.x + f.w)) <= tol) return "rightEdge"; }
    return x >= f.x && x <= f.x + f.w ? "inside" : "outside";
  }
  /** Decimated [x, yMin, yMax, …] per series of the topmost open lane, normalised into the navigator rect. */
  navigatorSilhouettes(layout: TrendLayout): { seriesId: string; color: string; points: Float64Array }[] {
    const nav = layout.navigator;
    if (!nav) return [];
    const lane = layout.lanes.find((l) => !l.collapsed);
    if (!lane) return [];
    const r = this.navigatorFullRange(), ns = this.navigatorScale(layout);
    const q = Math.max(1e-9, (r.t1 - r.t0) / Math.max(1, nav.w));
    if (q !== this.navQuantum) { this.navQuantum = q; for (const b of this.navBuckets.values()) b.setQuantum(q); }
    const out: { seriesId: string; color: string; points: Float64Array }[] = [];
    for (const s of this.seriesIn(lane.laneId)) {
      const ch = this.store.get(s.channelId);
      if (!ch) continue;
      let b = this.navBuckets.get(s.id);
      if (!b || b.buffer !== ch.buffer) { b = new BucketSeries(ch.buffer, q); this.navBuckets.set(s.id, b); }
      b.update();
      const buckets = b.bucketsInRange(r.t0, r.t1);
      let lo = Infinity, hi = -Infinity;
      for (const k of buckets) { if (k.min < lo) lo = k.min; if (k.max > hi) hi = k.max; }
      if (!Number.isFinite(lo) || !Number.isFinite(hi)) continue;
      const span = hi - lo || 1, pad = 3;
      const pts = new Float64Array(buckets.length * 3);
      let i = 0;
      for (const k of buckets) { pts[i++] = ns.scale(k.t); pts[i++] = nav.y + nav.h - pad - (k.min - lo) / span * (nav.h - 2 * pad); pts[i++] = nav.y + nav.h - pad - (k.max - lo) / span * (nav.h - 2 * pad); }
      out.push({ seriesId: s.id, color: this.seriesColor(s), points: pts });
    }
    return out;
  }
  /** Starts moving (inside) or resizing (edge) the frame; a pick-up outside first centres the frame under the pointer. */
  beginNavigatorDrag(layout: TrendLayout, x: number): void {
    const zone = this.navigatorZone(layout, x);
    const { t0, t1 } = this.window();
    if (zone === "outside") { this.navigatorCenterAt(layout, x); this.navDrag = { zone: "inside", x0: x, ...this.window() }; return; }
    this.navDrag = { zone, x0: x, t0, t1 };
  }
  /** Applies the pointer x: moves the window, or stretches it from one edge (changing `timeSpan`); returns false without a drag. */
  updateNavigatorDrag(layout: TrendLayout, x: number): boolean {
    const d = this.navDrag;
    if (!d || !layout.navigator) return false;
    const ns = this.navigatorScale(layout), full = this.navigatorFullRange();
    const dt = ns.invert(x) - ns.invert(d.x0);
    if (d.zone === "inside") { const span = d.t1 - d.t0; const t1 = Math.max(full.t0 + span, d.t1 + dt); this.setReviewEnd(t1); return true; }
    if (d.zone === "leftEdge") { const t0 = Math.min(d.t1 - 1e-6, Math.max(full.t0, d.t0 + dt)); this.timeSpan = d.t1 - t0; if (this.paused) this.reviewEnd = d.t1; return true; }
    const t1 = Math.max(d.t0 + 1e-6, Math.min(this.now, d.t1 + dt)); this.timeSpan = t1 - d.t0; this.setReviewEnd(t1); return true;
  }
  /** Ends the navigator drag; the window stays where it was left. */
  endNavigatorDrag(): void { this.navDrag = null; }
  /** Click in the navigator: the frame jumps so its centre sits under the pointer. */
  navigatorCenterAt(layout: TrendLayout, x: number): void {
    if (!layout.navigator) return;
    const t = this.navigatorScale(layout).invert(x), full = this.navigatorFullRange();
    const t1 = Math.min(this.now, Math.max(full.t0 + this.timeSpan, t + this.timeSpan / 2));
    this.setReviewEnd(t1);
  }
  /** Arrow keys step the frame by a tenth of its width. */
  navigatorKey(key: string): boolean {
    if (key === "ArrowLeft") { this.scrollBy(-this.timeSpan / 10); return true; }
    if (key === "ArrowRight") { this.setReviewEnd((this.reviewEnd ?? this.now) + this.timeSpan / 10); return true; }
    return false;
  }
  /** Back to live, the configured time span, no cursors and autoscaled axes. */
  reset(): void {
    this.resume(); this.timeSpan = this.config.timeSpan; this.cursorA = this.cursorB = null;
    for (const a of this.config.axes) { delete a.min; delete a.max; }
  }
  /** Lane whose rect contains the pixel, or null. */
  laneAt(layout: TrendLayout, x: number, y: number): LaneLayout | null { return layout.lanes.find((l) => rectContains(l.rect, x, y)) ?? null; }

  /** Interpret a gesture effect; returns true when it changed the chart. */
  applyEffect(e: Effect, layout: TrendLayout): boolean {
    const ts = this.timeScale(layout);
    const secPerPx = this.timeSpan / Math.max(1, layout.plot.w);
    switch (e.type) {
      case "pan": this.scrollBy(-e.dx * secPerPx); return true;
      case "zoom": {
        const { t1 } = this.window();
        const anchor = this.paused ? ts.invert(e.x) : t1;
        this.timeSpan = Math.max(1e-6, this.timeSpan / e.factor);
        if (this.paused) this.reviewEnd = Math.min(this.now, anchor + (t1 - anchor) / e.factor);
        return true;
      }
      case "boxZoom": {
        const lane = this.laneAt(layout, e.rect.x + e.rect.w / 2, e.rect.y + e.rect.h / 2);
        const tA = ts.invert(e.rect.x), tB = ts.invert(e.rect.x + e.rect.w);
        this.pause(); this.timeSpan = Math.max(1e-6, tB - tA); this.reviewEnd = tB;
        if (lane) for (const ax of this.axesIn(lane.laneId)) {
          const ys = this.yScale(ax.id, lane);
          let cfg = this.config.axes.find((a) => a.id === ax.id);
          if (!cfg) { cfg = { id: ax.id }; this.config.axes.push(cfg); }
          cfg.min = ys.invert(e.rect.y + e.rect.h); cfg.max = ys.invert(e.rect.y);
        }
        return true;
      }
      case "hover": this.hoverTime = rectContains(layout.plot, e.x, e.y) ? ts.invert(e.x) : null; return true;
      case "click":
        if (!rectContains(layout.plot, e.x, e.y)) return false;
        this.setCursor(e.modifiers.shift ? "b" : "a", ts.invert(e.x)); return true;
      case "reset": this.reset(); return true;
      default: return false;
    }
  }
}
