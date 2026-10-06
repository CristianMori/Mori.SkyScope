// Mori.SkyScope — Round-trips the golden SkyScopeFrame binaries in spec/frames through the codec.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect } from "vitest";
import { decodeFrame, encodeFrame, frameByteLength, type SkyScopeFrame, type FrameChannel } from "./streaming/frame.js";
import { frameFromJson, frameToJson, type FrameJson } from "./streaming/frame-json.js";

const dir = path.resolve(fileURLToPath(new URL(".", import.meta.url)), "../../../../spec/frames");
const names = fs.readdirSync(dir).filter((f) => f.endsWith(".json") && !f.startsWith("layer-")).map((f) => f.slice(0, -5)).sort();

describe("SkyScopeFrame golden files", () => {
  for (const name of names) {
    const json = JSON.parse(fs.readFileSync(path.join(dir, `${name}.json`), "utf8")) as FrameJson;
    const binPath = path.join(dir, `${name}.bin`);
    it(`${name}: decode(.bin) matches .json`, () => {
      expect(fs.existsSync(binPath), `missing ${name}.bin — run: npm run frames`).toBe(true);
      const bytes = new Uint8Array(fs.readFileSync(binPath));
      expect(frameToJson(decodeFrame(bytes))).toEqual(json);
      expect(frameToJson(decodeFrame(bytes, { copy: true }))).toEqual(json);
    });
    it(`${name}: encode(.json) matches .bin byte for byte`, () => {
      const bytes = fs.readFileSync(binPath);
      const encoded = encodeFrame(frameFromJson(json));
      expect(Buffer.from(encoded).equals(bytes)).toBe(true);
      expect(frameByteLength(frameFromJson(json))).toBe(bytes.length);
    });
  }
});

describe("SkyScopeFrame round-trips", () => {
  it("random frames survive encode → decode, also from unaligned offsets", () => {
    let seed = 12345;
    const rnd = () => (seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296;
    for (let k = 0; k < 50; k++) {
      const channels: FrameChannel[] = [];
      const n = Math.floor(rnd() * 6);
      for (let c = 0; c < n; c++) {
        const len = Math.floor(rnd() * 9);
        const id = Math.floor(rnd() * 65536);
        const e = Math.floor(rnd() * 3);
        const values = Float32Array.from({ length: len }, () => Math.fround(rnd() * 200 - 100));
        if (e === 0) channels.push({ id, encoding: "timestamped", times: Float64Array.from({ length: len }, (_, i) => i * 0.5 + rnd()), values });
        else if (e === 1) channels.push({ id, encoding: "regular", tStart: rnd() * 1e6, dt: 1 / (1 + Math.floor(rnd() * 1000)), values });
        else channels.push({ id, encoding: "quantized", tStart: rnd() * 1e6, dt: 0.01, scale: Math.fround(rnd()), offset: Math.fround(rnd() * 10), q: Int16Array.from({ length: len }, () => Math.floor(rnd() * 65536) - 32768) });
      }
      const frame: SkyScopeFrame = { seq: Math.floor(rnd() * 4294967296), t0: rnd() * 1e9, channels };
      const bytes = encodeFrame(frame);
      expect(frameToJson(decodeFrame(bytes))).toEqual(frameToJson(frame));
      // unaligned: shift by 3 bytes into a bigger buffer → decoder must fall back to DataView reads
      const shifted = new Uint8Array(bytes.length + 3); shifted.set(bytes, 3);
      expect(frameToJson(decodeFrame(shifted.subarray(3)))).toEqual(frameToJson(frame));
      const padded = new Uint8Array(bytes.length + 5); encodeFrame(frame, padded.subarray(5));
      expect(Buffer.from(padded.subarray(5)).equals(Buffer.from(bytes))).toBe(true);
    }
  });
  it("rejects garbage", () => {
    expect(() => decodeFrame(new Uint8Array(3))).toThrow("truncated-frame");
    const bad = encodeFrame({ seq: 1, t0: 0, channels: [] }); bad[0] = 0;
    expect(() => decodeFrame(bad)).toThrow("bad-magic");
    const trunc = encodeFrame({ seq: 1, t0: 0, channels: [{ id: 1, encoding: "regular", tStart: 0, dt: 1, values: new Float32Array(4) }] });
    expect(() => decodeFrame(trunc.subarray(0, trunc.length - 8))).toThrow("truncated-frame");
  });
});
