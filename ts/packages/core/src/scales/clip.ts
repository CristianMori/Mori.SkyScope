// Mori.SkyScope — Data → WebGL clip-space mapping for a pair of axis scales: clip = data · scale + offset.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Scale } from "./scale.js";

/** Clip x = (x − xOrigin)·`sx` + `ox`; clip y = y·`sy` + `oy`. */
/**
 * Data → WebGL clip-space mapping for a pair of axis scales: clip = data · scale + offset.
 * `xOrigin` is subtracted from x before upload so f32 vertex buffers keep sub-millisecond precision
 * on unix-epoch timestamps. Valid for linear and time scales (pre-transform data for log axes).
 * Pinned by `spec/fixtures/clip-transform.json`.
 */
export interface ClipTransform { sx: number; ox: number; sy: number; oy: number }

/** Derives the mapping from the scales' current domain and pixel range and the viewport size in pixels. Use the same `xOrigin` in `applyClip` and when uploading vertices. */
export function clipTransform(xScale: Scale, yScale: Scale, viewportWidth: number, viewportHeight: number, xOrigin = 0): ClipTransform {
  // Slope and offset are evaluated *at the origin* so epoch-sized x never meets catastrophic cancellation.
  const kx = xScale.scale(xOrigin + 1) - xScale.scale(xOrigin), bx = xScale.scale(xOrigin);
  const ky = yScale.scale(1) - yScale.scale(0), by = yScale.scale(0);
  const sx = 2 * kx / viewportWidth;
  const ox = 2 * bx / viewportWidth - 1;
  const sy = -2 * ky / viewportHeight;
  const oy = 1 - 2 * by / viewportHeight;
  return { sx, ox, sy, oy };
}

/** Maps one data point to clip space on the CPU, for tests and picking. */
export function applyClip(t: ClipTransform, x: number, y: number, xOrigin = 0): { x: number; y: number } {
  return { x: (x - xOrigin) * t.sx + t.ox, y: y * t.sy + t.oy };
}
