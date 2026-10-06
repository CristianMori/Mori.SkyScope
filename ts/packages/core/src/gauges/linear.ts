// Mori.SkyScope — Bar / thermometer gauge with a scale beside the track.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import type { Rect } from "../scene/geometry.js";
import { formatNumber } from "../scales/ticks.js";
import { GAUGE_LIGHT, SmoothedValue, autoDecimals, clamp, scaleTicks, type Band, type GaugeTheme, type ScaleTick } from "./common.js";

/** Geometry in pixels (`barThickness` of the track, `tickLength`/`minorTickLength` of the marks, `pointerSize` of the triangle) and visibility switches; `fill` paints the track up to the value. */
export interface LinearGaugeStyle { barThickness: number; tickLength: number; minorTickLength: number; pointerSize: number; fill: boolean; showTicks: boolean; showLabels: boolean; showValue: boolean; showBands: boolean }
/** Default style; the `style` option merges over it. */
export const LINEAR_STYLE: LinearGaugeStyle = { barThickness: 18, tickLength: 8, minorTickLength: 4, pointerSize: 8, fill: true, showTicks: true, showLabels: true, showValue: true, showBands: true };

/** Configuration of a linear gauge: range, orientation, caption, unit, decimals, bands, tick counts, damping, theme and style. */
export interface LinearGaugeConfig {
  /** Value range and bar direction: "horizontal" fills left to right, "vertical" bottom to top. */
  min: number; max: number; orientation: "horizontal" | "vertical";
  /** Caption, unit suffix of the readout and readout decimals (undefined picks `autoDecimals`). */
  label?: string | undefined; unit?: string | undefined; decimals?: number | undefined;
  /** Coloured bands, approximate major tick count, minor ticks per major interval and damping time constant in seconds. */
  bands: Band[]; majorTicks: number; minorPerMajor: number; damping: number;
  /** Resolved theme and style. */
  theme: GaugeTheme; style: LinearGaugeStyle;
}
/** Constructor options: every setting optional, theme and style partially overridable. */
export type LinearGaugeOptions = Partial<Omit<LinearGaugeConfig, "theme" | "style">> & { theme?: Partial<GaugeTheme> | undefined; style?: Partial<LinearGaugeStyle> | undefined };
/** Resolves options against the defaults, `GAUGE_LIGHT` and `LINEAR_STYLE`. */
export function linearConfig(o: LinearGaugeOptions = {}): LinearGaugeConfig {
  const { theme, style, ...rest } = o;
  return { min: 0, max: 100, orientation: "horizontal", bands: [], majorTicks: 5, minorPerMajor: 5, damping: 0.15, ...rest, theme: { ...GAUGE_LIGHT, ...theme }, style: { ...LINEAR_STYLE, ...style } };
}

/** Track rectangle in control pixels and whether it runs horizontally. */
export interface LinearLayout { track: Rect; horizontal: boolean }

/** Bar / thermometer gauge with a scale beside the track. Pinned by `spec/fixtures/gauges.json`. */
export class LinearGauge {
  /** Settings resolved by `linearConfig`. */
  readonly config: LinearGaugeConfig;
  /** Damped value; its `displayed` member is what gets drawn. */
  readonly value: SmoothedValue;
  /** `initial` defaults to `min`. */
  constructor(options: LinearGaugeOptions = {}, initial?: number) { this.config = linearConfig(options); this.value = new SmoothedValue(initial ?? this.config.min, this.config.damping); }
  /** New target value; the pointer eases toward it. */
  setValue(v: number): void { this.value.set(v); }
  /** Advances the damping by `dt` seconds and returns the displayed value. */
  step(dt: number): number { return this.value.step(dt); }
  /** True while the value is still converging; keep calling `step` and redrawing until false. */
  get animating(): boolean { return !this.value.settled; }

  /** Track placement for a control size, leaving room for labels, ticks, readout and caption. */
  layout(width: number, height: number): LinearLayout {
    const c = this.config, st = c.style, pad = 12, labels = st.showLabels ? c.theme.fontSize * 2.2 : 0, ticks = st.showTicks ? st.tickLength + 2 : 0;
    const valueRoom = st.showValue ? c.theme.fontSize * 1.8 : 0;
    if (c.orientation === "horizontal") return { horizontal: true, track: { x: pad + labels / 2, y: (height - valueRoom - st.barThickness) / 2, w: width - 2 * pad - labels, h: st.barThickness } };
    const captionRoom = c.label ? c.theme.fontSize + 8 : 0;
    return { horizontal: false, track: { x: pad + labels + ticks, y: pad + valueRoom, w: st.barThickness, h: height - 2 * pad - valueRoom - captionRoom } };
  }
  /** Pixel position along the track for a value (left→right, or bottom→top). */
  posFor(l: LinearLayout, v: number): number {
    const c = this.config, t = (clamp(v, c.min, c.max) - c.min) / (c.max - c.min);
    return l.horizontal ? l.track.x + t * l.track.w : l.track.y + l.track.h - t * l.track.h;
  }
  /** Scale marks between min and max per the configured tick counts. */
  ticks(): ScaleTick[] { return scaleTicks(this.config.min, this.config.max, this.config.majorTicks, this.config.minorPerMajor); }
  /** Formats a value with the configured or automatic decimals, without the unit. */
  formatValue(v: number): string { return formatNumber(v, this.config.decimals ?? autoDecimals(this.config.min, this.config.max)); }

  /** Renders track, bands, fill, ticks, pointer, readout and caption for the displayed value. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, st = c.style, l = this.layout(width, height), t = l.track, v = this.value.displayed;
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    p.rect(t.x, t.y, t.w, t.h, { color: th.track }, undefined, 3);
    if (st.showBands) for (const b of c.bands) {
      const a = this.posFor(l, b.from), z = this.posFor(l, b.to);
      if (l.horizontal) p.rect(Math.min(a, z), t.y, Math.abs(z - a), t.h, { color: b.color, opacity: 0.35 });
      else p.rect(t.x, Math.min(a, z), t.w, Math.abs(z - a), { color: b.color, opacity: 0.35 });
    }
    const pv = this.posFor(l, v), p0 = this.posFor(l, c.min);
    if (st.fill) {
      if (l.horizontal) p.rect(p0, t.y, pv - p0, t.h, { color: th.accent }, undefined, 3);
      else p.rect(t.x, pv, t.w, p0 - pv, { color: th.accent }, undefined, 3);
    }
    if (st.showTicks) for (const k of this.ticks()) {
      const pos = this.posFor(l, k.value), len = k.major ? st.tickLength : st.minorTickLength, stroke = { color: k.major ? th.tick : th.minorTick, width: k.major ? 2 : 1 };
      if (l.horizontal) { p.line(pos, t.y + t.h + 2, pos, t.y + t.h + 2 + len, stroke); if (st.showLabels && k.label !== null) p.text(k.label, pos, t.y + t.h + 4 + st.tickLength, { ...text, align: "center", baseline: "top" }); }
      else { p.line(t.x - 2, pos, t.x - 2 - len, pos, stroke); if (st.showLabels && k.label !== null) p.text(k.label, t.x - 4 - st.tickLength, pos, { ...text, align: "right", baseline: "middle" }); }
    }
    const s = st.pointerSize;
    if (l.horizontal) p.polygon([pv, t.y - 2, pv - s, t.y - 2 - s, pv + s, t.y - 2 - s], { color: th.needle });
    else p.polygon([t.x + t.w + 2, pv, t.x + t.w + 2 + s, pv - s, t.x + t.w + 2 + s, pv + s], { color: th.needle });
    if (st.showValue) {
      const label = this.formatValue(v) + (c.unit ? ` ${c.unit}` : "");
      if (l.horizontal) p.text(label, t.x + t.w, height - 6, { ...text, color: th.value, size: th.fontSize * 1.4, weight: "bold", align: "right", baseline: "bottom" });
      else p.text(label, t.x + t.w / 2, 6, { ...text, color: th.value, size: th.fontSize * 1.4, weight: "bold", align: "center", baseline: "top" });
    }
    if (c.label) { if (l.horizontal) p.text(c.label, t.x, height - 6, { ...text, color: th.mutedText, baseline: "bottom" }); else p.text(c.label, t.x + t.w / 2, height - 4, { ...text, color: th.mutedText, align: "center", baseline: "bottom" }); }
  }
}
