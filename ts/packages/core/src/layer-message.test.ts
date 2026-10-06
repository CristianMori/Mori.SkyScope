// Mori.SkyScope — Round-trips the golden binary layer messages in spec/frames.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, it, expect } from "vitest";
import fs from "node:fs";
import path from "node:path";
import { decodeLayerMessage, encodeLayerMessage, hasBinaryPayload, isLayerMessage } from "./streaming/layer-message.js";
import { payloadFromJson, payloadToJson } from "./fixtures/scene3d-drivers.js";
import { fnv1a } from "./charts/colormaps.js";
import { decodeFrame } from "./streaming/frame.js";

const dir = path.resolve(__dirname, "../../../../spec/frames");

describe("SkyScopeLayer golden files", () => {
  const golden = JSON.parse(fs.readFileSync(path.join(dir, "layer-cloud.json"), "utf8")) as { id: string; payload: unknown; length: number; hash: number };
  const bin = new Uint8Array(fs.readFileSync(path.join(dir, "layer-cloud.bin")));
  it("encode(.json) matches .bin byte for byte", () => {
    const bytes = encodeLayerMessage(golden.id, payloadFromJson(golden.payload) as Record<string, unknown>);
    expect(bytes.length).toBe(golden.length);
    expect(fnv1a(bytes)).toBe(golden.hash);
    expect(Buffer.from(bytes).equals(Buffer.from(bin))).toBe(true);
  });
  it("decode(.bin) matches .json and is told apart from a frame", () => {
    const d = decodeLayerMessage(bin);
    expect(d.id).toBe(golden.id);
    expect(payloadToJson(d.payload)).toEqual(golden.payload);
    expect(isLayerMessage(bin)).toBe(true);
    expect(() => decodeFrame(bin.buffer)).toThrow();
  });
  it("decodes from an unaligned offset and keeps typed arrays", () => {
    const shifted = new Uint8Array(bin.length + 3); shifted.set(bin, 3);
    const d = decodeLayerMessage(shifted.subarray(3));
    expect(d.payload.positions).toBeInstanceOf(Float32Array);
    expect((d.payload.positions as Float32Array).length).toBe(15);
    expect(hasBinaryPayload(d.payload)).toBe(true);
  });
});
