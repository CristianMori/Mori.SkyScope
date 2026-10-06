// Mori.SkyScope — Generates the golden SkyScopeFrame binaries and their JSON expectations in spec/frames.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

// Regenerates spec/frames/*.bin from spec/frames/*.json using the TS encoder (build @mori/skyscope-core first).
// The C# tests then prove the independent C# encoder produces identical bytes.
import fs from "node:fs";
import path from "node:path";
import { encodeFrame, decodeFrame, frameFromJson, frameToJson } from "../ts/packages/core/dist/index.js";

const dir = path.resolve(import.meta.dirname, "../spec/frames");
// For each JSON expectation: encode, decode back, require an exact JSON round-trip, then write the .bin next to it.
for (const f of fs.readdirSync(dir).filter((f) => f.endsWith(".json")).sort()) {
  const json = JSON.parse(fs.readFileSync(path.join(dir, f), "utf8"));
  const bytes = encodeFrame(frameFromJson(json));
  const back = JSON.stringify(frameToJson(decodeFrame(bytes)));
  if (back !== JSON.stringify(json)) throw new Error(`${f}: round-trip mismatch\n${back}`);
  fs.writeFileSync(path.join(dir, f.replace(/\.json$/, ".bin")), bytes);
  console.log(`${f.padEnd(16)} -> ${bytes.length} bytes`);
}
