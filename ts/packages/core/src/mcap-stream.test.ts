// Mori.SkyScope — Streams an MCAP file through the recorder and reader and checks what comes back.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, expect, it } from "vitest";
import { McapWriter, readMcap } from "./recording/mcap.js";
import { McapRecorder } from "./recording/recorder.js";
import { ManualClock } from "./sources/contracts.js";

function fill(w: McapWriter): void {
  const s = w.addSchema("t", "jsonschema", "{}");
  const c = w.addChannel("/a", s, "json", { k: "v" });
  for (let i = 0; i < 50; i++) w.addMessage(c, BigInt(1_000_000_000 + i * 1000), new TextEncoder().encode(`{"i":${i}}`));
}

describe("mcap streaming output", () => {
  it("streams byte-identical records to a sink instead of buffering", () => {
    const buffered = new McapWriter(); fill(buffered);
    const chunks: Uint8Array[] = [];
    const streamed = new McapWriter({ sink: (b) => chunks.push(b.slice()) }); fill(streamed);
    const a = buffered.finish(), tail = streamed.finish();
    expect(tail.length).toBe(0);
    expect(streamed.streaming).toBe(true);
    const total = chunks.reduce((n, c) => n + c.length, 0);
    const b = new Uint8Array(total); let o = 0; for (const c of chunks) { b.set(c, o); o += c.length; }
    expect(b).toEqual(a);
    expect(streamed.length).toBe(a.length);
    expect(readMcap(b).messages.length).toBe(50);
  });

  it("recorder ignores the size limit while streaming and returns no bytes on stop", () => {
    const chunks: Uint8Array[] = [];
    const rec = new McapRecorder({}, new ManualClock(5), { maxBytes: 200 });
    rec.declareChannel({ id: 1, name: "a" });
    rec.start((b) => chunks.push(b));
    for (let i = 0; i < 20; i++) rec.pushFrame({ seq: i, t0: 5 + i, channels: [{ id: 1, encoding: "regular", tStart: 5 + i, dt: 1, values: Float32Array.from([i]) }] });
    expect(rec.recording).toBe(true);
    expect(rec.streaming).toBe(true);
    expect(rec.stop()!.length).toBe(0);
    expect(chunks.reduce((n, c) => n + c.length, 0)).toBeGreaterThan(200);
  });
});
