// Mori.SkyScope — 2D geometry shared by scene, charts and gauges.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * 2D geometry shared by scene, charts and gauges. Mirrors `Mori.SkyScope.Core.Scene.Geometry`;
 * pinned by `spec/fixtures/geometry.json`. Screen space is y-down; a positive rotation turns clockwise.
 */
/** A 2D point or vector. */
export interface Vec2 { x: number; y: number }
/** A 3D point or vector (z up in scenes). */
export interface Vec3 { x: number; y: number; z: number }
/** Axis-aligned rectangle: top-left corner plus a non-negative size. */
export interface Rect { x: number; y: number; w: number; h: number }

/** Affine matrix in DOMMatrix order: x' = a·x + c·y + e, y' = b·x + d·y + f. */
export interface Mat3 { a: number; b: number; c: number; d: number; e: number; f: number }

/** The identity affine matrix. */
export const IDENTITY: Mat3 = { a: 1, b: 0, c: 0, d: 1, e: 0, f: 0 };

/** Matrix that moves points by (tx, ty). */
export const translation = (tx: number, ty: number): Mat3 => ({ a: 1, b: 0, c: 0, d: 1, e: tx, f: ty });
/** Matrix that scales x by `sx` and y by `sy` about the origin. */
export const scaling = (sx: number, sy: number): Mat3 => ({ a: sx, b: 0, c: 0, d: sy, e: 0, f: 0 });
/** Matrix rotating by `radians` about the origin (clockwise on a y-down screen). */
export function rotation(radians: number): Mat3 {
  const c = Math.cos(radians), s = Math.sin(radians);
  return { a: c, b: s, c: -s, d: c, e: 0, f: 0 };
}

/** `mul(m, n)` applies `n` first, then `m` (like DOMMatrix.multiply). */
export function mul(m: Mat3, n: Mat3): Mat3 {
  return {
    a: m.a * n.a + m.c * n.b, b: m.b * n.a + m.d * n.b,
    c: m.a * n.c + m.c * n.d, d: m.b * n.c + m.d * n.d,
    e: m.a * n.e + m.c * n.f + m.e, f: m.b * n.e + m.d * n.f + m.f,
  };
}

/** Inverse of an affine matrix, or null when it is singular or non-finite. */
export function invert(m: Mat3): Mat3 | null {
  const det = m.a * m.d - m.b * m.c;
  if (det === 0 || !Number.isFinite(det)) return null;
  const ia = m.d / det, ib = -m.b / det, ic = -m.c / det, id = m.a / det;
  return { a: ia, b: ib, c: ic, d: id, e: -(ia * m.e + ic * m.f), f: -(ib * m.e + id * m.f) };
}

/** Transform a point (translation included). */
export const apply = (m: Mat3, x: number, y: number): Vec2 => ({ x: m.a * x + m.c * y + m.e, y: m.b * x + m.d * y + m.f });
/** Linear part only — for deltas. */
export const applyLinear = (m: Mat3, x: number, y: number): Vec2 => ({ x: m.a * x + m.c * y, y: m.b * x + m.d * y });

/** Bounding box of interleaved xy points, or null when the list is empty. */
export function rectFromPoints(points: ArrayLike<number>): Rect | null {
  const n = points.length / 2;
  if (n === 0) return null;
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (let i = 0; i < n; i++) {
    const x = points[2 * i]!, y = points[2 * i + 1]!;
    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
  }
  return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
}

/** Rectangle spanning two corners given in any order. */
export function rectNormalize(x0: number, y0: number, x1: number, y1: number): Rect {
  return { x: Math.min(x0, x1), y: Math.min(y0, y1), w: Math.abs(x1 - x0), h: Math.abs(y1 - y0) };
}

/** Smallest rectangle covering both; a null operand is ignored. */
export function rectUnion(a: Rect | null, b: Rect | null): Rect | null {
  if (!a) return b; if (!b) return a;
  const x0 = Math.min(a.x, b.x), y0 = Math.min(a.y, b.y);
  return { x: x0, y: y0, w: Math.max(a.x + a.w, b.x + b.w) - x0, h: Math.max(a.y + a.h, b.y + b.h) - y0 };
}

/** True when the point lies inside or on the edge of the rectangle. */
export const rectContains = (r: Rect, x: number, y: number): boolean => x >= r.x && x <= r.x + r.w && y >= r.y && y <= r.y + r.h;

/** Axis-aligned bounding box of a rectangle after transformation. */
export function transformRect(m: Mat3, r: Rect): Rect {
  const pts = [r.x, r.y, r.x + r.w, r.y, r.x + r.w, r.y + r.h, r.x, r.y + r.h];
  const out: number[] = [];
  for (let i = 0; i < 4; i++) { const p = apply(m, pts[2 * i]!, pts[2 * i + 1]!); out.push(p.x, p.y); }
  return rectFromPoints(out)!;
}

/** Distance from the point (px, py) to the segment from (ax, ay) to (bx, by). */
export function distToSegment(px: number, py: number, ax: number, ay: number, bx: number, by: number): number {
  const dx = bx - ax, dy = by - ay;
  const len2 = dx * dx + dy * dy;
  let t = len2 === 0 ? 0 : ((px - ax) * dx + (py - ay) * dy) / len2;
  t = Math.max(0, Math.min(1, t));
  const cx = ax + t * dx - px, cy = ay + t * dy - py;
  return Math.sqrt(cx * cx + cy * cy);
}

/** Ray casting; points interleaved, polygon implicitly closed. */
export function pointInPolygon(px: number, py: number, points: ArrayLike<number>): boolean {
  const n = points.length / 2;
  let inside = false;
  for (let i = 0, j = n - 1; i < n; j = i++) {
    const xi = points[2 * i]!, yi = points[2 * i + 1]!, xj = points[2 * j]!, yj = points[2 * j + 1]!;
    if ((yi > py) !== (yj > py) && px < (xj - xi) * (py - yi) / (yj - yi) + xi) inside = !inside;
  }
  return inside;
}
