// Mori.SkyScope — Fixture drivers for 2D geometry and the 2D camera, plus the rounding helpers shared by every driver.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { apply, invert, mul, rotation, scaling, translation, distToSegment, pointInPolygon, rectFromPoints, rectUnion, rectNormalize, transformRect, type Mat3, type Rect } from "../scene/geometry.js";
import { Camera2D, type Camera2DOptions } from "../scene/camera.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const nums = (v: unknown): number[] => v as number[];
/** 9-decimal rounding shared with the C# drivers; -0 normalised to 0. */
export const r9 = (x: number): number => { const v = Math.floor(x * 1e9 + 0.5) / 1e9; return v === 0 ? 0 : v; };
/** Round a rectangle's fields with `r9`; null passes through. */
export const rRect = (r: Rect | null): { x: number; y: number; w: number; h: number } | null => (r ? { x: r9(r.x), y: r9(r.y), w: r9(r.w), h: r9(r.h) } : null);
const rMat = (m: Mat3) => ({ a: r9(m.a), b: r9(m.b), c: r9(m.c), d: r9(m.d), e: r9(m.e), f: r9(m.f) });

function matrix(spec: unknown): Mat3 {
  const s = spec as Record<string, unknown>;
  if ("translation" in s) { const [x, y] = nums(s.translation); return translation(x!, y!); }
  if ("scaling" in s) { const [x, y] = nums(s.scaling); return scaling(x!, y!); }
  if ("rotation" in s) return rotation(num(s.rotation));
  if ("mul" in s) { const [m, n] = s.mul as unknown[]; return mul(matrix(m), matrix(n)); }
  return s as unknown as Mat3;
}

/**
 * 2D geometry functions; matrices in steps are literal `{a..f}` objects or `translation`, `scaling`, `rotation` and
 * `mul` specs.
 */
export const geometryDriver: FixtureDriver<{ queries: unknown[] }> = {
  component: "geometry",
  create() { return { queries: [] }; },
  /**
   * Queries: apply, invert, matrix, distToSegment, pointInPolygon, rectFromPoints, rectUnion, rectNormalize,
   * transformRect.
   */
  step(s, step: Step) {
    const q = s.queries;
    if ("apply" in step) { const [m, x, y] = step.apply as unknown[]; const p = apply(matrix(m), num(x), num(y)); q.push({ x: r9(p.x), y: r9(p.y) }); }
    else if ("invert" in step) { const m = invert(matrix(step.invert)); q.push(m ? rMat(m) : null); }
    else if ("matrix" in step) q.push(rMat(matrix(step.matrix)));
    else if ("distToSegment" in step) { const a = nums(step.distToSegment); q.push(r9(distToSegment(a[0]!, a[1]!, a[2]!, a[3]!, a[4]!, a[5]!))); }
    else if ("pointInPolygon" in step) { const [px, py, pts] = step.pointInPolygon as unknown[]; q.push(pointInPolygon(num(px), num(py), pts as number[])); }
    else if ("rectFromPoints" in step) q.push(rRect(rectFromPoints(nums(step.rectFromPoints))));
    else if ("rectUnion" in step) { const [a, b] = step.rectUnion as (Rect | null)[]; q.push(rRect(rectUnion(a!, b!))); }
    else if ("rectNormalize" in step) { const a = nums(step.rectNormalize); q.push(rRect(rectNormalize(a[0]!, a[1]!, a[2]!, a[3]!))); }
    else if ("transformRect" in step) { const [m, r] = step.transformRect as unknown[]; q.push(rRect(transformRect(matrix(m), r as Rect))); }
    else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

/** 2D camera: `setup` is `Camera2DOptions`. */
export const cameraDriver: FixtureDriver<{ camera: Camera2D; queries: unknown[] }> = {
  component: "camera",
  create(setup) { return { camera: new Camera2D(setup as Camera2DOptions), queries: [] }; },
  /**
   * Steps: pan, zoomAt, fitBounds, fitScreenRect, setZoom, setCenter, setViewport; queries: project, unproject,
   * worldBounds, zoom.
   */
  step(s, step: Step) {
    const { camera, queries } = s;
    switch (step.type) {
      case "pan": camera.pan(num(step.dx), num(step.dy)); break;
      case "zoomAt": camera.zoomAt(num(step.x), num(step.y), num(step.factor)); break;
      case "fitBounds": camera.fitBounds(step.rect as Rect, step.padding === undefined ? 0 : num(step.padding)); break;
      case "fitScreenRect": camera.fitScreenRect(step.rect as Rect, step.padding === undefined ? 0 : num(step.padding)); break;
      case "setZoom": camera.setZoom(num(step.zoom)); break;
      case "setCenter": camera.setCenter(num(step.x), num(step.y)); break;
      case "setViewport": camera.setViewport(num(step.width), num(step.height)); break;
      case "query":
        if ("project" in step) { const [x, y] = nums(step.project); const p = camera.project(x!, y!); queries.push({ x: r9(p.x), y: r9(p.y) }); }
        else if ("unproject" in step) { const [x, y] = nums(step.unproject); const p = camera.unproject(x!, y!); queries.push({ x: r9(p.x), y: r9(p.y) }); }
        else if ("worldBounds" in step) queries.push(rRect(camera.worldBounds()));
        else if ("zoom" in step) queries.push(r9(camera.zoom));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  /** Centre, zoom, rotation, version and the query answers. */
  snapshot({ camera, queries }) {
    return { centerX: r9(camera.centerX), centerY: r9(camera.centerY), zoom: r9(camera.zoom), rotation: r9(camera.rotation), version: camera.version, queries };
  },
};
