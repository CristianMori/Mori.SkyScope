// Mori.SkyScope — 3D math shared by the 3D camera, frame tree and layers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Vec3 } from "../scene/geometry.js";

/**
 * 3D math shared by the 3D camera, frame tree and layers. Mirrors `Mori.SkyScope.Core.Scene3D.Math3`;
 * pinned by `spec/fixtures/math3.json`. Matrices are column-major 16-element arrays (WebGL order):
 * element (row r, column c) is `m[c * 4 + r]`; `x' = m · [x y z 1]`. Right-handed, z up (ROS convention).
 */
/** Column-major 4 × 4 matrix as 16 numbers (element order in the module notes above). */
export type Mat4 = number[];
/** Unit quaternion (x, y, z, w). */
export interface Quat { x: number; y: number; z: number; w: number }
/** Axis-aligned box; `box3Empty()` has min above max. */
export interface Box3 { min: Vec3; max: Vec3 }
/** Origin plus unit direction. */
export interface Ray { origin: Vec3; dir: Vec3 }

/** Construct a vector. */
export const v3 = (x: number, y: number, z: number): Vec3 => ({ x, y, z });
/** Component-wise sum. */
export const v3add = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x + b.x, y: a.y + b.y, z: a.z + b.z });
/** Component-wise difference a − b. */
export const v3sub = (a: Vec3, b: Vec3): Vec3 => ({ x: a.x - b.x, y: a.y - b.y, z: a.z - b.z });
/** Multiply by a scalar. */
export const v3scale = (a: Vec3, s: number): Vec3 => ({ x: a.x * s, y: a.y * s, z: a.z * s });
/** Dot product. */
export const v3dot = (a: Vec3, b: Vec3): number => a.x * b.x + a.y * b.y + a.z * b.z;
/** Cross product a × b. */
export const v3cross = (a: Vec3, b: Vec3): Vec3 => ({ x: a.y * b.z - a.z * b.y, y: a.z * b.x - a.x * b.z, z: a.x * b.y - a.y * b.x });
/** Euclidean length. */
export const v3len = (a: Vec3): number => Math.sqrt(a.x * a.x + a.y * a.y + a.z * a.z);
/** Unit vector in the same direction; the zero vector stays zero. */
export function v3norm(a: Vec3): Vec3 { const l = v3len(a); return l > 0 ? v3scale(a, 1 / l) : { x: 0, y: 0, z: 0 }; }
/** Linear interpolation from `a` (t = 0) to `b` (t = 1). */
export const v3lerp = (a: Vec3, b: Vec3, t: number): Vec3 => ({ x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t, z: a.z + (b.z - a.z) * t });

/** No rotation. */
export const QUAT_IDENTITY: Quat = { x: 0, y: 0, z: 0, w: 1 };
/** Scale to unit length; a zero quaternion becomes the identity. */
export function quatNormalize(q: Quat): Quat {
  const l = Math.sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
  return l > 0 ? { x: q.x / l, y: q.y / l, z: q.z / l, w: q.w / l } : QUAT_IDENTITY;
}
/** Rotation of `radians` about a unit axis. */
export function quatFromAxisAngle(axis: Vec3, radians: number): Quat {
  const a = v3norm(axis), s = Math.sin(radians / 2);
  return { x: a.x * s, y: a.y * s, z: a.z * s, w: Math.cos(radians / 2) };
}
/** ZYX (yaw about z, then pitch about y, then roll about x) — the ROS/REP-103 convention. */
export function quatFromEuler(roll: number, pitch: number, yaw: number): Quat {
  const cr = Math.cos(roll / 2), sr = Math.sin(roll / 2), cp = Math.cos(pitch / 2), sp = Math.sin(pitch / 2), cy = Math.cos(yaw / 2), sy = Math.sin(yaw / 2);
  return { x: sr * cp * cy - cr * sp * sy, y: cr * sp * cy + sr * cp * sy, z: cr * cp * sy - sr * sp * cy, w: cr * cp * cy + sr * sp * sy };
}
/** Inverse of `quatFromEuler`; pitch clamped to ±π/2. */
export function quatToEuler(q: Quat): { roll: number; pitch: number; yaw: number } {
  const sinp = 2 * (q.w * q.y - q.z * q.x);
  return {
    roll: Math.atan2(2 * (q.w * q.x + q.y * q.z), 1 - 2 * (q.x * q.x + q.y * q.y)),
    pitch: Math.abs(sinp) >= 1 ? (sinp < 0 ? -Math.PI / 2 : Math.PI / 2) : Math.asin(sinp),
    yaw: Math.atan2(2 * (q.w * q.z + q.x * q.y), 1 - 2 * (q.y * q.y + q.z * q.z)),
  };
}
/** `quatMul(a, b)` applies `b` first, then `a`. */
export function quatMul(a: Quat, b: Quat): Quat {
  return {
    x: a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
    y: a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
    z: a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
    w: a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z,
  };
}
/** Inverse rotation of a unit quaternion. */
export const quatConjugate = (q: Quat): Quat => ({ x: -q.x, y: -q.y, z: -q.z, w: q.w });
/** Rotate a vector by a unit quaternion. */
export function quatRotate(q: Quat, v: Vec3): Vec3 {
  // v' = v + 2 · (q.w · (q.xyz × v) + q.xyz × (q.xyz × v))
  const tx = 2 * (q.y * v.z - q.z * v.y), ty = 2 * (q.z * v.x - q.x * v.z), tz = 2 * (q.x * v.y - q.y * v.x);
  return { x: v.x + q.w * tx + (q.y * tz - q.z * ty), y: v.y + q.w * ty + (q.z * tx - q.x * tz), z: v.z + q.w * tz + (q.x * ty - q.y * tx) };
}
/** Spherical interpolation along the shorter arc. */
export function quatSlerp(a: Quat, b: Quat, t: number): Quat {
  let bx = b.x, by = b.y, bz = b.z, bw = b.w;
  let cos = a.x * bx + a.y * by + a.z * bz + a.w * bw;
  if (cos < 0) { cos = -cos; bx = -bx; by = -by; bz = -bz; bw = -bw; }
  let ka: number, kb: number;
  if (cos > 0.9995) { ka = 1 - t; kb = t; }
  else { const th = Math.acos(cos), s = Math.sin(th); ka = Math.sin((1 - t) * th) / s; kb = Math.sin(t * th) / s; }
  return quatNormalize({ x: ka * a.x + kb * bx, y: ka * a.y + kb * by, z: ka * a.z + kb * bz, w: ka * a.w + kb * bw });
}

/** The identity matrix. */
export const MAT4_IDENTITY: Mat4 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
/** Matrix that moves points by (x, y, z). */
export const mat4Translation = (x: number, y: number, z: number): Mat4 => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];
/** Matrix that scales each axis about the origin. */
export const mat4Scaling = (x: number, y: number, z: number): Mat4 => [x, 0, 0, 0, 0, y, 0, 0, 0, 0, z, 0, 0, 0, 0, 1];
/** Rotation matrix of a unit quaternion. */
export function mat4FromQuat(q: Quat): Mat4 {
  const { x, y, z, w } = q;
  const xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
  return [1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy), 0, 2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx), 0, 2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy), 0, 0, 0, 0, 1];
}
/** Rigid transform: rotate by `q`, then translate by `t`. */
export function mat4FromPose(t: Vec3, q: Quat): Mat4 { const m = mat4FromQuat(q); m[12] = t.x; m[13] = t.y; m[14] = t.z; return m; }
/** `mat4Mul(a, b)` applies `b` first, then `a`. */
export function mat4Mul(a: Mat4, b: Mat4): Mat4 {
  const o: number[] = new Array<number>(16);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) {
    o[c * 4 + r] = a[r]! * b[c * 4]! + a[4 + r]! * b[c * 4 + 1]! + a[8 + r]! * b[c * 4 + 2]! + a[12 + r]! * b[c * 4 + 3]!;
  }
  return o;
}
/** Swap rows and columns. */
export function mat4Transpose(m: Mat4): Mat4 {
  const o: number[] = new Array<number>(16);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) o[c * 4 + r] = m[r * 4 + c]!;
  return o;
}
/** General inverse by cofactors, or null when singular or non-finite. */
export function mat4Invert(m: Mat4): Mat4 | null {
  const a00 = m[0]!, a01 = m[1]!, a02 = m[2]!, a03 = m[3]!, a10 = m[4]!, a11 = m[5]!, a12 = m[6]!, a13 = m[7]!;
  const a20 = m[8]!, a21 = m[9]!, a22 = m[10]!, a23 = m[11]!, a30 = m[12]!, a31 = m[13]!, a32 = m[14]!, a33 = m[15]!;
  const b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
  const b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12, b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
  const b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
  const det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
  if (det === 0 || !Number.isFinite(det)) return null;
  const d = 1 / det;
  return [
    (a11 * b11 - a12 * b10 + a13 * b09) * d, (a02 * b10 - a01 * b11 - a03 * b09) * d, (a31 * b05 - a32 * b04 + a33 * b03) * d, (a22 * b04 - a21 * b05 - a23 * b03) * d,
    (a12 * b08 - a10 * b11 - a13 * b07) * d, (a00 * b11 - a02 * b08 + a03 * b07) * d, (a32 * b02 - a30 * b05 - a33 * b01) * d, (a20 * b05 - a22 * b02 + a23 * b01) * d,
    (a10 * b10 - a11 * b08 + a13 * b06) * d, (a01 * b08 - a00 * b10 - a03 * b06) * d, (a30 * b04 - a31 * b02 + a33 * b00) * d, (a21 * b02 - a20 * b04 - a23 * b00) * d,
    (a11 * b07 - a10 * b09 - a12 * b06) * d, (a00 * b09 - a01 * b07 + a02 * b06) * d, (a31 * b01 - a30 * b03 - a32 * b00) * d, (a20 * b03 - a21 * b01 + a22 * b00) * d,
  ];
}
/** Transform a point (w = 1), dividing by w. */
export function mat4Point(m: Mat4, x: number, y: number, z: number): Vec3 {
  const w = m[3]! * x + m[7]! * y + m[11]! * z + m[15]!;
  const iw = w === 0 ? 1 : 1 / w;
  return { x: (m[0]! * x + m[4]! * y + m[8]! * z + m[12]!) * iw, y: (m[1]! * x + m[5]! * y + m[9]! * z + m[13]!) * iw, z: (m[2]! * x + m[6]! * y + m[10]! * z + m[14]!) * iw };
}
/** Transform a direction (w = 0). */
export const mat4Dir = (m: Mat4, x: number, y: number, z: number): Vec3 => ({ x: m[0]! * x + m[4]! * y + m[8]! * z, y: m[1]! * x + m[5]! * y + m[9]! * z, z: m[2]! * x + m[6]! * y + m[10]! * z });

/** Right-handed view matrix looking from `eye` to `target`. */
export function mat4LookAt(eye: Vec3, target: Vec3, up: Vec3): Mat4 {
  const f = v3norm(v3sub(target, eye));
  let s = v3cross(f, up);
  if (v3len(s) < 1e-9) s = v3cross(f, Math.abs(f.z) < 0.9 ? { x: 0, y: 0, z: 1 } : { x: 1, y: 0, z: 0 });
  s = v3norm(s);
  const u = v3cross(s, f);
  return [s.x, u.x, -f.x, 0, s.y, u.y, -f.y, 0, s.z, u.z, -f.z, 0, -v3dot(s, eye), -v3dot(u, eye), v3dot(f, eye), 1];
}
/** OpenGL clip space (z in [−1, 1]); `fovY` in radians. */
export function mat4Perspective(fovY: number, aspect: number, near: number, far: number): Mat4 {
  const f = 1 / Math.tan(fovY / 2), nf = 1 / (near - far);
  return [f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) * nf, -1, 0, 0, 2 * far * near * nf, 0];
}
/** Orthographic projection to OpenGL clip space (z in [−1, 1]) for the given view volume. */
export function mat4Ortho(left: number, right: number, bottom: number, top: number, near: number, far: number): Mat4 {
  const lr = 1 / (left - right), bt = 1 / (bottom - top), nf = 1 / (near - far);
  return [-2 * lr, 0, 0, 0, 0, -2 * bt, 0, 0, 0, 0, 2 * nf, 0, (left + right) * lr, (top + bottom) * bt, (far + near) * nf, 1];
}

/** A box containing nothing (min = +∞, max = −∞), ready for union. */
export const box3Empty = (): Box3 => ({ min: v3(Infinity, Infinity, Infinity), max: v3(-Infinity, -Infinity, -Infinity) });
/** True when any max is below its min. */
export const box3IsEmpty = (b: Box3): boolean => !(b.max.x >= b.min.x && b.max.y >= b.min.y && b.max.z >= b.min.z);
/** Smallest box covering both; a null operand is ignored. */
export function box3Union(a: Box3 | null, b: Box3 | null): Box3 | null {
  if (!a) return b; if (!b) return a;
  return { min: v3(Math.min(a.min.x, b.min.x), Math.min(a.min.y, b.min.y), Math.min(a.min.z, b.min.z)), max: v3(Math.max(a.max.x, b.max.x), Math.max(a.max.y, b.max.y), Math.max(a.max.z, b.max.z)) };
}
/** Bounds of interleaved xyz positions (optionally transformed). */
export function box3FromPositions(p: ArrayLike<number>, m?: Mat4): Box3 | null {
  const n = Math.floor(p.length / 3);
  if (n === 0) return null;
  const b = box3Empty();
  for (let i = 0; i < n; i++) {
    let x = p[3 * i]!, y = p[3 * i + 1]!, z = p[3 * i + 2]!;
    if (m) { const q = mat4Point(m, x, y, z); x = q.x; y = q.y; z = q.z; }
    if (x < b.min.x) b.min.x = x; if (x > b.max.x) b.max.x = x; if (y < b.min.y) b.min.y = y; if (y > b.max.y) b.max.y = y; if (z < b.min.z) b.min.z = z; if (z > b.max.z) b.max.z = z;
  }
  return b;
}
/** Bounding sphere of a box (centre + half diagonal). */
export function box3Sphere(b: Box3): { center: Vec3; radius: number } {
  return { center: v3scale(v3add(b.min, b.max), 0.5), radius: v3len(v3sub(b.max, b.min)) / 2 };
}

/** Distance along the ray to the plane `n · p = d`, or null when parallel or behind the origin. */
export function rayPlane(ray: Ray, n: Vec3, d: number): number | null {
  const denom = v3dot(n, ray.dir);
  if (Math.abs(denom) < 1e-12) return null;
  const t = (d - v3dot(n, ray.origin)) / denom;
  return t >= 0 ? t : null;
}
/** Slab test; entry distance or null. */
export function rayBox(ray: Ray, b: Box3): number | null {
  let t0 = 0, t1 = Infinity;
  const o = [ray.origin.x, ray.origin.y, ray.origin.z], dd = [ray.dir.x, ray.dir.y, ray.dir.z];
  const mn = [b.min.x, b.min.y, b.min.z], mx = [b.max.x, b.max.y, b.max.z];
  for (let i = 0; i < 3; i++) {
    if (Math.abs(dd[i]!) < 1e-12) { if (o[i]! < mn[i]! || o[i]! > mx[i]!) return null; continue; }
    let a = (mn[i]! - o[i]!) / dd[i]!, c = (mx[i]! - o[i]!) / dd[i]!;
    if (a > c) { const s = a; a = c; c = s; }
    if (a > t0) t0 = a; if (c < t1) t1 = c;
    if (t0 > t1) return null;
  }
  return t0;
}
/** Nearest hit on a sphere, or null. */
export function raySphere(ray: Ray, center: Vec3, radius: number): number | null {
  const oc = v3sub(ray.origin, center);
  const b = v3dot(oc, ray.dir), c = v3dot(oc, oc) - radius * radius;
  const disc = b * b - c;
  if (disc < 0) return null;
  const s = Math.sqrt(disc);
  const t = -b - s;
  if (t >= 0) return t;
  const t2 = -b + s;
  return t2 >= 0 ? t2 : null;
}
/** Point at distance `t` along the ray. */
export const rayAt = (r: Ray, t: number): Vec3 => v3add(r.origin, v3scale(r.dir, t));
