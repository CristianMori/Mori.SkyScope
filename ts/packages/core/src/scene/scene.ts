// Mori.SkyScope — The 2D scene: ordered layers, per-layer surface caching by projection version, hit testing.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter } from "../paint/painter.js";
import type { Painter3D } from "../scene3d/painter3d.js";
import { rectUnion, type Rect, type Vec2, type Vec3 } from "./geometry.js";
import type { CameraKind, Projection } from "./camera.js";

/** What a layer gets to draw with. Mirrors `Mori.SkyScope.Core.Scene.LayerContext`. */
export interface LayerContext {
  /** 2D painter to draw with; for cacheable layers it targets the layer's offscreen surface. */
  painter: Painter;
  /** Present when the host renders 3D; 3D layers draw through it, the 2D painter draws the HUD. */
  painter3d?: Painter3D | undefined;
  /** World ↔ screen mapping of the current camera. */
  projection: Projection;
  /** Viewport width in pixels. */
  width: number;
  /** Viewport height in pixels. */
  height: number;
  /** Chart time, seconds. */
  now: number;
  /** Effective opacity (scene × layer). Layers fold it into their styles. */
  opacity: number;
}

/** `LayerContext` without the painter: what `hitTest` receives. */
export type HitContext = Omit<LayerContext, "painter">;

/** What a hit test returns: which layer was hit, where, and how far the probe was from it. */
export interface HitResult {
  /** Id of the layer that was hit. */
  layerId: string;
  /** World coordinates of the probe; z = 0 for 2D layers. */
  world: Vec3;
  /** Screen position of the probe in pixels. */
  screen: Vec2;
  /** Screen-pixel distance from the probe to the hit geometry. */
  distance: number;
  /** Layer-specific element index (vertex, segment, cell, shape position) when meaningful. */
  index?: number | undefined;
  /** Layer-specific payload (intensity, occupancy value, shape id, pose, ...). */
  data?: unknown;
}

/**
 * A drawable, hit-testable member of a Scene. Layers with `cacheable = true` are drawn into an
 * offscreen surface (via `Painter.layer`) that is reused until the layer is marked dirty or the
 * projection changes — the mechanism that makes a 20-layer scene cheap.
 */
export interface Layer {
  /** Unique within a scene. */
  readonly id: string;
  /** Layer type name, e.g. "polyline"; the same string the JSON layer contract uses. */
  readonly kind: string;
  /** Projection kinds this layer can draw under; the scene skips it under any other. */
  readonly cameraKinds: readonly CameraKind[];
  /** Hidden layers are neither drawn, hit-tested nor included in bounds. */
  visible: boolean;
  /** Layer opacity 0..1, multiplied with the scene opacity. */
  opacity: number;
  /**
   * True to draw into a reusable offscreen surface; false for layers with screen-sized geometry that must redraw
   * every frame.
   */
  cacheable: boolean;
  /** Set to request a redraw of the cached surface; the scene clears it after drawing. */
  dirty: boolean;
  /** World bounding box for fit-all, or null when the layer has no extent. */
  bounds(): Rect | null;
  /** Draw the layer with the given context. */
  draw(ctx: LayerContext): void;
  /** Hit within `tolerance` screen pixels of (sx, sy), or null. */
  hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null;
}

/**
 * Default field values for layers: 2D only, visible, opaque, cacheable and dirty. Subclasses provide `kind` and
 * `draw`.
 */
export abstract class BaseLayer implements Layer {
  /** Layer type name, set by the subclass. */
  abstract readonly kind: string;
  /** 2D only by default. */
  readonly cameraKinds: readonly CameraKind[] = ["2d"];
  /** Default true. */
  visible = true;
  /** Default 1. */
  opacity = 1;
  /** Default true. */
  cacheable = true;
  /** Starts dirty so the first draw renders. */
  dirty = true;
  /** Assigns the unique layer id. */
  constructor(readonly id: string) {}
  /** Request a redraw of the cached surface. */
  markDirty(): void { this.dirty = true; }
  /** No extent by default. */
  bounds(): Rect | null { return null; }
  /** Draw the layer with the given context. */
  abstract draw(ctx: LayerContext): void;
  /** Never hits by default. */
  hitTest(_sx: number, _sy: number, _ctx: HitContext, _tolerance: number): HitResult | null { return null; }
}

/** An ordered stack of layers sharing one projection. Pinned by `spec/fixtures/scene.json`. */
export class Scene {
  /** Bottom-to-top draw order; prefer `add`, `remove` and `move` over editing it directly. */
  readonly layers: Layer[] = [];
  /** Scene-wide opacity 0..1, multiplied into every layer. */
  opacity = 1;
  private lastProjectionVersion = -1;

  /** Append (or insert at `index`) a layer; throws when the id is already present. Returns the scene for chaining. */
  add(layer: Layer, index?: number): this {
    if (this.layers.some((l) => l.id === layer.id)) throw new Error(`layer "${layer.id}" already in scene`);
    if (index === undefined || index >= this.layers.length) this.layers.push(layer); else this.layers.splice(Math.max(0, index), 0, layer);
    return this;
  }
  /** Remove a layer by id; false when absent. */
  remove(id: string): boolean {
    const i = this.layers.findIndex((l) => l.id === id);
    if (i < 0) return false;
    this.layers.splice(i, 1);
    return true;
  }
  /** Find a layer by id. */
  get(id: string): Layer | undefined { return this.layers.find((l) => l.id === id); }
  /** Move a layer to a new z position (0 = bottom). */
  move(id: string, index: number): boolean {
    const i = this.layers.findIndex((l) => l.id === id);
    if (i < 0) return false;
    const [l] = this.layers.splice(i, 1);
    this.layers.splice(Math.max(0, Math.min(index, this.layers.length)), 0, l!);
    return true;
  }
  /** Layer ids, bottom to top. */
  order(): string[] { return this.layers.map((l) => l.id); }

  /**
   * Draw every visible layer compatible with the projection; cacheable layers are re-rendered only when dirty or when
   * the projection version changed since the previous draw.
   */
  draw(painter: Painter, projection: Projection, now: number, painter3d?: Painter3D): void {
    const projectionChanged = projection.version !== this.lastProjectionVersion;
    this.lastProjectionVersion = projection.version;
    const width = painter.width, height = painter.height;
    for (const layer of this.layers) {
      if (!layer.visible || !layer.cameraKinds.includes(projection.kind)) continue;
      const ctx: LayerContext = { painter, painter3d, projection, width, height, now, opacity: this.opacity * layer.opacity };
      if (layer.cacheable) {
        painter.layer(`layer:${layer.id}`, width, height, (p) => layer.draw({ ...ctx, painter: p }), 0, 0, layer.dirty || projectionChanged);
        layer.dirty = false;
      } else {
        painter.save();
        layer.draw(ctx);
        painter.restore();
        layer.dirty = false;
      }
    }
  }

  /** Topmost hit within `tolerance` screen pixels. */
  hitTest(sx: number, sy: number, projection: Projection, width: number, height: number, tolerance = 6, now = 0): HitResult | null {
    const ctx: HitContext = { projection, width, height, now, opacity: 1 };
    for (let i = this.layers.length - 1; i >= 0; i--) {
      const layer = this.layers[i]!;
      if (!layer.visible || !layer.cameraKinds.includes(projection.kind)) continue;
      const hit = layer.hitTest(sx, sy, ctx, tolerance);
      if (hit) return hit;
    }
    return null;
  }

  /** Union of visible layers' world bounds (for fit-all). */
  bounds(): Rect | null {
    let r: Rect | null = null;
    for (const l of this.layers) if (l.visible) r = rectUnion(r, l.bounds());
    return r;
  }
}
