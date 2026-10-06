// Mori.SkyScope — Fixture driver for the clip-space transform used by the GPU line pass.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { applyClip, clipTransform, type ClipTransform } from "../scales/clip.js";
import { createScale, type Scale, type ScaleKind } from "../scales/scale.js";
import { r9 } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

interface State { t: ClipTransform; xOrigin: number; queries: unknown[] }
const mk = (s: Record<string, unknown>): Scale => createScale((s.kind as ScaleKind) ?? "linear", s.domain as [number, number], s.range as [number, number]);

/**
 * Clip-space transform of the GPU line pass: `setup` gives the x and y scale specs, the viewport and the x origin.
 */
export const clipDriver: FixtureDriver<State> = {
  component: "clip-transform",
  create(setup) {
    const [w, h] = setup.viewport as [number, number];
    const xOrigin = (setup.xOrigin as number | undefined) ?? 0;
    return { t: clipTransform(mk(setup.x as Record<string, unknown>), mk(setup.y as Record<string, unknown>), w, h, xOrigin), xOrigin, queries: [] };
  },
  /** Queries: transform (the four coefficients) and apply (a point through it), rounded to 9 decimals. */
  step(s, step) {
    if ("transform" in step) s.queries.push({ sx: r9(s.t.sx), ox: r9(s.t.ox), sy: r9(s.t.sy), oy: r9(s.t.oy) });
    else if ("apply" in step) { const [x, y] = step.apply as [number, number]; const p = applyClip(s.t, x, y, s.xOrigin); s.queries.push({ x: r9(p.x), y: r9(p.y) }); }
    else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
