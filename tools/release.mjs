// Mori.SkyScope — Cuts a release: sets one version everywhere (NuGet through Directory.Build.props, every npm workspace package and the pins between them), opens a changelog section, commits and tags.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

// Usage:  node tools/release.mjs 0.1.1 [--notes "one line"] [--no-commit] [--push]
//   - rewrites <Version> in dotnet/Directory.Build.props, "version" in package.json and every ts/packages/*/package.json,
//     and the "@mori/skyscope-*" dependency pins in packages and samples;
//   - inserts a "## 0.1.1 (YYYY-MM-DD)" section at the top of CHANGELOG.md with the notes (edit it before pushing);
//   - commits "Release 0.1.1" and creates the annotated tag v0.1.1 (unless --no-commit);
//   - with --push, pushes main and the tag; the release workflow then builds, tests and publishes the packages.
// Versions already on nuget.org are frozen, so this script refuses a version that is tagged already.
import { execSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

const args = process.argv.slice(2);
const version = args.find((a) => !a.startsWith("--"));
if (!version || !/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(version)) { console.error("usage: node tools/release.mjs <major.minor.patch[-pre]> [--notes \"...\"] [--no-commit] [--push]"); process.exit(1); }
const notes = args.includes("--notes") ? args[args.indexOf("--notes") + 1] ?? "" : "";
const root = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
const run = (cmd) => execSync(cmd, { cwd: root, stdio: "pipe" }).toString().trim();

if (run("git status --porcelain").length > 0) { console.error("the working tree has uncommitted changes; commit or stash them first"); process.exit(1); }
if (run("git tag --list v" + version)) { console.error(`v${version} is tagged already; published versions cannot change, pick a new one`); process.exit(1); }

const props = path.join(root, "dotnet/Directory.Build.props");
const propsText = fs.readFileSync(props, "utf8");
const current = /<Version>([^<]+)<\/Version>/.exec(propsText)?.[1];
if (!current) { console.error("no <Version> in dotnet/Directory.Build.props"); process.exit(1); }
fs.writeFileSync(props, propsText.replace(/<Version>[^<]+<\/Version>/, `<Version>${version}</Version>`));
console.log(`NuGet: ${current} -> ${version}`);

const packageJsons = ["package.json", ...fs.readdirSync(path.join(root, "ts/packages")).map((d) => `ts/packages/${d}/package.json`), "samples/react-sample/package.json", "samples/vanilla-ts/package.json"].filter((f) => fs.existsSync(path.join(root, f)));
for (const rel of packageJsons) {
  const file = path.join(root, rel);
  const json = JSON.parse(fs.readFileSync(file, "utf8"));
  const isPackage = rel.startsWith("ts/packages/") || rel === "package.json";
  if (isPackage) json.version = version;
  for (const key of ["dependencies", "devDependencies", "peerDependencies"]) {
    const deps = json[key]; if (!deps) continue;
    for (const name of Object.keys(deps)) if (name.startsWith("@mori/skyscope-")) deps[name] = version;
  }
  fs.writeFileSync(file, JSON.stringify(json, null, 2) + "\n");
  console.log(`npm: ${rel}`);
}
run("npm install --no-audit --no-fund --package-lock-only");

const changelog = path.join(root, "CHANGELOG.md");
const today = new Date().toISOString().slice(0, 10);
const head = `## ${version} (${today})\n\n${notes ? `- ${notes}\n` : "- \n"}\n`;
const existing = fs.existsSync(changelog) ? fs.readFileSync(changelog, "utf8") : "# Changelog\n\nEvery version of the NuGet and npm packages, newest first. The day-by-day log is docs/STATUS.md.\n\n";
const marker = existing.indexOf("\n## ");
fs.writeFileSync(changelog, marker < 0 ? existing + head : existing.slice(0, marker + 1) + head + existing.slice(marker + 1));
console.log(`CHANGELOG.md: section ${version} added`);

if (args.includes("--no-commit")) { console.log("not committed (--no-commit); review, then commit and tag v" + version); process.exit(0); }
run("git add -A");
run(`git commit -q -m "Release ${version}"`);
run(`git tag -a v${version} -m "Mori.SkyScope ${version}"`);
console.log(`committed and tagged v${version}`);
if (args.includes("--push")) { run("git push origin main --follow-tags"); console.log("pushed main and the tag; the release workflow publishes the packages"); }
else console.log("push with: git push origin main --follow-tags");
