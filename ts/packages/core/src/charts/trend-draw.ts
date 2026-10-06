// Mori.SkyScope — Trend chart drawing in three passes (background, series, foreground): lanes, axes, logic tracks, labels, legend, navigator, drag cues.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, Stroke, TextStyle } from "../paint/painter.js";
import { formatNumber } from "../scales/ticks.js";
import { clipTransform, type ClipTransform } from "../scales/clip.js";
import type { Rect } from "../scene/geometry.js";
import { legendRowHeight, type LaneLayout, type TrendLayout } from "./trend-config.js";
import type { TrendChartModel } from "./trend-model.js";

/** Legend/readout number formatting shared with C#: fewer decimals for bigger magnitudes. */
export function formatValue(v: number): string {
  const a = Math.abs(v);
  return formatNumber(v, a >= 1000 ? 0 : a >= 10 ? 1 : a >= 1 ? 2 : 3);
}

/** Switches for `drawTrendChart`. */
export interface TrendDrawOptions { /** Draw series polylines with the painter (false when a GPU pass draws them). */ series?: boolean }

interface Ctx { th: import("./trend-config.js").ChartTheme; text: TextStyle; muted: TextStyle; grid: Stroke; axis: Stroke; ts: import("../scales/scale.js").TimeScale; timeTicks: number[]; timeTickCount: number; t0: number; t1: number }

function ctx(m: TrendChartModel, layout: TrendLayout): Ctx {
  const th = m.config.theme;
  const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
  const ts = m.timeScale(layout);
  const timeTickCount = Math.max(2, Math.round(layout.plot.w / m.config.tickSpacing));
  const { t0, t1 } = m.window();
  const st = m.config.style;
  return { th, text, muted: { ...text, color: th.mutedText }, grid: { color: th.grid, width: st.gridWidth, dash: st.gridDash ?? undefined }, axis: { color: th.axis, width: 1 }, ts, timeTicks: ts.ticks(timeTickCount), timeTickCount, t0, t1 };
}

/**
 * Paints a TrendChart with any Painter in three passes — background (lane fills, thresholds, grid),
 * series, foreground (markers, cursors, axes, legend) — so a GPU pass can replace the middle one.
 * Every number goes through the same code in C#; RecordingPainter output is byte-comparable
 * across cores (`spec/fixtures/trend-chart.json`).
 */
export function drawTrendChart(m: TrendChartModel, p: Painter, layout: TrendLayout, options: TrendDrawOptions = {}): void {
  drawTrendChartBackground(m, p, layout);
  if (options.series !== false) drawTrendChartSeries(m, p, layout);
  drawTrendChartForeground(m, p, layout);
}

/** First pass: canvas clear, lane fills, threshold bands and lines, value and time grid, track separators, lane borders and the navigator background. */
export function drawTrendChartBackground(m: TrendChartModel, p: Painter, layout: TrendLayout): void {
  const c = ctx(m, layout), st = m.config.style;
  p.clear(c.th.background);
  for (const lane of layout.lanes) {
    const r = lane.rect;
    p.rect(r.x, r.y, r.w, r.h, { color: c.th.plotBackground });
    if (lane.collapsed) continue;
    // analog content (thresholds, value grid, series) is confined to the analog area above the logic stack
    const a = lane.analog;
    p.save();
    p.clipRect(a.x, a.y, a.w, a.h);
    const axes = m.axesIn(lane.laneId);
    for (const t of m.config.thresholds) {
      if (!axes.some((a) => a.id === t.axisId)) continue;
      const ys = m.yScale(t.axisId, lane);
      if (t.to !== undefined) { const y1 = ys.scale(t.to), y0 = ys.scale(t.from); p.rect(r.x, Math.min(y0, y1), r.w, Math.abs(y1 - y0), { color: t.color, opacity: st.thresholdBandOpacity }); }
      else { const y = ys.scale(t.from); p.line(r.x, y, r.x + r.w, y, { color: t.color, width: 1, dash: st.thresholdLineDash }); }
    }
    if (m.config.showGrid) {
      const first = st.showValueGrid ? axes[0] : undefined;
      if (first) { const ys = m.yScale(first.id, lane); for (const v of ys.ticks(yTickCount(a.h, m.config.tickSpacing))) { const y = ys.scale(v); p.line(r.x, y, r.x + r.w, y, c.grid); } }
    }
    p.restore();
    p.save();
    p.clipRect(r.x, r.y, r.w, r.h);
    if (m.config.showGrid) {
      const tracks = st.trackSeparators ? m.digitalTracks(lane.laneId) : [];
      for (let i = 1; i < tracks.length; i++) { const t = m.trackRect(lane, i, tracks.length); p.line(r.x, t.y, r.x + r.w, t.y, c.grid); }
      if (lane.stack && lane.analog.h > 0) p.line(r.x, lane.stack.y, r.x + r.w, lane.stack.y, c.axis);
      if (st.showTimeGrid) for (const v of c.timeTicks) { const x = c.ts.scale(v); p.line(x, r.y, x, r.y + r.h, c.grid); }
    }
    p.restore();
    if (st.laneBorder) p.rect(r.x, r.y, r.w, r.h, undefined, { color: c.th.laneBorder, width: st.laneBorderWidth });
  }
  if (layout.navigator) { const n = layout.navigator; p.rect(n.x, n.y, n.w, n.h, { color: c.th.navigatorBackground }, { color: c.th.laneBorder, width: 1 }); }
}

/** Second pass: analog series polylines, each clipped to its lane's analog area (digital tracks belong to the foreground pass). */
export function drawTrendChartSeries(m: TrendChartModel, p: Painter, layout: TrendLayout): void {
  const c = ctx(m, layout);
  const scratch: number[] = [];
  for (const lane of layout.lanes) {
    if (lane.collapsed || lane.analog.h <= 0) continue;
    const r = lane.analog;
    p.save();
    p.clipRect(r.x, r.y, r.w, r.h);
    for (const s of m.seriesIn(lane.laneId)) {
      if (s.kind === "digital") continue;   // logic tracks are drawn in the foreground pass (filled, 2D)
      const pts = m.seriesPolyline(s.id);
      if (pts.length < 4) continue;
      const ys = m.seriesScale(s, lane);
      scratch.length = 0;
      for (let i = 0; i < pts.length; i += 2) scratch.push(c.ts.scale(pts[i]!), ys.scale(pts[i + 1]!));
      p.polyline(scratch, { color: m.seriesColor(s), width: s.width ?? m.config.style.seriesWidth, join: "round" });
    }
    p.restore();
  }
}

/**
 * Logic-analyzer tracks: each digital series fills its band solid while high, with a stepped outline; its label (the drag
 * handle) sits at the left of the band. No Y axis: the stack is the lane's bottom part (the whole lane when it has no analog series).
 */
function drawDigitalTracks(m: TrendChartModel, p: Painter, lane: LaneLayout, c: Ctx): void {
  const tracks = m.digitalTracks(lane.laneId);
  if (tracks.length === 0 || !lane.stack) return;
  const st = m.config.style;
  tracks.forEach((s, i) => {
    const ys = m.seriesScale(s, lane), color = m.seriesColor(s);
    const pts = m.seriesPolyline(s.id);
    if (pts.length >= 4) {
      const base = ys.scale(0);
      const poly: number[] = [c.ts.scale(pts[0]!), base];
      for (let k = 0; k < pts.length; k += 2) poly.push(c.ts.scale(pts[k]!), ys.scale(pts[k + 1]!));
      poly.push(c.ts.scale(pts[pts.length - 2]!), base);
      p.polygon(poly, { color, opacity: st.digitalFillOpacity });
      p.polyline(poly.slice(2, -2), { color, width: s.width ?? st.seriesWidth, join: "round" });
    }
  });
}

/** Third pass: lane headers, logic tracks, markers, hover and cursor lines, plot labels, Y and time axes, navigator, legend and drag cues. */
export function drawTrendChartForeground(m: TrendChartModel, p: Painter, layout: TrendLayout): void {
  const c = ctx(m, layout), st = m.config.style;
  for (const lane of layout.lanes) {
    const r = lane.rect;
    if (lane.header) drawLaneHeader(m, p, lane, c);
    if (lane.collapsed) {
      const names = m.seriesIn(lane.laneId).map((s) => m.seriesName(s)).join(", ");
      p.text(names, r.x + 6, r.y + r.h / 2, { ...c.muted, baseline: "middle" });
      continue;
    }
    p.save();
    p.clipRect(r.x, r.y, r.w, r.h);
    drawDigitalTracks(m, p, lane, c);
    for (const mk of m.config.markers) {
      if (mk.time < c.t0 || mk.time > c.t1) continue;
      const x = c.ts.scale(mk.time);
      p.line(x, r.y, x, r.y + r.h, { color: mk.color ?? c.th.marker, width: st.markerWidth, dash: st.markerDash });
      if (mk.label) p.text(mk.label, x + 3, r.y + 3, { ...c.text, color: mk.color ?? c.th.marker, baseline: "top" });
    }
    if (m.hoverTime !== null) { const x = c.ts.scale(m.hoverTime); p.line(x, r.y, x, r.y + r.h, { color: c.th.hover, width: st.cursorWidth, dash: st.hoverDash }); }
    if (m.cursorA !== null) { const x = c.ts.scale(m.cursorA); p.line(x, r.y, x, r.y + r.h, { color: c.th.cursorA, width: st.cursorWidth }); }
    if (m.cursorB !== null) { const x = c.ts.scale(m.cursorB); p.line(x, r.y, x, r.y + r.h, { color: c.th.cursorB, width: st.cursorWidth }); }
    drawPlotLabels(m, p, lane, c.text, c.muted);
    p.restore();

    for (const al of lane.axes) {
      const ax = m.axis(al.axisId), ys = m.yScale(al.axisId, lane), ar = al.rect;
      const left = al.side === "left";
      const lineX = left ? ar.x + ar.w : ar.x;
      p.line(lineX, ar.y, lineX, ar.y + ar.h, c.axis);
      const count = yTickCount(ar.h, m.config.tickSpacing);
      for (const v of ys.ticks(count)) {
        const y = ys.scale(v);
        p.line(left ? lineX - st.axisTickLength : lineX, y, left ? lineX : lineX + st.axisTickLength, y, c.axis);
        p.text(ys.format(v, count), left ? lineX - st.axisLabelGap : lineX + st.axisLabelGap, y, { ...c.text, align: left ? "right" : "left", baseline: "middle" });
      }
      const title = ax.label ?? ax.unit;
      if (title) p.text(title, left ? ar.x + 2 : ar.x + ar.w - 2, ar.y + 2, { ...c.muted, align: left ? "left" : "right", baseline: "top" });
    }
  }

  const ta = layout.timeAxis;
  p.line(ta.x, ta.y, ta.x + ta.w, ta.y, c.axis);
  for (const v of c.timeTicks) {
    const x = c.ts.scale(v);
    p.line(x, ta.y, x, ta.y + st.axisTickLength, c.axis);
    p.text(c.ts.format(v, c.timeTickCount), x, ta.y + st.axisTickLength + 2, { ...c.text, align: "center", baseline: "top" });
  }

  if (layout.navigator) drawNavigator(m, p, layout, c);
  if (layout.legend) drawLegend(m, p, layout.legend, c.text, c.muted);
  if (m.laneDrag) drawLaneDragIndicator(m, p, layout);
  if (m.drag) drawDragIndicator(m, p, layout, c.text);
}

/** Header bar: fold arrow at the top, grip in the middle, remove cross at the bottom. */
function drawLaneHeader(m: TrendChartModel, p: Painter, lane: TrendLayout["lanes"][number], c: Ctx): void {
  const h = lane.header!, th = m.config.theme, color = th.laneHeaderText;
  p.rect(h.x, h.y, h.w, h.h, { color: th.laneHeader }, undefined, 2);
  const cx = h.x + h.w / 2;
  // fold arrow: down when open, right when collapsed
  const ay = h.y + 6;
  if (lane.collapsed) p.polygon([cx - 3, ay - 3, cx + 3, ay, cx - 3, ay + 3], { color });
  else p.polygon([cx - 3, ay - 2, cx + 3, ay - 2, cx, ay + 3], { color });
  if (h.h >= 36) {
    // grip dots
    const gy = h.y + h.h / 2;
    for (const dy of [-6, 0, 6]) p.circle(cx, gy + dy, 1.2, { color });
    const ry = h.y + h.h - 6;
    p.line(cx - 3, ry - 3, cx + 3, ry + 3, { color, width: 1.2 });
    p.line(cx - 3, ry + 3, cx + 3, ry - 3, { color, width: 1.2 });
  }
  if (m.laneDrag?.laneId === lane.laneId) p.rect(h.x, h.y, h.w, h.h, { color: th.dropIndicator, opacity: 0.25 });
}

/** In-plot series names (the drag handles); the dragged one dims and gets a wavy underline. */
function drawPlotLabels(m: TrendChartModel, p: Painter, lane: TrendLayout["lanes"][number], text: TextStyle, muted: TextStyle): void {
  const st = m.config.style, th = m.config.theme, sw = st.legendSwatchLength;
  for (const l of lane.labels) {
    const s = m.config.series.find((x) => x.id === l.seriesId);
    if (!s) continue;
    const r = l.rect, dragging = m.drag?.seriesIds.includes(s.id) ?? false, color = m.seriesColor(s);
    p.rect(r.x, r.y, r.w, r.h, { color: th.legendBackground, opacity: st.legendOpacity * 0.8 }, undefined, 3);
    p.line(r.x + 4, r.y + r.h / 2, r.x + 4 + sw, r.y + r.h / 2, { color, width: 2, opacity: dragging ? 0.35 : 1 });
    p.text(m.seriesName(s), r.x + 4 + sw + 4, r.y + r.h / 2, { ...(dragging ? muted : text), color: dragging ? th.mutedText : color, baseline: "middle" });
    if (dragging) {
      const pts: number[] = [];
      for (let x = r.x + 4; x <= r.x + r.w - 4; x += 3) pts.push(x, r.y + r.h - 2 + (((x - r.x) / 3) % 2 === 0 ? -1.5 : 1.5));
      p.polyline(pts, { color: th.dropIndicator, width: 1 });
    }
  }
}

function drawLaneDragIndicator(m: TrendChartModel, p: Painter, layout: TrendLayout): void {
  const d = m.laneDrag!, color = m.config.theme.dropIndicator;
  const at = layout.lanes[d.index];
  const x0 = layout.lanes[0]?.header?.x ?? layout.plot.x, x1 = layout.plot.x + layout.plot.w;
  if (at) p.rect(x0, at.rect.y, x1 - x0, at.rect.h, undefined, { color, width: 3, opacity: 0.9 });
  else { const last = layout.lanes[layout.lanes.length - 1]; const y = last ? last.rect.y + last.rect.h : layout.plot.y; p.line(x0, y, x1, y, { color, width: 3, opacity: 0.9 }); }
}

/** Overview strip: silhouettes of the top lane's series, the frame of the visible window, dimmed outside. */
function drawNavigator(m: TrendChartModel, p: Painter, layout: TrendLayout, c: Ctx): void {
  const nav = layout.navigator!, th = m.config.theme;
  p.save();
  p.clipRect(nav.x, nav.y, nav.w, nav.h);
  for (const sil of m.navigatorSilhouettes(layout)) {
    const n = sil.points.length / 3, pts: number[] = [];
    for (let i = 0; i < n; i++) pts.push(sil.points[3 * i]!, sil.points[3 * i + 2]!);
    for (let i = n - 1; i >= 0; i--) pts.push(sil.points[3 * i]!, sil.points[3 * i + 1]!);
    if (pts.length >= 6) p.polygon(pts, { color: sil.color, opacity: 0.35 }, { color: sil.color, width: 1, opacity: 0.8 });
  }
  const f = m.navigatorFrame(layout);
  if (f) {
    p.rect(nav.x, nav.y, Math.max(0, f.x - nav.x), nav.h, { color: th.background, opacity: 0.45 });
    p.rect(f.x + f.w, nav.y, Math.max(0, nav.x + nav.w - f.x - f.w), nav.h, { color: th.background, opacity: 0.45 });
    p.rect(f.x, f.y + 1, f.w, f.h - 2, undefined, { color: th.navigatorFrame, width: 2 });
    if (!m.config.navigatorFixedRange) { p.rect(f.x - 2, f.y + f.h / 2 - 6, 4, 12, { color: th.navigatorFrame }); p.rect(f.x + f.w - 2, f.y + f.h / 2 - 6, 4, 12, { color: th.navigatorFrame }); }
  }
  p.restore();
  const full = m.navigatorFullRange(), ns = m.navigatorScale(layout), count = Math.max(2, Math.round(nav.w / m.config.tickSpacing));
  for (const v of ns.ticks(count)) { const x = ns.scale(v); p.line(x, nav.y + nav.h - 4, x, nav.y + nav.h, c.axis); }
  p.text(ns.format(full.t0, count), nav.x + 3, nav.y + 2, { ...c.muted, baseline: "top" });
  p.text(ns.format(full.t1, count), nav.x + nav.w - 3, nav.y + 2, { ...c.muted, align: "right", baseline: "top" });
}

function drawDragIndicator(m: TrendChartModel, p: Painter, layout: TrendLayout, text: TextStyle): void {
  const d = m.drag!, th = m.config.theme, color = th.dropIndicator;
  if (d.target.kind === "join") {
    // common axis: highlight the axis strip and point a small grey arrow at it (ibaAnalyzer's cue)
    const t = d.target;
    const lane = layout.lanes.find((l) => l.laneId === t.laneId);
    const axis = lane?.axes.find((a) => a.axisId === t.axisId);
    if (axis) {
      const r = axis.rect;
      p.rect(r.x, r.y, r.w, r.h, { color, opacity: 0.12 }, { color, width: 2, opacity: 0.8 });
      const ax = axis.side === "left" ? r.x + r.w - 4 : r.x + 4, ay = d.y, dir = axis.side === "left" ? -1 : 1;
      p.polygon([ax, ay, ax + dir * 8, ay - 5, ax + dir * 8, ay + 5], { color: th.mutedText });
    } else if (lane) p.rect(lane.rect.x, lane.rect.y, lane.rect.w, lane.rect.h, undefined, { color, width: 2, opacity: 0.8 });
  } else if (d.target.kind === "ownAxis") {
    // own axis in this lane: dashed highlight of the free area
    const t = d.target;
    const lane = layout.lanes.find((l) => l.laneId === t.laneId);
    if (lane) p.rect(lane.rect.x, lane.rect.y, lane.rect.w, lane.rect.h, { color, opacity: 0.06 }, { color, width: 2, dash: [6, 4], opacity: 0.8 });
  } else if (d.target.kind === "stack") {
    // logic stack of this lane: solid highlight of the stack band (or the whole lane when it has none yet)
    const t = d.target;
    const lane = layout.lanes.find((l) => l.laneId === t.laneId);
    const s = lane?.stack ?? lane?.rect;
    if (s) p.rect(s.x, s.y, s.w, s.h, { color, opacity: 0.12 }, { color, width: 2, opacity: 0.8 });
  } else if (d.target.kind === "newLane") {
    const t = d.target;
    const after = t.afterLaneId === null ? null : layout.lanes.find((l) => l.laneId === t.afterLaneId);
    const y = after ? after.rect.y + after.rect.h + m.config.laneGap / 2 : layout.plot.y;
    p.line(layout.plot.x, y, layout.plot.x + layout.plot.w, y, { color, width: 3, opacity: 0.9 });
  }
  const names = d.seriesIds.map((id) => m.config.series.find((x) => x.id === id)).filter((x) => !!x).map((x) => m.seriesName(x!));
  for (const ch of d.channelIds) names.push(m.store.get(ch)?.info.name ?? `#${ch}`);
  if (names.length > 0) {
    const label = names.length > 3 ? `${names.slice(0, 3).join(", ")} +${names.length - 3}` : names.join(", ");
    const w = p.measureText(label, text).width + 16;
    p.rect(d.x + 12, d.y - 9, w, 18, { color: th.legendBackground, opacity: 0.95 }, { color, width: 1 }, 3);
    p.text(label, d.x + 20, d.y, { ...text, baseline: "middle" });
  }
}

function yTickCount(height: number, spacing: number): number { return Math.max(2, Math.round(height / (spacing * 0.6))); }

function drawLegend(m: TrendChartModel, p: Painter, r: Rect, text: TextStyle, muted: TextStyle): void {
  const th = m.config.theme, st = m.config.style, pad = st.legendPadding, sw = st.legendSwatchLength;
  p.rect(r.x, r.y, r.w, r.h, { color: th.legendBackground, opacity: st.legendOpacity }, { color: th.legendBorder, width: 1 }, st.legendRadius);
  const cr = m.cursorReadouts();
  let y = r.y + pad;
  const rowH = legendRowHeight(m.config);
  const lanes = m.lanes();
  for (const [i, lane] of lanes.entries()) {
    if (i > 0) { p.line(r.x + pad, y + 2, r.x + r.w - pad, y + 2, { color: th.legendBorder, width: 1 }); y += 4; }
    for (const g of m.legendGroups(lane.id)) {
      let first = -1, last = -1;
      for (const s of g.series) {
        if (y + rowH > r.y + r.h + 1) return;
        const dragging = m.drag?.seriesIds.includes(s.id) ?? false;
        const color = m.seriesColor(s);
        if (first < 0) first = y + rowH / 2;
        last = y + rowH / 2;
        p.line(r.x + pad, y + rowH / 2, r.x + pad + sw, y + rowH / 2, { color, width: 2, opacity: dragging ? 0.35 : 1 });
        p.text(m.seriesName(s), r.x + pad + sw + 6, y + rowH / 2, { ...(dragging ? muted : text), baseline: "middle" });
        const v = m.legendValue(s);
        const unit = m.unitOf(s);
        const shown = v === null ? "—" : s.kind === "digital" ? (v >= 0.5 ? "true" : "false") : formatValue(v) + (unit ? ` ${unit}` : "");
      p.text(shown, r.x + r.w - pad, y + rowH / 2, { ...text, align: "right", baseline: "middle" });
        y += rowH;
        if (cr.delta && s.kind !== "digital") {   // a difference of two logic levels means nothing
          const d = cr.delta.values.find((x) => x.seriesId === s.id);
          if (d) { p.text(`Δ ${formatValue(d.delta)}`, r.x + r.w - pad, y + rowH / 2 - 2, { ...muted, align: "right", baseline: "middle" }); y += rowH - 4; }
        }
      }
      // shared axis: a bracket on the left joins its rows
      if (g.series.length > 1 && first >= 0) {
        const bx = r.x + 3;
        p.line(bx, first, bx, last, { color: th.mutedText, width: 1 });
        p.line(bx, first, bx + 3, first, { color: th.mutedText, width: 1 });
        p.line(bx, last, bx + 3, last, { color: th.mutedText, width: 1 });
      }
    }
  }
  if (cr.delta) p.text(`Δt ${formatValue(cr.delta.dt)} s`, r.x + pad, r.y + r.h - pad + 2, { ...muted, baseline: "bottom" });
}

/** One analog series prepared for a GPU line pass. */
export interface SeriesGeometry { /** Series id. */ seriesId: string; /** Interleaved [t, v, …] in data space, absolute chart time. */ points: Float64Array; /** Time (the window start) subtracted from t before the clip transform, to keep float precision. */ xOrigin: number; /** Maps (t − xOrigin, v) to clip space. */ clip: ClipTransform; /** Stroke colour. */ color: string; /** Stroke width in pixels. */ width: number; /** Pixel rect to clip to: the lane's analog area. */ scissor: Rect }

/** Per-series data-space polylines plus clip transforms for a GPU line pass (scissor = the lane's analog area). */
export function seriesGeometry(m: TrendChartModel, layout: TrendLayout): SeriesGeometry[] {
  const ts = m.timeScale(layout);
  const { t0 } = m.window();
  const out: SeriesGeometry[] = [];
  for (const lane of layout.lanes) {
    if (lane.collapsed) continue;
    for (const s of m.seriesIn(lane.laneId)) {
      if (s.kind === "digital") continue;
      const points = m.seriesPolyline(s.id);
      if (points.length < 4) continue;
      const ys = m.seriesScale(s, lane);
      out.push({ seriesId: s.id, points, xOrigin: t0, clip: clipTransform(ts, ys, layout.width, layout.height, t0), color: m.seriesColor(s), width: s.width ?? m.config.style.seriesWidth, scissor: lane.analog });
    }
  }
  return out;
}
