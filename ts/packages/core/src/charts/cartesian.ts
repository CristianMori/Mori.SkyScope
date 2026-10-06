// Mori.SkyScope — CartesianChart: static/analytic XY charts — line, step, scatter, area, bar (grouped by default, stacked via stack groups) — sharing axes,…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import type { Rect } from "../scene/geometry.js";
import { LinearScale, LogScale, type Domain, type Scale } from "../scales/scale.js";
import { niceLinearDomain } from "../scales/ticks.js";
import { DEFAULT_STYLE, LIGHT_THEME, SERIES_PALETTE, type ChartStyle, type ChartTheme, type LegendPosition } from "./trend-config.js";
import { formatValue } from "./trend-draw.js";
import { drawSimpleLegend, legendItemAt, overlayLegendRect, reserveLegend, type LegendItem } from "./legend.js";

/**
 * CartesianChart: static/analytic XY charts — line, step, scatter, area, bar (grouped by default,
 * stacked via `stack` groups) — sharing axes, grid, legend, theme and style with the TrendChart.
 * Mirrors `Mori.SkyScope.Core.Charts.CartesianChart`; pinned by `spec/fixtures/cartesian.json`.
 */
export type CartesianSeriesKind = "line" | "step" | "scatter" | "area" | "bar";
/** Point marker drawn at each sample. */
export type MarkerShape = "none" | "circle" | "square" | "diamond";
/** Which value axis a series maps to: the left one ("y") or the right one ("y2"). */
export type YAxisId = "y" | "y2";

/** Per-axis options; everything is optional. */
export interface CartesianAxisConfig {
  /** Axis caption. */ label?: string | undefined; /** Appended to tooltip values; also the caption when there is no label. */ unit?: string | undefined;
  /** Fixed bounds; omit for autoscale (data range, niced). */
  min?: number | undefined; max?: number | undefined;
  /** Logarithmic scale (positive values only); disables nicing. */
  log?: boolean | undefined;
  /** Category axis (x only): positions are indices, labels come from here. */
  categories?: string[] | undefined;
  /** Force 0 into the autoscaled range (bars and areas always do). */
  includeZero?: boolean | undefined;
}

/** One series; `x` and `y` are parallel arrays. */
export interface CartesianSeriesConfig {
  /** Unique key. */ id: string; /** Legend text; defaults to the id. */ name?: string | undefined; /** Drawing style (default "line"). */ kind?: CartesianSeriesKind | undefined;
  /** Omit `x` for index positions (0, 1, 2…) — the natural fit for category axes. */
  x?: number[] | undefined; /** Values, one per point. */ y: number[];
  /** Colour; palette by series index when omitted. */ color?: string | undefined; /** Stroke width in pixels. */ width?: number | undefined; /** Value axis (default "y"). */ axis?: YAxisId | undefined;
  /** Series with the same stack key (same axis) stack on top of each other (bars and areas). */
  stack?: string | undefined;
  /** Point marker; scatter series default to circles. */ marker?: MarkerShape | undefined; /** Marker radius in pixels (default 4). */ markerSize?: number | undefined; /** Area and bar fill opacity. */ fillOpacity?: number | undefined;
  /** Bar width in x units; default fills `1 − barGap` of the slot between bar positions. */
  barWidth?: number | undefined;
  /** false removes the series from the chart and the legend. */
  visible?: boolean | undefined;
}

/** Full chart configuration; `defaultCartesianConfig` fills the defaults. */
export interface CartesianChartConfig {
  /** Centred above the chart; null reserves no title row. */
  title: string | null;
  /** Horizontal axis. */ xAxis: CartesianAxisConfig; /** Left value axis. */ yAxis: CartesianAxisConfig; /** Right value axis; null disables it. */ y2Axis: CartesianAxisConfig | null;
  /** Series in draw and legend order. */
  series: CartesianSeriesConfig[];
  /** Fraction of a bar slot left empty. */
  barGap: number;
  /** Legend placement. */ legend: LegendPosition; /** Master grid switch; `style.showValueGrid` / `style.showTimeGrid` refine it. */ showGrid: boolean;
  /** Colours and font. */ theme: ChartTheme; /** Widths, dashes and paddings. */ style: ChartStyle;
  /** Outer margin in pixels. */ margin: number; /** Width of each value-axis column in pixels. */ yAxisWidth: number; /** Height of the x-axis row in pixels, excluding the label. */ xAxisHeight: number; /** Title row height in pixels. */ titleHeight: number; /** Width in pixels of "right" and overlay legends. */ legendWidth: number; /** Target pixels per x tick; y ticks use 0.6 of it. */ tickSpacing: number;
  /** Pixels within which a point counts as hovered. */
  hoverRadius: number;
}
/** Partial config for construction; `theme` and `style` may themselves be partial and merge over the defaults. */
export type CartesianChartOptions = Omit<Partial<CartesianChartConfig>, "theme" | "style"> & { theme?: Partial<ChartTheme> | undefined; style?: Partial<ChartStyle> | undefined };

/** Fills in every default (no title, empty axes, legend top-right, 20 % bar gap) under the given options. */
export function defaultCartesianConfig(partial: CartesianChartOptions = {}): CartesianChartConfig {
  const { theme, style, ...rest } = partial;
  return {
    title: null, xAxis: {}, yAxis: {}, y2Axis: null, series: [], barGap: 0.2, legend: "top-right", showGrid: true,
    margin: 8, yAxisWidth: 48, xAxisHeight: 24, titleHeight: 22, legendWidth: 150, tickSpacing: 80, hoverRadius: 12,
    ...rest, theme: { ...LIGHT_THEME, ...theme }, style: { ...DEFAULT_STYLE, ...style },
  };
}

/** Pixel geometry for a canvas size. */
export interface CartesianLayout { /** Canvas width. */ width: number; /** Canvas height. */ height: number; /** Series area. */ plot: Rect; /** Left axis column. */ yAxis: Rect; /** Right axis column, null when no visible series uses y2. */ y2Axis: Rect | null; /** Axis row under the plot, label included. */ xAxis: Rect; /** Title row, null without a title. */ title: Rect | null; /** Legend rect, null when hidden or empty. */ legend: Rect | null }
/** One bar in pixels. */
export interface BarRect { /** Owning series. */ seriesId: string; /** Point index in the series. */ index: number; /** Left edge. */ x: number; /** Top edge. */ y: number; /** Width. */ w: number; /** Height. */ h: number }
/** A point identified by series and index. */
export interface ChartHit { /** Series id. */ seriesId: string; /** Point index in the series. */ index: number }
/** Tooltip content and its anchor pixel. */
export interface Tooltip { /** Anchor x; the box is placed beside it. */ x: number; /** Anchor y. */ y: number; /** First row, drawn muted. */ title: string; /** One row each: a swatch in `color`, then `name` and `value`. */ lines: { name: string; value: string; color: string }[] }

/** Cartesian chart state (config, hidden series, hover, zoom) with scales, layout, hit testing and drawing. */
export class CartesianChart {
  /** Effective configuration. */
  readonly config: CartesianChartConfig;
  /** Series ids toggled off through the legend. */
  readonly hidden = new Set<string>();
  /** Hovered point, or null. */
  hover: ChartHit | null = null;
  /** Hovered legend row id, or null. */
  legendHover: string | null = null;
  /** Rubber band while box-zooming (pixels). */
  box: { x0: number; y0: number; x1: number; y1: number } | null = null;
  /** Zoomed x domain; null = autoscale. */
  zoomX: Domain | null = null;
  /** Zoomed left-axis domain; null = autoscale. */
  zoomY: Domain | null = null;
  /** Zoomed right-axis domain; null = autoscale. */
  zoomY2: Domain | null = null;

  /** Creates a chart with the defaults merged under the options. */
  constructor(options: CartesianChartOptions = {}) { this.config = defaultCartesianConfig(options); }

  // ---- series helpers ----
  /** Series config by id. */
  series(id: string): CartesianSeriesConfig | undefined { return this.config.series.find((s) => s.id === id); }
  /** Series neither flagged invisible nor hidden through the legend, in config order. */
  visibleSeries(): CartesianSeriesConfig[] { return this.config.series.filter((s) => s.visible !== false && !this.hidden.has(s.id)); }
  /** Effective kind (default "line"). */
  kindOf(s: CartesianSeriesConfig): CartesianSeriesKind { return s.kind ?? "line"; }
  /** Display name: `name`, else the id. */
  seriesName(s: CartesianSeriesConfig): string { return s.name ?? s.id; }
  /** Colour: explicit, else from the palette by configured index. */
  seriesColor(s: CartesianSeriesConfig): string { return s.color ?? SERIES_PALETTE[this.config.series.indexOf(s) % SERIES_PALETTE.length]!; }
  /** Effective marker: explicit, else circles for scatter series and none otherwise. */
  markerOf(s: CartesianSeriesConfig): MarkerShape { return s.marker ?? (this.kindOf(s) === "scatter" ? "circle" : "none"); }
  /** X of point `i`: from `x`, or the index itself. */
  xValue(s: CartesianSeriesConfig, i: number): number { return s.x ? s.x[i]! : i; }
  /** Number of points: the shorter of `x` and `y`. */
  count(s: CartesianSeriesConfig): number { return s.x ? Math.min(s.x.length, s.y.length) : s.y.length; }
  /** Hides a visible series or shows a hidden one. */
  toggleSeries(id: string): void { if (this.hidden.has(id)) this.hidden.delete(id); else this.hidden.add(id); }
  /** One legend row per series with `visible !== false`; hidden ones are flagged. */
  legendItems(): LegendItem[] {
    return this.config.series.filter((s) => s.visible !== false).map((s) => ({ id: s.id, name: this.seriesName(s), color: this.seriesColor(s), hidden: this.hidden.has(s.id), shape: this.kindOf(s) === "bar" || this.kindOf(s) === "area" ? "box" : this.kindOf(s) === "scatter" ? "circle" : "line" }));
  }

  /** [base, top] for point `i`: stacked series sit on the same-signed sum of the visible series before them in the stack. */
  stackedValue(s: CartesianSeriesConfig, i: number): [number, number] {
    const y = s.y[i] ?? 0, kind = this.kindOf(s);
    if (!s.stack) return kind === "bar" || kind === "area" ? [0, y] : [y, y];
    let base = 0;
    for (const q of this.visibleSeries()) {
      if (q === s) break;
      if (q.stack !== s.stack || (q.axis ?? "y") !== (s.axis ?? "y")) continue;
      const v = q.y[i] ?? 0;
      if (y >= 0 ? v > 0 : v < 0) base += v;
    }
    return [base, base + y];
  }

  // ---- bars ----
  /** Visible series of kind "bar". */
  barSeries(): CartesianSeriesConfig[] { return this.visibleSeries().filter((s) => this.kindOf(s) === "bar"); }
  /** Smallest distance between distinct bar x positions (1 when there is only one). */
  barSlot(): number {
    const xs: number[] = [];
    for (const s of this.barSeries()) for (let i = 0; i < this.count(s); i++) xs.push(this.xValue(s, i));
    xs.sort((a, b) => a - b);
    let slot = Infinity;
    for (let i = 1; i < xs.length; i++) { const d = xs[i]! - xs[i - 1]!; if (d > 0 && d < slot) slot = d; }
    return Number.isFinite(slot) ? slot : 1;
  }
  /** Slot occupancy: stacked series share a column; others get their own. */
  barGroups(): string[] {
    const out: string[] = [];
    for (const s of this.barSeries()) { const key = s.stack ?? s.id; if (!out.includes(key)) out.push(key); }
    return out;
  }
  /** Total bar width per x position in x units: the largest explicit `barWidth`, else `barSlot × (1 − barGap)`; grouped series share it. */
  barWidth(): number {
    const slot = this.barSlot() * (1 - this.config.barGap);
    let w = 0;
    for (const s of this.barSeries()) w = Math.max(w, s.barWidth ?? slot);
    return w;
  }
  /** Pixel rectangle of every visible bar; grouped series split the width side by side, stacked series share a column. */
  bars(layout: CartesianLayout): BarRect[] {
    const out: BarRect[] = [];
    const bars = this.barSeries();
    if (bars.length === 0) return out;
    const groups = this.barGroups(), bw = this.barWidth(), each = bw / groups.length;
    const xs = this.xScale(layout);
    for (const s of bars) {
      const gi = groups.indexOf(s.stack ?? s.id), ys = this.yScale(s.axis ?? "y", layout);
      for (let i = 0; i < this.count(s); i++) {
        const xv = this.xValue(s, i), [b, t] = this.stackedValue(s, i);
        const x0 = xs.scale(xv - bw / 2 + gi * each), x1 = xs.scale(xv - bw / 2 + (gi + 1) * each);
        const y0 = ys.scale(b), y1 = ys.scale(t);
        out.push({ seriesId: s.id, index: i, x: Math.min(x0, x1), y: Math.min(y0, y1), w: Math.abs(x1 - x0), h: Math.abs(y1 - y0) });
      }
    }
    return out;
  }

  // ---- domains & scales ----
  private usesY2(): boolean { return this.config.y2Axis !== null && this.visibleSeries().some((s) => s.axis === "y2"); }

  /** X domain: the zoom when set; else categories padded by half a slot, or the data range (padded for bars, niced otherwise), with `min`/`max` overriding. */
  xDomain(): Domain {
    if (this.zoomX) return this.zoomX;
    const ax = this.config.xAxis;
    let lo = Infinity, hi = -Infinity;
    if (ax.categories) { lo = -0.5; hi = ax.categories.length - 0.5; }
    else {
      for (const s of this.visibleSeries()) for (let i = 0; i < this.count(s); i++) { const x = this.xValue(s, i); if (x < lo) lo = x; if (x > hi) hi = x; }
      if (!Number.isFinite(lo)) { lo = 0; hi = 1; }
      if (lo === hi) { lo -= 1; hi += 1; }
      if (this.barSeries().length > 0) { const half = this.barWidth() / 2 + this.barSlot() * this.config.barGap / 2; lo -= half; hi += half; }
      else if (ax.min === undefined && ax.max === undefined && !ax.log) [lo, hi] = niceLinearDomain(lo, hi, 10);
    }
    if (ax.min !== undefined) lo = ax.min;
    if (ax.max !== undefined) hi = ax.max;
    return [lo, hi];
  }

  /** Value domain of an axis: the zoom when set; else the stacked data range (0 included for bars/areas), niced unless log or fully fixed, with `min`/`max` overriding. */
  yDomain(axis: YAxisId): Domain {
    const zoom = axis === "y" ? this.zoomY : this.zoomY2;
    if (zoom) return zoom;
    const ax = (axis === "y2" ? this.config.y2Axis : this.config.yAxis) ?? {};
    let lo = Infinity, hi = -Infinity, zero = ax.includeZero === true;
    for (const s of this.visibleSeries()) {
      if ((s.axis ?? "y") !== axis) continue;
      const kind = this.kindOf(s);
      if (kind === "bar" || kind === "area") zero = true;
      for (let i = 0; i < this.count(s); i++) { const [b, t] = this.stackedValue(s, i); const l = Math.min(b, t), h = Math.max(b, t); if (l < lo) lo = l; if (h > hi) hi = h; }
    }
    if (!Number.isFinite(lo)) { lo = 0; hi = 1; }
    if (zero && !ax.log) { if (lo > 0) lo = 0; if (hi < 0) hi = 0; }
    if (lo === hi) { if (lo === 0) hi = 1; else { lo -= Math.abs(lo) * 0.1; hi += Math.abs(hi) * 0.1; } }
    if (!ax.log && !(ax.min !== undefined && ax.max !== undefined)) [lo, hi] = niceLinearDomain(lo, hi, 10);
    if (ax.min !== undefined) lo = ax.min;
    if (ax.max !== undefined) hi = ax.max;
    return [lo, hi];
  }

  /** Scale from the x domain to plot pixels, left to right (log when configured). */
  xScale(layout: CartesianLayout): Scale {
    const d = this.xDomain(), r: Domain = [layout.plot.x, layout.plot.x + layout.plot.w];
    return this.config.xAxis.log ? new LogScale(d, r) : new LinearScale(d, r);
  }
  /** Scale from an axis domain to plot pixels, bottom to top (log when configured). */
  yScale(axis: YAxisId, layout: CartesianLayout): Scale {
    const ax = (axis === "y2" ? this.config.y2Axis : this.config.yAxis) ?? {};
    const d = this.yDomain(axis), r: Domain = [layout.plot.y + layout.plot.h, layout.plot.y];
    return ax.log ? new LogScale(d, r) : new LinearScale(d, r);
  }
  /** Target x tick count from the plot width and `tickSpacing` (at least 2). */
  xTickCount(layout: CartesianLayout): number { return Math.max(2, Math.round(layout.plot.w / this.config.tickSpacing)); }
  /** Target y tick count from the plot height and 0.6 × `tickSpacing` (at least 2). */
  yTickCount(layout: CartesianLayout): number { return Math.max(2, Math.round(layout.plot.h / (this.config.tickSpacing * 0.6))); }
  /** Category ticks are thinned so labels keep at least `tickSpacing / 2` pixels. */
  xTicks(layout: CartesianLayout): number[] {
    const cats = this.config.xAxis.categories;
    if (!cats) return this.xScale(layout).ticks(this.xTickCount(layout));
    const [lo, hi] = this.xDomain();
    const perCat = layout.plot.w / Math.max(1e-9, hi - lo);
    const step = Math.max(1, Math.ceil(this.config.tickSpacing / 2 / Math.max(1e-9, perCat)));
    const out: number[] = [];
    for (let i = Math.max(0, Math.ceil(lo)); i < cats.length && i <= hi; i += step) out.push(i);
    return out;
  }
  /** X tick label: the category name at integer positions (empty elsewhere), else the scale's own formatting. */
  formatX(v: number, layout: CartesianLayout): string {
    const cats = this.config.xAxis.categories;
    if (cats) { const i = Math.floor(v + 0.5); return i >= 0 && i < cats.length && Math.abs(v - i) < 1e-9 ? cats[i]! : ""; }
    return this.xScale(layout).format(v, this.xTickCount(layout));
  }

  // ---- layout ----
  /** Pixel geometry for a canvas size: title, legend, plot, axis columns and the x-axis row. */
  layout(width: number, height: number): CartesianLayout {
    const c = this.config, m = c.margin;
    let outer: Rect = { x: m, y: m, w: Math.max(0, width - 2 * m), h: Math.max(0, height - 2 * m) };
    let title: Rect | null = null;
    if (c.title) { title = { x: outer.x, y: outer.y, w: outer.w, h: c.titleHeight }; outer = { x: outer.x, y: outer.y + c.titleHeight, w: outer.w, h: Math.max(0, outer.h - c.titleHeight) }; }
    const items = this.legendItems().length;
    const { inner, legend: reserved } = reserveLegend(c, outer, items);
    const xAxisH = c.xAxisHeight + (c.xAxis.label ? c.theme.fontSize + 4 : 0);
    const y2 = this.usesY2();
    const plot: Rect = { x: inner.x + c.yAxisWidth, y: inner.y, w: Math.max(0, inner.w - c.yAxisWidth - (y2 ? c.yAxisWidth : 0)), h: Math.max(0, inner.h - xAxisH) };
    const yAxis: Rect = { x: inner.x, y: plot.y, w: c.yAxisWidth, h: plot.h };
    const y2Axis: Rect | null = y2 ? { x: plot.x + plot.w, y: plot.y, w: c.yAxisWidth, h: plot.h } : null;
    const xAxis: Rect = { x: plot.x, y: plot.y + plot.h, w: plot.w, h: xAxisH };
    return { width, height, plot, yAxis, y2Axis, xAxis, title, legend: reserved ?? overlayLegendRect(c, plot, items) };
  }

  // ---- geometry ----
  /** Interleaved pixel polyline of a line/step/area/scatter series (top of the stack for stacked areas). */
  pixelPoints(s: CartesianSeriesConfig, layout: CartesianLayout): number[] {
    const xs = this.xScale(layout), ys = this.yScale(s.axis ?? "y", layout), step = this.kindOf(s) === "step";
    const out: number[] = [];
    let prevY = 0;
    for (let i = 0; i < this.count(s); i++) {
      const x = xs.scale(this.xValue(s, i)), y = ys.scale(this.stackedValue(s, i)[1]);
      if (step && i > 0) out.push(x, prevY);
      out.push(x, y);
      prevY = y;
    }
    return out;
  }
  /** Closed polygon: the top polyline followed by the base polyline reversed. */
  areaPolygon(s: CartesianSeriesConfig, layout: CartesianLayout): number[] {
    const top = this.pixelPoints(s, layout);
    const xs = this.xScale(layout), ys = this.yScale(s.axis ?? "y", layout);
    const base: number[] = [];
    for (let i = this.count(s) - 1; i >= 0; i--) base.push(xs.scale(this.xValue(s, i)), ys.scale(this.stackedValue(s, i)[0]));
    return top.concat(base);
  }

  // ---- interaction ----
  /** Nearest non-bar point within `hoverRadius`, else the bar under the pixel; null outside the plot. */
  hitTest(x: number, y: number, layout: CartesianLayout): ChartHit | null {
    const p = layout.plot;
    if (x < p.x || x > p.x + p.w || y < p.y || y > p.y + p.h) return null;
    let best: ChartHit | null = null, bestD = this.config.hoverRadius * this.config.hoverRadius;
    for (const s of this.visibleSeries()) {
      if (this.kindOf(s) === "bar") continue;
      const xs = this.xScale(layout), ys = this.yScale(s.axis ?? "y", layout);
      for (let i = 0; i < this.count(s); i++) {
        const dx = xs.scale(this.xValue(s, i)) - x, dy = ys.scale(this.stackedValue(s, i)[1]) - y, d = dx * dx + dy * dy;
        if (d < bestD) { bestD = d; best = { seriesId: s.id, index: i }; }
      }
    }
    if (best) return best;
    for (const b of this.bars(layout)) if (x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h) return { seriesId: b.seriesId, index: b.index };
    return null;
  }
  /** Tooltip for a hit: the x value (or category) as title, the series value with its unit as the line; null for an unknown series. */
  tooltip(hit: ChartHit, layout: CartesianLayout): Tooltip | null {
    const s = this.series(hit.seriesId);
    if (!s) return null;
    const xv = this.xValue(s, hit.index), cats = this.config.xAxis.categories;
    const title = cats ? cats[hit.index] ?? String(xv) : `${this.config.xAxis.label ?? "x"} ${formatValue(xv)}${this.config.xAxis.unit ? " " + this.config.xAxis.unit : ""}`;
    const unit = ((s.axis === "y2" ? this.config.y2Axis : this.config.yAxis) ?? {}).unit;
    const lines = [{ name: this.seriesName(s), value: formatValue(s.y[hit.index] ?? 0) + (unit ? ` ${unit}` : ""), color: this.seriesColor(s) }];
    const xs = this.xScale(layout), ys = this.yScale(s.axis ?? "y", layout);
    return { x: xs.scale(xv), y: ys.scale(this.stackedValue(s, hit.index)[1]), title, lines };
  }
  /** Updates `hover`, `legendHover` and the moving corner of the zoom box for a pointer position. */
  pointerMove(x: number, y: number, width: number, height: number): void {
    const l = this.layout(width, height);
    this.hover = this.hitTest(x, y, l);
    this.legendHover = legendItemAt(this.config, l.legend, this.legendItems(), x, y, (t) => t.length * this.config.theme.fontSize * 0.6);
    if (this.box) { this.box.x1 = x; this.box.y1 = y; }
  }
  /** Clears both hover states (the zoom box is kept). */
  pointerLeave(): void { this.hover = null; this.legendHover = null; }
  /** Click: toggles a legend item; returns true when it consumed the click. */
  click(x: number, y: number, width: number, height: number): boolean {
    const l = this.layout(width, height);
    const id = legendItemAt(this.config, l.legend, this.legendItems(), x, y, (t) => t.length * this.config.theme.fontSize * 0.6);
    if (!id) return false;
    this.toggleSeries(id);
    return true;
  }
  /** Starts a box zoom at the pixel; `pointerMove` drags the opposite corner. */
  beginBox(x: number, y: number): void { this.box = { x0: x, y0: y, x1: x, y1: y }; }
  /** Ends a box zoom; boxes under 6 px are treated as clicks and ignored. */
  endBox(width: number, height: number): boolean {
    const b = this.box; this.box = null;
    if (!b || Math.abs(b.x1 - b.x0) < 6 || Math.abs(b.y1 - b.y0) < 6) return false;
    this.zoomTo(b.x0, b.y0, b.x1, b.y1, this.layout(width, height));
    return true;
  }
  /** Zooms x, y and (when used) y2 to the pixel rectangle; corners may be given in any order. */
  zoomTo(x0: number, y0: number, x1: number, y1: number, layout: CartesianLayout): void {
    const xs = this.xScale(layout), ys = this.yScale("y", layout);
    this.zoomX = [xs.invert(Math.min(x0, x1)), xs.invert(Math.max(x0, x1))];
    this.zoomY = [ys.invert(Math.max(y0, y1)), ys.invert(Math.min(y0, y1))];
    if (this.usesY2()) { const y2 = this.yScale("y2", layout); this.zoomY2 = [y2.invert(Math.max(y0, y1)), y2.invert(Math.min(y0, y1))]; }
  }
  /** Wheel zoom about the pointer: factor 2^(−dy/400), matching the scene interaction. */
  wheelZoom(x: number, y: number, deltaY: number, layout: CartesianLayout): void {
    const f = Math.pow(2, -deltaY / 400);
    const zoom = (sc: Scale, px: number): Domain => { const c = sc.invert(px), [a, b] = sc.domain; return [c + (a - c) / f, c + (b - c) / f]; };
    this.zoomX = zoom(this.xScale(layout), x);
    this.zoomY = zoom(this.yScale("y", layout), y);
    if (this.usesY2()) this.zoomY2 = zoom(this.yScale("y2", layout), y);
  }
  /** Clears every zoom domain back to autoscale. */
  resetZoom(): void { this.zoomX = this.zoomY = this.zoomY2 = null; }
  /** True while an x or left-axis zoom is active. */
  get zoomed(): boolean { return this.zoomX !== null || this.zoomY !== null; }

  // ---- drawing ----
  /** Paints everything: title, grid, areas, bars, lines, markers, hover ring, axes, legend, tooltip and the zoom box. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, st = c.style, l = this.layout(width, height);
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    const muted: TextStyle = { ...text, color: th.mutedText };
    const grid = { color: th.grid, width: st.gridWidth, dash: st.gridDash ?? undefined };
    const axis = { color: th.axis, width: 1 };
    const plot = l.plot;
    p.clear(th.background);
    if (l.title && c.title) p.text(c.title, l.title.x + l.title.w / 2, l.title.y + l.title.h / 2, { ...text, size: th.fontSize + 3, weight: "bold", align: "center", baseline: "middle" });
    p.rect(plot.x, plot.y, plot.w, plot.h, { color: th.plotBackground });
    const xs = this.xScale(l), ys = this.yScale("y", l), xTicks = this.xTicks(l), yTicks = ys.ticks(this.yTickCount(l));
    if (c.showGrid) {
      if (st.showValueGrid) for (const v of yTicks) { const y = ys.scale(v); p.line(plot.x, y, plot.x + plot.w, y, grid); }
      if (st.showTimeGrid && !c.xAxis.categories) for (const v of xTicks) { const x = xs.scale(v); p.line(x, plot.y, x, plot.y + plot.h, grid); }
    }
    p.save();
    p.clipRect(plot.x, plot.y, plot.w, plot.h);
    const vis = this.visibleSeries();
    for (const s of vis) if (this.kindOf(s) === "area" && this.count(s) > 1) p.polygon(this.areaPolygon(s, l), { color: this.seriesColor(s), opacity: s.fillOpacity ?? 0.25 });
    for (const b of this.bars(l)) {
      const s = this.series(b.seriesId)!;
      const hot = this.hover !== null && this.hover.seriesId === b.seriesId && this.hover.index === b.index;
      p.rect(b.x, b.y, b.w, b.h, { color: this.seriesColor(s), opacity: s.fillOpacity ?? (hot ? 1 : 0.85) }, hot ? { color: th.text, width: 1 } : undefined);
    }
    for (const s of vis) {
      const kind = this.kindOf(s);
      if (kind === "bar" || kind === "scatter" || this.count(s) < 2) continue;
      p.polyline(this.pixelPoints(s, l), { color: this.seriesColor(s), width: s.width ?? st.seriesWidth, join: "round" });
    }
    for (const s of vis) {
      const marker = this.markerOf(s);
      if (marker === "none" || this.kindOf(s) === "bar") continue;
      const size = s.markerSize ?? 4, color = this.seriesColor(s);
      const ysA = this.yScale(s.axis ?? "y", l);
      for (let i = 0; i < this.count(s); i++) drawMarker(p, marker, xs.scale(this.xValue(s, i)), ysA.scale(this.stackedValue(s, i)[1]), size, color);
    }
    if (this.hover) {
      const s = this.series(this.hover.seriesId);
      if (s && this.kindOf(s) !== "bar") { const ysA = this.yScale(s.axis ?? "y", l); p.circle(xs.scale(this.xValue(s, this.hover.index)), ysA.scale(this.stackedValue(s, this.hover.index)[1]), (s.markerSize ?? 4) + 3, undefined, { color: this.seriesColor(s), width: 2 }); }
    }
    p.restore();

    // axes
    const ya = l.yAxis, lineX = ya.x + ya.w;
    p.line(lineX, ya.y, lineX, ya.y + ya.h, axis);
    const yCount = this.yTickCount(l);
    for (const v of yTicks) { const y = ys.scale(v); p.line(lineX - st.axisTickLength, y, lineX, y, axis); p.text(ys.format(v, yCount), lineX - st.axisLabelGap, y, { ...text, align: "right", baseline: "middle" }); }
    const yTitle = c.yAxis.label ?? c.yAxis.unit;
    if (yTitle) p.text(yTitle, ya.x + 2, ya.y + 2, { ...muted, baseline: "top" });
    if (l.y2Axis && c.y2Axis) {
      const y2 = this.yScale("y2", l), r = l.y2Axis;
      p.line(r.x, r.y, r.x, r.y + r.h, axis);
      for (const v of y2.ticks(yCount)) { const y = y2.scale(v); p.line(r.x, y, r.x + st.axisTickLength, y, axis); p.text(y2.format(v, yCount), r.x + st.axisLabelGap, y, { ...text, baseline: "middle" }); }
      const t2 = c.y2Axis.label ?? c.y2Axis.unit;
      if (t2) p.text(t2, r.x + r.w - 2, r.y + 2, { ...muted, align: "right", baseline: "top" });
    }
    const xa = l.xAxis;
    p.line(xa.x, xa.y, xa.x + xa.w, xa.y, axis);
    for (const v of xTicks) { const x = xs.scale(v); p.line(x, xa.y, x, xa.y + st.axisTickLength, axis); p.text(this.formatX(v, l), x, xa.y + st.axisTickLength + 2, { ...text, align: "center", baseline: "top" }); }
    if (c.xAxis.label) p.text(c.xAxis.label + (c.xAxis.unit ? ` (${c.xAxis.unit})` : ""), xa.x + xa.w / 2, xa.y + xa.h - 2, { ...muted, align: "center", baseline: "bottom" });

    if (l.legend) drawSimpleLegend(p, c, l.legend, this.legendItems(), this.legendHover);
    if (this.hover) { const tt = this.tooltip(this.hover, l); if (tt) drawTooltip(p, c, plot, tt); }
    if (this.box) { const b = this.box; p.rect(Math.min(b.x0, b.x1), Math.min(b.y0, b.y1), Math.abs(b.x1 - b.x0), Math.abs(b.y1 - b.y0), { color: th.dropIndicator, opacity: 0.1 }, { color: th.dropIndicator, width: 1, dash: [4, 3] }); }
  }
}

/** Paints a filled marker of the given shape and radius at a pixel; "none" draws nothing. */
export function drawMarker(p: Painter, shape: MarkerShape, x: number, y: number, size: number, color: string): void {
  if (shape === "circle") p.circle(x, y, size, { color });
  else if (shape === "square") p.rect(x - size, y - size, 2 * size, 2 * size, { color });
  else if (shape === "diamond") p.polygon([x, y - size * 1.3, x + size * 1.3, y, x, y + size * 1.3, x - size * 1.3, y], { color });
}

/** Tooltip box beside the anchor, flipped to stay inside `bounds`. Shared by the analytic charts. */
export function drawTooltip(p: Painter, c: { theme: ChartTheme; style: ChartStyle }, bounds: Rect, tt: Tooltip): void {
  const th = c.theme, st = c.style, pad = 6, rowH = th.fontSize + 6;
  const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize, baseline: "middle" };
  let w = p.measureText(tt.title, text).width;
  for (const ln of tt.lines) w = Math.max(w, 16 + p.measureText(`${ln.name}  ${ln.value}`, text).width);
  w += 2 * pad;
  const h = 2 * pad + rowH * (1 + tt.lines.length);
  let x = tt.x + 12, y = tt.y + 12;
  if (x + w > bounds.x + bounds.w) x = tt.x - 12 - w;
  if (y + h > bounds.y + bounds.h) y = tt.y - 12 - h;
  p.rect(x, y, w, h, { color: th.legendBackground, opacity: 0.95 }, { color: th.legendBorder, width: 1 }, st.legendRadius);
  p.text(tt.title, x + pad, y + pad + rowH / 2, { ...text, color: th.mutedText });
  tt.lines.forEach((ln, i) => {
    const cy = y + pad + rowH * (i + 1) + rowH / 2;
    p.rect(x + pad, cy - 4, 8, 8, { color: ln.color }, undefined, 2);
    p.text(`${ln.name}  ${ln.value}`, x + pad + 16, cy, text);
  });
}
