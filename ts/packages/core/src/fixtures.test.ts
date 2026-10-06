// Mori.SkyScope — Runs every shared fixture in spec/fixtures through the TypeScript drivers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { describe, it, expect } from "vitest";
import { fixtureDrivers } from "./fixtures/drivers.js";

interface FixtureFile {
  component: string;
  cases: { name: string; setup?: Record<string, unknown>; steps?: Record<string, unknown>[]; expect: Record<string, unknown> }[];
}

const dir = path.resolve(fileURLToPath(new URL(".", import.meta.url)), "../../../../spec/fixtures");
const files = fs.readdirSync(dir).filter((f) => f.endsWith(".json")).sort();

for (const file of files) {
  const spec = JSON.parse(fs.readFileSync(path.join(dir, file), "utf8")) as FixtureFile;
  describe(`fixture ${file} (${spec.component})`, () => {
    const driver = fixtureDrivers[spec.component];
    it("has a TS fixture driver", () => {
      expect(driver, `no TS fixture driver registered for component "${spec.component}"`).toBeDefined();
    });
    for (const c of spec.cases) {
      it(c.name, () => {
        if (!driver) return;
        let state = driver.create(c.setup ?? {});
        for (const step of c.steps ?? []) state = driver.step(state, step);
        const snapshot = driver.snapshot(state);
        for (const [key, expected] of Object.entries(c.expect)) {
          expect(snapshot[key], `property "${key}" — snapshot: ${JSON.stringify(snapshot)}`).toEqual(expected);
        }
      });
    }
  });
}
