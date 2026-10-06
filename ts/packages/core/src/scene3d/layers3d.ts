// Mori.SkyScope — A layer that lives in 3D: draws through ctx.painter3d, reports 3D bounds for fit-all.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { BaseLayer, type HitContext, type HitResult, type Layer, type LayerContext, type Scene } from "../scene/scene.js";
import type { CameraKind, Projection } from "../scene/camera.js";
import { colormapLut, colormapStops, parseHex, type ColormapName } from "../charts/colormaps.js";
import { box3FromPositions, box3Union, mat4Point, v3, type Box3, type Mat4 } from "./math3.js";
import { createMesh, type Mesh3D } from "./painter3d.js";
import type { FrameTree } from "./frame-tree.js";
import type { Camera3D } from "./camera3d.js";

/** A layer that lives in 3D: draws through `ctx.painter3d`, reports 3D bounds for fit-all. */
export interface Layer3D extends Layer { bounds3(): Box3 | null }
/** Type guard: a layer that implements `bounds3`. */
export const isLayer3D = (l: Layer): l is Layer3D => "bounds3" in l;
/** Type guard: a projection that is a `Camera3D` (kind "3d" with `project3`). */
export const isCamera3D = (p: Projection): p is Camera3D => p.kind === "3d" && "project3" in p;

/** Union of visible 3D layers' bounds (for fit-all). */
export function sceneBounds3(scene: Scene): Box3 | null {
  let b: Box3 | null = null;
  for (const l of scene.layers) if (l.visible && isLayer3D(l)) b = box3Union(b, l.bounds3());
  return b;
}

/**
 * Base for 3D layers: camera kind "3d" only and never surface-cached, because the renderer owns the vertex buffers.
 */
export abstract class BaseLayer3D extends BaseLayer implements Layer3D {
  /** Always ["3d"]. */
  override readonly cameraKinds: readonly CameraKind[] = ["3d"];
  /** Assigns the id and disables surface caching. */
  constructor(id: string) { super(id); this.cacheable = false; }
  /** No extent by default. */
  bounds3(): Box3 | null { return null; }
}

/** Construction options for `Grid3DLayer`. */
export interface Grid3DOptions {
  /** Half extent in world units; lines run from −size to +size. */
  size?: number | undefined;
  /** World units between lines; default 1. */
  spacing?: number | undefined;
  /** Every n-th line is drawn in `majorColor`. */
  majorEvery?: number | undefined;
  /** Hex colours for minor and major lines. */
  color?: string | undefined; majorColor?: string | undefined;
  /** Height of the grid plane. */
  z?: number | undefined;
}

/** A metric ground grid on the plane z = `z`. Excluded from fit-all bounds. */
export class Grid3DLayer extends BaseLayer3D {
  /** Always "grid3d". */
  readonly kind = "grid3d";
  /** Current settings (see `Grid3DOptions`); the line mesh is rebuilt when any of them changes. */
  size: number; spacing: number; majorEvery: number; color: string; majorColor: string; z: number;
  private mesh: Mesh3D | null = null;
  private built = "";
  /** Defaults: half extent 10, spacing 1, every 5th line major, slate colours, z = 0. */
  constructor(id: string, o: Grid3DOptions = {}) {
    super(id);
    this.size = o.size ?? 10; this.spacing = o.spacing ?? 1; this.majorEvery = o.majorEvery ?? 5;
    this.color = o.color ?? "#3f4a5a"; this.majorColor = o.majorColor ?? "#6b7a90"; this.z = o.z ?? 0;
  }
  private build(): Mesh3D {
    const sig = `${this.size}|${this.spacing}|${this.majorEvery}|${this.color}|${this.majorColor}|${this.z}`;
    if (this.mesh && this.built === sig) return this.mesh;
    const n = Math.max(0, Math.round(this.size / this.spacing)), s = n * this.spacing;
    const pos: number[] = [], col: number[] = [];
    const minor = parseHex(this.color), major = parseHex(this.majorColor);
    for (let i = -n; i <= n; i++) {
      const v = i * this.spacing, c = this.majorEvery > 0 && i % this.majorEvery === 0 ? major : minor;
      pos.push(v, -s, this.z, v, s, this.z, -s, v, this.z, s, v, this.z);
      for (let k = 0; k < 4; k++) col.push(c[0], c[1], c[2], 255);
    }
    this.mesh = createMesh(Float32Array.from(pos), { key: `grid3d:${this.id}`, colors: Uint8Array.from(col) });
    this.built = sig;
    return this.mesh;
  }
  /** Draws the grid lines 1 px wide. */
  draw(ctx: LayerContext): void { ctx.painter3d?.lines(this.build(), { opacity: ctx.opacity, lineWidth: 1 }); }
}

/** Construction options for `AxesLayer`. */
export interface AxesLayerOptions {
  /** Frame tree the transforms are read from. */
  frames: FrameTree;
  /** The frame the scene is drawn in. */
  fixedFrame: string;
  /** Frames to draw; every known frame when omitted. */
  ids?: readonly string[] | undefined;
  /** Axis length in world units. */
  length?: number | undefined;
  /** Axis line width in pixels; default 2. */
  lineWidth?: number | undefined;
}

/** An RGB triad (x red, y green, z blue) at every frame of a `FrameTree`, looked up at `ctx.now`. */
export class AxesLayer extends BaseLayer3D {
  /** Always "axes". */
  readonly kind = "axes";
  /**
   * The frame tree, the frame drawn in, the frame filter (null = all), the axis length in world units and the line
   * width in pixels.
   */
  readonly frames: FrameTree; fixedFrame: string; ids: readonly string[] | null; length: number; lineWidth: number;
  private readonly mesh: Mesh3D = createMesh(new Float32Array(0), { key: "", colors: new Uint8Array(0) });
  private builtFor = { version: -1, now: NaN, ids: "", fixed: "", length: 0 };
  /** Frames drawn in the last build (in mesh order). */
  drawn: string[] = [];
  /** Defaults: all frames, 0.5 world units long, 2 px lines. */
  constructor(id: string, o: AxesLayerOptions) {
    super(id);
    this.frames = o.frames; this.fixedFrame = o.fixedFrame; this.ids = o.ids ? [...o.ids] : null; this.length = o.length ?? 0.5; this.lineWidth = o.lineWidth ?? 2;
    (this.mesh as { key: string }).key = `axes:${id}`;
  }
  private build(now: number): Mesh3D {
    const idsKey = this.ids ? this.ids.join(",") : "*";
    const b = this.builtFor;
    if (b.version === this.frames.version && b.now === now && b.ids === idsKey && b.fixed === this.fixedFrame && b.length === this.length) return this.mesh;
    const ids = this.ids ?? this.frames.frameIds();
    const pos: number[] = [], col: number[] = [], L = this.length;
    this.drawn = [];
    for (const f of ids) {
      const m = this.frames.lookup(this.fixedFrame, f, now);
      if (!m) continue;
      this.drawn.push(f);
      const o = mat4Point(m, 0, 0, 0), x = mat4Point(m, L, 0, 0), y = mat4Point(m, 0, L, 0), z = mat4Point(m, 0, 0, L);
      pos.push(o.x, o.y, o.z, x.x, x.y, x.z, o.x, o.y, o.z, y.x, y.y, y.z, o.x, o.y, o.z, z.x, z.y, z.z);
      col.push(220, 38, 38, 255, 220, 38, 38, 255, 22, 163, 74, 255, 22, 163, 74, 255, 37, 99, 235, 255, 37, 99, 235, 255);
    }
    this.mesh.positions = Float32Array.from(pos); this.mesh.colors = Uint8Array.from(col); this.mesh.version++;
    this.builtFor = { version: this.frames.version, now, ids: idsKey, fixed: this.fixedFrame, length: L };
    return this.mesh;
  }
  /**
   * Rebuilds the triads for `ctx.now` when the tree, time or settings changed, then draws them without depth test.
   */
  draw(ctx: LayerContext): void { const m = this.build(ctx.now); if (m.positions.length) ctx.painter3d?.lines(m, { opacity: ctx.opacity, lineWidth: this.lineWidth, depthTest: false }); }
  /** Bounds of the triads from the last build, or null before the first draw. */
  override bounds3(): Box3 | null { return this.mesh.positions.length ? box3FromPositions(this.mesh.positions) : null; }
}

/** Construction options for `PointCloud3DLayer`. */
export interface PointCloud3DOptions {
  /** Frame the points are expressed in; drawn through `frames` into `fixedFrame` when both are set. */
  frame?: string | undefined;
  /** Frame tree for the lookup. */
  frames?: FrameTree | undefined;
  /** Frame the scene is drawn in. */
  fixedFrame?: string | undefined;
  /** Colormap name or list of hex stops for intensities; default "turbo". */
  colormap?: ColormapName | string[] | undefined;
  /** Fixed intensity range for the colormap; defaults to the data's min and max. */
  intensityRange?: [number, number] | undefined;
  /** Flat colour when the cloud has neither intensities nor colours; default white. */
  color?: string | undefined;
  /** Screen pixels. */
  pointSize?: number | undefined;
}

/** A 3D point cloud: xyz positions, coloured flat, by intensity through a colormap, or by RGBA bytes. */
export class PointCloud3DLayer extends BaseLayer3D {
  /** "pointCloud3d"; subclasses override it. */
  readonly kind: string = "pointCloud3d";
  /**
   * Frame the points are expressed in, the tree and the fixed frame; null for any of them draws in the fixed frame
   * directly.
   */
  frame: string | null; frames: FrameTree | null; fixedFrame: string | null;
  /** Colouring and point size (see `PointCloud3DOptions`); colours are recomputed on the next `setPoints`. */
  colormap: ColormapName | string[]; intensityRange: [number, number] | null; color: string; pointSize: number;
  /** The renderer buffer: positions xyz and, when coloured, RGBA bytes per point. */
  readonly mesh: Mesh3D;
  private intensities: Float32Array | null = null;
  /** Time of the latest cloud (for frame lookup); null uses the latest transform. */
  stamp: number | null = null;
  /** Starts empty; defaults to the turbo colormap, data-range scaling, white, 2 px. */
  constructor(id: string, o: PointCloud3DOptions = {}) {
    super(id);
    this.frame = o.frame ?? null; this.frames = o.frames ?? null; this.fixedFrame = o.fixedFrame ?? null;
    this.colormap = o.colormap ?? "turbo"; this.intensityRange = o.intensityRange ?? null; this.color = o.color ?? "#ffffff"; this.pointSize = o.pointSize ?? 2;
    this.mesh = createMesh(new Float32Array(0), { key: `pointCloud3d:${id}` });
  }
  /** Number of points in the mesh. */
  get pointCount(): number { return Math.floor(this.mesh.positions.length / 3); }
  /** Replace the cloud. `colors` (RGBA per point) wins over `intensities`. */
  setPoints(positions: Float32Array, intensities?: Float32Array | null, colors?: Uint8Array | null, stamp?: number): void {
    this.mesh.positions = positions;
    this.intensities = intensities ?? null;
    if (colors) this.mesh.colors = colors;
    else if (intensities) this.mesh.colors = this.colorize(intensities);
    else this.mesh.colors = undefined;
    this.mesh.count = undefined;
    this.mesh.version++;
    this.stamp = stamp ?? null;
    this.markDirty();
  }
  private colorize(v: Float32Array): Uint8Array {
    const lut = colormapLut(colormapStops(this.colormap), 256);
    let lo: number, hi: number;
    if (this.intensityRange) [lo, hi] = this.intensityRange;
    else { lo = Infinity; hi = -Infinity; for (const x of v) { if (x < lo) lo = x; if (x > hi) hi = x; } if (!(Number.isFinite(lo) && hi > lo)) { lo = 0; hi = 1; } }
    const span = Math.max(1e-12, hi - lo), out = new Uint8Array(v.length * 4);
    for (let i = 0; i < v.length; i++) {
      let k = Math.floor((v[i]! - lo) / span * 255 + 0.5);
      if (k < 0) k = 0; else if (k > 255) k = 255;
      out[4 * i] = lut[k * 3]!; out[4 * i + 1] = lut[k * 3 + 1]!; out[4 * i + 2] = lut[k * 3 + 2]!; out[4 * i + 3] = 255;
    }
    return out;
  }
  /** Model matrix (frame → fixed frame) at `now`, or null when the frame is unknown. */
  model(now: number): Mat4 | null | undefined {
    if (!this.frame || !this.frames || !this.fixedFrame) return undefined;
    return this.frames.lookup(this.fixedFrame, this.frame, this.stamp ?? now);
  }
  /** Draws the cloud as points; skipped when empty or when the frame cannot be resolved. */
  draw(ctx: LayerContext): void {
    if (!this.pointCount) return;
    const model = this.model(ctx.now);
    if (model === null) return;
    ctx.painter3d?.points(this.mesh, { color: this.mesh.colors ? "#ffffff" : this.color, opacity: ctx.opacity, pointSize: this.pointSize, model: model ?? undefined });
  }
  /** Bounds of the points in the fixed frame (transform looked up at `stamp`), or null when unresolved or empty. */
  override bounds3(): Box3 | null { const m = this.model(this.stamp ?? 0); return m === null ? null : box3FromPositions(this.mesh.positions, m ?? undefined); }
  /**
   * Nearest visible point within `tolerance + pointSize / 2` pixels under a `Camera3D`; `data` is its intensity when
   * present.
   */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    if (!isCamera3D(ctx.projection)) return null;
    const cam = ctx.projection, p = this.mesh.positions, model = this.model(ctx.now);
    if (model === null) return null;
    let best = Infinity, bestIndex = -1, bw = v3(0, 0, 0);
    for (let i = 0; i < this.pointCount; i++) {
      let x = p[3 * i]!, y = p[3 * i + 1]!, z = p[3 * i + 2]!;
      if (model) { const w = mat4Point(model, x, y, z); x = w.x; y = w.y; z = w.z; }
      const q = cam.project3(x, y, z);
      if (!q.visible) continue;
      const d = Math.hypot(q.x - sx, q.y - sy);
      if (d < best) { best = d; bestIndex = i; bw = v3(x, y, z); }
    }
    if (bestIndex < 0 || best > tolerance + this.pointSize / 2) return null;
    return { layerId: this.id, world: bw, screen: { x: sx, y: sy }, distance: best, index: bestIndex, data: this.intensities?.[bestIndex] };
  }
}

/** Payload shape of a `frames` layer push: transforms to record in the shared `FrameTree`. */
export interface FrameTransformMessage { child: string; parent: string; t: [number, number, number]; q: [number, number, number, number]; time?: number | undefined }

/** An invisible layer whose pushes feed the scene's `FrameTree` (`{ transforms: FrameTransformMessage[] }`). */
export class FramesLayer extends BaseLayer3D {
  /** Always "frames". */
  readonly kind = "frames";
  /** `frames` is the shared tree that `apply` writes to. */
  constructor(id: string, readonly frames: FrameTree) { super(id); }
  /** Record every transform in the tree (static when `time` is absent). */
  apply(msgs: readonly FrameTransformMessage[]): void {
    for (const m of msgs) this.frames.set(m.child, m.parent, { t: v3(m.t[0], m.t[1], m.t[2]), q: { x: m.q[0], y: m.q[1], z: m.q[2], w: m.q[3] } }, m.time);
  }
  /** Draws nothing. */
  draw(): void {}
}
