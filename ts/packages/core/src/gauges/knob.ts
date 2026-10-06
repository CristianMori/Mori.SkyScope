// Mori.SkyScope — Rotary knob input: the value follows the pointer's angle about the centre, with steps and limits.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { formatNumber } from "../scales/ticks.js";
import { GAUGE_LIGHT, arcAngle, autoDecimals, clamp, polar, scaleTicks, type GaugeTheme } from "./common.js";

/** Snap to `step` multiples (from `min`) and clamp; `step` 0 = continuous. */
export function snapValue(v: number, min: number, max: number, step: number): number {
  const c = clamp(v, min, max);
  return step > 0 ? clamp(min + Math.round((c - min) / step) * step, min, max) : c;
}

/** Resolved settings. `step` 0 means continuous; `startAngle` and `sweep` are degrees (0 = up, clockwise), default −135 and 270; `decimals` undefined picks `autoDecimals`. */
export interface KnobConfig { min: number; max: number; step: number; startAngle: number; sweep: number; label?: string | undefined; unit?: string | undefined; decimals?: number | undefined; theme: GaugeTheme }
/** Constructor options: every setting optional, theme partially overridable. */
export type KnobOptions = Partial<Omit<KnobConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };
/** Centre and radius of the dial in control pixels. */
export interface KnobLayout { cx: number; cy: number; r: number }

/** Rotary input. Drag anywhere on the dial: the value follows the pointer's angle about the centre. */
export class Knob {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: KnobConfig;
  /** Current value, always snapped and clamped to the configured range. */
  value: number;
  /** True between `pointerDown` and `pointerUp`. */
  dragging = false;
  /** Called with the new value whenever it changes, by pointer, `nudge` or `setValue`. */
  onChange: ((v: number) => void) | null = null;
  /** `initial` defaults to `min`. */
  constructor(o: KnobOptions = {}, initial?: number) {
    const { theme, ...rest } = o;
    this.config = { min: 0, max: 100, step: 0, startAngle: -135, sweep: 270, ...rest, theme: { ...GAUGE_LIGHT, ...theme } };
    this.value = initial ?? this.config.min;
  }
  /** Dial centre and radius for a control size (80 % of the half-extent, leaving room for the outer arc). */
  layout(width: number, height: number): KnobLayout { return { cx: width / 2, cy: height / 2, r: Math.min(width, height) / 2 * 0.8 }; }
  /** Gauge angle in degrees for a value, clamped to the sweep. */
  angleFor(v: number): number { const c = this.config; return c.startAngle + (clamp(v, c.min, c.max) - c.min) / (c.max - c.min) * c.sweep; }
  /** Value for a pointer position: its angle about the centre, clamped to the sweep; the dead zone below snaps to the nearer end. */
  valueFromPoint(l: KnobLayout, x: number, y: number): number {
    const c = this.config;
    const a = Math.atan2(x - l.cx, -(y - l.cy)) * 180 / Math.PI;
    let rel = ((a - c.startAngle) % 360 + 360) % 360;                 // 0 = start of sweep, growing clockwise
    if (rel > c.sweep) rel = rel - c.sweep > (360 - c.sweep) / 2 ? 0 : c.sweep;   // dead zone: nearer end wins
    return snapValue(c.min + rel / c.sweep * (c.max - c.min), c.min, c.max, c.step);
  }
  private update(v: number): void { if (v !== this.value) { this.value = v; this.onChange?.(v); } }
  /** Programmatic set (snapped and clamped). */
  setValue(v: number): void { this.update(snapValue(v, this.config.min, this.config.max, this.config.step)); }
  /** Starts a drag and jumps the value to the pointer; coordinates are control pixels. */
  pointerDown(l: KnobLayout, x: number, y: number): void { this.dragging = true; this.update(this.valueFromPoint(l, x, y)); }
  /** Updates the value while dragging; ignored otherwise. */
  pointerMove(l: KnobLayout, x: number, y: number): void { if (this.dragging) this.update(this.valueFromPoint(l, x, y)); }
  /** Ends the drag. */
  pointerUp(): void { this.dragging = false; }
  /** Wheel or arrow keys: one step (or 1 % of range) per notch. */
  nudge(direction: number): void {
    const c = this.config, s = c.step > 0 ? c.step : (c.max - c.min) / 100;
    this.update(snapValue(this.value + Math.sign(direction) * s, c.min, c.max, c.step));
  }
  /** Renders the arc, ticks, dial, pointer and readout for the current value. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, l = this.layout(width, height);
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    p.arc(l.cx, l.cy, l.r * 1.12, arcAngle(c.startAngle), arcAngle(c.startAngle + c.sweep), { color: th.track, width: l.r * 0.1 });
    p.arc(l.cx, l.cy, l.r * 1.12, arcAngle(c.startAngle), arcAngle(this.angleFor(this.value)), { color: th.accent, width: l.r * 0.1 });
    for (const t of scaleTicks(c.min, c.max, 5, 1, c.decimals)) {
      const o = polar(l.cx, l.cy, l.r * 1.22, this.angleFor(t.value)), i = polar(l.cx, l.cy, l.r * 1.18, this.angleFor(t.value));
      p.line(o.x, o.y, i.x, i.y, { color: th.tick, width: 1 });
    }
    p.circle(l.cx, l.cy, l.r, { color: th.face }, { color: th.track, width: 2 });
    const g0 = polar(l.cx, l.cy, l.r * 0.55, this.angleFor(this.value)), g1 = polar(l.cx, l.cy, l.r * 0.92, this.angleFor(this.value));
    p.line(g0.x, g0.y, g1.x, g1.y, { color: th.needle, width: Math.max(2, l.r * 0.08), cap: "round" });
    p.text(formatNumber(this.value, c.decimals ?? autoDecimals(c.min, c.max)) + (c.unit ? ` ${c.unit}` : ""), l.cx, l.cy + l.r * 0.05, { ...text, color: th.value, size: th.fontSize * 1.2, weight: "bold", align: "center", baseline: "middle" });
    if (c.label) p.text(c.label, l.cx, height - 2, { ...text, color: th.mutedText, align: "center", baseline: "bottom" });
  }
}
