// Mori.SkyScope — Basic 2D scene layers: polylines, point sets and the metric grid.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Fill, Stroke } from "../paint/painter.js";
import { tickSpec, ticksFromSpec } from "../scales/ticks.js";
import { distToSegment, rectFromPoints, type Rect } from "./geometry.js";
import { BaseLayer, type HitContext, type HitResult, type LayerContext } from "./scene.js";

const withOpacity = (s: Stroke, o: number): Stroke => (o === 1 ? s : { ...s, opacity: (s.opacity ?? 1) * o });
const fillWithOpacity = (f: Fill, o: number): Fill => (o === 1 ? f : { ...f, opacity: (f.opacity ?? 1) * o });

/** World-space polyline (robot path, contour, static series). */
export class PolylineLayer extends BaseLayer {
  /** Always "polyline". */
  readonly kind = "polyline";
  private points: Float64Array;
  private screen = new Float64Array(0);
  /**
   * `points` is interleaved world xy (copied); `stroke` and `closed` are public fields and may be changed, followed
   * by `markDirty`.
   */
  constructor(id: string, points: ArrayLike<number>, public stroke: Stroke = { color: "#2563eb", width: 2 }, public closed = false) {
    super(id);
    this.points = Float64Array.from(points);
  }
  /** Number of vertices. */
  get pointCount(): number { return this.points.length / 2; }
  /** The backing array (interleaved world xy); do not mutate. */
  rawPoints(): Float64Array { return this.points; }
  /** Replace all vertices (copied) and mark the layer dirty. */
  setPoints(points: ArrayLike<number>): void { this.points = Float64Array.from(points); this.markDirty(); }
  /** Add one vertex at the end (reallocates the backing array). */
  append(x: number, y: number): void {
    const next = new Float64Array(this.points.length + 2); next.set(this.points); next[this.points.length] = x; next[this.points.length + 1] = y;
    this.points = next; this.markDirty();
  }
  /** Bounding box of the vertices, or null when empty. */
  override bounds(): Rect | null { return rectFromPoints(this.points); }

  private projectAll(ctx: HitContext): Float64Array {
    const n = this.pointCount;
    if (this.screen.length !== n * 2) this.screen = new Float64Array(n * 2);
    for (let i = 0; i < n; i++) { const p = ctx.projection.project(this.points[2 * i]!, this.points[2 * i + 1]!); this.screen[2 * i] = p.x; this.screen[2 * i + 1] = p.y; }
    return this.screen;
  }
  /** Projects every vertex and strokes the polyline (or the polygon outline when `closed`). */
  draw(ctx: LayerContext): void {
    const s = this.projectAll(ctx);
    if (this.closed) ctx.painter.polygon(s, undefined, withOpacity(this.stroke, ctx.opacity));
    else ctx.painter.polyline(s, withOpacity(this.stroke, ctx.opacity));
  }
  /**
   * Nearest segment within `tolerance` pixels; `index` is the segment's first vertex and `world` the probe position.
   */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    const s = this.projectAll(ctx);
    const n = this.pointCount;
    let best = Infinity, bestIndex = -1;
    const segs = this.closed ? n : n - 1;
    for (let i = 0; i < segs; i++) {
      const j = (i + 1) % n;
      const d = distToSegment(sx, sy, s[2 * i]!, s[2 * i + 1]!, s[2 * j]!, s[2 * j + 1]!);
      if (d < best) { best = d; bestIndex = i; }
    }
    if (bestIndex < 0 || best > tolerance) return null;
    const w = ctx.projection.unproject(sx, sy);
    return { layerId: this.id, world: { x: w.x, y: w.y, z: 0 }, screen: { x: sx, y: sy }, distance: best, index: bestIndex };
  }
}

/** World-space markers (waypoints, scatter). */
export class PointsLayer extends BaseLayer {
  /** Always "points". */
  readonly kind = "points";
  private points: Float64Array;
  /**
   * `points` is interleaved world xy (copied); `radius` is in screen pixels; `fill` may be undefined for stroke-only
   * markers.
   */
  constructor(id: string, points: ArrayLike<number>, public radius = 4, public fill: Fill | undefined = { color: "#dc2626" }, public stroke?: Stroke) {
    super(id);
    this.points = Float64Array.from(points);
  }
  /** Number of markers. */
  get pointCount(): number { return this.points.length / 2; }
  /** Replace all markers (copied) and mark the layer dirty. */
  setPoints(points: ArrayLike<number>): void { this.points = Float64Array.from(points); this.markDirty(); }
  /** Bounding box of the marker centres, or null when empty. */
  override bounds(): Rect | null { return rectFromPoints(this.points); }
  /** Draws one circle of `radius` pixels per point. */
  draw(ctx: LayerContext): void {
    const fill = this.fill ? fillWithOpacity(this.fill, ctx.opacity) : undefined;
    const stroke = this.stroke ? withOpacity(this.stroke, ctx.opacity) : undefined;
    for (let i = 0; i < this.pointCount; i++) {
      const p = ctx.projection.project(this.points[2 * i]!, this.points[2 * i + 1]!);
      ctx.painter.circle(p.x, p.y, this.radius, fill, stroke);
    }
  }
  /**
   * Nearest marker whose centre is within `radius + tolerance` pixels; `world` is the marker position and `index` its
   * list position.
   */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    let best = Infinity, bestIndex = -1;
    for (let i = 0; i < this.pointCount; i++) {
      const p = ctx.projection.project(this.points[2 * i]!, this.points[2 * i + 1]!);
      const d = Math.hypot(p.x - sx, p.y - sy);
      if (d < best) { best = d; bestIndex = i; }
    }
    if (bestIndex < 0 || best > this.radius + tolerance) return null;
    return { layerId: this.id, world: { x: this.points[2 * bestIndex]!, y: this.points[2 * bestIndex + 1]!, z: 0 }, screen: { x: sx, y: sy }, distance: best, index: bestIndex };
  }
}

/** Construction options for `GridLayer`. */
export interface GridLayerOptions {
  /** World units between lines; "auto" picks a 1-2-5 step for ~`targetPixels` px. */
  spacing?: number | "auto";
  /** Desired screen spacing between lines when `spacing` is "auto"; default 80. */
  targetPixels?: number;
  /** Stroke for minor lines. */
  stroke?: Stroke;
  /** Every n-th line is drawn with `majorStroke`. */
  majorEvery?: number;
  /** Stroke for major lines. */
  majorStroke?: Stroke;
}

/** World-aligned grid that adapts its spacing to the zoom. Works with any projection. */
export class GridLayer extends BaseLayer {
  /** Always "grid". */
  readonly kind = "grid";
  /** Current settings (see `GridLayerOptions`); change them and call `markDirty`. */
  spacing: number | "auto"; targetPixels: number; stroke: Stroke; majorEvery: number; majorStroke: Stroke;
  /** Defaults: auto spacing, 80 px target, light slate strokes, every 5th line major. */
  constructor(id: string, o: GridLayerOptions = {}) {
    super(id);
    this.spacing = o.spacing ?? "auto"; this.targetPixels = o.targetPixels ?? 80;
    this.stroke = o.stroke ?? { color: "#e2e8f0", width: 1 };
    this.majorEvery = o.majorEvery ?? 5; this.majorStroke = o.majorStroke ?? { color: "#cbd5e1", width: 1 };
  }

  /** Visible world AABB from the viewport corners — works for cameras and scale projections alike. */
  static visibleBounds(ctx: HitContext): Rect {
    const pts: number[] = [];
    for (const [x, y] of [[0, 0], [ctx.width, 0], [ctx.width, ctx.height], [0, ctx.height]] as const) { const w = ctx.projection.unproject(x, y); pts.push(w.x, w.y); }
    return rectFromPoints(pts)!;
  }

  /** The grid step for the current view (exposed for tests and labels). */
  step(ctx: HitContext): { x: number; y: number } {
    if (this.spacing !== "auto") return { x: this.spacing, y: this.spacing };
    const b = GridLayer.visibleBounds(ctx);
    const nx = Math.max(1, Math.round(ctx.width / this.targetPixels)), ny = Math.max(1, Math.round(ctx.height / this.targetPixels));
    return { x: tickSpec(b.x, b.x + b.w, nx).step, y: tickSpec(b.y, b.y + b.h, ny).step };
  }

  /**
   * Strokes vertical and horizontal lines across the visible world bounds at the current step; nothing when the step
   * is not positive.
   */
  draw(ctx: LayerContext): void {
    const b = GridLayer.visibleBounds(ctx);
    const st = this.step(ctx);
    if (!(st.x > 0) || !(st.y > 0)) return;
    const minor = withOpacity(this.stroke, ctx.opacity), major = withOpacity(this.majorStroke, ctx.opacity);
    const xs = ticksFromSpec(b.x, b.x + b.w, { step: st.x, inv: st.x < 1 ? 1 / st.x : 0, decimals: 0 });
    const ys = ticksFromSpec(b.y, b.y + b.h, { step: st.y, inv: st.y < 1 ? 1 / st.y : 0, decimals: 0 });
    for (const x of xs) {
      const a = ctx.projection.project(x, b.y), c = ctx.projection.project(x, b.y + b.h);
      ctx.painter.line(a.x, a.y, c.x, c.y, Math.round(x / st.x) % this.majorEvery === 0 ? major : minor);
    }
    for (const y of ys) {
      const a = ctx.projection.project(b.x, y), c = ctx.projection.project(b.x + b.w, y);
      ctx.painter.line(a.x, a.y, c.x, c.y, Math.round(y / st.y) % this.majorEvery === 0 ? major : minor);
    }
  }
}
