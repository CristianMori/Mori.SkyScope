// Mori.SkyScope — tf-style frame tree: timed rigid transforms between named frames, lookups at a stamp, static and dynamic entries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Vec3 } from "../scene/geometry.js";
import { mat4FromPose, mat4Invert, mat4Mul, quatSlerp, v3lerp, QUAT_IDENTITY, MAT4_IDENTITY, type Mat4, type Quat } from "./math3.js";

/** Rigid transform of a child frame expressed in its parent: rotate by `q`, then translate by `t`. */
export interface Transform3 { t: Vec3; q: Quat }
interface Sample { time: number; t: Vec3; q: Quat }
interface FrameEntry { parent: string; samples: Sample[]; static: boolean }

/**
 * A tf2-style tree of coordinate frames with timed transforms. `set` records "child is at `tf` in parent at
 * `time`" (or a static transform when `time` is omitted); `lookup(target, source, time)` returns the matrix that
 * maps points expressed in `source` into `target`, interpolating between the two nearest samples and clamping
 * outside the recorded range. Pinned by `spec/fixtures/frame-tree.json`. Mirrors
 * `Mori.SkyScope.Core.Scene3D.FrameTree`.
 */
export class FrameTree {
  private readonly frames = new Map<string, FrameEntry>();
  private _version = 0;
  /** Samples kept per frame; older ones are dropped. */
  constructor(readonly maxSamples = 200) {}

  /** Change counter bumped by `set`, `remove` and `clear`; layers use it to invalidate cached lookups. */
  get version(): number { return this._version; }

  /**
   * Record that `child` sits at `tf` in `parent` at `time`, or as a static transform when `time` is omitted. Samples
   * stay sorted by time; a static entry replaces the history, a timed one after a static entry discards it, and
   * reparenting starts a fresh history. A frame cannot be its own parent.
   */
  set(child: string, parent: string, tf: Transform3, time?: number): void {
    if (child === parent) return;
    let e = this.frames.get(child);
    if (!e || e.parent !== parent) { e = { parent, samples: [], static: false }; this.frames.set(child, e); }
    const s: Sample = { time: time ?? 0, t: { ...tf.t }, q: { ...tf.q } };
    if (time === undefined) { e.samples = [s]; e.static = true; }
    else {
      if (e.static) { e.samples = []; e.static = false; }
      const arr = e.samples;
      if (arr.length === 0 || time >= arr[arr.length - 1]!.time) arr.push(s);
      else { let i = arr.length - 1; while (i >= 0 && arr[i]!.time > time) i--; arr.splice(i + 1, 0, s); }
      if (arr.length > this.maxSamples) arr.splice(0, arr.length - this.maxSamples);
    }
    this._version++;
  }
  /** Forget a frame's link to its parent. */
  remove(frame: string): void { if (this.frames.delete(frame)) this._version++; }
  /** Forget every frame. */
  clear(): void { if (this.frames.size) { this.frames.clear(); this._version++; } }

  /** True when the frame has a recorded parent. */
  has(frame: string): boolean { return this.frames.has(frame); }
  /** The frame's parent, or null for roots and unknown frames. */
  parent(frame: string): string | null { return this.frames.get(frame)?.parent ?? null; }
  /** Every frame that has a parent, plus every parent, sorted. */
  frameIds(): string[] {
    const s = new Set<string>();
    for (const [c, e] of this.frames) { s.add(c); s.add(e.parent); }
    return [...s].sort();
  }
  /** Frames with no parent (usually one: the fixed frame). */
  roots(): string[] { return this.frameIds().filter((f) => !this.frames.has(f)); }
  /** Latest sample time of a frame, or null for static/unknown. */
  latestTime(frame: string): number | null { const e = this.frames.get(frame); return e && !e.static && e.samples.length ? e.samples[e.samples.length - 1]!.time : null; }

  /** The child's transform in its parent at `time` (latest when omitted); null when unknown. */
  transformAt(child: string, time?: number): Transform3 | null {
    const e = this.frames.get(child);
    if (!e || e.samples.length === 0) return null;
    const arr = e.samples;
    if (e.static || time === undefined || arr.length === 1) { const s = arr[arr.length - 1]!; return { t: s.t, q: s.q }; }
    if (time <= arr[0]!.time) return { t: arr[0]!.t, q: arr[0]!.q };
    if (time >= arr[arr.length - 1]!.time) { const s = arr[arr.length - 1]!; return { t: s.t, q: s.q }; }
    let lo = 0, hi = arr.length - 1;
    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (arr[mid]!.time <= time) lo = mid; else hi = mid; }
    const a = arr[lo]!, b = arr[hi]!;
    const span = b.time - a.time, u = span > 0 ? (time - a.time) / span : 0;
    return { t: v3lerp(a.t, b.t, u), q: quatSlerp(a.q, b.q, u) };
  }

  /** Matrix mapping points in `frame` into its root frame; null if a link has no samples. */
  private toRoot(frame: string, time: number | undefined): { root: string; m: Mat4 } | null {
    let m: Mat4 = MAT4_IDENTITY, f = frame;
    const seen = new Set<string>();
    for (;;) {
      const e = this.frames.get(f);
      if (!e) return { root: f, m };
      if (seen.has(f)) return null;
      seen.add(f);
      const tf = this.transformAt(f, time);
      if (!tf) return null;
      m = mat4Mul(mat4FromPose(tf.t, tf.q), m);
      f = e.parent;
    }
  }

  /** Matrix mapping points expressed in `source` into `target`, or null when the frames are not connected. */
  lookup(target: string, source: string, time?: number): Mat4 | null {
    if (target === source) return MAT4_IDENTITY;
    const s = this.toRoot(source, time), t = this.toRoot(target, time);
    if (!s || !t || s.root !== t.root) return null;
    const inv = mat4Invert(t.m);
    return inv ? mat4Mul(inv, s.m) : null;
  }
  /** True when `lookup` would return a matrix. */
  canTransform(target: string, source: string, time?: number): boolean { return this.lookup(target, source, time) !== null; }
}

/** No translation, no rotation. */
export const IDENTITY_TRANSFORM: Transform3 = { t: { x: 0, y: 0, z: 0 }, q: QUAT_IDENTITY };
