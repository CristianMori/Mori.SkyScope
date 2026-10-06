// Mori.SkyScope — The lz4- and zstd-chunked golden MCAP files (one of them written by the C# writer) read exactly like the unchunked sample.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { readMcap } from "./recording/mcap.js";
import { readRecording } from "./recording/recorder.js";

const dir = path.resolve(fileURLToPath(new URL(".", import.meta.url)), "../../../../spec/mcap");
const load = (name: string): Uint8Array => new Uint8Array(fs.readFileSync(path.join(dir, name)));

describe("chunked golden MCAP files", () => {
  const plain = load("sample.mcap");

  it.each(["sample-lz4.mcap", "sample-zstd.mcap", "sample-zstd-written.mcap"])("%s parses to the same records as sample.mcap", (name) => {
    const a = readMcap(plain), b = readMcap(load(name));
    expect(b.profile).toBe(a.profile);
    expect(b.library).toBe(a.library);
    expect(b.schemas.size).toBe(a.schemas.size);
    expect(b.channels.size).toBe(a.channels.size);
    expect([...b.channels.values()].map((c) => c.topic)).toEqual([...a.channels.values()].map((c) => c.topic));
    expect(b.messages.length).toBe(a.messages.length);
    expect(b.messageStart).toBe(a.messageStart);
    expect(b.messageEnd).toBe(a.messageEnd);
    for (let i = 0; i < a.messages.length; i++) {
      const x = a.messages[i]!, y = b.messages[i]!;
      expect([y.channelId, y.sequence, y.logTime, y.publishTime]).toEqual([x.channelId, x.sequence, x.logTime, x.publishTime]);
      expect(y.data).toEqual(x.data);
    }
  });

  it.each(["sample-lz4.mcap", "sample-zstd.mcap", "sample-zstd-written.mcap"])("%s decodes to the same recording as sample.mcap", (name) => {
    const a = readRecording(plain), b = readRecording(load(name));
    expect(b.channels).toEqual(a.channels);
    expect(b.layerEvents).toEqual(a.layerEvents);
    expect(b.start).toBe(a.start);
    expect(b.end).toBe(a.end);
    expect(b.frames.length).toBe(a.frames.length);
    expect(b.frames.length).toBeGreaterThan(0);
    for (let i = 0; i < a.frames.length; i++) {
      const x = a.frames[i]!, y = b.frames[i]!;
      expect(y.t0).toBe(x.t0);
      expect(y.channels.length).toBe(x.channels.length);
      for (let c = 0; c < x.channels.length; c++) {
        const xc = x.channels[c]!, yc = y.channels[c]!;
        expect(yc.id).toBe(xc.id);
        expect(yc.encoding).toBe(xc.encoding);
        expect(yc).toEqual(xc);
      }
    }
  });

  it.each(["sample-zstd.mcap", "sample-zstd-written.mcap"])("%s really carries zstd chunks", (name) => {
    const bytes = load(name);
    expect(new TextDecoder().decode(bytes).includes("zstd")).toBe(true);
    expect(bytes.length).toBeLessThan(plain.length);
  });
});
