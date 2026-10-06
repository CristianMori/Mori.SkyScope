// Mori.SkyScope — Orbit camera for the 3D viewport: perspective or orthographic projection, fit, picking rays.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Vec2, Vec3 } from "../scene/geometry.js";
import type { Projection } from "../scene/camera.js";
import { mat4Invert, mat4LookAt, mat4Mul, mat4Ortho, mat4Perspective, mat4Point, box3Sphere, rayPlane, rayAt, v3, v3add, v3norm, v3scale, v3sub, MAT4_IDENTITY, type Box3, type Mat4, type Ray } from "./math3.js";

/**
 * Initial state for a `Camera3D`; missing fields take the constructor defaults (distance 10, yaw π, pitch 0.6, fov
 * π/4, perspective).
 */
export interface Camera3DOptions {
  /** Viewport size in pixels. */
  width?: number; height?: number;
  /** Orbit centre. */
  target?: Vec3;
  /** Eye distance from the target, world units. */
  distance?: number;
  /** Azimuth of the eye around +z, radians; 0 puts the eye on +x looking towards −x. */
  yaw?: number;
  /** Elevation of the eye above the xy plane, radians; π/2 is top-down. */
  pitch?: number;
  /** Vertical field of view, radians. */
  fov?: number;
  /** Clip plane distances in world units (defaults 0.05 and 5000). */
  near?: number; far?: number;
  /** Orthographic projection with the same framing (half-height = distance · tan(fov/2)). */
  ortho?: boolean;
  /** Dolly clamp in world units (defaults 1e-3 and 1e6). */
  minDistance?: number; maxDistance?: number;
}

const MAX_PITCH = Math.PI / 2 - 1e-3;
const clamp = (v: number, lo: number, hi: number): number => Math.min(hi, Math.max(lo, v));

/** Result of `Camera3D.project3`: screen pixels (y down) plus depth and visibility. */
export interface Projected3 { x: number; y: number; /** NDC depth in [−1, 1]. */ depth: number; /** In front of the camera and inside the frustum depth range. */ visible: boolean }

/**
 * An orbit camera: target + distance + yaw + pitch, z up, perspective or orthographic. Implements the
 * dimension-agnostic `Projection` (kind `3d`): `project`/`unproject` work on the ground plane z = 0 so 2D-aware
 * tools (cursor readout, measure) keep functioning. Pinned by `spec/fixtures/camera3d.json`.
 * Mirrors `Mori.SkyScope.Core.Scene3D.Camera3D`.
 */
export class Camera3D implements Projection {
  /** Always "3d". */
  readonly kind = "3d" as const;
  /** Viewport size in pixels; set it through `setViewport`. */
  width: number; height: number;
  /** Orbit centre in world units; set it through `setTarget`, or let `pan` and `dollyAt` move it. */
  target: Vec3;
  private _distance: number; private _yaw: number; private _pitch: number;
  /**
   * Vertical field of view in radians, clip distances and projection mode; use `setFov` and `setOrtho` so `version`
   * advances.
   */
  fov: number; near: number; far: number; ortho: boolean;
  /** Dolly clamp in world units. */
  readonly minDistance: number; readonly maxDistance: number;
  private _version = 0;
  private cache: { version: number; view: Mat4; proj: Mat4; viewProj: Mat4; inv: Mat4 } | null = null;

  /** Builds a camera from options; see `Camera3DOptions` for the defaults. */
  constructor(o: Camera3DOptions = {}) {
    this.width = o.width ?? 1; this.height = o.height ?? 1;
    this.target = o.target ? { ...o.target } : v3(0, 0, 0);
    this.minDistance = o.minDistance ?? 1e-3; this.maxDistance = o.maxDistance ?? 1e6;
    this._distance = clamp(o.distance ?? 10, this.minDistance, this.maxDistance);
    this._yaw = o.yaw ?? Math.PI; this._pitch = clamp(o.pitch ?? 0.6, -MAX_PITCH, MAX_PITCH);
    this.fov = o.fov ?? Math.PI / 4; this.near = o.near ?? 0.05; this.far = o.far ?? 5000;
    this.ortho = o.ortho ?? false;
  }

  /** Change counter; incremented by every setter and motion method. */
  get version(): number { return this._version; }
  /** Eye distance from the target, world units. */
  get distance(): number { return this._distance; }
  /** Azimuth of the eye around +z, radians. */
  get yaw(): number { return this._yaw; }
  /** Elevation of the eye above the xy plane, radians, clamped just short of ±π/2. */
  get pitch(): number { return this._pitch; }
  /** Viewport width divided by height. */
  get aspect(): number { return this.width / Math.max(1e-9, this.height); }
  private touch(): void { this._version++; }

  /** Unit vector from the target towards the eye. */
  private orbitDir(): Vec3 { const cp = Math.cos(this._pitch); return v3(cp * Math.cos(this._yaw), cp * Math.sin(this._yaw), Math.sin(this._pitch)); }
  /** Eye position in world units. */
  eye(): Vec3 { return v3add(this.target, v3scale(this.orbitDir(), this._distance)); }

  /** Resize the viewport; no version bump when unchanged. */
  setViewport(width: number, height: number): void { if (width === this.width && height === this.height) return; this.width = width; this.height = height; this.touch(); }
  /** Move the orbit centre. */
  setTarget(x: number, y: number, z: number): void { this.target = v3(x, y, z); this.touch(); }
  /** Set the eye distance, clamped to [minDistance, maxDistance]. */
  setDistance(d: number): void { this._distance = clamp(d, this.minDistance, this.maxDistance); this.touch(); }
  /** Set yaw and pitch in radians (pitch clamped). */
  setOrbit(yaw: number, pitch: number): void { this._yaw = yaw; this._pitch = clamp(pitch, -MAX_PITCH, MAX_PITCH); this.touch(); }
  /** Set the vertical field of view in radians, clamped to (0, π). */
  setFov(fov: number): void { this.fov = clamp(fov, 0.01, Math.PI - 0.01); this.touch(); }
  /** Switch between perspective and orthographic projection. */
  setOrtho(ortho: boolean): void { if (ortho === this.ortho) return; this.ortho = ortho; this.touch(); }

  private matrices(): { view: Mat4; proj: Mat4; viewProj: Mat4; inv: Mat4 } {
    if (this.cache && this.cache.version === this._version) return this.cache;
    const view = mat4LookAt(this.eye(), this.target, v3(0, 0, 1));
    let proj: Mat4;
    if (this.ortho) { const h = this._distance * Math.tan(this.fov / 2), w = h * this.aspect; proj = mat4Ortho(-w, w, -h, h, -this.far, this.far); }
    else proj = mat4Perspective(this.fov, this.aspect, this.near, this.far);
    const viewProj = mat4Mul(proj, view);
    this.cache = { version: this._version, view, proj, viewProj, inv: mat4Invert(viewProj) ?? MAT4_IDENTITY };
    return this.cache;
  }
  /** World → eye matrix (cached per version). */
  view(): Mat4 { return this.matrices().view; }
  /** Eye → clip matrix (cached per version). */
  projection(): Mat4 { return this.matrices().proj; }
  /** `projection × view`, cached per version. */
  viewProj(): Mat4 { return this.matrices().viewProj; }

  /** World point → screen pixels (y down) + NDC depth. */
  project3(x: number, y: number, z: number): Projected3 {
    const m = this.matrices().viewProj;
    const w = m[3]! * x + m[7]! * y + m[11]! * z + m[15]!;
    const cx = m[0]! * x + m[4]! * y + m[8]! * z + m[12]!, cy = m[1]! * x + m[5]! * y + m[9]! * z + m[13]!, cz = m[2]! * x + m[6]! * y + m[10]! * z + m[14]!;
    const iw = w === 0 ? 1 : 1 / w;
    const nx = cx * iw, ny = cy * iw, nz = cz * iw;
    return { x: (nx + 1) / 2 * this.width, y: (1 - ny) / 2 * this.height, depth: nz, visible: w > 0 && nz >= -1 && nz <= 1 };
  }
  /** Screen pixels + NDC depth → world point. */
  unproject3(sx: number, sy: number, depth: number): Vec3 {
    return mat4Point(this.matrices().inv, sx / this.width * 2 - 1, 1 - sy / this.height * 2, depth);
  }
  /** Ray through a screen pixel, unit direction. */
  ray(sx: number, sy: number): Ray {
    const a = this.unproject3(sx, sy, -1), b = this.unproject3(sx, sy, 1);
    return { origin: a, dir: v3norm(v3sub(b, a)) };
  }
  /** Where the pixel's ray meets the ground plane z = 0, or null when it looks away from it. */
  groundPoint(sx: number, sy: number): Vec3 | null {
    const r = this.ray(sx, sy);
    const t = rayPlane(r, v3(0, 0, 1), 0);
    return t === null ? null : rayAt(r, t);
  }

  // Projection (2D contract): the ground plane.
  /** 2D contract: projects the ground point (wx, wy, 0) to screen pixels. */
  project(wx: number, wy: number): Vec2 { const p = this.project3(wx, wy, 0); return { x: p.x, y: p.y }; }
  /**
   * 2D contract: the ground-plane point under the pixel, or the point at the target's distance along the ray when the
   * ray misses the ground.
   */
  unproject(sx: number, sy: number): Vec2 {
    const g = this.groundPoint(sx, sy);
    if (g) return { x: g.x, y: g.y };
    const r = this.ray(sx, sy), p = rayAt(r, this._distance);
    return { x: p.x, y: p.y };
  }

  /** World units per screen pixel at the target's depth. */
  worldPerPixel(): number { return 2 * this._distance * Math.tan(this.fov / 2) / Math.max(1, this.height); }

  /** Rotate the eye around the target by yaw and pitch deltas in radians. */
  orbit(dyaw: number, dpitch: number): void { this.setOrbit(this._yaw + dyaw, this._pitch + dpitch); }
  /** Drag the scene by (dx, dy) screen pixels: the target slides in the view plane. */
  pan(dx: number, dy: number): void {
    const v = this.matrices().view, upp = this.worldPerPixel();
    const right = v3(v[0]!, v[4]!, v[8]!), up = v3(v[1]!, v[5]!, v[9]!);
    this.target = v3add(this.target, v3add(v3scale(right, -dx * upp), v3scale(up, dy * upp)));
    this.touch();
  }
  /** Multiply the distance by `factor` (< 1 moves closer). */
  dolly(factor: number): void { this.setDistance(this._distance * factor); }
  /** Dolly about the world point under (sx, sy): that point stays fixed on screen. */
  dollyAt(sx: number, sy: number, factor: number): void {
    const r = this.ray(sx, sy);
    const t = rayPlane(r, v3(0, 0, 1), 0);
    const p = t === null ? rayAt(r, this._distance) : rayAt(r, t);
    const f = clamp(this._distance * factor, this.minDistance, this.maxDistance) / this._distance;
    this.target = v3add(p, v3scale(v3sub(this.target, p), f));
    this._distance *= f;
    this.touch();
  }
  /** Frame a sphere: the whole sphere fits in the smaller viewport dimension. */
  fitSphere(center: Vec3, radius: number): void {
    this.target = { ...center };
    const r = Math.max(radius, 1e-6), k = Math.min(1, this.aspect);
    const d = this.ortho ? r / (Math.tan(this.fov / 2) * k) : r / (Math.sin(this.fov / 2) * k);
    this._distance = clamp(d, this.minDistance, this.maxDistance);
    this.touch();
  }
  /** Frame a box through its bounding sphere. */
  fitBox(box: Box3): void { const s = box3Sphere(box); this.fitSphere(s.center, s.radius); }
}
