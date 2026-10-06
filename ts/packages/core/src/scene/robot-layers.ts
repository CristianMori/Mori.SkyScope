// Mori.SkyScope — Robotics layers for the SceneView: georeferenced bitmaps, occupancy grids, point clouds (with a LaserScan converter), poses with footprin…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Fill, ImageHandle, Painter, RasterImage, Stroke, TextStyle } from "../paint/painter.js";
import { colormapLut, colormapStops, parseHex, type ColormapName } from "../charts/colormaps.js";
import { apply, invert, mul, pointInPolygon, rectFromPoints, rotation, scaling, translation, transformRect, type Mat3, type Rect, type Vec2 } from "./geometry.js";
import { BaseLayer, type HitContext, type HitResult, type LayerContext } from "./scene.js";
import { PolylineLayer } from "./layers.js";

/**
 * Robotics layers for the SceneView: georeferenced bitmaps, occupancy grids, point clouds (with a LaserScan
 * converter), poses with footprints, shapes and trails. World space is y-up with yaw counter-clockwise (ROS).
 * Mirrors `Mori.SkyScope.Core.Scene.RobotLayers`; pinned by `spec/fixtures/robot-layers.json`.
 */
/** Planar pose: world position and heading in radians, counter-clockwise from +x. */
export interface Pose2D { x: number; y: number; yaw: number }

/** Body frame → world: T(x, y) · R(yaw). */
export const poseMatrix = (p: Pose2D): Mat3 => mul(translation(p.x, p.y), rotation(p.yaw));

/** Where a raster sits in the world (ROS map convention: `origin` is the world position of the bottom-left pixel corner). */
export interface RasterPlacement { originX: number; originY: number; resolution: number; rotation: number }

/** Pixel space (x right, y down, origin top-left) → world. */
export function rasterToWorld(p: RasterPlacement, heightPx: number): Mat3 {
  return mul(translation(p.originX, p.originY), mul(rotation(p.rotation), mul(scaling(p.resolution, -p.resolution), translation(0, -heightPx))));
}

/** The projection as an affine matrix, sampled from three points (exact for cameras and axis scales). */
export function projectionMatrix(ctx: HitContext): Mat3 {
  const o = ctx.projection.project(0, 0), px = ctx.projection.project(1, 0), py = ctx.projection.project(0, 1);
  return { a: px.x - o.x, b: px.y - o.y, c: py.x - o.x, d: py.y - o.y, e: o.x, f: o.y };
}

/** Split a conformal affine into translate → rotate → scale for painters without a general transform. */
export function decomposeConformal(m: Mat3): { tx: number; ty: number; rotation: number; sx: number; sy: number } {
  const sx = Math.hypot(m.a, m.b);
  return { tx: m.e, ty: m.f, rotation: Math.atan2(m.b, m.a), sx, sy: sx === 0 ? 0 : (m.a * m.d - m.b * m.c) / sx };
}

/** Draw an image through `pixelToWorld` under the layer context's projection. */
export function drawPlacedImage(ctx: LayerContext, image: ImageHandle, pixelToWorld: Mat3, opacity: number): void {
  const d = decomposeConformal(mul(projectionMatrix(ctx), pixelToWorld));
  const p = ctx.painter;
  p.save();
  p.translate(d.tx, d.ty); p.rotate(d.rotation); p.scale(d.sx, d.sy);
  p.image(image, 0, 0, image.width, image.height, opacity);
  p.restore();
}

/** A georeferenced bitmap (floor plan, satellite tile, camera frame). */
export class BitmapLayer extends BaseLayer {
  /** Always "bitmap". */
  readonly kind = "bitmap";
  /**
   * `image` and `placement` are public fields; replace them through `setImage` and `setPlacement` so the layer
   * redraws.
   */
  constructor(id: string, public image: ImageHandle, public placement: RasterPlacement) { super(id); }
  /** Swap the bitmap and mark the layer dirty. */
  setImage(image: ImageHandle): void { this.image = image; this.markDirty(); }
  /** Move the bitmap and mark the layer dirty. */
  setPlacement(p: RasterPlacement): void { this.placement = p; this.markDirty(); }
  /** Matrix from image pixel space to world for the current placement. */
  pixelToWorld(): Mat3 { return rasterToWorld(this.placement, this.image.height); }
  /** World bounding box of the image rectangle. */
  override bounds(): Rect | null { return transformRect(this.pixelToWorld(), { x: 0, y: 0, w: this.image.width, h: this.image.height }); }
  /** Draws the image through its placement under the context projection. */
  draw(ctx: LayerContext): void { drawPlacedImage(ctx, this.image, this.pixelToWorld(), ctx.opacity); }
  /** Pixel under the probe, as `index = row × width + col`. */
  override hitTest(sx: number, sy: number, ctx: HitContext, _tolerance: number): HitResult | null {
    const inv = invert(this.pixelToWorld());
    if (!inv) return null;
    const w = ctx.projection.unproject(sx, sy), px = apply(inv, w.x, w.y);
    const col = Math.floor(px.x), row = Math.floor(px.y);
    if (col < 0 || row < 0 || col >= this.image.width || row >= this.image.height) return null;
    return { layerId: this.id, world: { x: w.x, y: w.y, z: 0 }, screen: { x: sx, y: sy }, distance: 0, index: row * this.image.width + col };
  }
}

/** Construction options for `OccupancyGridLayer`. */
export interface OccupancyGridOptions {
  /** Grid size in cells (columns × rows). */
  width: number; height: number;
  /** World units per cell. */
  resolution: number;
  /** Pose of the bottom-left cell corner in the world; default the world origin with zero yaw. */
  origin?: Pose2D | undefined;
  /** Row-major, row 0 at the origin (ROS `nav_msgs/OccupancyGrid`): −1 unknown, 0 free … 100 occupied. */
  data?: ArrayLike<number> | undefined;
  /** CSS colours for free (0), occupied (100) and unknown (−1) cells; defaults white, dark slate and light slate. */
  freeColor?: string | undefined; occupiedColor?: string | undefined; unknownColor?: string | undefined;
  /** Alpha of unknown cells (0 = transparent). */
  unknownOpacity?: number | undefined;
}

/** ROS-style occupancy grid rendered as a raster; cells are edited in place and re-uploaded on the next draw. */
export class OccupancyGridLayer extends BaseLayer {
  /** Always "occupancyGrid". */
  readonly kind = "occupancyGrid";
  /** Grid size in cells and the world size of one cell. */
  readonly width: number; readonly height: number; readonly resolution: number;
  /** Pose of the bottom-left cell corner; set it through `setOrigin`. */
  origin: Pose2D;
  /**
   * Occupancy values, row-major with row 0 at the origin: −1 unknown, 0..100 occupied. Edit through `set` or
   * `setData` so the raster refreshes.
   */
  readonly data: Int8Array;
  /**
   * Cell colours and the alpha of unknown cells. The raster cache is keyed on data edits only, so set these before
   * the next `set` or `setData`.
   */
  freeColor: string; occupiedColor: string; unknownColor: string; unknownOpacity: number;
  private version = 0;
  private raster: RasterImage | null = null;
  /** Allocates `width × height` cells initialised to unknown, then applies `o.data` when present. */
  constructor(id: string, o: OccupancyGridOptions) {
    super(id);
    this.width = o.width; this.height = o.height; this.resolution = o.resolution; this.origin = o.origin ?? { x: 0, y: 0, yaw: 0 };
    this.data = new Int8Array(o.width * o.height).fill(-1);
    if (o.data) this.setData(o.data);
    this.freeColor = o.freeColor ?? "#ffffff"; this.occupiedColor = o.occupiedColor ?? "#0f172a"; this.unknownColor = o.unknownColor ?? "#cbd5e1"; this.unknownOpacity = o.unknownOpacity ?? 1;
  }
  /**
   * Copy up to `width × height` values into `data` (a shorter input leaves the tail untouched) and refresh the
   * raster.
   */
  setData(data: ArrayLike<number>): void { const n = Math.min(data.length, this.data.length); for (let i = 0; i < n; i++) this.data[i] = data[i]!; this.version++; this.markDirty(); }
  /** Write one cell by (row, col) with no bounds check. */
  set(row: number, col: number, v: number): void { this.data[row * this.width + col] = v; this.version++; this.markDirty(); }
  /** Read one cell; −1 (unknown) outside the grid. */
  get(row: number, col: number): number { return row < 0 || col < 0 || row >= this.height || col >= this.width ? -1 : this.data[row * this.width + col]!; }
  /** Move the grid in the world. */
  setOrigin(p: Pose2D): void { this.origin = p; this.markDirty(); }
  /** The grid's raster placement derived from `origin` and `resolution`. */
  placement(): RasterPlacement { return { originX: this.origin.x, originY: this.origin.y, resolution: this.resolution, rotation: this.origin.yaw }; }
  /** Matrix from raster pixel space (row 0 at the top) to world. */
  pixelToWorld(): Mat3 { return rasterToWorld(this.placement(), this.height); }
  /** World → (row, col) or null outside the grid. */
  cellAt(wx: number, wy: number): { row: number; col: number } | null {
    const inv = invert(this.pixelToWorld());
    if (!inv) return null;
    const px = apply(inv, wx, wy), col = Math.floor(px.x), row = this.height - 1 - Math.floor(px.y);
    return col < 0 || row < 0 || col >= this.width || row >= this.height ? null : { row, col };
  }
  /** RGBA raster (row 0 = top = last data row). Free → occupied interpolates the two colours. */
  image(): RasterImage {
    if (this.raster && this.raster.version === this.version) return this.raster;
    const free = parseHex(this.freeColor), occ = parseHex(this.occupiedColor), unk = parseHex(this.unknownColor);
    const rgba = new Uint8ClampedArray(this.width * this.height * 4), ua = Math.floor(this.unknownOpacity * 255 + 0.5);
    for (let row = 0; row < this.height; row++) {
      const py = this.height - 1 - row;
      for (let col = 0; col < this.width; col++) {
        const v = this.data[row * this.width + col]!, o = (py * this.width + col) * 4;
        if (v < 0) { rgba[o] = unk[0]; rgba[o + 1] = unk[1]; rgba[o + 2] = unk[2]; rgba[o + 3] = ua; continue; }
        const t = Math.min(100, v) / 100;
        rgba[o] = Math.floor(free[0] + (occ[0] - free[0]) * t + 0.5); rgba[o + 1] = Math.floor(free[1] + (occ[1] - free[1]) * t + 0.5); rgba[o + 2] = Math.floor(free[2] + (occ[2] - free[2]) * t + 0.5); rgba[o + 3] = 255;
      }
    }
    this.raster = { width: this.width, height: this.height, rgba, version: this.version };
    return this.raster;
  }
  /** World bounding box of the grid rectangle. */
  override bounds(): Rect | null { return transformRect(this.pixelToWorld(), { x: 0, y: 0, w: this.width, h: this.height }); }
  /** Draws the cached raster through the placement. */
  draw(ctx: LayerContext): void { drawPlacedImage(ctx, this.image(), this.pixelToWorld(), ctx.opacity); }
  /** The cell under the probe: `index = row × width + col`, `data` = occupancy value. */
  override hitTest(sx: number, sy: number, ctx: HitContext, _tolerance: number): HitResult | null {
    const w = ctx.projection.unproject(sx, sy), c = this.cellAt(w.x, w.y);
    if (!c) return null;
    return { layerId: this.id, world: { x: w.x, y: w.y, z: 0 }, screen: { x: sx, y: sy }, distance: 0, index: c.row * this.width + c.col, data: this.get(c.row, c.col) };
  }
}

/**
 * The parts of `sensor_msgs/LaserScan` needed to convert it: angles in radians, ranges in world units; readings
 * outside [rangeMin, rangeMax] are dropped.
 */
export interface LaserScanLike { angleMin: number; angleIncrement: number; ranges: ArrayLike<number>; rangeMin?: number | undefined; rangeMax?: number | undefined }

/** `sensor_msgs/LaserScan` → interleaved world xy through `pose` (the sensor frame); out-of-range and non-finite readings are dropped. */
export function laserScanToPoints(scan: LaserScanLike, pose: Pose2D = { x: 0, y: 0, yaw: 0 }): Float64Array {
  const m = poseMatrix(pose), lo = scan.rangeMin ?? 0, hi = scan.rangeMax ?? Infinity;
  const out: number[] = [];
  for (let i = 0; i < scan.ranges.length; i++) {
    const r = scan.ranges[i]!;
    if (!Number.isFinite(r) || r < lo || r > hi) continue;
    const a = scan.angleMin + i * scan.angleIncrement, p = apply(m, r * Math.cos(a), r * Math.sin(a));
    out.push(p.x, p.y);
  }
  return Float64Array.from(out);
}

/** Construction options for `PointCloudLayer`. */
export interface PointCloudOptions {
  /** Interleaved world xy. */
  points?: ArrayLike<number> | undefined;
  /** One value per point, coloured through `colormap` over `intensityRange` (data range by default). */
  intensities?: ArrayLike<number> | undefined;
  /** Colormap name or list of hex stops; default "turbo". */
  colormap?: ColormapName | string[] | undefined;
  /** Fixed intensity range for the colormap; defaults to the data's min and max. */
  intensityRange?: [number, number] | undefined;
  /** Flat colour used when there are no intensities; default red. */
  color?: string | undefined;
  /** Screen pixels. */
  pointSize?: number | undefined;
}

/** Lidar / point-cloud layer: one square per point, optional intensity colouring. */
export class PointCloudLayer extends BaseLayer {
  /** Always "pointCloud". */
  readonly kind = "pointCloud";
  private points: Float64Array;
  private intensities: Float64Array | null;
  /** Colouring and point size (see `PointCloudOptions`); change them and call `markDirty`. */
  colormap: ColormapName | string[]; intensityRange: [number, number] | null; color: string; pointSize: number;
  /**
   * Copies points and intensities from the options; defaults to the turbo colormap, data-range scaling, red, 2 px.
   */
  constructor(id: string, o: PointCloudOptions = {}) {
    super(id);
    this.points = Float64Array.from(o.points ?? []);
    this.intensities = o.intensities ? Float64Array.from(o.intensities) : null;
    this.colormap = o.colormap ?? "turbo"; this.intensityRange = o.intensityRange ?? null; this.color = o.color ?? "#dc2626"; this.pointSize = o.pointSize ?? 2;
  }
  /** Number of points. */
  get pointCount(): number { return this.points.length / 2; }
  /** Replace the points and the optional per-point intensities (both copied) and mark the layer dirty. */
  setPoints(points: ArrayLike<number>, intensities?: ArrayLike<number>): void {
    this.points = Float64Array.from(points); this.intensities = intensities ? Float64Array.from(intensities) : null; this.markDirty();
  }
  /** Replace the points with a laser scan converted through `pose` (the sensor frame). */
  setScan(scan: LaserScanLike, pose?: Pose2D): void { this.setPoints(laserScanToPoints(scan, pose)); }
  /** Bounding box of the points, or null when empty. */
  override bounds(): Rect | null { return rectFromPoints(this.points); }
  /** Colour of point `i` (hex). */
  colorOf(i: number): string {
    if (!this.intensities || i >= this.intensities.length) return this.color;
    const [lo, hi] = this.intensityRange ?? this.dataRange();
    const lut = colormapLut(colormapStops(this.colormap), 256);
    let k = Math.floor((this.intensities[i]! - lo) / Math.max(1e-12, hi - lo) * 255 + 0.5);
    if (k < 0) k = 0; else if (k > 255) k = 255;
    return "#" + [lut[k * 3]!, lut[k * 3 + 1]!, lut[k * 3 + 2]!].map((v) => (v < 16 ? "0" : "") + v.toString(16)).join("");
  }
  private dataRange(): [number, number] {
    let lo = Infinity, hi = -Infinity;
    for (const v of this.intensities!) { if (v < lo) lo = v; if (v > hi) hi = v; }
    return Number.isFinite(lo) && hi > lo ? [lo, hi] : [0, 1];
  }
  /** Draws one `pointSize` square per point, coloured flat or through the colormap. */
  draw(ctx: LayerContext): void {
    const s = this.pointSize, h = s / 2, p = ctx.painter;
    const plain: Fill = { color: this.color, opacity: ctx.opacity };
    let lut: Uint8Array | null = null, lo = 0, span = 1;
    if (this.intensities) { lut = colormapLut(colormapStops(this.colormap), 256); [lo, span] = this.intensityRange ?? this.dataRange(); span = Math.max(1e-12, span - lo); }
    for (let i = 0; i < this.pointCount; i++) {
      const q = ctx.projection.project(this.points[2 * i]!, this.points[2 * i + 1]!);
      let fill = plain;
      if (lut && i < this.intensities!.length) {
        let k = Math.floor((this.intensities![i]! - lo) / span * 255 + 0.5);
        if (k < 0) k = 0; else if (k > 255) k = 255;
        fill = { color: "#" + [lut[k * 3]!, lut[k * 3 + 1]!, lut[k * 3 + 2]!].map((v) => (v < 16 ? "0" : "") + v.toString(16)).join(""), opacity: ctx.opacity };
      }
      p.rect(q.x - h, q.y - h, s, s, fill);
    }
  }
  /** Nearest point within `tolerance + pointSize / 2` pixels; `data` carries its intensity when present. */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    let best = Infinity, bestIndex = -1;
    for (let i = 0; i < this.pointCount; i++) {
      const q = ctx.projection.project(this.points[2 * i]!, this.points[2 * i + 1]!);
      const d = Math.hypot(q.x - sx, q.y - sy);
      if (d < best) { best = d; bestIndex = i; }
    }
    if (bestIndex < 0 || best > tolerance + this.pointSize / 2) return null;
    return { layerId: this.id, world: { x: this.points[2 * bestIndex]!, y: this.points[2 * bestIndex + 1]!, z: 0 }, screen: { x: sx, y: sy }, distance: best, index: bestIndex, data: this.intensities?.[bestIndex] };
  }
}

/** Construction options for `PoseLayer`. */
export interface PoseLayerOptions {
  /** Body-frame outline (interleaved, world units), e.g. a robot footprint. */
  footprint?: ArrayLike<number> | undefined;
  /** Colour of arrow, footprint and label, and the footprint fill opacity (default 0.25). */
  color?: string | undefined; fillOpacity?: number | undefined;
  /** Arrow size in screen pixels (used with or without a footprint). */
  arrowSize?: number | undefined;
  /** Text drawn above the arrow. */
  label?: string | undefined;
  /** Label font; size in pixels (default 11). */
  fontFamily?: string | undefined; fontSize?: number | undefined;
}

/** A robot (or any oriented thing): heading arrow, optional footprint and label. */
export class PoseLayer extends BaseLayer {
  /** Always "pose". */
  readonly kind = "pose";
  /** Current pose; set it through `setPose`. */
  pose: Pose2D;
  /** Appearance (see `PoseLayerOptions`); `footprint` is the body-frame outline, interleaved. */
  footprint: Float64Array | null; color: string; fillOpacity: number; arrowSize: number; label: string | null; fontFamily: string | undefined; fontSize: number;
  /** Not cacheable: the arrow has a fixed screen size, so the layer is redrawn every frame. */
  constructor(id: string, pose: Pose2D, o: PoseLayerOptions = {}) {
    super(id);
    this.pose = pose; this.footprint = o.footprint ? Float64Array.from(o.footprint) : null;
    this.color = o.color ?? "#2563eb"; this.fillOpacity = o.fillOpacity ?? 0.25; this.arrowSize = o.arrowSize ?? 14; this.label = o.label ?? null; this.fontFamily = o.fontFamily; this.fontSize = o.fontSize ?? 11;
    this.cacheable = false;
  }
  /** Move the pose and mark the layer dirty. */
  setPose(p: Pose2D): void { this.pose = p; this.markDirty(); }
  /** Footprint in world coordinates (interleaved). */
  worldFootprint(): Float64Array | null {
    if (!this.footprint) return null;
    const m = poseMatrix(this.pose), out = new Float64Array(this.footprint.length);
    for (let i = 0; i < this.footprint.length; i += 2) { const p = apply(m, this.footprint[i]!, this.footprint[i + 1]!); out[i] = p.x; out[i + 1] = p.y; }
    return out;
  }
  /** Footprint bounds in world units, or a zero-size rectangle at the pose position. */
  override bounds(): Rect | null { const f = this.worldFootprint(); return f ? rectFromPoints(f) : { x: this.pose.x, y: this.pose.y, w: 0, h: 0 }; }
  /** Screen-space arrow polygon: tip, right wing, notch, left wing. */
  arrow(ctx: HitContext): number[] {
    const c = ctx.projection.project(this.pose.x, this.pose.y), f = ctx.projection.project(this.pose.x + Math.cos(this.pose.yaw), this.pose.y + Math.sin(this.pose.yaw));
    const len = Math.hypot(f.x - c.x, f.y - c.y) || 1, ux = (f.x - c.x) / len, uy = (f.y - c.y) / len, s = this.arrowSize;
    const tip = { x: c.x + ux * s, y: c.y + uy * s }, back = { x: c.x - ux * s * 0.5, y: c.y - uy * s * 0.5 };
    return [tip.x, tip.y, back.x - uy * s * 0.45, back.y + ux * s * 0.45, c.x - ux * s * 0.2, c.y - uy * s * 0.2, back.x + uy * s * 0.45, back.y - ux * s * 0.45];
  }
  /** Draws the footprint polygon, the heading arrow and the label. */
  draw(ctx: LayerContext): void {
    const p = ctx.painter, stroke: Stroke = { color: this.color, width: 1.5, opacity: ctx.opacity, join: "round" };
    const wf = this.worldFootprint();
    if (wf) {
      const s: number[] = [];
      for (let i = 0; i < wf.length; i += 2) { const q = ctx.projection.project(wf[i]!, wf[i + 1]!); s.push(q.x, q.y); }
      p.polygon(s, { color: this.color, opacity: this.fillOpacity * ctx.opacity }, stroke);
    }
    p.polygon(this.arrow(ctx), { color: this.color, opacity: ctx.opacity }, { color: "#ffffff", width: 1, opacity: ctx.opacity });
    if (this.label) {
      const c = ctx.projection.project(this.pose.x, this.pose.y);
      const t: TextStyle = { color: this.color, family: this.fontFamily, size: this.fontSize, align: "center", baseline: "bottom", opacity: ctx.opacity };
      p.text(this.label, c.x, c.y - this.arrowSize - 3, t);
    }
  }
  /** Hit when the probe is within `tolerance + arrowSize` pixels of the pose position; `data` is the pose. */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    const c = ctx.projection.project(this.pose.x, this.pose.y), d = Math.hypot(c.x - sx, c.y - sy);
    if (d > tolerance + this.arrowSize) return null;
    return { layerId: this.id, world: { x: this.pose.x, y: this.pose.y, z: 0 }, screen: { x: sx, y: sy }, distance: d, data: this.pose };
  }
}

/**
 * A world-space annotation keyed by `id`: a circle (radius in world units), a rect, a polygon or line (interleaved
 * points) or a text label. Fill and stroke default to a blue outline.
 */
export type Shape =
  | { id: string; kind: "circle"; x: number; y: number; r: number; fill?: Fill | undefined; stroke?: Stroke | undefined; label?: string | undefined }
  | { id: string; kind: "rect"; x: number; y: number; w: number; h: number; fill?: Fill | undefined; stroke?: Stroke | undefined; label?: string | undefined }
  | { id: string; kind: "polygon"; points: number[]; fill?: Fill | undefined; stroke?: Stroke | undefined; label?: string | undefined }
  | { id: string; kind: "line"; points: number[]; stroke?: Stroke | undefined; label?: string | undefined }
  | { id: string; kind: "text"; x: number; y: number; text: string; style?: TextStyle | undefined };

/** Annotations in world units: zones, goals, no-go areas, labels. */
export class ShapeLayer extends BaseLayer {
  /** Always "shapes". */
  readonly kind = "shapes";
  /**
   * `shapes` is kept by reference and mutated by `upsert` and `removeShape`; the font settings apply to labels and
   * text shapes.
   */
  constructor(id: string, public shapes: Shape[] = [], public fontFamily?: string, public fontSize = 11) { super(id); }
  /** Replace the shape list and mark the layer dirty. */
  setShapes(shapes: Shape[]): void { this.shapes = shapes; this.markDirty(); }
  /** Replace the shape with the same id, or append it. */
  upsert(shape: Shape): void { const i = this.shapes.findIndex((s) => s.id === shape.id); if (i < 0) this.shapes.push(shape); else this.shapes[i] = shape; this.markDirty(); }
  /** Remove by id; false when absent. */
  removeShape(id: string): boolean { const i = this.shapes.findIndex((s) => s.id === id); if (i < 0) return false; this.shapes.splice(i, 1); this.markDirty(); return true; }
  /** World outline of a shape (circles are approximated by their bounding square for bounds). */
  static outline(s: Shape): number[] {
    switch (s.kind) {
      case "circle": return [s.x - s.r, s.y - s.r, s.x + s.r, s.y - s.r, s.x + s.r, s.y + s.r, s.x - s.r, s.y + s.r];
      case "rect": return [s.x, s.y, s.x + s.w, s.y, s.x + s.w, s.y + s.h, s.x, s.y + s.h];
      case "polygon": case "line": return s.points;
      case "text": return [s.x, s.y];
    }
  }
  /** Bounding box of every shape's outline, or null when there are none. */
  override bounds(): Rect | null { const pts: number[] = []; for (const s of this.shapes) pts.push(...ShapeLayer.outline(s)); return rectFromPoints(pts); }
  private static center(s: Shape): Vec2 {
    if (s.kind === "circle" || s.kind === "text") return { x: s.x, y: s.y };
    if (s.kind === "rect") return { x: s.x + s.w / 2, y: s.y + s.h / 2 };
    const r = rectFromPoints(s.points) ?? { x: 0, y: 0, w: 0, h: 0 }; return { x: r.x + r.w / 2, y: r.y + r.h / 2 };
  }
  /** Draws each shape in list order, then its label at the shape centre. */
  draw(ctx: LayerContext): void {
    const p = ctx.painter, o = ctx.opacity;
    const fo = (f?: Fill): Fill | undefined => (f ? { ...f, opacity: (f.opacity ?? 1) * o } : undefined);
    const so = (s?: Stroke): Stroke | undefined => (s ? { ...s, opacity: (s.opacity ?? 1) * o } : undefined);
    for (const s of this.shapes) {
      if (s.kind === "circle") {
        const c = ctx.projection.project(s.x, s.y), e = ctx.projection.project(s.x + s.r, s.y);
        p.circle(c.x, c.y, Math.hypot(e.x - c.x, e.y - c.y), fo(s.fill), so(s.stroke ?? (s.fill ? undefined : { color: "#2563eb", width: 1.5 })));
      } else if (s.kind === "text") {
        const c = ctx.projection.project(s.x, s.y);
        p.text(s.text, c.x, c.y, { color: "#0f172a", family: this.fontFamily, size: this.fontSize, align: "center", baseline: "middle", ...s.style, opacity: (s.style?.opacity ?? 1) * o });
        continue;
      } else {
        const pts = ShapeLayer.outline(s), scr: number[] = [];
        for (let i = 0; i < pts.length; i += 2) { const q = ctx.projection.project(pts[i]!, pts[i + 1]!); scr.push(q.x, q.y); }
        if (s.kind === "line") p.polyline(scr, so(s.stroke) ?? { color: "#2563eb", width: 1.5, opacity: o });
        else p.polygon(scr, fo(s.fill), so(s.stroke ?? (s.fill ? undefined : { color: "#2563eb", width: 1.5 })));
      }
      if (s.label) { const c = ctx.projection.project(ShapeLayer.center(s).x, ShapeLayer.center(s).y); p.text(s.label, c.x, c.y, { color: "#0f172a", family: this.fontFamily, size: this.fontSize, align: "center", baseline: "middle", opacity: o }); }
    }
  }
  /**
   * Topmost (last) shape under the probe: circles and polygons by containment, lines and text by distance within
   * `tolerance`; `data` is the shape id.
   */
  override hitTest(sx: number, sy: number, ctx: HitContext, tolerance: number): HitResult | null {
    const w = ctx.projection.unproject(sx, sy);
    for (let i = this.shapes.length - 1; i >= 0; i--) {
      const s = this.shapes[i]!;
      let hit = false, dist = 0;
      if (s.kind === "circle") { const c = ctx.projection.project(s.x, s.y), e = ctx.projection.project(s.x + s.r, s.y); dist = Math.hypot(c.x - sx, c.y - sy); hit = dist <= Math.hypot(e.x - c.x, e.y - c.y) + tolerance; }
      else if (s.kind === "text") { const c = ctx.projection.project(s.x, s.y); dist = Math.hypot(c.x - sx, c.y - sy); hit = dist <= tolerance + this.fontSize; }
      else if (s.kind === "line") {
        const pts = s.points; dist = Infinity;
        for (let k = 0; k + 3 < pts.length; k += 2) { const a = ctx.projection.project(pts[k]!, pts[k + 1]!), b = ctx.projection.project(pts[k + 2]!, pts[k + 3]!); const dx = b.x - a.x, dy = b.y - a.y, l2 = dx * dx + dy * dy; const t = l2 === 0 ? 0 : Math.max(0, Math.min(1, ((sx - a.x) * dx + (sy - a.y) * dy) / l2)); dist = Math.min(dist, Math.hypot(a.x + t * dx - sx, a.y + t * dy - sy)); }
        hit = dist <= tolerance;
      } else { hit = pointInPolygon(w.x, w.y, ShapeLayer.outline(s)); }
      if (hit) return { layerId: this.id, world: { x: w.x, y: w.y, z: 0 }, screen: { x: sx, y: sy }, distance: dist, index: i, data: s.id };
    }
    return null;
  }
}

/** A polyline that keeps only the last `maxPoints` (robot trail). */
export class TrailLayer extends PolylineLayer {
  /** `maxPoints` is a public field and may be changed; trimming happens on the next `append`. */
  constructor(id: string, public maxPoints = 2000, stroke?: Stroke) { super(id, [], stroke ?? { color: "#16a34a", width: 2 }); }
  /** Add a point and drop the oldest ones beyond `maxPoints`. */
  override append(x: number, y: number): void {
    super.append(x, y);
    if (this.pointCount > this.maxPoints) { const all = this.rawPoints(); this.setPoints(all.subarray(all.length - this.maxPoints * 2)); }
  }
}
