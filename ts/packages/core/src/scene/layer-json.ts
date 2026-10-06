// Mori.SkyScope — Layers described as plain JSON — the shape a LayerSink receives from a source plugin (ROS 2 relay, MQTT, a Blazor page pushing through in…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { ImageHandle } from "../paint/painter.js";
import type { Layer } from "./scene.js";
import { GridLayer, PointsLayer, PolylineLayer } from "./layers.js";
import { BitmapLayer, OccupancyGridLayer, PointCloudLayer, PoseLayer, ShapeLayer, TrailLayer, type LaserScanLike, type Pose2D, type RasterPlacement, type Shape } from "./robot-layers.js";
import { AxesLayer, FramesLayer, Grid3DLayer, PointCloud3DLayer, type FrameTransformMessage } from "../scene3d/layers3d.js";
import { LaserScan3DLayer, MarkerLayer, MeshRegistry, MeshesLayer, OccupancyGridPlaneLayer, Path3DLayer, Pose3DLayer, type Marker, type Pose3D } from "../scene3d/robot-layers3d.js";
import { QUAT_IDENTITY } from "../scene3d/math3.js";
import { FrameTree } from "../scene3d/frame-tree.js";

/**
 * Layers described as plain JSON — the shape a `LayerSink` receives from a source plugin (ROS 2 relay, MQTT, a
 * Blazor page pushing through interop). `createLayer` builds one from `declareLayer(id, kind, meta)`;
 * `applyLayerPayload` feeds `push(id, payload)`. Unknown kinds/payloads are ignored and reported through the result.
 */
/** Layer kinds understood by `createLayer`: the string a sink receives from `declareLayer`. */
export type LayerKind = "grid" | "polyline" | "points" | "bitmap" | "occupancyGrid" | "pointCloud" | "pose" | "shapes" | "trail" | "grid3d" | "axes" | "pointCloud3d" | "frames" | "path3d" | "pose3d" | "markers" | "laserScan3d" | "occupancyGrid3d" | "meshes";

/** What a sink shares between the layers it creates: the frame tree and the frame the scene is drawn in. */
export interface LayerEnv { frames?: FrameTree | undefined; fixedFrame?: string | undefined; meshes?: MeshRegistry | undefined }

/**
 * Declaration metadata: free-form keys passed to the layer constructor, plus the few typed ones the 2D layers read.
 */
export interface LayerMeta {
  /** Kind-specific options, passed straight to the layer constructor. */
  [key: string]: unknown;
  /** Interleaved world xy for polyline and points layers. */
  points?: number[] | undefined;
  /** Bitmap to draw (bitmap kind). */
  image?: ImageHandle | undefined;
  /** Where the bitmap sits in the world (bitmap kind). */
  placement?: RasterPlacement | undefined;
}

/**
 * Build a layer for a declaration; null for an unknown kind or a bitmap without image and placement. 3D layers share
 * `env.frames` and `env.meshes`; `meta.fixedFrame` overrides `env.fixedFrame` (default "map").
 */
export function createLayer(id: string, kind: string, meta: LayerMeta = {}, env: LayerEnv = {}): Layer | null {
  const frames = env.frames ?? new FrameTree(), fixedFrame = (meta.fixedFrame as string | undefined) ?? env.fixedFrame ?? "map", meshes = env.meshes ?? new MeshRegistry();
  switch (kind as LayerKind) {
    case "grid3d": return new Grid3DLayer(id, meta as never);
    case "axes": return new AxesLayer(id, { frames, fixedFrame, ids: meta.ids as string[] | undefined, length: meta.length as number | undefined, lineWidth: meta.lineWidth as number | undefined });
    case "pointCloud3d": return new PointCloud3DLayer(id, { ...(meta as object), frames, fixedFrame } as never);
    case "frames": { const l = new FramesLayer(id, frames); if (Array.isArray(meta.transforms)) l.apply(meta.transforms as FrameTransformMessage[]); return l; }
    case "path3d": return new Path3DLayer(id, { ...(meta as object), frames, fixedFrame } as never);
    case "pose3d": return new Pose3DLayer(id, { ...(meta as object), pose: meta.pose ? poseFromJson(meta.pose) : undefined, frames, fixedFrame } as never);
    case "meshes": return new MeshesLayer(id, meshes);
    case "markers": { const l = new MarkerLayer(id, { ...(meta as object), frames, fixedFrame, meshes } as never); if (Array.isArray(meta.markers)) l.setMarkers(meta.markers as Marker[]); return l; }
    case "laserScan3d": return new LaserScan3DLayer(id, { ...(meta as object), frames, fixedFrame } as never);
    case "occupancyGrid3d": return new OccupancyGridPlaneLayer(id, { ...(meta as object), frames, fixedFrame } as never);
    case "grid": return new GridLayer(id, meta as never);
    case "polyline": return new PolylineLayer(id, meta.points ?? [], meta.stroke as never, meta.closed === true);
    case "points": return new PointsLayer(id, meta.points ?? [], meta.radius as number | undefined, meta.fill as never, meta.stroke as never);
    case "bitmap": return meta.image && meta.placement ? new BitmapLayer(id, meta.image, meta.placement) : null;
    case "occupancyGrid": return new OccupancyGridLayer(id, meta as never);
    case "pointCloud": return new PointCloudLayer(id, meta as never);
    case "pose": return new PoseLayer(id, (meta.pose as Pose2D | undefined) ?? { x: 0, y: 0, yaw: 0 }, meta as never);
    case "shapes": return new ShapeLayer(id, (meta.shapes as Shape[] | undefined) ?? []);
    case "trail": return new TrailLayer(id, meta.maxPoints as number | undefined, meta.stroke as never);
    default: return null;
  }
}

/** `{ t: [x, y, z], q: [x, y, z, w] }` or `{ x, y, z, qx, qy, qz, qw }`. */
export function poseFromJson(v: unknown): Pose3D {
  const o = v as Record<string, unknown>;
  if (Array.isArray(o.t)) { const t = o.t as number[], q = (o.q as number[] | undefined) ?? [0, 0, 0, 1]; return { t: { x: t[0]!, y: t[1]!, z: t[2] ?? 0 }, q: { x: q[0]!, y: q[1]!, z: q[2]!, w: q[3]! } }; }
  const n = (k: string, d: number): number => (typeof o[k] === "number" ? (o[k] as number) : d);
  return { t: { x: n("x", 0), y: n("y", 0), z: n("z", 0) }, q: "qw" in o ? { x: n("qx", 0), y: n("qy", 0), z: n("qz", 0), w: n("qw", 1) } : QUAT_IDENTITY };
}

/**
 * Update a layer from a push payload. Accepted payloads by kind:
 * polyline/trail `{ points }` or `{ append: [x, y] }`; points `{ points }`; occupancyGrid `{ data } | { cells: [row, col, v][] } | { origin }`;
 * pointCloud `{ points, intensities? } | { scan: LaserScanLike, pose? }`; pose `{ x, y, yaw }`; shapes `{ shapes } | { upsert: Shape } | { remove: id }`;
 * bitmap `{ image?, placement? }`. Any layer accepts `{ visible?, opacity? }`. Returns false when the payload was not understood.
 * 3D kinds: path3d `{ positions } | { append: [x, y, z, …] } | { clear }`; pose3d `{ pose } | { t, q } | { label }`; markers `{ markers } | { upsert } | { remove } | { clear }`;
 * laserScan3d `{ scan }`; occupancyGrid3d `{ z }` plus the occupancyGrid payloads; meshes `{ uri, positions, normals?, indices?, color? }`; frames `{ transforms }`;
 * pointCloud3d `{ positions, intensities?, colors? }`. Layers with a frame also accept `{ frame }`; `stamp` tags the update time for frame lookups.
 */
export function applyLayerPayload(layer: Layer, payload: unknown): boolean {
  if (!payload || typeof payload !== "object") return false;
  const p = payload as Record<string, unknown>;
  let handled = false;
  if (typeof p.visible === "boolean") { layer.visible = p.visible; handled = true; }
  if (typeof p.opacity === "number") { layer.opacity = p.opacity; layer.dirty = true; handled = true; }
  if (typeof p.frame === "string" && "frame" in layer) { (layer as { frame: string | null }).frame = p.frame; handled = true; }
  const stamp = typeof p.stamp === "number" ? p.stamp : undefined;
  if (layer instanceof Path3DLayer) {
    if (p.positions instanceof Float32Array || Array.isArray(p.positions)) { layer.setPoints(p.positions instanceof Float32Array ? p.positions : Float32Array.from(p.positions as number[]), stamp); return true; }
    if (Array.isArray(p.append)) { const a = p.append as number[]; for (let i = 0; i + 2 < a.length; i += 3) layer.append(a[i]!, a[i + 1]!, a[i + 2]!); return true; }
    if (p.clear === true) { layer.clear(); return true; }
    return handled;
  }
  if (layer instanceof Pose3DLayer) {
    if (p.pose || Array.isArray(p.t)) { layer.setPose(poseFromJson(p.pose ?? p), stamp); return true; }
    if (typeof p.label === "string") { layer.label = p.label; handled = true; }
    return handled;
  }
  if (layer instanceof MarkerLayer) {
    const now = typeof p.now === "number" ? p.now : 0;
    if (Array.isArray(p.markers)) { layer.setMarkers(p.markers as Marker[], now); return true; }
    if (p.upsert) { layer.upsert(p.upsert as Marker, now); return true; }
    if (typeof p.remove === "string") { layer.remove(p.remove); return true; }
    if (p.clear === true) { layer.clear(); return true; }
    return handled;
  }
  if (layer instanceof LaserScan3DLayer) {
    if (p.scan) { layer.setScan(p.scan as LaserScanLike, stamp); return true; }
  }
  if (layer instanceof OccupancyGridPlaneLayer) {
    if (typeof p.z === "number") { layer.z = p.z; layer.markDirty(); handled = true; }
    return applyLayerPayload(layer.grid, payload) || handled;
  }
  if (layer instanceof MeshesLayer) {
    if (typeof p.uri === "string" && (p.positions instanceof Float32Array || Array.isArray(p.positions))) {
      const pos = p.positions instanceof Float32Array ? p.positions : Float32Array.from(p.positions as number[]);
      const nor = p.normals instanceof Float32Array ? p.normals : Array.isArray(p.normals) ? Float32Array.from(p.normals as number[]) : null;
      const idx = p.indices instanceof Uint32Array ? p.indices : Array.isArray(p.indices) ? Uint32Array.from(p.indices as number[]) : null;
      const col = ArrayBuffer.isView(p.color) || Array.isArray(p.color) ? Array.from(p.color as ArrayLike<number>) : null;
      layer.register(p.uri, { positions: pos, normals: nor, indices: idx, ...(col && col.length >= 3 ? { color: [col[0]!, col[1]!, col[2]!, col[3] ?? 1] as [number, number, number, number] } : {}) });
      return true;
    }
    return handled;
  }
  if (layer instanceof FramesLayer) {
    if (Array.isArray(p.transforms)) { layer.apply(p.transforms as FrameTransformMessage[]); return true; }
  } else if (layer instanceof PointCloud3DLayer) {
    if (typeof p.frame === "string") { layer.frame = p.frame; handled = true; }
    if (p.positions instanceof Float32Array || Array.isArray(p.positions)) {
      const pos = p.positions instanceof Float32Array ? p.positions : Float32Array.from(p.positions as number[]);
      const inten = p.intensities instanceof Float32Array ? p.intensities : Array.isArray(p.intensities) ? Float32Array.from(p.intensities as number[]) : null;
      const colors = p.colors instanceof Uint8Array ? p.colors : Array.isArray(p.colors) ? Uint8Array.from(p.colors as number[]) : null;
      layer.setPoints(pos, inten, colors, typeof p.stamp === "number" ? p.stamp : undefined);
      return true;
    }
  } else if (layer instanceof PoseLayer) {
    if (typeof p.x === "number" && typeof p.y === "number") { layer.setPose({ x: p.x, y: p.y, yaw: typeof p.yaw === "number" ? p.yaw : layer.pose.yaw }); return true; }
  } else if (layer instanceof PointCloudLayer) {
    if (p.scan) { layer.setScan(p.scan as LaserScanLike, p.pose as Pose2D | undefined); return true; }
    if (Array.isArray(p.points)) { layer.setPoints(p.points as number[], p.intensities as number[] | undefined); return true; }
  } else if (layer instanceof OccupancyGridLayer) {
    if (p.origin) { layer.setOrigin(p.origin as Pose2D); handled = true; }
    if (Array.isArray(p.data)) { layer.setData(p.data as number[]); handled = true; }
    if (Array.isArray(p.cells)) { for (const c of p.cells as [number, number, number][]) layer.set(c[0], c[1], c[2]); handled = true; }
    return handled;
  } else if (layer instanceof ShapeLayer) {
    if (Array.isArray(p.shapes)) { layer.setShapes(p.shapes as Shape[]); return true; }
    if (p.upsert) { layer.upsert(p.upsert as Shape); return true; }
    if (typeof p.remove === "string") { layer.removeShape(p.remove); return true; }
  } else if (layer instanceof PolylineLayer) {
    if (Array.isArray(p.points)) { layer.setPoints(p.points as number[]); return true; }
    if (Array.isArray(p.append)) { const a = p.append as number[]; for (let i = 0; i + 1 < a.length; i += 2) layer.append(a[i]!, a[i + 1]!); return true; }
  } else if (layer instanceof PointsLayer) {
    if (Array.isArray(p.points)) { layer.setPoints(p.points as number[]); return true; }
  } else if (layer instanceof BitmapLayer) {
    if (p.image) { layer.setImage(p.image as ImageHandle); handled = true; }
    if (p.placement) { layer.setPlacement(p.placement as RasterPlacement); handled = true; }
  }
  return handled;
}

import type { LayerSink } from "../sources/contracts.js";
import type { Scene } from "./scene.js";

/** A LayerSink that maintains a Scene: declarations create or replace layers, pushes update them. */
export class SceneLayerSink implements LayerSink {
  /** Layer kinds that were declared but `createLayer` did not recognise, for diagnostics. */
  readonly unknown = new Set<string>();
  private readonly declared = new Set<string>();
  /** Shared by every 3D layer this sink creates. */
  readonly frames: FrameTree;
  /** Mesh resources shared by the marker layers and robot models this sink creates. */
  readonly meshes: MeshRegistry;
  /**
   * `scene` receives the layers; `onChange` fires after every declaration, understood push and reset; `env` supplies
   * the shared frame tree, mesh registry and fixed frame.
   */
  constructor(readonly scene: Scene, private readonly onChange?: (() => void) | undefined, env: LayerEnv = {}) { this.frames = env.frames ?? new FrameTree(); this.meshes = env.meshes ?? new MeshRegistry(); this.fixedFrame = env.fixedFrame ?? "map"; }
  /** Frame the 3D layers are drawn in (default "map"); applies to layers declared after a change. */
  fixedFrame: string;
  /** Create (or replace, by id) a layer of `kind`; unknown kinds are recorded in `unknown` and skipped. */
  declareLayer(id: string, kind: string, meta?: Record<string, unknown>): void {
    this.scene.remove(id);
    const layer = createLayer(id, kind, (meta ?? {}) as LayerMeta, { frames: this.frames, fixedFrame: this.fixedFrame, meshes: this.meshes });
    if (!layer) { this.unknown.add(kind); return; }
    this.scene.add(layer); this.declared.add(id);
    this.onChange?.();
  }
  /** Removes the layers this sink created (a host's own layers, e.g. a grid, stay). */
  reset(): void { for (const id of this.declared) this.scene.remove(id); this.declared.clear(); this.frames.clear(); this.onChange?.(); }
  /**
   * Apply a payload to the layer with that id; silently ignored when the layer is missing or the payload is not
   * understood.
   */
  push(id: string, payload: unknown): void {
    const layer = this.scene.get(id);
    if (layer && applyLayerPayload(layer, payload)) this.onChange?.();
  }
}

/** Fans layer messages out to several sinks; late sinks receive every declaration seen so far. */
export class FanoutLayerSink implements LayerSink {
  private readonly sinks = new Set<LayerSink>();
  private readonly declared = new Map<string, { kind: string; meta: Record<string, unknown> | undefined }>();
  /** Attach a sink, replaying every declaration seen so far; returns a function that detaches it. */
  add(sink: LayerSink): () => void {
    for (const [id, d] of this.declared) sink.declareLayer(id, d.kind, d.meta);
    this.sinks.add(sink);
    return () => { this.sinks.delete(sink); };
  }
  /** Record the declaration for late sinks and forward it to every attached sink. */
  declareLayer(id: string, kind: string, meta?: Record<string, unknown>): void {
    this.declared.set(id, { kind, meta });
    for (const s of this.sinks) s.declareLayer(id, kind, meta);
  }
  /** Forward a payload to every attached sink. */
  push(id: string, payload: unknown): void { for (const s of this.sinks) s.push(id, payload); }
  /** Forget the recorded declarations and reset every attached sink that supports it. */
  reset(): void { this.declared.clear(); for (const s of this.sinks) s.reset?.(); }
}
