// Mori.SkyScope — Generates the demo CSV recording used by the playback samples.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

// Writes samples/demo-recording.csv: 20 s at 100 Hz of sine, triangle, sawtooth and noise (channels 1–4 when played back).
import fs from "node:fs";
const rate = 100, seconds = 20, lines = ["t,sine,triangle,sawtooth,noise"];
// Deterministic LCG so the noise column is identical on every run (same generator as the React sample's demoCsv).
let seed = 1; const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
// One row per sample: t in seconds, then the four waveforms (sine ±2.5, triangle ±3, sawtooth ±1, noise ±1).
for (let i = 0; i <= rate * seconds; i++) {
  const t = i / rate;
  lines.push([t.toFixed(3), (2.5 * Math.sin(t * 0.7 * 2 * Math.PI / 3)).toFixed(4), (3 * (2 * Math.abs((t * 0.47) % 1 - 0.5) - 0.5)).toFixed(4), (2 * ((t * 0.84) % 1) - 1).toFixed(4), ((rnd() - 0.5) * 2).toFixed(4)].join(","));
}
fs.writeFileSync("samples/demo-recording.csv", lines.join("\n") + "\n");
console.log("wrote samples/demo-recording.csv", lines.length - 1, "rows");
