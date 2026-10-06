// Mori.SkyScope — Ingest benchmark: encode, decode and store N channels at 1 kHz for a given time and report throughput and drops.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

// Ingest budget from the plan: 500 channels × 1 kHz through encode → decode → SignalStore, zero drops.
//   npm run bench -- 60          (seconds of data; default 10)
import { SignalStore, encodeFrame, decodeFrame } from "../ts/packages/core/dist/index.js";

// ---- setup: a store with 500 regular 1 kHz channels and 5 s of retention ----
const seconds = Number(process.argv[2] ?? 10), channels = 500, rate = 1000, batchMs = 20;
const store = new SignalStore({ retentionSeconds: 5 });
for (let c = 0; c < channels; c++) store.declareChannel({ id: c + 1, name: `ch${c + 1}`, rate, timing: "regular" });
const perFrame = rate * batchMs / 1000, frames = Math.round(seconds * 1000 / batchMs);
const values = new Float32Array(perFrame);
let bytes = 0;
// ---- run: one 20 ms frame per iteration, every channel sharing the same sine block; encode -> decode -> push, timed as a whole ----
const t0 = performance.now();
for (let f = 0; f < frames; f++) {
  const tStart = f * batchMs / 1000;
  for (let i = 0; i < perFrame; i++) values[i] = Math.sin((tStart + i / rate) * 6.283);
  const chans = [];
  for (let c = 0; c < channels; c++) chans.push({ id: c + 1, encoding: "regular", tStart, dt: 1 / rate, values });
  const encoded = encodeFrame({ seq: f, t0: tStart, channels: chans });
  bytes += encoded.byteLength;
  store.pushFrame(decodeFrame(encoded.buffer.slice(encoded.byteOffset, encoded.byteOffset + encoded.byteLength)));
}
// ---- report: throughput in Msamples/s and drops; exit 1 when samples were dropped or the run was slower than real time ----
const ms = performance.now() - t0, samples = frames * channels * perFrame;
console.log(`${channels} ch × ${rate} Hz for ${seconds} s: ${samples.toLocaleString()} samples, ${(bytes / 1e6).toFixed(1)} MB in ${ms.toFixed(0)} ms → ${(samples / (ms / 1000) / 1e6).toFixed(1)} Msamples/s, dropped ${store.dropped}`);
if (store.dropped !== 0 || ms / 1000 > seconds) { console.error("FAIL: drops or slower than real time"); process.exit(1); }
