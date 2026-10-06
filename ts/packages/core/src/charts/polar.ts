// Mori.SkyScope — Polar chart (angle in degrees, radius = value) and radar chart (categories at equal angles).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import type { Rect } from "../scene/geometry.js";
import { LinearScale } from "../scales/scale.js";
import { niceLinearDomain } from "../scales/ticks.js";
import { polar } from "../gauges/common.js";
import { DEFAULT_STYLE, LIGHT_THEME, SERIES_PALETTE, type ChartStyle, type ChartTheme, type LegendPosition } from "./trend-config.js";
import { formatValue } from "./trend-draw.js";
import { drawSimpleLegend, legendItemAt, overlayLegendRect, reserveLegend, type LegendItem } from "./legend.js";
import { drawMarker, drawTooltip, type ChartHit, type MarkerShape } from "./cartesian.js";

/**
 * Polar chart (angle in degrees, radius = value) and radar chart (categories at equal angles). Angles
 * follow the gauge convention on screen: `startAngle` is where 0° points (0 = up), clockwise by default.
 * Mirrors `Mori.SkyScope.Core.Charts.PolarChart`; pinned by `spec/fixtures/polar.json`.
 */
export type PolarSeriesKind = "line" | "area" | "scatter";
/** One series: values by angle, or by category index in radar mode. */
export interface PolarSeriesConfig {
  /** Unique key. */ id: string; /** Legend text; defaults to the id. */ name?: string | undefined; /** Drawing style; defaults to "area" with categories, else "line". */ kind?: PolarSeriesKind | undefined;
  /** Degrees; omit for categories (radar) or equally spaced samples. */
  angles?: number[] | undefined; /** Radius per point, in data units. */ values: number[];
  /** Stroke and fill colour; palette by series index when omitted. */ color?: string | undefined; /** Stroke width in pixels. */ width?: number | undefined; /** Area fill opacity (default 0.25). */ fillOpacity?: number | undefined;
  /** Join the last point back to the first (radar default). */
  closed?: boolean | undefined;
  /** Point marker; scatter series and radar mode default to circles. */ marker?: MarkerShape | undefined; /** Marker radius in pixels (default 3.5). */ markerSize?: number | undefined; /** false removes the series from the chart and the legend. */ visible?: boolean | undefined;
}
/** Full polar/radar configuration; `defaultPolarConfig` fills the defaults. */
export interface PolarChartConfig {
  /** Centred above the chart; null reserves no title row. */
  title: string | null;
  /** Series in draw order. */
  series: PolarSeriesConfig[];
  /** Radar mode: one spoke per category, series values indexed by category. */
  categories: string[] | null;
  /** Fixed radial range; omit for a niced data range that starts at 0 or below. */
  min?: number | undefined; max?: number | undefined;
  /** Screen angle of data angle 0, in degrees (0 = up). */ startAngle: number; /** Data angles increase clockwise on screen. */ clockwise: boolean;
  /** Spoke spacing in degrees (ignored with categories). */
  angleStep: number;
  /** Rings as circles, or as polygons through the spokes (radar look). */
  gridShape: "circle" | "polygon";
  /** Target ring count. */
  rings: number;
  /** Legend placement. */ legend: LegendPosition; /** Draw rings and spokes. */ showGrid: boolean; /** Colours and font. */ theme: ChartTheme; /** Widths, dashes and paddings. */ style: ChartStyle;
  /** Outer margin in pixels. */ margin: number; /** Title row height in pixels. */ titleHeight: number; /** Width in pixels of "right" and overlay legends. */ legendWidth: number; /** Pixels within which a point counts as hovered. */ hoverRadius: number;
}
/** Partial config for construction; `theme` and `style` may themselves be partial and merge over the defaults. */
export type PolarChartOptions = Omit<Partial<PolarChartConfig>, "theme" | "style"> & { theme?: Partial<ChartTheme> | undefined; style?: Partial<ChartStyle> | undefined };

/** Fills in every default (30° spokes, 5 rings, circular grid, legend top-right) under the given options. */
export function defaultPolarConfig(partial: PolarChartOptions = {}): PolarChartConfig {
  const { theme, style, ...rest } = partial;
  return {
    title: null, series: [], categories: null, startAngle: 0, clockwise: true, angleStep: 30, gridShape: "circle", rings: 5,
    legend: "top-right", showGrid: true, margin: 8, titleHeight: 22, legendWidth: 150, hoverRadius: 12,
    ...rest, theme: { ...LIGHT_THEME, ...theme }, style: { ...DEFAULT_STYLE, ...style },
  };
}

/** Pixel geometry for a canvas size. */
export interface PolarLayout { /** Canvas width. */ width: number; /** Canvas height. */ height: number; /** Centre x. */ cx: number; /** Centre y. */ cy: number; /** Outer radius, leaving room for the angle labels. */ r: number; /** Area left for the chart after title and legend. */ plot: Rect; /** Title row, null without a title. */ title: Rect | null; /** Legend rect, null when hidden or empty. */ legend: Rect | null }

/** Polar/radar chart state (config, hidden series, hover) with scales, layout, hit testing and drawing. */
export class PolarChart {
  /** Effective configuration. */
  readonly config: PolarChartConfig;
  /** Series ids toggled off through the legend. */
  readonly hidden = new Set<string>();
  /** Hovered point, or null. */
  hover: ChartHit | null = null;
  /** Hovered legend row id, or null. */
  legendHover: string | null = null;

  /** Creates a chart with the defaults merged under the options. */
  constructor(options: PolarChartOptions = {}) { this.config = defaultPolarConfig(options); }

  /** Series config by id. */
  series(id: string): PolarSeriesConfig | undefined { return this.config.series.find((s) => s.id === id); }
  /** Series neither flagged invisible nor hidden through the legend, in config order. */
  visibleSeries(): PolarSeriesConfig[] { return this.config.series.filter((s) => s.visible !== false && !this.hidden.has(s.id)); }
  /** Effective kind: explicit, else area in radar mode and line otherwise. */
  kindOf(s: PolarSeriesConfig): PolarSeriesKind { return s.kind ?? (this.config.categories ? "area" : "line"); }
  /** Display name: `name`, else the id. */
  seriesName(s: PolarSeriesConfig): string { return s.name ?? s.id; }
  /** Colour: explicit, else from the palette by configured index. */
  seriesColor(s: PolarSeriesConfig): string { return s.color ?? SERIES_PALETTE[this.config.series.indexOf(s) % SERIES_PALETTE.length]!; }
  /** Whether the outline joins back to the first point: explicit, else true in radar mode or for areas. */
  isClosed(s: PolarSeriesConfig): boolean { return s.closed ?? (this.config.categories !== null || this.kindOf(s) === "area"); }
  /** Effective marker: explicit, else circles for scatter series and in radar mode, none otherwise. */
  markerOf(s: PolarSeriesConfig): MarkerShape { return s.marker ?? (this.kindOf(s) === "scatter" || this.config.categories ? "circle" : "none"); }
  /** Number of points drawn: bounded by the categories, the angles and the values. */
  count(s: PolarSeriesConfig): number { return this.config.categories ? Math.min(s.values.length, this.config.categories.length) : s.angles ? Math.min(s.angles.length, s.values.length) : s.values.length; }
  /** Hides a visible series or shows a hidden one. */
  toggleSeries(id: string): void { if (this.hidden.has(id)) this.hidden.delete(id); else this.hidden.add(id); }
  /** One legend row per series with `visible !== false`; hidden ones are flagged. */
  legendItems(): LegendItem[] {
    return this.config.series.filter((s) => s.visible !== false).map((s) => ({ id: s.id, name: this.seriesName(s), color: this.seriesColor(s), hidden: this.hidden.has(s.id), shape: this.kindOf(s) === "area" ? "box" : this.kindOf(s) === "scatter" ? "circle" : "line" }));
  }

  /** Data angle (degrees) of point `i`: explicit, category spoke, or equally spaced over 360°. */
  angleOf(s: PolarSeriesConfig, i: number): number {
    const cats = this.config.categories;
    if (cats) return i * 360 / Math.max(1, cats.length);
    if (s.angles) return s.angles[i]!;
    return i * 360 / Math.max(1, s.values.length);
  }
  /** Data angle → screen angle (gauge convention). */
  screenAngle(deg: number): number { return this.config.startAngle + (this.config.clockwise ? deg : -deg); }

  /** Radial domain: from 0 (or the data minimum when negative) to the data maximum, niced to the ring count; `min`/`max` override. */
  rDomain(): [number, number] {
    const c = this.config;
    let lo = Infinity, hi = -Infinity;
    for (const s of this.visibleSeries()) for (let i = 0; i < this.count(s); i++) { const v = s.values[i]!; if (v < lo) lo = v; if (v > hi) hi = v; }
    if (!Number.isFinite(lo)) { lo = 0; hi = 1; }
    if (lo > 0) lo = 0;
    if (hi <= lo) hi = lo + 1;
    if (!(c.min !== undefined && c.max !== undefined)) [lo, hi] = niceLinearDomain(lo, hi, c.rings);
    if (c.min !== undefined) lo = c.min;
    if (c.max !== undefined) hi = c.max;
    return [lo, hi];
  }
  /** Linear scale from the radial domain to [0, r] pixels. */
  rScale(l: PolarLayout): LinearScale { return new LinearScale(this.rDomain(), [0, l.r]); }
  /** Data angles of the spokes: one per category, else every `angleStep` degrees from 0. */
  gridAngles(): number[] {
    const c = this.config, out: number[] = [];
    if (c.categories) { for (let i = 0; i < c.categories.length; i++) out.push(i * 360 / c.categories.length); return out; }
    for (let a = 0; a < 360 - 1e-9; a += Math.max(1, c.angleStep)) out.push(a);
    return out;
  }

  /** Pixel geometry for a canvas size; the radius leaves 2.2 font sizes around the circle for the angle labels. */
  layout(width: number, height: number): PolarLayout {
    const c = this.config, m = c.margin;
    let outer: Rect = { x: m, y: m, w: Math.max(0, width - 2 * m), h: Math.max(0, height - 2 * m) };
    let title: Rect | null = null;
    if (c.title) { title = { x: outer.x, y: outer.y, w: outer.w, h: c.titleHeight }; outer = { x: outer.x, y: outer.y + c.titleHeight, w: outer.w, h: Math.max(0, outer.h - c.titleHeight) }; }
    const items = this.legendItems().length;
    const { inner: plot, legend: reserved } = reserveLegend(c, outer, items);
    const r = Math.max(0, Math.min(plot.w, plot.h) / 2 - c.theme.fontSize * 2.2);
    return { width, height, cx: plot.x + plot.w / 2, cy: plot.y + plot.h / 2, r, plot, title, legend: reserved ?? overlayLegendRect(c, plot, items) };
  }

  /** Pixel position of (data angle, value); values below the domain start collapse to the centre. */
  point(l: PolarLayout, angleDeg: number, value: number): { x: number; y: number } {
    const rs = this.rScale(l);
    return polar(l.cx, l.cy, Math.max(0, rs.scale(value)), this.screenAngle(angleDeg));
  }
  /** Interleaved pixel polyline of a series, closed by repeating the first point when `isClosed`. */
  pixelPoints(s: PolarSeriesConfig, l: PolarLayout): number[] {
    const out: number[] = [];
    const n = this.count(s);
    for (let i = 0; i < n; i++) { const pt = this.point(l, this.angleOf(s, i), s.values[i]!); out.push(pt.x, pt.y); }
    if (this.isClosed(s) && n > 1) out.push(out[0]!, out[1]!);
    return out;
  }

  /** Nearest visible point within `hoverRadius` of the pixel, or null. */
  hitTest(x: number, y: number, l: PolarLayout): ChartHit | null {
    let best: ChartHit | null = null, bestD = this.config.hoverRadius * this.config.hoverRadius;
    for (const s of this.visibleSeries()) for (let i = 0; i < this.count(s); i++) {
      const pt = this.point(l, this.angleOf(s, i), s.values[i]!), dx = pt.x - x, dy = pt.y - y, d = dx * dx + dy * dy;
      if (d < bestD) { bestD = d; best = { seriesId: s.id, index: i }; }
    }
    return best;
  }
  /** Updates `hover` and `legendHover` for a pointer position on a canvas of the given size. */
  pointerMove(x: number, y: number, width: number, height: number): void {
    const l = this.layout(width, height);
    this.hover = this.hitTest(x, y, l);
    this.legendHover = legendItemAt(this.config, l.legend, this.legendItems(), x, y, (t) => t.length * this.config.theme.fontSize * 0.6);
  }
  /** Clears both hover states. */
  pointerLeave(): void { this.hover = null; this.legendHover = null; }
  /** Toggles the legend row under the point; returns true when it consumed the click. */
  click(x: number, y: number, width: number, height: number): boolean {
    const l = this.layout(width, height);
    const id = legendItemAt(this.config, l.legend, this.legendItems(), x, y, (t) => t.length * this.config.theme.fontSize * 0.6);
    if (!id) return false;
    this.toggleSeries(id);
    return true;
  }
  /** Spoke caption: the category name in radar mode, else the rounded degrees. */
  angleLabel(deg: number): string {
    const cats = this.config.categories;
    if (cats) { const i = Math.floor(deg * cats.length / 360 + 0.5); return cats[i] ?? ""; }
    return `${Math.floor(deg + 0.5)}°`;
  }

  /** Paints title, grid, spoke and ring labels, areas, lines, markers, hover ring, legend and tooltip. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, st = c.style, l = this.layout(width, height);
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    const muted: TextStyle = { ...text, color: th.mutedText };
    const grid = { color: th.grid, width: st.gridWidth, dash: st.gridDash ?? undefined };
    p.clear(th.background);
    if (l.title && c.title) p.text(c.title, l.title.x + l.title.w / 2, l.title.y + l.title.h / 2, { ...text, size: th.fontSize + 3, weight: "bold", align: "center", baseline: "middle" });
    const angles = this.gridAngles(), rs = this.rScale(l), ringTicks = rs.ticks(c.rings);
    const ringPoly = (rr: number): number[] => { const out: number[] = []; for (const a of angles) { const pt = polar(l.cx, l.cy, rr, this.screenAngle(a)); out.push(pt.x, pt.y); } return out; };
    if (c.gridShape === "polygon" && angles.length > 2) p.polygon(ringPoly(l.r), { color: th.plotBackground });
    else p.circle(l.cx, l.cy, l.r, { color: th.plotBackground });
    if (c.showGrid) {
      for (const v of ringTicks) {
        const rr = rs.scale(v);
        if (rr <= 0) continue;
        if (c.gridShape === "polygon" && angles.length > 2) p.polygon(ringPoly(rr), undefined, grid); else p.circle(l.cx, l.cy, rr, undefined, grid);
      }
      for (const a of angles) { const pt = polar(l.cx, l.cy, l.r, this.screenAngle(a)); p.line(l.cx, l.cy, pt.x, pt.y, grid); }
    }
    for (const a of angles) {
      const pt = polar(l.cx, l.cy, l.r + th.fontSize * 0.9, this.screenAngle(a));
      const dx = pt.x - l.cx;
      p.text(this.angleLabel(a), pt.x, pt.y, { ...text, align: Math.abs(dx) < 1 ? "center" : dx > 0 ? "left" : "right", baseline: "middle" });
    }
    for (const v of ringTicks) { const rr = rs.scale(v); if (rr <= 0) continue; const pt = polar(l.cx, l.cy, rr, this.screenAngle(0)); p.text(rs.format(v, c.rings), pt.x + 3, pt.y, { ...muted, size: th.fontSize - 1, baseline: "middle" }); }
    const vis = this.visibleSeries();
    for (const s of vis) if (this.kindOf(s) === "area" && this.count(s) > 2) p.polygon(this.pixelPoints(s, l), { color: this.seriesColor(s), opacity: s.fillOpacity ?? 0.25 });
    for (const s of vis) if (this.kindOf(s) !== "scatter" && this.count(s) > 1) p.polyline(this.pixelPoints(s, l), { color: this.seriesColor(s), width: s.width ?? st.seriesWidth, join: "round" });
    for (const s of vis) {
      const marker = this.markerOf(s);
      if (marker === "none") continue;
      for (let i = 0; i < this.count(s); i++) { const pt = this.point(l, this.angleOf(s, i), s.values[i]!); drawMarker(p, marker, pt.x, pt.y, s.markerSize ?? 3.5, this.seriesColor(s)); }
    }
    if (this.hover) {
      const s = this.series(this.hover.seriesId);
      if (s) {
        const pt = this.point(l, this.angleOf(s, this.hover.index), s.values[this.hover.index]!);
        p.circle(pt.x, pt.y, (s.markerSize ?? 3.5) + 3, undefined, { color: this.seriesColor(s), width: 2 });
        if (l.legend) drawSimpleLegend(p, c, l.legend, this.legendItems(), this.legendHover);
        drawTooltip(p, c, l.plot, { x: pt.x, y: pt.y, title: this.angleLabel(this.angleOf(s, this.hover.index)), lines: [{ name: this.seriesName(s), value: formatValue(s.values[this.hover.index]!), color: this.seriesColor(s) }] });
        return;
      }
    }
    if (l.legend) drawSimpleLegend(p, c, l.legend, this.legendItems(), this.legendHover);
  }
}
