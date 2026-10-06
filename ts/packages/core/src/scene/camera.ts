// Mori.SkyScope — Maps world coordinates to screen pixels.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { apply, applyLinear, invert, mul, rotation, scaling, translation, transformRect, type Mat3, type Rect, type Vec2, IDENTITY } from "./geometry.js";
import type { Scale } from "../scales/scale.js";

/**
 * Dimensionality of a projection: 2D cameras and scale projections are "2d", the orbit camera is "3d". Layers list
 * the kinds they can draw under.
 */
export type CameraKind = "2d" | "3d";

/**
 * Maps world coordinates to screen pixels. `version` increments on every change so cached layer
 * surfaces know when to redraw. Mirrors `Mori.SkyScope.Core.Scene.IProjection`.
 */
export interface Projection {
  /** Which camera family this is; layers whose `cameraKinds` do not include it are skipped. */
  readonly kind: CameraKind;
  /** Monotonic counter bumped on every change; equal versions mean an identical mapping. */
  readonly version: number;
  /** World point → screen pixels (y down). */
  project(wx: number, wy: number): Vec2;
  /** Screen pixels → world point; the inverse of `project`. */
  unproject(sx: number, sy: number): Vec2;
}

/**
 * Initial state for a `Camera2D`; every field falls back to the constructor default (1 × 1 viewport, origin at the
 * centre, zoom 1, no rotation).
 */
export interface Camera2DOptions {
  /** Viewport size in pixels, and the world point placed at its centre. */
  width?: number; height?: number; centerX?: number; centerY?: number;
  /** Pixels per world unit. */
  zoom?: number;
  /** Radians, clockwise on screen. */
  rotation?: number;
  /** World y grows upwards (maps, robotics). Default true. */
  flipY?: boolean;
  /** Zoom clamp in pixels per world unit, applied by every zoom and fit method (defaults 1e-6 and 1e6). */
  minZoom?: number; maxZoom?: number;
}

/**
 * A 2D camera: center + zoom + rotation over a viewport. The interface is dimension-agnostic so a
 * `Camera3D` can be added later; layers declare which kinds they support.
 * Pinned by `spec/fixtures/camera.json`.
 */
export class Camera2D implements Projection {
  /** Always "2d". */
  readonly kind = "2d" as const;
  /** Viewport size in pixels; write through `setViewport` so `version` advances. */
  width: number; height: number;
  /** World point shown at the viewport centre; write through `setCenter` so `version` advances. */
  centerX: number; centerY: number;
  private _zoom: number; private _rotation: number;
  /** True when world y grows upwards on screen (the default), which negates the y scale. */
  readonly flipY: boolean;
  /** Zoom clamp in pixels per world unit. */
  readonly minZoom: number; readonly maxZoom: number;
  private _version = 0;
  private cache: { version: number; m: Mat3; inv: Mat3 } | null = null;

  /** Builds a camera from options; missing values take the defaults described on `Camera2DOptions`. */
  constructor(o: Camera2DOptions = {}) {
    this.width = o.width ?? 1; this.height = o.height ?? 1;
    this.centerX = o.centerX ?? 0; this.centerY = o.centerY ?? 0;
    this.minZoom = o.minZoom ?? 1e-6; this.maxZoom = o.maxZoom ?? 1e6;
    this._zoom = Math.min(this.maxZoom, Math.max(this.minZoom, o.zoom ?? 1));
    this._rotation = o.rotation ?? 0;
    this.flipY = o.flipY ?? true;
  }

  /** Change counter; incremented by every setter, by `pan` and `zoomAt`, and by the fit methods. */
  get version(): number { return this._version; }
  /** Pixels per world unit. */
  get zoom(): number { return this._zoom; }
  /** View rotation in radians, clockwise on screen. */
  get rotation(): number { return this._rotation; }

  private touch(): void { this._version++; }

  /** Resize the viewport; a no-op (no version bump) when the size is unchanged. */
  setViewport(width: number, height: number): void {
    if (width === this.width && height === this.height) return;
    this.width = width; this.height = height; this.touch();
  }
  /** Move the world point shown at the viewport centre. */
  setCenter(x: number, y: number): void { this.centerX = x; this.centerY = y; this.touch(); }
  /** Set pixels per world unit, clamped to [minZoom, maxZoom]. */
  setZoom(zoom: number): void { this._zoom = Math.min(this.maxZoom, Math.max(this.minZoom, zoom)); this.touch(); }
  /** Set the view rotation in radians (clockwise on screen). */
  setRotation(radians: number): void { this._rotation = radians; this.touch(); }

  /** World → screen matrix: T(viewport/2) · R · S(zoom, ±zoom) · T(−center). */
  matrix(): Mat3 {
    if (this.cache && this.cache.version === this._version) return this.cache.m;
    const m = mul(translation(this.width / 2, this.height / 2), mul(rotation(this._rotation), mul(scaling(this._zoom, this.flipY ? -this._zoom : this._zoom), translation(-this.centerX, -this.centerY))));
    this.cache = { version: this._version, m, inv: invert(m) ?? IDENTITY };
    return m;
  }
  /** Screen → world matrix, cached together with `matrix()`; the identity when the matrix is singular. */
  inverse(): Mat3 { this.matrix(); return this.cache!.inv; }

  /** World point → screen pixels through `matrix()`. */
  project(wx: number, wy: number): Vec2 { return apply(this.matrix(), wx, wy); }
  /** Screen pixels → world point through `inverse()`. */
  unproject(sx: number, sy: number): Vec2 { return apply(this.inverse(), sx, sy); }

  /** Drag the content by (dx, dy) screen pixels. */
  pan(dx: number, dy: number): void {
    const d = applyLinear(this.inverse(), dx, dy);
    this.centerX -= d.x; this.centerY -= d.y; this.touch();
  }

  /** Multiply zoom by `factor`, keeping the world point under (sx, sy) fixed on screen. */
  zoomAt(sx: number, sy: number, factor: number): void {
    const w = this.unproject(sx, sy);
    this.setZoom(this._zoom * factor);
    const p = this.project(w.x, w.y);
    this.pan(sx - p.x, sy - p.y);
  }

  /** Fit a world rectangle into the viewport with `padding` pixels on each side (respects rotation). */
  fitBounds(r: Rect, padding = 0): void {
    if (!(r.w >= 0) || !(r.h >= 0)) return;
    const rot = mul(rotation(this._rotation), scaling(1, this.flipY ? -1 : 1));
    const box = transformRect(rot, r);
    const availW = Math.max(1, this.width - 2 * padding), availH = Math.max(1, this.height - 2 * padding);
    const zoom = Math.min(box.w > 0 ? availW / box.w : Infinity, box.h > 0 ? availH / box.h : Infinity);
    this.centerX = r.x + r.w / 2; this.centerY = r.y + r.h / 2;
    this._zoom = Math.min(this.maxZoom, Math.max(this.minZoom, Number.isFinite(zoom) ? zoom : this._zoom));
    this.touch();
  }

  /** Zoom to a screen-space rectangle (box zoom). */
  fitScreenRect(r: Rect, padding = 0): void {
    const pts: number[] = [];
    for (const [x, y] of [[r.x, r.y], [r.x + r.w, r.y], [r.x + r.w, r.y + r.h], [r.x, r.y + r.h]] as const) { const w = this.unproject(x, y); pts.push(w.x, w.y); }
    const b = rectFromPointsSafe(pts);
    this.fitBounds(b, padding);
  }

  /** Axis-aligned world rectangle covering the viewport. */
  worldBounds(): Rect {
    const pts: number[] = [];
    for (const [x, y] of [[0, 0], [this.width, 0], [this.width, this.height], [0, this.height]] as const) { const w = this.unproject(x, y); pts.push(w.x, w.y); }
    return rectFromPointsSafe(pts);
  }
}

function rectFromPointsSafe(pts: number[]): Rect {
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
  for (let i = 0; i < pts.length; i += 2) { const x = pts[i]!, y = pts[i + 1]!; if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
  return { x: x0, y: y0, w: x1 - x0, h: y1 - y0 };
}

/** A projection built from two axis scales — how chart plot areas host scene layers. */
export class ScaleProjection implements Projection {
  /** Always "2d". */
  readonly kind = "2d" as const;
  private _version = 0;
  /** Wraps the two axis scales, kept as public fields; replace them through `setScales` so `version` advances. */
  constructor(public xScale: Scale, public yScale: Scale) {}
  /** Bumped by `setScales`; hosts must call that whenever either scale's domain or range changes. */
  get version(): number { return this._version; }
  /** Replace both scales and bump `version` so cached layer surfaces redraw. */
  setScales(x: Scale, y: Scale): void { this.xScale = x; this.yScale = y; this._version++; }
  /** World → screen through the two scales. */
  project(wx: number, wy: number): Vec2 { return { x: this.xScale.scale(wx), y: this.yScale.scale(wy) }; }
  /** Screen → world through the scales' `invert`. */
  unproject(sx: number, sy: number): Vec2 { return { x: this.xScale.invert(sx), y: this.yScale.invert(sy) }; }
}
