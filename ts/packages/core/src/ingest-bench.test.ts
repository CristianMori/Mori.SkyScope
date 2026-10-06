// Mori.SkyScope — The ingest budget from the plan, shortened to 2 s of data for CI (tools/bench-ingest.mjs runs the full 60 s).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, expect, it } from "vitest";
import { SignalStore } from "./sources/signal-store.js";
import { decodeFrame, encodeFrame, type FrameChannel } from "./streaming/frame.js";

/** The ingest budget from the plan, shortened to 2 s of data for CI (tools/bench-ingest.mjs runs the full 60 s). */
describe("ingest bench", () => {
  it("takes 500 channels × 1 kHz through encode → decode → store faster than real time with zero drops", () => {
    const seconds = 2, channels = 500, rate = 1000, batchMs = 20;
    const store = new SignalStore({ retentionSeconds: 5 });
    for (let c = 0; c < channels; c++) store.declareChannel({ id: c + 1, name: `ch${c + 1}`, rate, timing: "regular" });
    const perFrame = rate * batchMs / 1000, frames = Math.round(seconds * 1000 / batchMs);
    const values = new Float32Array(perFrame);
    const t0 = performance.now();
    for (let f = 0; f < frames; f++) {
      const tStart = f * batchMs / 1000;
      for (let i = 0; i < perFrame; i++) values[i] = Math.sin((tStart + i / rate) * 6.283);
      const chans: FrameChannel[] = [];
      for (let c = 0; c < channels; c++) chans.push({ id: c + 1, encoding: "regular", tStart, dt: 1 / rate, values });
      const encoded = encodeFrame({ seq: f, t0: tStart, channels: chans });
      store.pushFrame(decodeFrame(encoded.buffer.slice(encoded.byteOffset, encoded.byteOffset + encoded.byteLength)));
    }
    const ms = performance.now() - t0;
    expect(store.dropped).toBe(0);
    expect(store.get(500)!.buffer.length).toBe(rate * seconds);
    expect(ms / 1000).toBeLessThan(seconds);
  });
});
