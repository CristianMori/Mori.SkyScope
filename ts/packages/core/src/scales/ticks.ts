// Mori.SkyScope — Tick generation.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * Tick generation. Mirrors `Mori.SkyScope.Core.Scales.Ticks`; pinned by `spec/fixtures/scales.json`.
 * Everything here is written so both languages produce bit-identical doubles: powers of ten are built by
 * repeated multiplication (never Math.pow), and sub-unit ticks are `k / inv` (nearest-double decimals).
 */
export interface TickSpec {
  /** Distance between ticks (0 when the domain is degenerate). */
  step: number;
  /** When step < 1: ticks are k / inv. 0 otherwise. */
  inv: number;
  /** Decimals needed to print a tick exactly. */
  decimals: number;
}

/** 10 to the power `p` by repeated multiplication, so both cores get the same double. */
export function pow10(p: number): number {
  let r = 1;
  const n = p < 0 ? -p : p;
  for (let i = 0; i < n; i++) r *= 10;
  return p < 0 ? 1 / r : r;
}

const E10 = Math.sqrt(50), E5 = Math.sqrt(10), E2 = Math.sqrt(2);

/** Identical in both languages (Math.round and Math.Round disagree on negative halves). */
const roundHalfUp = (x: number): number => Math.floor(x + 0.5);

/** d3-style 1/2/5 tick step for about `count` ticks. */
export function tickSpec(d0: number, d1: number, count = 10): TickSpec {
  const span = Math.abs(d1 - d0);
  if (!(span > 0) || !(count > 0) || !Number.isFinite(span)) return { step: 0, inv: 0, decimals: 0 };
  const raw = span / count;
  let power = Math.floor(Math.log10(raw));
  const error = raw / pow10(power);
  let factor = error >= E10 ? 10 : error >= E5 ? 5 : error >= E2 ? 2 : 1;
  if (factor === 10) { factor = 1; power += 1; }
  if (power >= 0) return { step: factor * pow10(power), inv: 0, decimals: 0 };
  const inv = pow10(-power) / factor;
  return { step: 1 / inv, inv, decimals: -power };
}

/** Ticks at multiples of the spec inside [min(d0,d1), max(d0,d1)], ascending. */
export function ticksFromSpec(d0: number, d1: number, spec: TickSpec): number[] {
  const lo = Math.min(d0, d1), hi = Math.max(d0, d1);
  const out: number[] = [];
  if (spec.step === 0) return out;
  if (spec.inv > 0) {
    let i0 = roundHalfUp(lo * spec.inv), i1 = roundHalfUp(hi * spec.inv);
    if (i0 / spec.inv < lo) i0++;
    if (i1 / spec.inv > hi) i1--;
    for (let i = i0; i <= i1; i++) out.push(i / spec.inv);
  } else {
    let i0 = roundHalfUp(lo / spec.step), i1 = roundHalfUp(hi / spec.step);
    if (i0 * spec.step < lo) i0++;
    if (i1 * spec.step > hi) i1--;
    for (let i = i0; i <= i1; i++) out.push(i * spec.step);
  }
  return out;
}

/** 1/2/5-stepped ticks inside the domain for about `count` ticks, ascending. */
export function linearTicks(d0: number, d1: number, count = 10): number[] {
  return ticksFromSpec(d0, d1, tickSpec(d0, d1, count));
}

/** Extend the domain outwards to tick multiples (iterates like d3 so the step is stable). */
export function niceLinearDomain(d0: number, d1: number, count = 10): [number, number] {
  let lo = Math.min(d0, d1), hi = Math.max(d0, d1);
  let prev = 0;
  for (let i = 0; i < 10; i++) {
    const s = tickSpec(lo, hi, count);
    if (s.step === 0 || s.step === prev) break;
    prev = s.step;
    if (s.inv > 0) { lo = Math.floor(lo * s.inv) / s.inv; hi = Math.ceil(hi * s.inv) / s.inv; }
    else { lo = Math.floor(lo / s.step) * s.step; hi = Math.ceil(hi / s.step) * s.step; }
  }
  return d0 <= d1 ? [lo, hi] : [hi, lo];
}

/** Fixed-point formatting that never prints a negative zero ("-0.0" becomes "0.0"). */
export function formatNumber(v: number, decimals: number): string {
  const s = v.toFixed(decimals);
  return s === "-0" || /^-0\.0*$/.test(s) ? s.slice(1) : s;
}

/** Ticks for a base-10 log axis: every mantissa within a decade, 1/2/5 across a few decades, powers beyond. */
export function logTicks(d0: number, d1: number, count = 10): number[] {
  const lo = Math.min(d0, d1), hi = Math.max(d0, d1);
  if (!(lo > 0) || !(hi > 0) || !Number.isFinite(hi)) return [];
  const l0 = Math.log10(lo), l1 = Math.log10(hi);
  const p0 = Math.floor(l0), p1 = Math.ceil(l1);
  const span = l1 - l0;
  const mantissas = span <= 1 ? [1, 2, 3, 4, 5, 6, 7, 8, 9] : span <= 3 ? [1, 2, 5] : [1];
  let out: number[] = [];
  for (let p = p0; p <= p1; p++) {
    for (const m of mantissas) {
      const v = p >= 0 ? m * pow10(p) : m / pow10(-p);
      if (v >= lo && v <= hi) out.push(v);
    }
  }
  if (mantissas.length === 1 && out.length > count) {
    const every = Math.ceil(out.length / count);
    out = out.filter((_, i) => i % every === 0);
  }
  return out;
}

/** Decimals needed to print a log tick exactly: 0 at or above 1, otherwise enough to reach the leading digit. */
export function logTickDecimals(v: number): number {
  if (!(v > 0) || v >= 1) return 0;
  return -Math.floor(Math.log10(v));
}

/** Seconds. Chosen so multiples from epoch land on natural boundaries (15 s, 15 min, even hours, days…). */
export const TIME_STEPS = [0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600, 43200, 86400, 172800, 604800];

/** Smallest `TIME_STEPS` entry that yields at most about `count` ticks; beyond a week falls back to 1/2/5 multiples of a day. */
export function timeTickSpec(d0: number, d1: number, count = 10): TickSpec {
  const span = Math.abs(d1 - d0);
  if (!(span > 0) || !(count > 0) || !Number.isFinite(span)) return { step: 0, inv: 0, decimals: 0 };
  const target = span / count;
  for (const step of TIME_STEPS) {
    if (step >= target) {
      if (step >= 1) return { step, inv: 0, decimals: 0 };
      const inv = roundHalfUp(1 / step);
      return { step, inv, decimals: step < 0.01 ? 3 : step < 0.1 ? 2 : 1 };
    }
  }
  const days = tickSpec(d0 / 86400, d1 / 86400, count);
  return { step: days.step * 86400, inv: 0, decimals: 0 };
}

/** Ticks at multiples of the `timeTickSpec` step inside the domain, ascending. */
export function timeTicks(d0: number, d1: number, count = 10): number[] {
  return ticksFromSpec(d0, d1, timeTickSpec(d0, d1, count));
}
