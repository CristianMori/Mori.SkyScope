// Mori.SkyScope — Linear, logarithmic and time scales mapping a data domain onto a pixel range, with ticks and labels.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { tickSpec, ticksFromSpec, niceLinearDomain, formatNumber, logTicks, logTickDecimals, timeTickSpec, type TickSpec } from "./ticks.js";
import { formatTimeTick, type TimeFormat } from "./format.js";

/** Inclusive [start, end] pair in data or pixel units; start may exceed end for an inverted axis. */
export type Domain = readonly [number, number];
/** Mapping family: "linear", "log" (base 10, positive domains only) or "time" (unix seconds). */
export type ScaleKind = "linear" | "log" | "time";

/** Maps a data domain onto a pixel range. Immutable: `withDomain`/`withRange` return new scales. */
export interface Scale {
  /** Which mapping family this is. */
  readonly kind: ScaleKind;
  /** Data extent. */
  readonly domain: Domain;
  /** Pixel extent the domain maps onto. */
  readonly range: Domain;
  /** Data value → pixel; extrapolates outside the domain. */
  scale(x: number): number;
  /** Pixel → data value; the inverse of `scale`. */
  invert(y: number): number;
  /** Tick values inside the domain, ascending, about `count` of them (default 10). */
  ticks(count?: number): number[];
  /** Step, inverse and decimals behind `ticks(count)`. */
  tickSpec(count?: number): TickSpec;
  /** Label for a tick value, precise to the tick step for `count` ticks. */
  format(v: number, count?: number): string;
  /** Copy whose domain is widened to tick multiples; log and time scales return themselves. */
  nice(count?: number): Scale;
  /** Copy with a new domain and the same range. */
  withDomain(domain: Domain): Scale;
  /** Copy with a new range and the same domain. */
  withRange(range: Domain): Scale;
}

abstract class BaseScale implements Scale {
  abstract readonly kind: ScaleKind;
  constructor(readonly domain: Domain, readonly range: Domain) {}
  protected abstract fwd(x: number): number;
  protected abstract inv(u: number): number;
  protected abstract make(domain: Domain, range: Domain): BaseScale;

  scale(x: number): number {
    const [d0, d1] = this.domain, [r0, r1] = this.range;
    const u0 = this.fwd(d0), u1 = this.fwd(d1);
    if (u1 === u0) return r0;
    return r0 + (this.fwd(x) - u0) / (u1 - u0) * (r1 - r0);
  }
  invert(y: number): number {
    const [d0, d1] = this.domain, [r0, r1] = this.range;
    const u0 = this.fwd(d0), u1 = this.fwd(d1);
    if (r1 === r0) return d0;
    return this.inv(u0 + (y - r0) / (r1 - r0) * (u1 - u0));
  }
  abstract ticks(count?: number): number[];
  abstract tickSpec(count?: number): TickSpec;
  abstract format(v: number, count?: number): string;
  nice(_count?: number): Scale { return this; }
  withDomain(domain: Domain): Scale { return this.make(domain, this.range); }
  withRange(range: Domain): Scale { return this.make(this.domain, range); }
}

/** Identity mapping with 1/2/5-stepped ticks. */
export class LinearScale extends BaseScale {
  readonly kind = "linear" as const;
  protected fwd(x: number): number { return x; }
  protected inv(u: number): number { return u; }
  protected make(domain: Domain, range: Domain): BaseScale { return new LinearScale(domain, range); }
  tickSpec(count = 10): TickSpec { return tickSpec(this.domain[0], this.domain[1], count); }
  ticks(count = 10): number[] { return ticksFromSpec(this.domain[0], this.domain[1], this.tickSpec(count)); }
  format(v: number, count = 10): string { return formatNumber(v, this.tickSpec(count).decimals); }
  /** Widens the domain outwards to tick multiples for about `count` ticks. */
  override nice(count = 10): Scale { return new LinearScale(niceLinearDomain(this.domain[0], this.domain[1], count), this.range); }
}

/** Base-10 logarithmic mapping; domain values must be positive. */
export class LogScale extends BaseScale {
  readonly kind = "log" as const;
  protected fwd(x: number): number { return Math.log10(x); }
  protected inv(u: number): number { return 10 ** u; }
  protected make(domain: Domain, range: Domain): BaseScale { return new LogScale(domain, range); }
  /** Log ticks have no uniform step, so this is always the zero spec. */
  tickSpec(_count = 10): TickSpec { return { step: 0, inv: 0, decimals: 0 }; }
  ticks(count = 10): number[] { return logTicks(this.domain[0], this.domain[1], count); }
  /** Labels with just enough decimals for values below 1. */
  format(v: number): string { return formatNumber(v, logTickDecimals(v)); }
}

/** Linear mapping over unix seconds with time-aware tick steps and labels. */
export class TimeScale extends BaseScale {
  readonly kind = "time" as const;
  /** `origin`: ticks are placed and labelled relative to it (relative mode: the live edge, so labels read −30 … 0). */
  constructor(domain: Domain, range: Domain, readonly mode: TimeFormat = "utc", readonly origin = 0) { super(domain, range); }
  protected fwd(x: number): number { return x; }
  protected inv(u: number): number { return u; }
  protected make(domain: Domain, range: Domain): BaseScale { return new TimeScale(domain, range, this.mode, this.origin); }
  tickSpec(count = 10): TickSpec { return timeTickSpec(this.domain[0], this.domain[1], count); }
  /** Tick times at multiples of the step counted from `origin`. */
  ticks(count = 10): number[] { return ticksFromSpec(this.domain[0] - this.origin, this.domain[1] - this.origin, this.tickSpec(count)).map((t) => t + this.origin); }
  /** Label relative to `origin`; the axis extent picks the shape so every label on the axis matches. */
  format(v: number, count = 10): string { const s = this.tickSpec(count); return formatTimeTick(v - this.origin, s.step, this.mode, s.decimals, Math.abs(this.domain[1] - this.domain[0])); }
}

/** Factory by kind; `mode` applies to time scales only (origin 0). */
export function createScale(kind: ScaleKind, domain: Domain, range: Domain, mode: TimeFormat = "utc"): Scale {
  switch (kind) {
    case "linear": return new LinearScale(domain, range);
    case "log": return new LogScale(domain, range);
    case "time": return new TimeScale(domain, range, mode);
  }
}
