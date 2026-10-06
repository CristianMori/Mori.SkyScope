// Mori.SkyScope — Heatmap / spectrogram: a rows × cols matrix mapped through a colour map onto a raster, with axes and a colour bar.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, RasterImage, TextStyle } from "../paint/painter.js";
import type { Rect } from "../scene/geometry.js";
import { LinearScale } from "../scales/scale.js";
import { DEFAULT_STYLE, LIGHT_THEME, type ChartStyle, type ChartTheme } from "./trend-config.js";
import { formatValue } from "./trend-draw.js";
import { drawTooltip } from "./cartesian.js";
import { colormapHex, colormapLut, colormapStops, type ColormapName } from "./colormaps.js";

/**
 * Heatmap / spectrogram: a rows × cols matrix mapped through a colour map onto a raster, with axes and a
 * colour bar. `rolling` mode turns it into a spectrogram: `pushColumn` appends on the right and the x
 * extent slides. Mirrors `Mori.SkyScope.Core.Charts.Heatmap`; pinned by `spec/fixtures/heatmap.json`.
 */
export interface HeatmapConfig {
  /** Centred above the plot; null reserves no title row. */
  title: string | null;
  /** Matrix width in cells. */ cols: number; /** Matrix height in cells. */ rows: number;
  /** Data extents of the matrix (row 0 sits at yMin, column 0 at xMin). */
  xMin: number; xMax: number; yMin: number; yMax: number;
  /** Caption under the x axis. */ xLabel: string | null; /** Caption at the top of the y axis. */ yLabel: string | null; /** Caption above the colour bar and in the tooltip. */ valueLabel: string | null;
  /** Built-in map name or custom hex stops, low to high. */
  colormap: ColormapName | string[];
  /** Colour range; omit for the data range. */
  min?: number | undefined; max?: number | undefined;
  /** Show the colour bar right of the plot. */ colorbar: boolean; /** Colour bar width in pixels; its tick labels take 44 px more. */ colorbarWidth: number;
  /** Spectrogram mode: `pushColumn` scrolls the matrix. */
  rolling: boolean;
  /** Draw grid lines at the axis ticks over the raster. */
  showGrid: boolean;
  /** Colours and font. */ theme: ChartTheme; /** Line widths, dashes and paddings. */ style: ChartStyle;
  /** Outer margin in pixels. */ margin: number; /** Width of the y-axis column in pixels. */ yAxisWidth: number; /** Height of the x-axis row in pixels, excluding the label. */ xAxisHeight: number; /** Height of the title row in pixels. */ titleHeight: number; /** Target pixels per x tick; y and colour-bar ticks use 0.6 of it. */ tickSpacing: number;
}
/** Partial config for construction; `theme` and `style` may themselves be partial and merge over the defaults. */
export type HeatmapOptions = Omit<Partial<HeatmapConfig>, "theme" | "style"> & { theme?: Partial<ChartTheme> | undefined; style?: Partial<ChartStyle> | undefined };

/** Fills in every default (a 1×1 matrix over [0, 1]², viridis, colour bar on) under the given options. */
export function defaultHeatmapConfig(partial: HeatmapOptions = {}): HeatmapConfig {
  const { theme, style, ...rest } = partial;
  return {
    title: null, cols: 1, rows: 1, xMin: 0, xMax: 1, yMin: 0, yMax: 1, xLabel: null, yLabel: null, valueLabel: null, colormap: "viridis",
    colorbar: true, colorbarWidth: 14, rolling: false, showGrid: false,
    margin: 8, yAxisWidth: 48, xAxisHeight: 24, titleHeight: 22, tickSpacing: 80,
    ...rest, theme: { ...LIGHT_THEME, ...theme }, style: { ...DEFAULT_STYLE, ...style },
  };
}

/** Pixel rectangles for a given canvas size. */
export interface HeatmapLayout { /** Canvas width. */ width: number; /** Canvas height. */ height: number; /** Raster area. */ plot: Rect; /** Column left of the plot. */ yAxis: Rect; /** Row under the plot, label included. */ xAxis: Rect; /** Colour bar, null when disabled. */ colorbar: Rect | null; /** Title row, null without a title. */ title: Rect | null }
/** A hovered cell in logical coordinates. */
export interface CellHit { /** Logical column, 0 = left. */ col: number; /** Logical row, 0 = bottom. */ row: number; /** Cell value; never NaN. */ value: number }

/** Matrix storage, colour mapping, layout, hover and drawing of a heatmap or spectrogram. */
export class Heatmap {
  /** Effective configuration; `xMin`/`xMax` slide as columns are pushed in rolling mode. */
  readonly config: HeatmapConfig;
  /** Row-major, row 0 = yMin; physical column = (head + logical) % cols in rolling mode. NaN = empty. */
  readonly values: Float64Array;
  private head = 0;
  private version = 0;
  private raster: RasterImage | null = null;
  private rasterVersion = -1;
  /** Cell under the pointer, set by `pointerMove`; null when none. */
  hover: CellHit | null = null;

  /** Allocates the matrix (all NaN) from the options and optionally fills it with row-major values. */
  constructor(options: HeatmapOptions = {}, values?: ArrayLike<number>) {
    this.config = defaultHeatmapConfig(options);
    this.values = new Float64Array(this.config.rows * this.config.cols).fill(NaN);
    if (values) this.setValues(values);
  }

  /** Matrix width in cells. */
  get cols(): number { return this.config.cols; }
  /** Matrix height in cells. */
  get rows(): number { return this.config.rows; }

  /** Copies row-major values in (extra entries ignored, missing ones left as they were), resets the rolling head and invalidates the raster. */
  setValues(values: ArrayLike<number>): void {
    const n = Math.min(values.length, this.values.length);
    for (let i = 0; i < n; i++) this.values[i] = values[i]!;
    this.head = 0; this.version++;
  }
  /** Rolling mode: the new column becomes the right-most one and the x extents slide by one column. */
  pushColumn(column: ArrayLike<number>): void {
    const c = this.config;
    for (let r = 0; r < c.rows; r++) this.values[r * c.cols + this.head] = r < column.length ? column[r]! : NaN;
    this.head = (this.head + 1) % c.cols;
    if (c.rolling) { const dx = (c.xMax - c.xMin) / c.cols; c.xMin += dx; c.xMax += dx; }
    this.version++;
  }
  /** Value at logical (row, col). */
  get(row: number, col: number): number {
    const c = this.config;
    if (row < 0 || row >= c.rows || col < 0 || col >= c.cols) return NaN;
    return this.values[row * c.cols + (this.head + col) % c.cols]!;
  }
  /** Writes one logical cell and invalidates the raster; no bounds check. */
  set(row: number, col: number, v: number): void { const c = this.config; this.values[row * c.cols + (this.head + col) % c.cols] = v; this.version++; }

  /** Colour range: configured bounds, else the finite data range (degenerate → [v, v + 1]). */
  range(): [number, number] {
    let lo = Infinity, hi = -Infinity;
    for (let i = 0; i < this.values.length; i++) { const v = this.values[i]!; if (Number.isNaN(v)) continue; if (v < lo) lo = v; if (v > hi) hi = v; }
    if (this.config.min !== undefined) lo = this.config.min;
    if (this.config.max !== undefined) hi = this.config.max;
    if (!Number.isFinite(lo) || !Number.isFinite(hi)) return [0, 1];
    if (hi <= lo) hi = lo + 1;
    return [lo, hi];
  }
  /** Resolved colour stops of the configured map. */
  stops(): string[] { return colormapStops(this.config.colormap); }
  /** Hex colour of a value under the current colour range. */
  colorAt(v: number): string { const [lo, hi] = this.range(); return colormapHex(this.stops(), (v - lo) / (hi - lo)); }

  /** RGBA raster (row 0 = top = yMax); rebuilt only after data changes. NaN cells are transparent. */
  image(): RasterImage {
    if (this.raster && this.rasterVersion === this.version) return this.raster;
    const c = this.config, [lo, hi] = this.range(), lut = colormapLut(this.stops(), 256), span = hi - lo;
    const rgba = new Uint8ClampedArray(c.cols * c.rows * 4);
    for (let row = 0; row < c.rows; row++) {
      const py = c.rows - 1 - row;
      for (let col = 0; col < c.cols; col++) {
        const v = this.get(row, col), o = (py * c.cols + col) * 4;
        if (Number.isNaN(v)) continue;
        let k = Math.floor((v - lo) / span * 255 + 0.5);
        if (k < 0) k = 0; else if (k > 255) k = 255;
        rgba[o] = lut[k * 3]!; rgba[o + 1] = lut[k * 3 + 1]!; rgba[o + 2] = lut[k * 3 + 2]!; rgba[o + 3] = 255;
      }
    }
    this.raster = { width: c.cols, height: c.rows, rgba, version: this.version };
    this.rasterVersion = this.version;
    return this.raster;
  }

  /** Pixel layout for a canvas size: title, plot, axes and colour bar. */
  layout(width: number, height: number): HeatmapLayout {
    const c = this.config, m = c.margin;
    let x0 = m, y0 = m, x1 = width - m, y1 = height - m;
    let title: Rect | null = null;
    if (c.title) { title = { x: x0, y: y0, w: x1 - x0, h: c.titleHeight }; y0 += c.titleHeight; }
    let colorbar: Rect | null = null;
    const xAxisH = c.xAxisHeight + (c.xLabel ? c.theme.fontSize + 4 : 0);
    if (c.colorbar) { const w = c.colorbarWidth; colorbar = { x: x1 - 44 - w, y: y0, w, h: Math.max(0, y1 - y0 - xAxisH) }; x1 = colorbar.x - m; }
    const plot: Rect = { x: x0 + c.yAxisWidth, y: y0, w: Math.max(0, x1 - x0 - c.yAxisWidth), h: Math.max(0, y1 - y0 - xAxisH) };
    return { width, height, plot, yAxis: { x: x0, y: plot.y, w: c.yAxisWidth, h: plot.h }, xAxis: { x: plot.x, y: plot.y + plot.h, w: plot.w, h: xAxisH }, colorbar, title };
  }
  /** Linear scale from the x data extents to plot pixels. */
  xScale(l: HeatmapLayout): LinearScale { return new LinearScale([this.config.xMin, this.config.xMax], [l.plot.x, l.plot.x + l.plot.w]); }
  /** Linear scale from the y data extents to plot pixels, yMin at the bottom. */
  yScale(l: HeatmapLayout): LinearScale { return new LinearScale([this.config.yMin, this.config.yMax], [l.plot.y + l.plot.h, l.plot.y]); }

  /** Cell under a pixel position, or null outside the plot or over an empty (NaN) cell. */
  cellAt(x: number, y: number, l: HeatmapLayout): CellHit | null {
    const p = l.plot;
    if (x < p.x || x >= p.x + p.w || y < p.y || y >= p.y + p.h || p.w <= 0 || p.h <= 0) return null;
    const col = Math.floor((x - p.x) / p.w * this.config.cols), row = Math.floor((p.y + p.h - y) / p.h * this.config.rows);
    const v = this.get(row, col);
    return Number.isNaN(v) ? null : { col, row, value: v };
  }
  /** Updates `hover` for a pointer position on a canvas of the given size. */
  pointerMove(x: number, y: number, width: number, height: number): void { this.hover = this.cellAt(x, y, this.layout(width, height)); }
  /** Clears `hover`. */
  pointerLeave(): void { this.hover = null; }

  /** Paints everything: background, raster, grid, hover outline, axes, colour bar and tooltip. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, st = c.style, l = this.layout(width, height), plot = l.plot;
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    const muted: TextStyle = { ...text, color: th.mutedText };
    const axis = { color: th.axis, width: 1 };
    p.clear(th.background);
    if (l.title && c.title) p.text(c.title, l.title.x + l.title.w / 2, l.title.y + l.title.h / 2, { ...text, size: th.fontSize + 3, weight: "bold", align: "center", baseline: "middle" });
    p.rect(plot.x, plot.y, plot.w, plot.h, { color: th.plotBackground });
    if (plot.w > 0 && plot.h > 0) p.image(this.image(), plot.x, plot.y, plot.w, plot.h);
    const xs = this.xScale(l), ys = this.yScale(l);
    const xCount = Math.max(2, Math.round(plot.w / c.tickSpacing)), yCount = Math.max(2, Math.round(plot.h / (c.tickSpacing * 0.6)));
    const xTicks = xs.ticks(xCount), yTicks = ys.ticks(yCount);
    if (c.showGrid) {
      const grid = { color: th.grid, width: st.gridWidth, dash: st.gridDash ?? undefined, opacity: 0.6 };
      for (const v of yTicks) { const y = ys.scale(v); p.line(plot.x, y, plot.x + plot.w, y, grid); }
      for (const v of xTicks) { const x = xs.scale(v); p.line(x, plot.y, x, plot.y + plot.h, grid); }
    }
    if (this.hover) {
      const h = this.hover, cw = plot.w / c.cols, ch = plot.h / c.rows;
      p.rect(plot.x + h.col * cw, plot.y + plot.h - (h.row + 1) * ch, cw, ch, undefined, { color: th.text, width: 1.5 });
    }
    const ya = l.yAxis, lineX = ya.x + ya.w;
    p.line(lineX, ya.y, lineX, ya.y + ya.h, axis);
    for (const v of yTicks) { const y = ys.scale(v); p.line(lineX - st.axisTickLength, y, lineX, y, axis); p.text(ys.format(v, yCount), lineX - st.axisLabelGap, y, { ...text, align: "right", baseline: "middle" }); }
    if (c.yLabel) p.text(c.yLabel, ya.x + 2, ya.y + 2, { ...muted, baseline: "top" });
    const xa = l.xAxis;
    p.line(xa.x, xa.y, xa.x + xa.w, xa.y, axis);
    for (const v of xTicks) { const x = xs.scale(v); p.line(x, xa.y, x, xa.y + st.axisTickLength, axis); p.text(xs.format(v, xCount), x, xa.y + st.axisTickLength + 2, { ...text, align: "center", baseline: "top" }); }
    if (c.xLabel) p.text(c.xLabel, xa.x + xa.w / 2, xa.y + xa.h - 2, { ...muted, align: "center", baseline: "bottom" });
    if (l.colorbar) {
      const cb = l.colorbar, [lo, hi] = this.range(), stops = this.stops(), n = 32;
      for (let i = 0; i < n; i++) { const y0 = cb.y + cb.h * (1 - (i + 1) / n); p.rect(cb.x, y0, cb.w, cb.h / n + 0.5, { color: colormapHex(stops, (i + 0.5) / n) }); }
      p.rect(cb.x, cb.y, cb.w, cb.h, undefined, axis);
      const cs = new LinearScale([lo, hi], [cb.y + cb.h, cb.y]), cCount = Math.max(2, Math.round(cb.h / (c.tickSpacing * 0.6)));
      for (const v of cs.ticks(cCount)) { const y = cs.scale(v); p.line(cb.x + cb.w, y, cb.x + cb.w + st.axisTickLength, y, axis); p.text(cs.format(v, cCount), cb.x + cb.w + st.axisLabelGap, y, { ...text, baseline: "middle" }); }
      if (c.valueLabel) p.text(c.valueLabel, cb.x, cb.y - 4, { ...muted, baseline: "bottom" });
    }
    if (this.hover) {
      const h = this.hover, cw = (c.xMax - c.xMin) / c.cols, ch = (c.yMax - c.yMin) / c.rows;
      const cx = plot.x + (h.col + 0.5) * plot.w / c.cols, cy = plot.y + plot.h - (h.row + 0.5) * plot.h / c.rows;
      drawTooltip(p, c, plot, { x: cx, y: cy, title: `${c.xLabel ?? "x"} ${formatValue(c.xMin + (h.col + 0.5) * cw)}, ${c.yLabel ?? "y"} ${formatValue(c.yMin + (h.row + 0.5) * ch)}`, lines: [{ name: c.valueLabel ?? "value", value: formatValue(h.value), color: this.colorAt(h.value) }] });
    }
  }
}
