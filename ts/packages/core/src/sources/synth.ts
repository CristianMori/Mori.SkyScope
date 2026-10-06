// Mori.SkyScope — Deterministic waveform generator, identical in both cores (pinned by spec/fixtures/synthetic.json), used by SyntheticSource for demos and…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * Deterministic waveform generator, identical in both cores (pinned by `spec/fixtures/synthetic.json`),
 * used by SyntheticSource for demos and throughput benches. Noise is a stateless integer hash of
 * (seed, sample index) so any batch can be regenerated independently.
 */
/** Waveform shapes; "ramp" is `t` itself and "noise" is zero plus the noise term. */
export type Waveform = "sine" | "square" | "triangle" | "sawtooth" | "ramp" | "noise";

/** Parameters of one generated signal: `offset + amplitude × wave(frequency, phase) + noise`. */
export interface SynthSpec {
  /** Shape of the base wave. */
  waveform: Waveform;
  /** Hz. */
  frequency?: number | undefined;
  /** Peak value of the base wave; default 1. */
  amplitude?: number | undefined;
  /** Constant added to the wave; default 0. */
  offset?: number | undefined;
  /** Radians. */
  phase?: number | undefined;
  /** Amplitude of added uniform noise in [-noise, +noise]. */
  noise?: number | undefined;
  /** Noise seed; default 0. */
  seed?: number | undefined;
}

/** Uniform [0, 1) from a 32-bit hash of (seed, index). Uses only uint32 ops so C# matches bit-for-bit. */
export function hash01(seed: number, index: number): number {
  let u = (Math.imul((seed ^ 0x9e3779b9) >>> 0, 0x85ebca6b) ^ Math.imul(index >>> 0, 0xc2b2ae35)) >>> 0;
  u ^= u >>> 16; u = Math.imul(u, 0x7feb352d) >>> 0;
  u ^= u >>> 15; u = Math.imul(u, 0x846ca68b) >>> 0;
  u ^= u >>> 16; u >>>= 0;
  return u / 4294967296;
}

const frac = (x: number): number => x - Math.floor(x);

/**
 * Value of the signal at time `t` (seconds) for absolute sample `index` (used by the noise only), in double
 * precision.
 */
export function synthValue(spec: SynthSpec, t: number, index: number): number {
  const f = spec.frequency ?? 1, a = spec.amplitude ?? 1, o = spec.offset ?? 0, ph = spec.phase ?? 0;
  const x = f * t + ph / (2 * Math.PI);
  let base: number;
  switch (spec.waveform) {
    case "sine": base = Math.sin(2 * Math.PI * f * t + ph); break;
    case "square": base = frac(x) < 0.5 ? 1 : -1; break;
    case "triangle": base = 4 * Math.abs(frac(x) - 0.5) - 1; break;
    case "sawtooth": base = 2 * frac(x) - 1; break;
    case "ramp": base = t; break;
    case "noise": base = 0; break;
  }
  let v = o + a * base;
  const n = spec.noise ?? 0;
  if (n > 0) v += n * (hash01(spec.seed ?? 0, index) * 2 - 1);
  return v;
}

/** `count` samples starting at `tStart` every `dt`, with absolute sample indices from `startIndex` (for noise). */
export function synthesize(spec: SynthSpec, tStart: number, dt: number, count: number, startIndex = 0): Float32Array {
  const out = new Float32Array(count);
  for (let k = 0; k < count; k++) out[k] = Math.fround(synthValue(spec, tStart + k * dt, startIndex + k));
  return out;
}
