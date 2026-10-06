// Mori.SkyScope — Plain-JSON shape of a frame — used by golden files, debugging tools and tests.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { FrameChannel, SkyScopeFrame } from "./frame.js";

/** Plain-JSON shape of a frame — used by golden files, debugging tools and tests. */
export type FrameChannelJson =
  /** Explicit time per sample. */
  | { id: number; encoding: "timestamped"; times: number[]; values: number[] }
  /** Evenly spaced: time i = tStart + i·dt. */
  | { id: number; encoding: "regular"; tStart: number; dt: number; values: number[] }
  /** Evenly spaced int16 samples: value = q·scale + offset. */
  | { id: number; encoding: "quantized"; tStart: number; dt: number; scale: number; offset: number; q: number[] };
/** Whole frame: `seq` counter, `t0` reference time in seconds, channels in wire order. */
export interface FrameJson { seq: number; t0: number; channels: FrameChannelJson[] }

/** Copies typed arrays into plain arrays; the result survives `JSON.stringify` unchanged. */
export function frameToJson(frame: SkyScopeFrame): FrameJson {
  return {
    seq: frame.seq, t0: frame.t0,
    channels: frame.channels.map((ch): FrameChannelJson => {
      switch (ch.encoding) {
        case "timestamped": return { id: ch.id, encoding: ch.encoding, times: Array.from(ch.times), values: Array.from(ch.values) };
        case "regular": return { id: ch.id, encoding: ch.encoding, tStart: ch.tStart, dt: ch.dt, values: Array.from(ch.values) };
        case "quantized": return { id: ch.id, encoding: ch.encoding, tStart: ch.tStart, dt: ch.dt, scale: ch.scale, offset: ch.offset, q: Array.from(ch.q) };
      }
    }),
  };
}

/** Rebuilds the typed arrays; quantized `scale`/`offset` are rounded to f32 as they are on the wire. */
export function frameFromJson(json: FrameJson): SkyScopeFrame {
  return {
    seq: json.seq, t0: json.t0,
    channels: json.channels.map((ch): FrameChannel => {
      switch (ch.encoding) {
        case "timestamped": return { id: ch.id, encoding: ch.encoding, times: Float64Array.from(ch.times), values: Float32Array.from(ch.values) };
        case "regular": return { id: ch.id, encoding: ch.encoding, tStart: ch.tStart, dt: ch.dt, values: Float32Array.from(ch.values) };
        case "quantized": return { id: ch.id, encoding: ch.encoding, tStart: ch.tStart, dt: ch.dt, scale: Math.fround(ch.scale), offset: Math.fround(ch.offset), q: Int16Array.from(ch.q) };
      }
    }),
  };
}
