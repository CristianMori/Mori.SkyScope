// Mori.SkyScope — Packs every npm workspace package into artifacts/npm.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

// npm pack every publishable workspace package into artifacts/npm.
import { execSync } from "node:child_process";
import fs from "node:fs";
const out = "artifacts/npm";
fs.mkdirSync(out, { recursive: true });
// Dependency order (tokens first); blazor is bundled into the Razor package and is not published to npm.
for (const p of ["tokens", "core", "render", "sources", "react", "source-template"]) {
  execSync(`npm pack --pack-destination ${out} -w @mori/skyscope-${p}`, { stdio: "inherit" });
}
console.log(fs.readdirSync(out).join("\n"));
