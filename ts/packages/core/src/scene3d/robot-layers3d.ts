// Mori.SkyScope — Robotics 3D layers: paths, poses, markers (including meshes), laser scans, occupancy grid planes, and the mesh registry.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { HitContext, HitResult, LayerContext } from "../scene/scene.js";
import { OccupancyGridLayer, laserScanToPoints, rasterToWorld, type LaserScanLike, type OccupancyGridOptions } from "../scene/robot-layers.js";
import { apply } from "../scene/geometry.js";
import type { Vec3 } from "../scene/geometry.js";
import { BaseLayer3D, PointCloud3DLayer, isCamera3D, type PointCloud3DOptions } from "./layers3d.js";
import type { FrameTree } from "./frame-tree.js";
import { box3FromPositions, box3Sphere, box3Union, mat4FromPose, mat4Mul, mat4Point, mat4Scaling, raySphere, v3, v3sub, MAT4_IDENTITY, QUAT_IDENTITY, type Box3, type Mat4, type Quat } from "./math3.js";
import { createMesh, type Mesh3D } from "./painter3d.js";
import { unitArrow, unitCube, unitCylinder, unitSphere } from "./primitives.js";
import { meshFromParsed, type ParsedMesh } from "./mesh-formats.js";

/** Loaded mesh resources by URI, shared by marker layers and robot models of one scene (via `LayerEnv.meshes`). */
export class MeshRegistry {
  private readonly meshes = new Map<string, Mesh3D>();
  private _version = 0;
  /** Bumped on every `set` and on a non-empty `clear`. */
  get version(): number { return this._version; }
  /**
   * Build a renderer mesh (key `mesh:<uri>`) from a parsed resource and store it under `uri`, replacing any previous
   * one.
   */
  set(uri: string, parsed: ParsedMesh): Mesh3D { const m = meshFromParsed(`mesh:${uri}`, parsed); this.meshes.set(uri, m); this._version++; return m; }
  /** The mesh for a URI, or undefined when not loaded. */
  get(uri: string): Mesh3D | undefined { return this.meshes.get(uri); }
  /** True when a mesh is registered for the URI. */
  has(uri: string): boolean { return this.meshes.has(uri); }
  /** Registered URIs, sorted. */
  uris(): string[] { return [...this.meshes.keys()].sort(); }
  /** Drop every mesh. */
  clear(): void { if (this.meshes.size) { this.meshes.clear(); this._version++; } }
}

/** Frame plumbing shared by the robot layers: a model matrix from the layer's frame into the fixed frame at a time. */
export interface FrameOptions { frame?: string | undefined; frames?: FrameTree | undefined; fixedFrame?: string | undefined }
function modelFor(o: { frame: string | null; frames: FrameTree | null; fixedFrame: string | null }, time: number | undefined): Mat4 | null | undefined {
  if (!o.frame || !o.frames || !o.fixedFrame) return undefined;
  return o.frames.lookup(o.fixedFrame, o.frame, time);
}
function nearestVertex(cam: { project3(x: number, y: number, z: number): { x: number; y: number; visible: boolean } }, p: ArrayLike<number>, count: number, model: Mat4 | null | undefined, sx: number, sy: number): { index: number; distance: number; world: Vec3 } | null {
  let best = Infinity, bi = -1, bw = v3(0, 0, 0);
  for (let i = 0; i < count; i++) {
    let x = p[3 * i]!, y = p[3 * i + 1]!, z = p[3 * i + 2]!;
    if (model) { const w = mat4Point(model, x, y, z); x = w.x; y = w.y; z = w.z; }
    const q = cam.project3(x, y, z);
    if (!q.visible) continue;
    const d = Math.hypot(q.x - sx, q.y - sy);
    if (d < best) { best = d; bi = i; bw = v3(x, y, z); }
  }
  return bi < 0 ? null : { index: bi, distance: best, world: bw };
}

/**
 * Construction options for `Path3DLayer`: frame plumbing, hex colour, line width in pixels and the cap on retained
 * points (default 10000).
 */
export interface Path3DOptions extends FrameOptions { color?: string | undefined; lineWidth?: number | undefined; maxPoints?: number | undefined }

/** A polyline in 3D (nav_msgs/Path, a trajectory, a trail); `append` keeps the newest `maxPoints`. */
export class Path3DLayer extends BaseLayer3D {
  /** Always "path3d". */
  readonly kind = "path3d";
  /**
   * Frame the points are expressed in, the tree and the fixed frame; null for any of them draws in the fixed frame
   * directly.
   */
  frame: string | null; frames: FrameTree | null; fixedFrame: string | null;
  /** Hex colour, line width in pixels and the maximum number of points kept. */
  color: string; lineWidth: number; maxPoints: number;
  /** The renderer buffer: positions xyz, drawn as a line strip. */
  readonly mesh: Mesh3D;
  /** Time of the latest update, used for the frame lookup; null uses the latest transform. */
  stamp: number | null = null;
  /** Starts empty; defaults to cyan, 2 px, 10000 points. */
  constructor(id: string, o: Path3DOptions = {}) {
    super(id);
    this.frame = o.frame ?? null; this.frames = o.frames ?? null; this.fixedFrame = o.fixedFrame ?? null;
    this.color = o.color ?? "#22d3ee"; this.lineWidth = o.lineWidth ?? 2; this.maxPoints = o.maxPoints ?? 10000;
    this.mesh = createMesh(new Float32Array(0), { key: `path3d:${id}` });
  }
  /** Number of vertices. */
  get pointCount(): number { return Math.floor(this.mesh.positions.length / 3); }
  /**
   * Replace the vertices (keeping only the newest `maxPoints`, the array itself when it fits) and record the stamp.
   */
  setPoints(positions: Float32Array, stamp?: number): void {
    const max = this.maxPoints * 3;
    this.mesh.positions = positions.length > max ? positions.slice(positions.length - max) : positions;
    this.mesh.version++; this.stamp = stamp ?? null; this.markDirty();
  }
  /** Add a vertex, dropping the oldest one once `maxPoints` is reached. */
  append(x: number, y: number, z: number): void {
    const p = this.mesh.positions, n = p.length;
    if (n >= this.maxPoints * 3) { const q = new Float32Array(n); q.set(p.subarray(3)); q[n - 3] = x; q[n - 2] = y; q[n - 1] = z; this.mesh.positions = q; }
    else { const q = new Float32Array(n + 3); q.set(p); q[n] = x; q[n + 1] = y; q[n + 2] = z; this.mesh.positions = q; }
    this.mesh.version++; this.markDirty();
  }
  /** Remove every vertex. */
  clear(): void { this.mesh.positions = new Float32Array(0); this.mesh.version++; this.markDirty(); }
  /**
   * Matrix from the layer's frame into the fixed frame at `stamp` (or `now` when unset); undefined when no frame is
   * configured, null when the lookup fails.
   */
  model(now: number): Mat4 | null | undefined { return modelFor(this, this.stamp ?? now); }
  /** Draws the line strip; skipped with fewer than two points or an unresolved frame. */
  draw(ctx: LayerContext): void {
    if (this.pointCount < 2) return;
    const model = this.model(ctx.now);
    if (model === null) return;
    ctx.painter3d?.lines(this.mesh, { color: this.color, opacity: ctx.opacity, lineWidth: this.lineWidth, model: model ?? undefined }, true);
  }
  /** Bounds of the vertices in the fixed frame, or null when unresolved or empty. */
  override bounds3(): Box3 | null { const m = this.model(this.stamp ?? 0); return m === null ? null : box3FromPositions(this.mesh.positions, m ?? undefined); }
  /** Nearest visible vertex within `tolerance + lineWidth` pixels under a `Camera3D`. */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    if (!isCamera3D(ctx.projection)) return null;
    const model = this.model(ctx.now);
    if (model === null) return null;
    const h = nearestVertex(ctx.projection, this.mesh.positions, this.pointCount, model, sx, sy);
    if (!h || h.distance > tolerance + this.lineWidth) return null;
    return { layerId: this.id, world: h.world, screen: { x: sx, y: sy }, distance: h.distance, index: h.index };
  }
}

/** Rigid pose: translation plus unit quaternion. */
export interface Pose3D { t: Vec3; q: Quat }
/**
 * Construction options for `Pose3DLayer`: frame plumbing, initial pose, hex colour, axis length in world units, label
 * and whether to draw the triad.
 */
export interface Pose3DOptions extends FrameOptions { pose?: Pose3D | undefined; color?: string | undefined; axisLength?: number | undefined; label?: string | undefined; showAxes?: boolean | undefined }

/** A pose (geometry_msgs/PoseStamped, an odometry estimate): an arrow along its +x, an optional axes triad and label. */
export class Pose3DLayer extends BaseLayer3D {
  /** Always "pose3d". */
  readonly kind = "pose3d";
  /**
   * Frame the pose is expressed in, the tree and the fixed frame; null for any of them draws in the fixed frame
   * directly.
   */
  frame: string | null; frames: FrameTree | null; fixedFrame: string | null;
  /** Current pose, hex colour of arrow and label, triad length in world units, optional label and triad toggle. */
  pose: Pose3D; color: string; axisLength: number; label: string | null; showAxes: boolean;
  /** Time of the latest pose, used for the frame lookup; null uses the latest transform. */
  stamp: number | null = null;
  private readonly axes: Mesh3D;
  /** Defaults: identity pose, amber, 0.5 world units, no label, axes shown. */
  constructor(id: string, o: Pose3DOptions = {}) {
    super(id);
    this.frame = o.frame ?? null; this.frames = o.frames ?? null; this.fixedFrame = o.fixedFrame ?? null;
    this.pose = o.pose ?? { t: v3(0, 0, 0), q: QUAT_IDENTITY }; this.color = o.color ?? "#f59e0b"; this.axisLength = o.axisLength ?? 0.5; this.label = o.label ?? null; this.showAxes = o.showAxes ?? true;
    this.axes = createMesh(Float32Array.from([0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1]), { key: `pose3d:${id}:axes`, colors: Uint8Array.from([220, 38, 38, 255, 220, 38, 38, 255, 22, 163, 74, 255, 22, 163, 74, 255, 37, 99, 235, 255, 37, 99, 235, 255]) });
  }
  /** Replace the pose and record its stamp. */
  setPose(pose: Pose3D, stamp?: number): void { this.pose = pose; this.stamp = stamp ?? null; this.markDirty(); }
  /** Pose → fixed frame. */
  model(now: number): Mat4 | null {
    const frame = modelFor(this, this.stamp ?? now);
    if (frame === null) return null;
    const local = mat4FromPose(this.pose.t, this.pose.q);
    return frame ? mat4Mul(frame, local) : local;
  }
  /** Draws the lit arrow, the triad (no depth test) and the label through the 2D painter. */
  draw(ctx: LayerContext): void {
    const m = this.model(ctx.now);
    if (!m || !ctx.painter3d) return;
    const L = this.axisLength;
    ctx.painter3d.triangles(unitArrow(), { color: this.color, opacity: ctx.opacity, lit: true, model: mat4Mul(m, mat4Scaling(L, L, L)) });
    if (this.showAxes) ctx.painter3d.lines(this.axes, { opacity: ctx.opacity, lineWidth: 2, depthTest: false, model: mat4Mul(m, mat4Scaling(L * 0.6, L * 0.6, L * 0.6)) });
    if (this.label && isCamera3D(ctx.projection)) {
      const o = mat4Point(m, 0, 0, 0), s = ctx.projection.project3(o.x, o.y, o.z);
      if (s.visible) ctx.painter.text(this.label, s.x + 8, s.y - 8, { color: this.color, size: 11, align: "left", baseline: "bottom", opacity: ctx.opacity });
    }
  }
  /** Box around the pose origin and its three axis tips, or null when the frame is unresolved. */
  override bounds3(): Box3 | null { const m = this.model(this.stamp ?? 0); if (!m) return null; const L = this.axisLength; return box3FromPositions([0, 0, 0, L, 0, 0, 0, L, 0, 0, 0, L], m); }
  /** Hit when the pose origin projects within `tolerance + 6` pixels; `data` is the label. */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    if (!isCamera3D(ctx.projection)) return null;
    const m = this.model(ctx.now);
    if (!m) return null;
    const o = mat4Point(m, 0, 0, 0), s = ctx.projection.project3(o.x, o.y, o.z);
    if (!s.visible) return null;
    const d = Math.hypot(s.x - sx, s.y - sy);
    return d <= tolerance + 6 ? { layerId: this.id, world: o, screen: { x: sx, y: sy }, distance: d, data: this.label ?? undefined } : null;
  }
}

/**
 * Marker shapes after `visualization_msgs/Marker`: solid primitives, line and point lists, a text label, or a
 * registered mesh.
 */
export type MarkerType = "cube" | "sphere" | "cylinder" | "arrow" | "lineList" | "lineStrip" | "points" | "text" | "mesh";
/** One marker, after visualization_msgs/Marker: pose + scale + colour, or point lists, or a text label. */
export interface Marker {
  /** Unique within the layer; `upsert` replaces by id. */
  id: string;
  /** Which shape to draw. */
  type: MarkerType;
  /** Frame the pose is expressed in; falls back to the layer's frame. */
  frame?: string | undefined;
  /** Translation xyz in the frame; default the origin. */
  position?: [number, number, number] | undefined;
  /** Unit quaternion xyzw; default identity. */
  orientation?: [number, number, number, number] | undefined;
  /** cube/sphere/cylinder: size; arrow: [length, width, height]; lines: [width]; points: [size]; text: [height px]. */
  scale?: [number, number, number] | undefined;
  /** Hex colour; default white. */
  color?: string | undefined;
  /** 0..1, multiplied with the layer opacity. */
  opacity?: number | undefined;
  /** xyz flat, in the marker's pose. */
  points?: number[] | Float32Array | undefined;
  /** RGBA bytes per point. */
  colors?: number[] | Uint8Array | undefined;
  /** Label text (text type). */
  text?: string | undefined;
  /** mesh: URI of a resource registered in the layer's `MeshRegistry` (drawn once it is loaded). */
  meshResource?: string | undefined;
  /** Seconds; 0 or absent = forever. */
  lifetime?: number | undefined;
  /** Time for the frame lookup; defaults to the layer's `now`. */
  stamp?: number | undefined;
}
interface MarkerEntry { marker: Marker; mesh: Mesh3D | null; added: number }

/** Construction options for `MarkerLayer`: frame plumbing, text size in pixels and the shared mesh registry. */
export interface MarkerLayerOptions extends FrameOptions { fontSize?: number | undefined; meshes?: MeshRegistry | undefined }

/** A visualization_msgs/MarkerArray-style layer: upsert/remove markers by id; each has its own frame and lifetime. */
export class MarkerLayer extends BaseLayer3D {
  /** Always "markers". */
  readonly kind = "markers";
  /** Frame tree, fixed frame and the default frame for markers that name none. */
  frames: FrameTree | null; fixedFrame: string | null; frame: string | null;
  /** Pixel size of text markers whose scale is unset; default 12. */
  fontSize: number;
  /** Registry that mesh markers are resolved against. */
  readonly meshes: MeshRegistry;
  private readonly entries = new Map<string, MarkerEntry>();
  private seq = 0;
  /** Defaults: no frames, 12 px text, a private mesh registry. */
  constructor(id: string, o: MarkerLayerOptions = {}) {
    super(id);
    this.frames = o.frames ?? null; this.fixedFrame = o.fixedFrame ?? null; this.frame = o.frame ?? null; this.fontSize = o.fontSize ?? 12;
    this.meshes = o.meshes ?? new MeshRegistry();
  }
  /** Number of markers. */
  get count(): number { return this.entries.size; }
  /** Marker ids in insertion order. */
  ids(): string[] { return [...this.entries.keys()]; }
  /** A marker by id. */
  get(id: string): Marker | undefined { return this.entries.get(id)?.marker; }
  /**
   * Add or replace a marker; line and point markers get (or update) a renderer mesh, and `now` starts the lifetime.
   */
  upsert(m: Marker, now = 0): void {
    const prev = this.entries.get(m.id);
    let mesh = prev?.mesh ?? null;
    if (m.type === "lineList" || m.type === "lineStrip" || m.type === "points") {
      const pos = m.points instanceof Float32Array ? m.points : Float32Array.from(m.points ?? []);
      const col = m.colors ? (m.colors instanceof Uint8Array ? m.colors : Uint8Array.from(m.colors)) : undefined;
      if (!mesh) { mesh = createMesh(pos, { key: `marker:${this.id}:${m.id}:${++this.seq}` }); mesh.colors = col; }
      else { mesh.positions = pos; mesh.colors = col; mesh.version++; }
    } else mesh = null;
    this.entries.set(m.id, { marker: m, mesh, added: now });
    this.markDirty();
  }
  /** Remove by id; false when absent. */
  remove(id: string): boolean { const ok = this.entries.delete(id); if (ok) this.markDirty(); return ok; }
  /** Remove every marker. */
  clear(): void { this.entries.clear(); this.markDirty(); }
  /** Replace the whole set. */
  setMarkers(markers: readonly Marker[], now = 0): void { this.entries.clear(); for (const m of markers) this.upsert(m, now); }
  /** Drop markers whose lifetime has passed; returns how many. */
  expire(now: number): number {
    let n = 0;
    for (const [id, e] of this.entries) if (e.marker.lifetime && now - e.added > e.marker.lifetime) { this.entries.delete(id); n++; }
    if (n) this.markDirty();
    return n;
  }
  /** Marker pose → fixed frame (null when its frame is unknown). */
  modelOf(m: Marker, now: number): Mat4 | null {
    const frame = m.frame ?? this.frame;
    let base: Mat4 | null | undefined;
    if (frame && this.frames && this.fixedFrame) base = this.frames.lookup(this.fixedFrame, frame, m.stamp ?? now); else base = undefined;
    if (base === null) return null;
    const p = m.position ?? [0, 0, 0], q = m.orientation ?? [0, 0, 0, 1];
    const local = mat4FromPose(v3(p[0], p[1], p[2]), { x: q[0], y: q[1], z: q[2], w: q[3] });
    return base ? mat4Mul(base, local) : local;
  }
  private static shapeModel(m: Marker, pose: Mat4): Mat4 {
    const s = m.scale ?? [1, 1, 1];
    return mat4Mul(pose, mat4Scaling(s[0], s[1], s[2]));
  }
  /**
   * Expires markers first, then draws each one: lit primitives and meshes, lines, points, and text through the 2D
   * painter.
   */
  draw(ctx: LayerContext): void {
    this.expire(ctx.now);
    const p3 = ctx.painter3d;
    if (!p3) return;
    for (const e of this.entries.values()) {
      const m = e.marker, pose = this.modelOf(m, ctx.now);
      if (!pose) continue;
      const color = m.color ?? "#ffffff", opacity = (m.opacity ?? 1) * ctx.opacity, s = m.scale ?? [1, 1, 1];
      switch (m.type) {
        case "cube": p3.triangles(unitCube(), { color, opacity, lit: true, model: MarkerLayer.shapeModel(m, pose) }); break;
        case "sphere": p3.triangles(unitSphere(), { color, opacity, lit: true, model: MarkerLayer.shapeModel(m, pose) }); break;
        case "cylinder": p3.triangles(unitCylinder(), { color, opacity, lit: true, model: MarkerLayer.shapeModel(m, pose) }); break;
        case "arrow": p3.triangles(unitArrow(), { color, opacity, lit: true, model: mat4Mul(pose, mat4Scaling(s[0], s[1] * 2, s[2] * 2)) }); break;
        case "mesh": { const mesh = m.meshResource ? this.meshes.get(m.meshResource) : undefined; if (mesh) p3.triangles(mesh, { color, opacity, lit: true, model: MarkerLayer.shapeModel(m, pose) }); break; }
        case "lineList": if (e.mesh) p3.lines(e.mesh, { color, opacity, lineWidth: s[0], model: pose }, false); break;
        case "lineStrip": if (e.mesh) p3.lines(e.mesh, { color, opacity, lineWidth: s[0], model: pose }, true); break;
        case "points": if (e.mesh) p3.points(e.mesh, { color, opacity, pointSize: s[0], model: pose }); break;
        case "text": {
          if (!m.text || !isCamera3D(ctx.projection)) break;
          const o = mat4Point(pose, 0, 0, 0), sc = ctx.projection.project3(o.x, o.y, o.z);
          if (sc.visible) ctx.painter.text(m.text, sc.x, sc.y, { color, size: s[0] > 0 && s[0] !== 1 ? s[0] : this.fontSize, align: "center", baseline: "middle", opacity });
          break;
        }
      }
    }
  }
  /** Bounding sphere of a shape marker in the fixed frame. */
  private sphereOf(m: Marker, pose: Mat4): { center: Vec3; radius: number } {
    const s = m.scale ?? [1, 1, 1];
    if (m.type === "mesh") {
      const mesh = m.meshResource ? this.meshes.get(m.meshResource) : undefined;
      const b = mesh ? box3FromPositions(mesh.positions, MarkerLayer.shapeModel(m, pose)) : null;
      if (!b) return { center: mat4Point(pose, 0, 0, 0), radius: 0 };
      const sp = box3Sphere(b); return { center: sp.center, radius: sp.radius };
    }
    const c = mat4Point(pose, m.type === "arrow" ? s[0] / 2 : 0, 0, 0);
    const r = m.type === "arrow" ? Math.max(s[0], s[1] * 2, s[2] * 2) / 2 : Math.sqrt(s[0] * s[0] + s[1] * s[1] + s[2] * s[2]) / 2;
    return { center: c, radius: r };
  }
  /** Union of every resolvable marker's extent (meshes exactly, primitives by bounding sphere, text as a point). */
  override bounds3(): Box3 | null {
    let b: Box3 | null = null;
    for (const e of this.entries.values()) {
      const m = e.marker, pose = this.modelOf(m, m.stamp ?? 0);
      if (!pose) continue;
      if (e.mesh) b = box3Union(b, box3FromPositions(e.mesh.positions, pose));
      else if (m.type === "mesh") { const mesh = m.meshResource ? this.meshes.get(m.meshResource) : undefined; if (mesh) b = box3Union(b, box3FromPositions(mesh.positions, MarkerLayer.shapeModel(m, pose))); }
      else if (m.type === "text") b = box3Union(b, box3FromPositions([0, 0, 0], pose));
      else { const sp = this.sphereOf(m, pose), r = sp.radius; b = box3Union(b, { min: v3(sp.center.x - r, sp.center.y - r, sp.center.z - r), max: v3(sp.center.x + r, sp.center.y + r, sp.center.z + r) }); }
    }
    return b;
  }
  /**
   * Primitives by ray-sphere intersection (nearest along the ray, `distance` 0), line and point markers by nearest
   * vertex, text by screen distance; `data` is the marker id.
   */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    if (!isCamera3D(ctx.projection)) return null;
    const cam = ctx.projection, ray = cam.ray(sx, sy);
    let best: HitResult | null = null, bestT = Infinity;
    for (const [id, e] of this.entries) {
      const m = e.marker, pose = this.modelOf(m, ctx.now);
      if (!pose) continue;
      if (e.mesh) {
        const s = m.scale ?? [1, 1, 1];
        const h = nearestVertex(cam, e.mesh.positions, Math.floor(e.mesh.positions.length / 3), pose, sx, sy);
        if (h && h.distance <= tolerance + s[0] / 2 && (!best || h.distance < best.distance)) best = { layerId: this.id, world: h.world, screen: { x: sx, y: sy }, distance: h.distance, index: h.index, data: id };
      } else if (m.type === "text") {
        const o = mat4Point(pose, 0, 0, 0), sc = cam.project3(o.x, o.y, o.z);
        const d = Math.hypot(sc.x - sx, sc.y - sy);
        if (sc.visible && d <= tolerance + 8 && (!best || d < best.distance)) best = { layerId: this.id, world: o, screen: { x: sx, y: sy }, distance: d, data: id };
      } else {
        const sp = this.sphereOf(m, pose), t = raySphere(ray, sp.center, sp.radius);
        if (t !== null && t < bestT) { bestT = t; best = { layerId: this.id, world: v3(ray.origin.x + ray.dir.x * t, ray.origin.y + ray.dir.y * t, ray.origin.z + ray.dir.z * t), screen: { x: sx, y: sy }, distance: 0, data: id }; }
      }
    }
    return best;
  }
}

/** An invisible layer whose pushes register mesh resources in the sink's shared `MeshRegistry`: `{ uri, positions, normals?, indices? }`. */
export class MeshesLayer extends BaseLayer3D {
  /** Always "meshes". */
  readonly kind = "meshes";
  /** `meshes` is the shared registry that `register` writes to. */
  constructor(id: string, readonly meshes: MeshRegistry) { super(id); }
  /** Store a parsed mesh under `uri`. */
  register(uri: string, parsed: ParsedMesh): void { this.meshes.set(uri, parsed); }
  /** Draws nothing. */
  draw(): void {}
}

/** A sensor_msgs/LaserScan drawn in 3D: the scan's points on the z = 0 plane of its own frame. */
export class LaserScan3DLayer extends PointCloud3DLayer {
  /** Always "laserScan3d". */
  override readonly kind: string = "laserScan3d";
  /** Same options as `PointCloud3DLayer`, defaulting to red 3 px points. */
  constructor(id: string, o: PointCloud3DOptions = {}) { super(id, { color: "#f87171", pointSize: 3, ...o }); }
  /** Convert the scan to xy points on z = 0 (no pose: the layer's frame is the sensor frame) and store them. */
  setScan(scan: LaserScanLike, stamp?: number): void {
    const xy = laserScanToPoints(scan), n = xy.length / 2, pos = new Float32Array(n * 3);
    for (let i = 0; i < n; i++) { pos[3 * i] = xy[2 * i]!; pos[3 * i + 1] = xy[2 * i + 1]!; pos[3 * i + 2] = 0; }
    this.setPoints(pos, null, null, stamp);
  }
}

/** `OccupancyGridOptions` plus frame plumbing and the plane height `z` in the grid's frame (default 0). */
export interface OccupancyGridPlaneOptions extends OccupancyGridOptions, FrameOptions { z?: number | undefined }

/** A 2D occupancy grid drawn as a textured plane at height `z` in its frame (nav_msgs/OccupancyGrid). */
export class OccupancyGridPlaneLayer extends BaseLayer3D {
  /** Always "occupancyGrid3d". */
  readonly kind = "occupancyGrid3d";
  /** The underlying 2D grid; edit cells and origin through it. */
  readonly grid: OccupancyGridLayer;
  /**
   * Frame the grid is expressed in, the tree and the fixed frame; null for any of them draws in the fixed frame
   * directly.
   */
  frame: string | null; frames: FrameTree | null; fixedFrame: string | null;
  /** Height of the plane in the grid's frame; call `markDirty` after changing it. */
  z: number;
  /** Time used for the frame lookup; null uses the latest transform. */
  stamp: number | null = null;
  /** Creates the inner grid with id `<id>:grid`. */
  constructor(id: string, o: OccupancyGridPlaneOptions) {
    super(id);
    this.grid = new OccupancyGridLayer(`${id}:grid`, o);
    this.frame = o.frame ?? null; this.frames = o.frames ?? null; this.fixedFrame = o.fixedFrame ?? null; this.z = o.z ?? 0;
  }
  /** World corners (in the grid's frame): bottom-left, bottom-right, top-right, top-left of the raster. */
  corners(): number[] {
    const m = rasterToWorld(this.grid.placement(), this.grid.height), w = this.grid.width, h = this.grid.height;
    const out: number[] = [];
    for (const [px, py] of [[0, h], [w, h], [w, 0], [0, 0]] as const) { const p = apply(m, px, py); out.push(p.x, p.y, this.z); }
    return out;
  }
  /**
   * Matrix from the grid's frame into the fixed frame at `stamp` (or `now` when unset); undefined when no frame is
   * configured, null when the lookup fails.
   */
  model(now: number): Mat4 | null | undefined { return modelFor(this, this.stamp ?? now); }
  /** Draws the grid raster as a textured quad on the plane. */
  draw(ctx: LayerContext): void {
    const model = this.model(ctx.now);
    if (model === null) return;
    ctx.painter3d?.image(this.grid.image(), this.corners(), { opacity: ctx.opacity, model: model ?? undefined });
  }
  /** Bounds of the plane's corners in the fixed frame, or null when unresolved. */
  override bounds3(): Box3 | null { const m = this.model(this.stamp ?? 0); return m === null ? null : box3FromPositions(this.corners(), m ?? undefined); }
  /** Intersects the pixel's ray with the plane; `index` is `row × width + col` and `data` the occupancy value. */
  override hitTest(sx: number, sy: number, ctx: HitContext, _tolerance: number): HitResult | null {
    if (!isCamera3D(ctx.projection)) return null;
    const model = this.model(ctx.now);
    if (model === null) return null;
    // ray against the grid plane, then the cell under it
    const ray = ctx.projection.ray(sx, sy), c = this.corners(), M = model ?? MAT4_IDENTITY;
    const p0 = mat4Point(M, c[0]!, c[1]!, c[2]!), p1 = mat4Point(M, c[3]!, c[4]!, c[5]!), p3 = mat4Point(M, c[9]!, c[10]!, c[11]!);
    const u = v3sub(p1, p0), v = v3sub(p3, p0);
    const n = v3(u.y * v.z - u.z * v.y, u.z * v.x - u.x * v.z, u.x * v.y - u.y * v.x);
    const denom = n.x * ray.dir.x + n.y * ray.dir.y + n.z * ray.dir.z;
    if (Math.abs(denom) < 1e-12) return null;
    const t = (n.x * (p0.x - ray.origin.x) + n.y * (p0.y - ray.origin.y) + n.z * (p0.z - ray.origin.z)) / denom;
    if (t < 0) return null;
    const w = v3(ray.origin.x + ray.dir.x * t, ray.origin.y + ray.dir.y * t, ray.origin.z + ray.dir.z * t);
    const d = v3sub(w, p0), uu = u.x * u.x + u.y * u.y + u.z * u.z, vv = v.x * v.x + v.y * v.y + v.z * v.z;
    const a = (d.x * u.x + d.y * u.y + d.z * u.z) / uu, b = (d.x * v.x + d.y * v.y + d.z * v.z) / vv;
    if (a < 0 || a > 1 || b < 0 || b > 1) return null;
    const col = Math.min(this.grid.width - 1, Math.floor(a * this.grid.width)), row = Math.min(this.grid.height - 1, Math.floor((1 - b) * this.grid.height));
    return { layerId: this.id, world: w, screen: { x: sx, y: sy }, distance: 0, index: row * this.grid.width + col, data: this.grid.get(row, col) };
  }
}
