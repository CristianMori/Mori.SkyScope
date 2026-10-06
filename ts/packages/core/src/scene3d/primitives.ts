// Mori.SkyScope — Unit meshes shared by markers: cube (±0.5), sphere (radius 0.5), cylinder (radius 0.5 along z, height 1), cone (base radius 0.5 at z = 0,…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { createMesh, type Mesh3D } from "./painter3d.js";

/**
 * Unit meshes shared by markers: cube (±0.5), sphere (radius 0.5), cylinder (radius 0.5 along z, height 1),
 * cone (base radius 0.5 at z = 0, apex at z = 1) and an arrow along +x (shaft + head, length 1). Flat normals,
 * built the same way in both cores so `RecordingPainter3D` hashes agree. Mirrors `Scene3D/Primitives.cs`.
 */
const cache = new Map<string, Mesh3D>();
function build(key: string, tris: number[]): Mesh3D {
  let m = cache.get(key);
  if (m) return m;
  const pos = Float32Array.from(tris), n = pos.length / 9, nor = new Float32Array(pos.length);
  for (let t = 0; t < n; t++) {
    const o = t * 9;
    const ax = pos[o + 3]! - pos[o]!, ay = pos[o + 4]! - pos[o + 1]!, az = pos[o + 5]! - pos[o + 2]!;
    const bx = pos[o + 6]! - pos[o]!, by = pos[o + 7]! - pos[o + 1]!, bz = pos[o + 8]! - pos[o + 2]!;
    let nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
    const l = Math.sqrt(nx * nx + ny * ny + nz * nz) || 1; nx /= l; ny /= l; nz /= l;
    for (let k = 0; k < 3; k++) { nor[o + 3 * k] = nx; nor[o + 3 * k + 1] = ny; nor[o + 3 * k + 2] = nz; }
  }
  m = createMesh(pos, { key: `unit:${key}`, normals: nor });
  cache.set(key, m);
  return m;
}
const tri = (out: number[], a: number[], b: number[], c: number[]): void => { out.push(a[0]!, a[1]!, a[2]!, b[0]!, b[1]!, b[2]!, c[0]!, c[1]!, c[2]!); };
const quad = (out: number[], a: number[], b: number[], c: number[], d: number[]): void => { tri(out, a, b, c); tri(out, a, c, d); };

/** Cube of side 1 centred at the origin (12 triangles), cached. */
export function unitCube(): Mesh3D {
  return build("cube", (() => {
    const o: number[] = [], h = 0.5;
    const p = (x: number, y: number, z: number): number[] => [x * h, y * h, z * h];
    quad(o, p(-1, -1, 1), p(1, -1, 1), p(1, 1, 1), p(-1, 1, 1));       // +z
    quad(o, p(1, -1, -1), p(-1, -1, -1), p(-1, 1, -1), p(1, 1, -1));   // −z
    quad(o, p(1, -1, 1), p(1, -1, -1), p(1, 1, -1), p(1, 1, 1));       // +x
    quad(o, p(-1, -1, -1), p(-1, -1, 1), p(-1, 1, 1), p(-1, 1, -1));   // −x
    quad(o, p(-1, 1, 1), p(1, 1, 1), p(1, 1, -1), p(-1, 1, -1));       // +y
    quad(o, p(-1, -1, -1), p(1, -1, -1), p(1, -1, 1), p(-1, -1, 1));   // −y
    return o;
  })());
}

/**
 * UV sphere of radius 0.5 centred at the origin: `rings` latitude bands and `segments` longitude slices, cached per
 * resolution.
 */
export function unitSphere(rings = 8, segments = 16): Mesh3D {
  return build(`sphere:${rings}:${segments}`, (() => {
    const o: number[] = [], r = 0.5;
    const at = (i: number, j: number): number[] => { const th = Math.PI * i / rings, ph = 2 * Math.PI * j / segments; return [r * Math.sin(th) * Math.cos(ph), r * Math.sin(th) * Math.sin(ph), r * Math.cos(th)]; };
    for (let i = 0; i < rings; i++) for (let j = 0; j < segments; j++) {
      const a = at(i, j), b = at(i + 1, j), c = at(i + 1, j + 1), d = at(i, j + 1);
      if (i > 0) tri(o, a, b, d);
      if (i < rings - 1) tri(o, b, c, d);
    }
    return o;
  })());
}

/** Cylinder of radius 0.5 along z from −0.5 to 0.5 with capped ends, cached per `segments`. */
export function unitCylinder(segments = 16): Mesh3D {
  return build(`cylinder:${segments}`, (() => {
    const o: number[] = [], r = 0.5;
    const ring = (j: number, z: number): number[] => { const ph = 2 * Math.PI * j / segments; return [r * Math.cos(ph), r * Math.sin(ph), z]; };
    for (let j = 0; j < segments; j++) {
      quad(o, ring(j, -0.5), ring(j + 1, -0.5), ring(j + 1, 0.5), ring(j, 0.5));
      tri(o, [0, 0, 0.5], ring(j, 0.5), ring(j + 1, 0.5));
      tri(o, [0, 0, -0.5], ring(j + 1, -0.5), ring(j, -0.5));
    }
    return o;
  })());
}

/** Cone with base radius 0.5 at z = 0 and apex at z = 1, with a capped base, cached per `segments`. */
export function unitCone(segments = 16): Mesh3D {
  return build(`cone:${segments}`, (() => {
    const o: number[] = [], r = 0.5;
    const ring = (j: number): number[] => { const ph = 2 * Math.PI * j / segments; return [r * Math.cos(ph), r * Math.sin(ph), 0]; };
    for (let j = 0; j < segments; j++) { tri(o, ring(j), ring(j + 1), [0, 0, 1]); tri(o, [0, 0, 0], ring(j + 1), ring(j)); }
    return o;
  })());
}

/** Arrow along +x: shaft (radius 0.1, x ∈ [0, 0.7]) and head (base radius 0.25, x ∈ [0.7, 1]). */
export function unitArrow(segments = 12): Mesh3D {
  return build(`arrow:${segments}`, (() => {
    const o: number[] = [];
    const ring = (j: number, x: number, r: number): number[] => { const ph = 2 * Math.PI * j / segments; return [x, r * Math.cos(ph), r * Math.sin(ph)]; };
    for (let j = 0; j < segments; j++) {
      quad(o, ring(j, 0, 0.1), ring(j, 0.7, 0.1), ring(j + 1, 0.7, 0.1), ring(j + 1, 0, 0.1));
      tri(o, [0, 0, 0], ring(j, 0, 0.1), ring(j + 1, 0, 0.1));
      quad(o, ring(j, 0.7, 0.1), ring(j, 0.7, 0.25), ring(j + 1, 0.7, 0.25), ring(j + 1, 0.7, 0.1));
      tri(o, ring(j, 0.7, 0.25), [1, 0, 0], ring(j + 1, 0.7, 0.25));
    }
    return o;
  })());
}
