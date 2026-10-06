// Mori.SkyScope — Shared gauge building blocks.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { tickSpec, ticksFromSpec, formatNumber } from "../scales/ticks.js";

/**
 * Shared gauge building blocks. Mirrors `Mori.SkyScope.Core.Gauges.GaugeCommon`; pinned by
 * `spec/fixtures/gauges.json`. Angles: degrees, 0 = 12 o'clock, positive clockwise (screen).
 */
export interface GaugeTheme {
  /** Colours as CSS strings: `background` fills the control, `face` the dial, `track` the ring or bar, `tick`/`minorTick` the marks, `text` the labels, `mutedText` the captions. */
  background: string; face: string; track: string; tick: string; minorTick: string; text: string; mutedText: string;
  /** `needle`, `hub` and `value` colour the pointer, its pivot and the readout; `ledOff` is an unlit lamp; `accent` fills arcs, sliders and switches; `fontFamily`/`fontSize` (CSS px) apply to every label. */
  needle: string; hub: string; value: string; ledOff: string; accent: string; fontFamily: string; fontSize: number;
}

/** Default theme for light backgrounds; every gauge merges its `theme` option over it. */
export const GAUGE_LIGHT: GaugeTheme = {
  background: "#ffffff", face: "#f8fafc", track: "#e2e8f0", tick: "#334155", minorTick: "#94a3b8", text: "#0f172a", mutedText: "#64748b",
  needle: "#dc2626", hub: "#334155", value: "#0f172a", ledOff: "#e2e8f0", accent: "#2563eb",
  fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", fontSize: 12,
};
/** Theme for dark backgrounds, derived from `GAUGE_LIGHT`. */
export const GAUGE_DARK: GaugeTheme = {
  ...GAUGE_LIGHT,
  background: "#0f172a", face: "#1e293b", track: "#334155", tick: "#e2e8f0", minorTick: "#64748b", text: "#f8fafc", mutedText: "#94a3b8",
  needle: "#f87171", hub: "#e2e8f0", value: "#f8fafc", ledOff: "#1e293b", accent: "#60a5fa",
};

/** `from`/`to` are values on the gauge scale, in either order; `color` is any CSS colour; `label` is informational and not drawn by the built-in gauges. */
/** A coloured range on a scale (IOComp-style "sections"). */
export interface Band { from: number; to: number; color: string; label?: string | undefined }

/** Limits `v` to [lo, hi]. */
export const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));
/** Linear interpolation from `a` to `b` by `t` (0 → a, 1 → b), not clamped. */
export const lerp = (a: number, b: number, t: number): number => a + (b - a) * t;
/** Degrees to radians. */
export const degToRad = (d: number): number => d * Math.PI / 180;
/** Screen-space point on a circle for a gauge angle (0 = up, clockwise). */
export function polar(cx: number, cy: number, r: number, angleDeg: number): { x: number; y: number } {
  const a = degToRad(angleDeg - 90);
  return { x: cx + r * Math.cos(a), y: cy + r * Math.sin(a) };
}
/** Gauge angle (0 = up, clockwise) → Painter arc angle (radians from +x, clockwise). */
export const arcAngle = (angleDeg: number): number => degToRad(angleDeg - 90);

/**
 * Exponential smoothing toward a target (needle damping). `tau` seconds to close 63 % of the gap;
 * 0 disables. `wrap` (e.g. 360) makes the shortest path across the seam the one taken.
 */
export class SmoothedValue {
  /** Value most recently set; the display converges toward it. */
  target: number;
  /** Current animated value; read this when drawing. */
  displayed: number;
  /** `initial` seeds both target and display; `tau` in seconds; `wrap` is the modulus for circular quantities (null = linear). */
  constructor(initial = 0, public tau = 0.15, public wrap: number | null = null) { this.target = initial; this.displayed = initial; }
  /** Sets the target; with damping disabled the display jumps immediately. */
  set(v: number): void { this.target = v; if (this.tau <= 0) this.displayed = v; }
  /** Advance by `dt` seconds; returns the displayed value. */
  step(dt: number): number {
    if (this.tau <= 0 || dt <= 0) { this.displayed = this.target; return this.displayed; }
    let delta = this.target - this.displayed;
    if (this.wrap !== null) { const w = this.wrap; delta = ((delta % w) + w * 1.5) % w - w / 2; }
    const k = 1 - Math.exp(-dt / this.tau);
    this.displayed += delta * k;
    if (this.wrap !== null) this.displayed = ((this.displayed % this.wrap) + this.wrap) % this.wrap;
    if (Math.abs(delta) * (1 - k) < 1e-4) this.displayed = this.wrap !== null ? ((this.target % this.wrap) + this.wrap) % this.wrap : this.target;
    return this.displayed;
  }
  /** True once the display has reached the target, i.e. further `step` calls change nothing. */
  get settled(): boolean { return this.displayed === this.target; }
}

/** One mark on a gauge scale, in scale units. Major ticks carry a formatted `label`; minor ticks have `label` null. */
export interface ScaleTick { value: number; major: boolean; label: string | null }

/** Major ticks at 1-2-5 steps for about `majorCount`, with `minorPerMajor − 1` minors between. */
export function scaleTicks(min: number, max: number, majorCount = 10, minorPerMajor = 5, decimals?: number): ScaleTick[] {
  const spec = tickSpec(min, max, majorCount);
  const majors = ticksFromSpec(min, max, spec);
  const dec = decimals ?? spec.decimals;
  const out: ScaleTick[] = [];
  if (spec.step === 0 || majors.length === 0) return out;
  const minorStep = spec.step / Math.max(1, minorPerMajor);
  const first = majors[0]!;
  // minors before the first major (down to min) and after each major
  for (let k = 1; k < minorPerMajor; k++) { const v = first - k * minorStep; if (v >= min - 1e-9) out.unshift({ value: v, major: false, label: null }); }
  for (let i = 0; i < majors.length; i++) {
    const mv = majors[i]!;
    out.push({ value: mv, major: true, label: formatNumber(mv, dec) });
    for (let k = 1; k < minorPerMajor; k++) { const v = mv + k * minorStep; if (v <= max + 1e-9 && (i + 1 >= majors.length || v < majors[i + 1]! - 1e-9)) out.push({ value: v, major: false, label: null }); }
  }
  return out;
}

/** Decimals that make sense for a range when the caller did not say. */
export function autoDecimals(min: number, max: number): number {
  const span = Math.abs(max - min);
  return span >= 100 ? 0 : span >= 10 ? 1 : span >= 1 ? 2 : 3;
}
