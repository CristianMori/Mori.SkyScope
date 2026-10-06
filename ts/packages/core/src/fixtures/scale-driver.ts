// Mori.SkyScope — Fixture driver for scales, ticks and number/time formatting.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { createScale, type Scale, type ScaleKind } from "../scales/scale.js";
import type { TimeFormat } from "../scales/format.js";
import type { FixtureDriver } from "./drivers.js";

interface State { scale: Scale; queries: unknown[] }
const num = (v: unknown): number => v as number;
const pair = (v: unknown): [number, number] => v as [number, number];

/** Scales: `setup` gives kind, domain, range and the time format mode. */
export const scaleDriver: FixtureDriver<State> = {
  component: "scales",
  create(setup) {
    const [d0, d1] = pair(setup.domain), [r0, r1] = pair(setup.range);
    return { scale: createScale(setup.kind as ScaleKind, [d0, d1], [r0, r1], (setup.mode as TimeFormat) ?? "utc"), queries: [] };
  },
  /** Queries: scale, invert, ticks, tickSpec, format, nice. */
  step(s, step) {
    const { scale, queries } = s;
    if (step.type !== "query") throw new Error(`unknown step ${String(step.type)}`);
    if ("scale" in step) queries.push(scale.scale(num(step.scale)));
    else if ("invert" in step) queries.push(scale.invert(num(step.invert)));
    else if ("ticks" in step) queries.push(scale.ticks(num(step.ticks)));
    else if ("tickSpec" in step) queries.push(scale.tickSpec(num(step.tickSpec)));
    else if ("format" in step) { const [v, c] = pair(step.format); queries.push(scale.format(v, c)); }
    else if ("nice" in step) queries.push([...scale.nice(num(step.nice)).domain]);
    else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  /** Kind, domain, range and the query answers. */
  snapshot({ scale, queries }) {
    return { kind: scale.kind, domain: [...scale.domain], range: [...scale.range], queries };
  },
};
