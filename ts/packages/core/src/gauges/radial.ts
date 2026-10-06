// Mori.SkyScope — Radial gauge: sweep, bands, ticks, needle or filled arc, value readout.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { formatNumber } from "../scales/ticks.js";
import { GAUGE_LIGHT, SmoothedValue, arcAngle, autoDecimals, clamp, polar, scaleTicks, type Band, type GaugeTheme, type ScaleTick } from "./common.js";

/** Visual style of a radial gauge: ring and tick proportions (fractions of the radius), needle style and visibility toggles. */
export interface RadialGaugeStyle {
  /** Fractions of the radius. */
  ringWidth: number; tickLength: number; minorTickLength: number; needleWidth: number; hubRadius: number; labelRadius: number;
  /** "needle" draws a pivoting pointer; "arc" instead fills the ring from min up to the value. */
  needleStyle: "needle" | "arc";
  /** Visibility of tick marks, tick labels, the numeric readout and the coloured bands. */
  showTicks: boolean; showLabels: boolean; showValue: boolean; showBands: boolean;
}
/** Default style; the `style` option merges over it. */
export const RADIAL_STYLE: RadialGaugeStyle = { ringWidth: 0.08, tickLength: 0.12, minorTickLength: 0.06, needleWidth: 0.04, hubRadius: 0.08, labelRadius: 0.68, needleStyle: "needle", showTicks: true, showLabels: true, showValue: true, showBands: true };

/** Configuration of a radial gauge: range, sweep geometry, caption, unit, decimals, bands, tick counts, damping, theme and style. */
export interface RadialGaugeConfig {
  /** Value range of the dial. */
  min: number; max: number;
  /** Degrees, 0 = up, clockwise. Default −135 … +135 (a 270° sweep). */
  startAngle: number; sweep: number;
  /** Caption, unit suffix of the readout and readout decimals (undefined picks `autoDecimals`). */
  label?: string | undefined; unit?: string | undefined; decimals?: number | undefined;
  /** Coloured bands on the ring, approximate major tick count and minor ticks per major interval. */
  bands: Band[]; majorTicks: number; minorPerMajor: number;
  /** Needle damping time constant, seconds. */
  damping: number;
  /** Resolved theme and style. */
  theme: GaugeTheme; style: RadialGaugeStyle;
}
/** Constructor options: every setting optional, theme and style partially overridable. */
export type RadialGaugeOptions = Partial<Omit<RadialGaugeConfig, "theme" | "style">> & { theme?: Partial<GaugeTheme> | undefined; style?: Partial<RadialGaugeStyle> | undefined };

/** Resolves options against the defaults, `GAUGE_LIGHT` and `RADIAL_STYLE`. */
export function radialConfig(o: RadialGaugeOptions = {}): RadialGaugeConfig {
  const { theme, style, ...rest } = o;
  return { min: 0, max: 100, startAngle: -135, sweep: 270, bands: [], majorTicks: 10, minorPerMajor: 5, damping: 0.15, ...rest, theme: { ...GAUGE_LIGHT, ...theme }, style: { ...RADIAL_STYLE, ...style } };
}

/** Centre and radius of the dial in control pixels. */
export interface RadialLayout { cx: number; cy: number; r: number }

/** Analog dial: bands, ticks, labels, needle (or filled arc), value readout. Pinned by `spec/fixtures/gauges.json`. */
export class RadialGauge {
  /** Settings resolved by `radialConfig`. */
  readonly config: RadialGaugeConfig;
  /** Damped value; its `displayed` member is what gets drawn. */
  readonly value: SmoothedValue;
  /** `initial` defaults to `min`. */
  constructor(options: RadialGaugeOptions = {}, initial?: number) {
    this.config = radialConfig(options);
    this.value = new SmoothedValue(initial ?? this.config.min, this.config.damping);
  }
  /** New target value; the needle eases toward it. */
  setValue(v: number): void { this.value.set(v); }
  /** Advances the damping by `dt` seconds and returns the displayed value. */
  step(dt: number): number { return this.value.step(dt); }
  /** True while the value is still converging; keep calling `step` and redrawing until false. */
  get animating(): boolean { return !this.value.settled; }

  /** Dial placement: sweeps of 180° or less sit on the bottom edge as a half dial, wider ones are centred. */
  layout(width: number, height: number): RadialLayout {
    const c = this.config;
    const half = Math.abs(c.sweep) <= 180;
    const r = half ? Math.min(width / 2, height) * 0.92 : Math.min(width, height) / 2 * 0.92;
    return { cx: width / 2, cy: half ? height * 0.9 : height / 2, r };
  }
  /** Gauge angle in degrees (0 = up, clockwise) for a value, clamped to the range. */
  angleFor(v: number): number {
    const c = this.config;
    return c.startAngle + (clamp(v, c.min, c.max) - c.min) / (c.max - c.min) * c.sweep;
  }
  /** Scale marks between min and max per the configured tick counts. */
  ticks(): ScaleTick[] { return scaleTicks(this.config.min, this.config.max, this.config.majorTicks, this.config.minorPerMajor); }
  /** Needle polygon (tip, two base corners, tail) for the displayed value. */
  needle(l: RadialLayout, v = this.value.displayed): number[] {
    const a = this.angleFor(v), w = l.r * this.config.style.needleWidth;
    const tip = polar(l.cx, l.cy, l.r * 0.9, a), tail = polar(l.cx, l.cy, l.r * 0.15, a + 180);
    const left = polar(l.cx, l.cy, w, a - 90), right = polar(l.cx, l.cy, w, a + 90);
    return [tip.x, tip.y, right.x, right.y, tail.x, tail.y, left.x, left.y];
  }
  /** Formats a value with the configured or automatic decimals, without the unit. */
  formatValue(v: number): string { return formatNumber(v, this.config.decimals ?? autoDecimals(this.config.min, this.config.max)); }

  /** Renders face, ring, bands, ticks, labels, needle or arc, readout and caption for the displayed value. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, st = c.style, l = this.layout(width, height);
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    p.circle(l.cx, l.cy, l.r, { color: th.face });
    const a0 = this.angleFor(c.min), a1 = this.angleFor(c.max), ring = l.r * st.ringWidth;
    p.arc(l.cx, l.cy, l.r - ring / 2, arcAngle(a0), arcAngle(a1), { color: th.track, width: ring });
    if (st.showBands) for (const b of c.bands) {
      p.arc(l.cx, l.cy, l.r - ring / 2, arcAngle(this.angleFor(b.from)), arcAngle(this.angleFor(b.to)), { color: b.color, width: ring });
    }
    if (st.needleStyle === "arc") p.arc(l.cx, l.cy, l.r - ring * 1.7, arcAngle(a0), arcAngle(this.angleFor(this.value.displayed)), { color: th.accent, width: ring * 1.2 });
    if (st.showTicks) for (const t of this.ticks()) {
      const a = this.angleFor(t.value), len = l.r * (t.major ? st.tickLength : st.minorTickLength);
      const o = polar(l.cx, l.cy, l.r - ring, a), i = polar(l.cx, l.cy, l.r - ring - len, a);
      p.line(o.x, o.y, i.x, i.y, { color: t.major ? th.tick : th.minorTick, width: t.major ? 2 : 1 });
      if (st.showLabels && t.major && t.label !== null) { const lp = polar(l.cx, l.cy, l.r * st.labelRadius, a); p.text(t.label, lp.x, lp.y, { ...text, align: "center", baseline: "middle" }); }
    }
    if (st.needleStyle === "needle") {
      p.polygon(this.needle(l), { color: th.needle });
      p.circle(l.cx, l.cy, l.r * st.hubRadius, { color: th.hub });
    }
    if (st.showValue) {
      const vy = l.cy + l.r * (Math.abs(c.sweep) <= 180 ? -0.25 : 0.3);
      p.text(this.formatValue(this.value.displayed) + (c.unit ? ` ${c.unit}` : ""), l.cx, vy, { ...text, color: th.value, size: th.fontSize * 1.6, weight: "bold", align: "center", baseline: "middle" });
    }
    if (c.label) p.text(c.label, l.cx, l.cy + l.r * (Math.abs(c.sweep) <= 180 ? -0.55 : 0.52), { ...text, color: th.mutedText, align: "center", baseline: "middle" });
  }
}
