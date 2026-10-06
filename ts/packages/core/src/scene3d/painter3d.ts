// Mori.SkyScope — The 3D rendering contract.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { ImageHandle } from "../paint/painter.js";
import { r3 } from "../paint/recording-painter.js";
import { fnv1a } from "../charts/colormaps.js";
import type { Mat4 } from "./math3.js";

/**
 * The 3D rendering contract. The core builds versioned vertex buffers (`Mesh3D`) and calls these primitives; a
 * renderer (WebGL2, OpenTK) uploads each buffer once per `version` and draws it. Mirrors
 * `Mori.SkyScope.Core.Scene3D.IPainter3D`. Positions are interleaved xyz in world units (or in the space of
 * `Material3D.model` when given); colours are RGBA bytes per vertex; screen sizes are CSS pixels.
 */
export interface Mesh3D {
  /** Stable identity for the renderer's buffer cache. */
  readonly key: string;
  /** Bump after changing any array so renderers re-upload. */
  version: number;
  /** Interleaved xyz, float32. */
  positions: Float32Array;
  /** RGBA bytes per vertex, optional. */
  colors?: Uint8Array | undefined;
  /** Unit normals per vertex; needed for `lit` triangles. */
  normals?: Float32Array | undefined;
  /** Triangle (or line) indices; absent for unindexed geometry. */
  indices?: Uint32Array | undefined;
  /** Vertices (or indices) to draw; defaults to the whole buffer. */
  count?: number | undefined;
}

/** Per-draw appearance; every field is optional and resolved by `resolveMaterial`. */
export interface Material3D {
  /** Flat colour, multiplied with per-vertex colours when both are present. Default white. */
  color?: string | undefined;
  /** 0..1, default 1. */
  opacity?: number | undefined;
  /** Points: diameter in pixels. */
  pointSize?: number | undefined;
  /** Lines: width in pixels. */
  lineWidth?: number | undefined;
  /** Default true; false draws on top of everything (overlays). */
  depthTest?: boolean | undefined;
  /** Triangles: simple head-light shading from `normals`. */
  lit?: boolean | undefined;
  /** Model matrix applied to positions. */
  model?: Mat4 | undefined;
}

/** `Material3D` with every default applied (`model` null when none). */
export interface ResolvedMaterial3D { color: string; opacity: number; pointSize: number; lineWidth: number; depthTest: boolean; lit: boolean; model: Mat4 | null }
/** Apply the defaults: white, opaque, 3 px points, 1 px lines, depth test on, unlit, no model matrix. */
export function resolveMaterial(m: Material3D | undefined): ResolvedMaterial3D {
  return { color: m?.color ?? "#ffffff", opacity: m?.opacity ?? 1, pointSize: m?.pointSize ?? 3, lineWidth: m?.lineWidth ?? 1, depthTest: m?.depthTest ?? true, lit: m?.lit ?? false, model: m?.model ?? null };
}

/**
 * One frame is `begin`, any number of draw calls, then `end`. Implementations cache buffers by `Mesh3D.key` and
 * re-upload when `version` changes.
 */
export interface Painter3D {
  /** Viewport width in CSS pixels. */
  readonly width: number;
  /** Viewport height in CSS pixels. */
  readonly height: number;
  /** Device pixels per CSS pixel. */
  readonly pixelRatio: number;
  /** Start a frame: camera matrices and an optional clear colour. */
  begin(view: Mat4, proj: Mat4, clear?: string | null): void;
  /** Draw every vertex as a square point. */
  points(mesh: Mesh3D, material?: Material3D): void;
  /** Line list (pairs) or, with `strip`, a connected polyline. */
  lines(mesh: Mesh3D, material?: Material3D, strip?: boolean): void;
  /** Draw a triangle list (indexed when `indices` is present). */
  triangles(mesh: Mesh3D, material?: Material3D): void;
  /** A textured quad; `corners` holds 4 × xyz in the order bottom-left, bottom-right, top-right, top-left. */
  image(image: ImageHandle, corners: ArrayLike<number>, material?: Material3D): void;
  /** Finish the frame. */
  end(): void;
}

let meshSeq = 0;
/** Create a mesh with a unique key. */
export function createMesh(positions: Float32Array, o: { key?: string; colors?: Uint8Array; normals?: Float32Array; indices?: Uint32Array; count?: number } = {}): Mesh3D {
  const m: Mesh3D = { key: o.key ?? `mesh:${++meshSeq}`, version: 0, positions };
  if (o.colors) m.colors = o.colors; if (o.normals) m.normals = o.normals; if (o.indices) m.indices = o.indices; if (o.count !== undefined) m.count = o.count;
  return m;
}
/** Vertices (or indices) a draw call covers: `count` when set, else the index count, else the position count. */
export const meshVertexCount = (m: Mesh3D): number => m.count ?? (m.indices ? m.indices.length : Math.floor(m.positions.length / 3));

const bytesOf = (a: Float32Array | Uint8Array | Uint32Array): Uint8Array => new Uint8Array(a.buffer, a.byteOffset, a.byteLength);
/** Hash of a mesh's arrays (little-endian bytes) — how the fixtures compare buffers across cores. */
export function meshHash(m: Mesh3D): { positions: number; colors: number | null; normals: number | null; indices: number | null } {
  return { positions: fnv1a(bytesOf(m.positions)), colors: m.colors ? fnv1a(bytesOf(m.colors)) : null, normals: m.normals ? fnv1a(bytesOf(m.normals)) : null, indices: m.indices ? fnv1a(bytesOf(m.indices)) : null };
}

/** One recorded call: `op` names it, the other keys are its arguments (matrices rounded to 3 decimals). */
export interface Paint3DOp { op: string; [key: string]: unknown }
const rMat = (m: Mat4 | null): number[] | null => (m ? m.map(r3) : null);

/** A Painter3D that records what it is asked to draw; parity fixtures compare the recordings of both cores. */
export class RecordingPainter3D implements Painter3D {
  /** Every recorded call, in order. */
  readonly ops: Paint3DOp[] = [];
  /** Viewport size in CSS pixels and device pixel ratio (default 1). */
  constructor(readonly width: number, readonly height: number, readonly pixelRatio = 1) {}
  private push(op: Paint3DOp): void { this.ops.push(op); }
  private geom(op: string, mesh: Mesh3D, material: Material3D | undefined, extra: Record<string, unknown> = {}): void {
    const r = resolveMaterial(material);
    this.push({ op, key: mesh.key, version: mesh.version, count: meshVertexCount(mesh), hash: meshHash(mesh), material: { ...r, model: rMat(r.model) }, ...extra });
  }
  /** Records the view and projection matrices and the clear colour. */
  begin(view: Mat4, proj: Mat4, clear: string | null = null): void { this.push({ op: "begin", view: rMat(view), proj: rMat(proj), clear }); }
  /** Records a points call with the mesh's hash and the resolved material. */
  points(mesh: Mesh3D, material?: Material3D): void { this.geom("points", mesh, material); }
  /** Records a lines call with the `strip` flag. */
  lines(mesh: Mesh3D, material?: Material3D, strip = false): void { this.geom("lines", mesh, material, { strip }); }
  /** Records a triangles call. */
  triangles(mesh: Mesh3D, material?: Material3D): void { this.geom("triangles", mesh, material); }
  /** Records an image call with the image size and the rounded corners. */
  image(image: ImageHandle, corners: ArrayLike<number>, material?: Material3D): void {
    const r = resolveMaterial(material);
    this.push({ op: "image", imageWidth: image.width, imageHeight: image.height, corners: Array.from(corners, r3), material: { ...r, model: rMat(r.model) } });
  }
  /** Records the end of the frame. */
  end(): void { this.push({ op: "end" }); }
}
