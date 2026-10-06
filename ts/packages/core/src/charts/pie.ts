// Mori.SkyScope — Pie / donut chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import type { Rect } from "../scene/geometry.js";
import { formatNumber } from "../scales/ticks.js";
import { arcAngle, polar } from "../gauges/common.js";
import { DEFAULT_STYLE, LIGHT_THEME, SERIES_PALETTE, type ChartStyle, type ChartTheme, type LegendPosition } from "./trend-config.js";
import { drawSimpleLegend, legendItemAt, overlayLegendRect, reserveLegend, type LegendItem } from "./legend.js";
import { drawTooltip } from "./cartesian.js";

/**
 * Pie / donut chart. Angles follow the gauge convention (degrees, 0 = up, clockwise).
 * Mirrors `Mori.SkyScope.Core.Charts.PieChart`; pinned by `spec/fixtures/pie.json`.
 */
export interface PieSliceConfig { /** Unique key, used for hiding and hover. */ id: string; /** Legend and label text; defaults to the id. */ name?: string | undefined; /** Non-negative weight; zero-valued slices are skipped. */ value: number; /** Fill colour; defaults to the series palette by slice index. */ color?: string | undefined }
/** What each slice is labelled with. */
export type PieLabelMode = "none" | "percent" | "value" | "name";

/** Full pie configuration; `defaultPieConfig` fills the defaults. */
export interface PieChartConfig {
  /** Centred above the chart; null reserves no title row. */
  title: string | null;
  /** Slices in configuration order, drawn clockwise from `startAngle` (largest first when `sort` is set). */
  slices: PieSliceConfig[];
  /** Inner radius as a fraction of the outer one; 0 = pie. */
  donut: number;
  /** Degrees where the first slice starts (0 = up, clockwise). */
  startAngle: number;
  /** Degrees of gap between slices. */
  padAngle: number;
  /** Label content. */ labels: PieLabelMode; /** Inside the slice, or outside with a leader line. */ labelPosition: "inside" | "outside";
  /** Slices under this fraction get no inside label. */
  minLabelFraction: number;
  /** Draw slices largest first. */
  sort: boolean;
  /** Text in the middle; a donut defaults to the formatted total. */
  centerText: string | null;
  /** Hovered slice offset as a fraction of the radius. */
  hoverExplode: number;
  /** Decimals for values and percentages. */
  decimals: number;
  /** Legend placement. */ legend: LegendPosition; /** Colours and font. */ theme: ChartTheme; /** Paddings, widths and legend geometry. */ style: ChartStyle;
  /** Outer margin in pixels. */ margin: number; /** Title row height in pixels. */ titleHeight: number; /** Width in pixels of "right" and overlay legends. */ legendWidth: number;
}
/** Partial config for construction; `theme` and `style` may themselves be partial and merge over the defaults. */
export type PieChartOptions = Omit<Partial<PieChartConfig>, "theme" | "style"> & { theme?: Partial<ChartTheme> | undefined; style?: Partial<ChartStyle> | undefined };

/** Fills in every default (percent labels inside, legend on the right, 4 % hover explode) under the given options. */
export function defaultPieConfig(partial: PieChartOptions = {}): PieChartConfig {
  const { theme, style, ...rest } = partial;
  return {
    title: null, slices: [], donut: 0, startAngle: 0, padAngle: 0, labels: "percent", labelPosition: "inside", minLabelFraction: 0.04, sort: false, centerText: null,
    hoverExplode: 0.04, decimals: 0, legend: "right", margin: 8, titleHeight: 22, legendWidth: 150,
    ...rest, theme: { ...LIGHT_THEME, ...theme }, style: { ...DEFAULT_STYLE, ...style },
  };
}

/** Angular span of one visible slice. */
export interface PieSliceGeometry { /** Slice id. */ id: string; /** Slice value. */ value: number; /** Share of the visible total, 0..1. */ frac: number; /** Start angle in degrees, pad applied. */ start: number; /** End angle in degrees, pad applied. */ end: number; /** Centre angle in degrees. */ mid: number }
/** Pixel geometry for a canvas size. */
export interface PieLayout { /** Canvas width. */ width: number; /** Canvas height. */ height: number; /** Centre x. */ cx: number; /** Centre y. */ cy: number; /** Outer radius. */ r: number; /** Inner (donut) radius; 0 for a pie. */ inner: number; /** Area left for the pie after title and legend. */ plot: Rect; /** Title row, null without a title. */ title: Rect | null; /** Legend rect, null when hidden or empty. */ legend: Rect | null }

/** Pie chart state (config, hidden slices, hover) with layout, hit testing and drawing. */
export class PieChart {
  /** Effective configuration. */
  readonly config: PieChartConfig;
  /** Slice ids toggled off through the legend. */
  readonly hidden = new Set<string>();
  /** Hovered slice id, or null. */
  hover: string | null = null;
  /** Hovered legend row id, or null. */
  legendHover: string | null = null;

  /** Creates a chart with the defaults merged under the options. */
  constructor(options: PieChartOptions = {}) { this.config = defaultPieConfig(options); }

  /** Slice config by id. */
  slice(id: string): PieSliceConfig | undefined { return this.config.slices.find((s) => s.id === id); }
  /** Display name: `name`, else the id. */
  sliceName(s: PieSliceConfig): string { return s.name ?? s.id; }
  /** Fill colour: explicit, else from the palette by configured index. */
  sliceColor(s: PieSliceConfig): string { return s.color ?? SERIES_PALETTE[this.config.slices.indexOf(s) % SERIES_PALETTE.length]!; }
  /** Hides a visible slice or shows a hidden one. */
  toggleSlice(id: string): void { if (this.hidden.has(id)) this.hidden.delete(id); else this.hidden.add(id); }
  /** Slices that are not hidden and have a positive value, sorted by value when `sort` is set. */
  visibleSlices(): PieSliceConfig[] {
    const out = this.config.slices.filter((s) => !this.hidden.has(s.id) && s.value > 0);
    if (this.config.sort) out.sort((a, b) => b.value - a.value);
    return out;
  }
  /** Sum of the visible slice values. */
  total(): number { let t = 0; for (const s of this.visibleSlices()) t += s.value; return t; }
  /** One legend row per configured slice; hidden ones are included and flagged. */
  legendItems(): LegendItem[] { return this.config.slices.map((s) => ({ id: s.id, name: this.sliceName(s), color: this.sliceColor(s), hidden: this.hidden.has(s.id), shape: "box" })); }

  /** Slice angles in degrees (gauge convention), pad applied symmetrically inside each slice. */
  slices(): PieSliceGeometry[] {
    const total = this.total(), out: PieSliceGeometry[] = [];
    if (total <= 0) return out;
    let a = this.config.startAngle;
    for (const s of this.visibleSlices()) {
      const frac = s.value / total, span = frac * 360, pad = Math.min(this.config.padAngle, span);
      out.push({ id: s.id, value: s.value, frac, start: a + pad / 2, end: a + span - pad / 2, mid: a + span / 2 });
      a += span;
    }
    return out;
  }

  /** Pixel geometry for a canvas size; outside labels shrink the radius to 70 % of the available half-size, else 92 %. */
  layout(width: number, height: number): PieLayout {
    const c = this.config, m = c.margin;
    let outer: Rect = { x: m, y: m, w: Math.max(0, width - 2 * m), h: Math.max(0, height - 2 * m) };
    let title: Rect | null = null;
    if (c.title) { title = { x: outer.x, y: outer.y, w: outer.w, h: c.titleHeight }; outer = { x: outer.x, y: outer.y + c.titleHeight, w: outer.w, h: Math.max(0, outer.h - c.titleHeight) }; }
    const items = this.legendItems().length;
    const { inner: plot, legend: reserved } = reserveLegend(c, outer, items);
    const outside = c.labels !== "none" && c.labelPosition === "outside";
    const r = Math.max(0, Math.min(plot.w, plot.h) / 2 * (outside ? 0.7 : 0.92));
    return { width, height, cx: plot.x + plot.w / 2, cy: plot.y + plot.h / 2, r, inner: r * c.donut, plot, title, legend: reserved ?? overlayLegendRect(c, plot, items) };
  }

  /** Slice under (x, y): between the inner and outer radius and inside the slice's angular span. */
  hitTest(x: number, y: number, l: PieLayout): string | null {
    const dx = x - l.cx, dy = y - l.cy, d = Math.sqrt(dx * dx + dy * dy);
    if (d > l.r || d < l.inner) return null;
    const ang = ((Math.atan2(dy, dx) * 180 / Math.PI + 90) % 360 + 360) % 360;
    for (const s of this.slices()) {
      const start = ((s.start % 360) + 360) % 360;
      if (((ang - start) % 360 + 360) % 360 <= s.end - s.start) return s.id;
    }
    return null;
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
    this.toggleSlice(id);
    return true;
  }

  /** Label text of a slice under the configured mode, or null for "none". */
  labelFor(g: PieSliceGeometry): string | null {
    const c = this.config, s = this.slice(g.id)!;
    switch (c.labels) {
      case "percent": return formatNumber(g.frac * 100, c.decimals) + "%";
      case "value": return formatNumber(g.value, c.decimals);
      case "name": return this.sliceName(s);
      default: return null;
    }
  }

  /** Paints title, slices (the hovered one exploded), labels, centre text, legend and tooltip. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, l = this.layout(width, height);
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    if (l.title && c.title) p.text(c.title, l.title.x + l.title.w / 2, l.title.y + l.title.h / 2, { ...text, size: th.fontSize + 3, weight: "bold", align: "center", baseline: "middle" });
    const slices = this.slices();
    for (const g of slices) {
      const s = this.slice(g.id)!, hot = this.hover === g.id || this.legendHover === g.id;
      const off = hot ? polar(0, 0, l.r * c.hoverExplode, g.mid) : { x: 0, y: 0 };
      p.sector(l.cx + off.x, l.cy + off.y, l.inner, l.r, arcAngle(g.start), arcAngle(g.end), { color: this.sliceColor(s), opacity: hot ? 1 : 0.92 }, { color: th.background, width: 1 });
    }
    if (c.labels !== "none") {
      for (const g of slices) {
        const label = this.labelFor(g);
        if (!label) continue;
        if (c.labelPosition === "inside") {
          if (g.frac < c.minLabelFraction) continue;
          const pt = polar(l.cx, l.cy, (l.inner + l.r) / 2, g.mid);
          p.text(label, pt.x, pt.y, { ...text, color: th.background, weight: "bold", align: "center", baseline: "middle" });
        } else {
          const a = polar(l.cx, l.cy, l.r * 1.02, g.mid), b = polar(l.cx, l.cy, l.r * 1.12, g.mid), right = b.x >= l.cx;
          p.line(a.x, a.y, b.x, b.y, { color: th.axis, width: 1 });
          p.line(b.x, b.y, b.x + (right ? 8 : -8), b.y, { color: th.axis, width: 1 });
          p.text(label, b.x + (right ? 11 : -11), b.y, { ...text, align: right ? "left" : "right", baseline: "middle" });
        }
      }
    }
    const center = c.centerText ?? (c.donut > 0 ? formatNumber(this.total(), c.decimals) : null);
    if (center) p.text(center, l.cx, l.cy, { ...text, size: th.fontSize + 5, weight: "bold", align: "center", baseline: "middle" });
    if (l.legend) drawSimpleLegend(p, c, l.legend, this.legendItems(), this.legendHover);
    if (this.hover) {
      const g = slices.find((x) => x.id === this.hover);
      if (g) { const s = this.slice(g.id)!, pt = polar(l.cx, l.cy, l.r * 0.8, g.mid); drawTooltip(p, c, l.plot, { x: pt.x, y: pt.y, title: this.sliceName(s), lines: [{ name: formatNumber(g.value, c.decimals), value: formatNumber(g.frac * 100, 1) + "%", color: this.sliceColor(s) }] }); }
    }
  }
}
