// Mori.SkyScope — Range export: the shared fixed-point formatter on exact halves, and a CSV export that parses back with its empty cells.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, it, expect } from "vitest";
import { exportRangeCsv, exportRangeMcap, formatFixed } from "./recording/export.js";
import { readRecording } from "./recording/recorder.js";
import { parseCsv } from "./sources/playback.js";
import { SignalStore } from "./sources/signal-store.js";

/** The same table is pinned in RangeExportTests.cs: value, decimals, text. */
const TABLE: [number, number, string][] = [
  [0.5, 0, "1"], [1.5, 0, "2"], [2.5, 0, "3"], [-0.5, 0, "-1"], [-2.5, 0, "-3"],
  [0.125, 2, "0.13"], [-0.125, 2, "-0.13"], [0.375, 2, "0.38"], [0.995, 2, "1.00"], [5e-7, 6, "0.000001"],
  [-1e-7, 6, "0.000000"], [-0, 3, "0.000"], [1234.5678, 2, "1234.57"], [2.3000000000000003, 6, "2.300000"], [1700000000.123456789, 6, "1700000000.123457"],
  [-123.456, 0, "-123"], [0.1, 15, "0.100000000000000"], [NaN, 2, "NaN"], [Infinity, 2, "Infinity"], [-Infinity, 1, "-Infinity"], [3.14159, 20, "3.141590000000000"], [7, -1, "7"],
];

describe("formatFixed", () => {
  it("rounds exact halves away from zero, never prints -0 and clamps the decimals", () => {
    for (const [v, d, text] of TABLE) expect(formatFixed(v, d), `${v} @ ${d}`).toBe(text);
  });
});

describe("exportRangeCsv / exportRangeMcap", () => {
  const store = new SignalStore({ retentionSeconds: 10 });
  store.declareChannel({ id: 1, name: "a", rate: 10 });
  store.declareChannel({ id: 2, name: "b", timing: "timestamped" });
  store.pushFrame({ seq: 0, t0: 0, channels: [{ id: 1, encoding: "regular", tStart: 0, dt: 0.5, values: Float32Array.from([1, 2, 3, 4, 5]) }] });
  store.pushFrame({ seq: 1, t0: 0.25, channels: [{ id: 2, encoding: "timestamped", times: Float64Array.from([0.25, 1, 1.75]), values: Float32Array.from([10, 20, 30]) }] });

  it("writes one row per distinct time with empty cells, and parseCsv reads the empty cells as missing samples", () => {
    const csv = exportRangeCsv(store, [1, 2], 0, 2, { decimals: 2, valueDecimals: 1 });
    expect(csv).toBe("t,a,b\n0.00,1.0,\n0.25,,10.0\n0.50,2.0,\n1.00,3.0,20.0\n1.50,4.0,\n1.75,,30.0\n2.00,5.0,\n");
    const rec = parseCsv(csv);
    expect(rec.channels.map((c) => c.name)).toEqual(["a", "b"]);
    const [f] = rec.frames;
    const a = f!.channels[0]!, b = f!.channels[1]!;
    expect(a.encoding === "timestamped" && Array.from(a.times)).toEqual([0, 0.5, 1, 1.5, 2]);
    expect(b.encoding === "timestamped" && Array.from(b.values)).toEqual([10, 20, 30]);
  });

  it("writes an MCAP that reads back as a recording of the span", () => {
    const rec = readRecording(exportRangeMcap(store, [2, 1], 0.5, 1.75));
    expect(rec.channels.map((c) => c.id)).toEqual([1, 2]);
    expect(rec.frames.map((f) => [f.t0, f.channels[0]!.id, f.channels[0]!.encoding])).toEqual([[0.5, 1, "regular"], [1, 2, "timestamped"]]);
    expect(rec.start).toBe(0.5); expect(rec.end).toBe(1.75);
  });
});
