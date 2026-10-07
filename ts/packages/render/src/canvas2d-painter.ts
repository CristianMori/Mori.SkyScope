// Mori.SkyScope — Canvas2D implementation of the painter contract with device-pixel-ratio handling, layer surfaces and raster upload.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { isRasterImage, resolveFill, resolveStroke, resolveText, type Fill, type ImageHandle, type RasterImage, type Painter, type ResolvedStroke, type Stroke, type TextMetrics, type TextStyle } from "@cmori/skyscope-core";

/** Any canvas the painter can draw on: a DOM canvas or an OffscreenCanvas (layers and rasters prefer the latter when available). */
export type CanvasLike = HTMLCanvasElement | OffscreenCanvas;
/** The 2D context of a `CanvasLike`; both flavours expose the same drawing API. */
export type Context2D = CanvasRenderingContext2D | OffscreenCanvasRenderingContext2D;

interface LayerEntry { canvas: CanvasLike; width: number; height: number }

function createCanvas(w: number, h: number): CanvasLike {
  if (typeof OffscreenCanvas !== "undefined") return new OffscreenCanvas(w, h);
  const c = document.createElement("canvas"); c.width = w; c.height = h; return c;
}

/** Core rasters (heatmaps) are uploaded to a canvas once per version; nearest-neighbour when scaled up. */
const rasterCache = new WeakMap<RasterImage, { canvas: CanvasLike; version: number }>();
function rasterCanvas(img: RasterImage): CanvasLike {
  const hit = rasterCache.get(img);
  if (hit && hit.version === img.version) return hit.canvas;
  const canvas = hit?.canvas ?? createCanvas(img.width, img.height);
  const cctx = canvas.getContext("2d") as Context2D | null;
  if (!cctx) throw new Error("2D context unavailable for raster");
  cctx.putImageData(new ImageData(new Uint8ClampedArray(img.rgba), img.width, img.height), 0, 0);
  rasterCache.set(img, { canvas, version: img.version });
  return canvas;
}

/** Painter over a 2D canvas context. Handles device-pixel-ratio and caches layers as offscreen canvases. */
export class Canvas2DPainter implements Painter {
  private readonly layers = new Map<string, LayerEntry>();

  /**
   * Wraps an existing context. `width`/`height` are CSS pixels; `pixelRatio` scales the base transform so callers
   * draw in CSS pixels while the backing store stays device-resolution. Resets the context transform.
   */
  constructor(readonly ctx: Context2D, readonly width: number, readonly height: number, readonly pixelRatio = 1) {
    ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);
  }

  /** Sizes the canvas backing store for `pixelRatio` and returns a painter for it. */
  static forCanvas(canvas: HTMLCanvasElement, width: number, height: number, pixelRatio = globalThis.devicePixelRatio || 1): Canvas2DPainter {
    const w = Math.max(1, Math.round(width * pixelRatio)), h = Math.max(1, Math.round(height * pixelRatio));
    if (canvas.width !== w) canvas.width = w;
    if (canvas.height !== h) canvas.height = h;
    canvas.style.width = `${width}px`; canvas.style.height = `${height}px`;
    const ctx = canvas.getContext("2d");
    if (!ctx) throw new Error("2D context unavailable");
    return new Canvas2DPainter(ctx, width, height, pixelRatio);
  }

  /** Pushes the context state (transform, clip, styles); pair with `restore`. */
  save(): void { this.ctx.save(); }
  /** Pops the state pushed by the matching `save`. */
  restore(): void { this.ctx.restore(); }
  /** Moves the origin by (x, y) CSS pixels for subsequent drawing. */
  translate(x: number, y: number): void { this.ctx.translate(x, y); }
  /** Scales subsequent drawing by `sx`/`sy` about the current origin. */
  scale(sx: number, sy: number): void { this.ctx.scale(sx, sy); }
  /** Rotates subsequent drawing clockwise by `radians` about the current origin. */
  rotate(radians: number): void { this.ctx.rotate(radians); }
  /** Intersects the clip region with a rectangle; lasts until the enclosing `restore`. */
  clipRect(x: number, y: number, w: number, h: number): void { this.ctx.beginPath(); this.ctx.rect(x, y, w, h); this.ctx.clip(); }

  /** Clears the whole backing store, ignoring the current transform; fills with `color` when given, otherwise to transparent. */
  clear(color?: string): void {
    const { ctx } = this;
    ctx.save();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    if (color) { ctx.fillStyle = color; ctx.fillRect(0, 0, ctx.canvas.width, ctx.canvas.height); }
    else ctx.clearRect(0, 0, ctx.canvas.width, ctx.canvas.height);
    ctx.restore();
  }

  private applyStroke(s: ResolvedStroke): void {
    const { ctx } = this;
    ctx.strokeStyle = s.color; ctx.lineWidth = s.width; ctx.lineCap = s.cap; ctx.lineJoin = s.join;
    ctx.setLineDash(s.dash ?? []); ctx.globalAlpha = s.opacity;
  }
  private strokePath(stroke: Stroke): void { this.applyStroke(resolveStroke(stroke)); this.ctx.stroke(); this.ctx.globalAlpha = 1; }
  private fillPath(fill: Fill): void { const f = resolveFill(fill); this.ctx.fillStyle = f.color; this.ctx.globalAlpha = f.opacity; this.ctx.fill(); this.ctx.globalAlpha = 1; }

  /** Strokes a single segment between two points (CSS pixels). */
  line(x1: number, y1: number, x2: number, y2: number, stroke: Stroke): void {
    this.ctx.beginPath(); this.ctx.moveTo(x1, y1); this.ctx.lineTo(x2, y2); this.strokePath(stroke);
  }

  /**
   * Strokes an open polyline from interleaved [x, y, …] coordinates. `offset` is the index of the first x in
   * `points`; `count` is the number of points (default: all remaining). Fewer than two points draws nothing.
   */
  polyline(points: ArrayLike<number>, stroke: Stroke, offset = 0, count?: number): void {
    const n = count === undefined ? (points.length - offset) / 2 : count;
    if (n < 2) return;
    const { ctx } = this;
    ctx.beginPath();
    ctx.moveTo(points[offset]!, points[offset + 1]!);
    for (let i = 1; i < n; i++) ctx.lineTo(points[offset + 2 * i]!, points[offset + 2 * i + 1]!);
    this.strokePath(stroke);
  }

  private tracePolygon(points: ArrayLike<number>): void {
    const n = points.length / 2;
    if (n < 2) return;
    const { ctx } = this;
    ctx.beginPath();
    ctx.moveTo(points[0]!, points[1]!);
    for (let i = 1; i < n; i++) ctx.lineTo(points[2 * i]!, points[2 * i + 1]!);
    ctx.closePath();
  }
  /** Fills and/or strokes a closed polygon from interleaved [x, y, …] coordinates; the closing edge is implicit. */
  polygon(points: ArrayLike<number>, fill?: Fill, stroke?: Stroke): void {
    this.tracePolygon(points);
    if (fill) this.fillPath(fill);
    if (stroke) this.strokePath(stroke);
  }

  /** Fills and/or strokes an axis-aligned rectangle; `radius` > 0 rounds the corners where the context supports it. */
  rect(x: number, y: number, w: number, h: number, fill?: Fill, stroke?: Stroke, radius = 0): void {
    const { ctx } = this;
    ctx.beginPath();
    if (radius > 0 && "roundRect" in ctx) ctx.roundRect(x, y, w, h, radius); else ctx.rect(x, y, w, h);
    if (fill) this.fillPath(fill);
    if (stroke) this.strokePath(stroke);
  }

  /** Fills and/or strokes a full circle; a non-positive or NaN radius draws nothing. */
  circle(cx: number, cy: number, r: number, fill?: Fill, stroke?: Stroke): void {
    if (!(r > 0)) return;
    this.ctx.beginPath(); this.ctx.arc(cx, cy, r, 0, Math.PI * 2);
    if (fill) this.fillPath(fill);
    if (stroke) this.strokePath(stroke);
  }

  /** Strokes an open circular arc; angles are radians, clockwise from +x (canvas convention). */
  arc(cx: number, cy: number, r: number, startAngle: number, endAngle: number, stroke: Stroke): void {
    this.ctx.beginPath(); this.ctx.arc(cx, cy, r, startAngle, endAngle); this.strokePath(stroke);
  }

  /** Fills and/or strokes an annular sector (a pie slice when `innerRadius` is 0); angles are radians, clockwise from +x. */
  sector(cx: number, cy: number, innerRadius: number, outerRadius: number, startAngle: number, endAngle: number, fill?: Fill, stroke?: Stroke): void {
    const { ctx } = this;
    ctx.beginPath();
    ctx.arc(cx, cy, outerRadius, startAngle, endAngle);
    if (innerRadius > 0) ctx.arc(cx, cy, innerRadius, endAngle, startAngle, true); else ctx.lineTo(cx, cy);
    ctx.closePath();
    if (fill) this.fillPath(fill);
    if (stroke) this.strokePath(stroke);
  }

  private applyText(style: TextStyle) {
    const t = resolveText(style);
    this.ctx.font = `${t.weight} ${t.size}px ${t.family}`;
    this.ctx.textAlign = t.align; this.ctx.textBaseline = t.baseline;
    return t;
  }

  /** Draws one line of text anchored at (x, y) per the style's align/baseline; a non-zero `rotation` turns it about the anchor. */
  text(text: string, x: number, y: number, style: TextStyle): void {
    const { ctx } = this;
    const t = this.applyText(style);
    ctx.fillStyle = t.color; ctx.globalAlpha = t.opacity;
    if (t.rotation !== 0) { ctx.save(); ctx.translate(x, y); ctx.rotate(t.rotation); ctx.fillText(text, 0, 0); ctx.restore(); }
    else ctx.fillText(text, x, y);
    ctx.globalAlpha = 1;
  }

  /** Measures `text` in CSS pixels; height is ascent + descent, falling back to 0.8/0.2 of the font size where the context lacks bounding-box metrics. Leaves the font set on the context. */
  measureText(text: string, style: TextStyle): TextMetrics {
    const t = this.applyText(style);
    const m = this.ctx.measureText(text);
    const ascent = m.actualBoundingBoxAscent ?? t.size * 0.8, descent = m.actualBoundingBoxDescent ?? t.size * 0.2;
    return { width: m.width, height: ascent + descent };
  }

  /** Draws an image scaled into the given rectangle. Core `RasterImage`s go through the per-version canvas cache and are drawn without smoothing; anything else is passed to `drawImage` as is. */
  image(image: ImageHandle, x: number, y: number, w: number, h: number, opacity = 1): void {
    this.ctx.globalAlpha = opacity;
    if (isRasterImage(image)) { const prev = this.ctx.imageSmoothingEnabled; this.ctx.imageSmoothingEnabled = false; this.ctx.drawImage(rasterCanvas(image), x, y, w, h); this.ctx.imageSmoothingEnabled = prev; }
    else this.ctx.drawImage(image as unknown as CanvasImageSource, x, y, w, h);
    this.ctx.globalAlpha = 1;
  }

  /**
   * Draws a cached offscreen layer at (x, y). `draw` runs only when the layer is new, `dirty`, or its CSS size changed;
   * otherwise the cached canvas is blitted. The cache lives as long as this painter; `dropLayer` evicts an entry.
   */
  layer(key: string, width: number, height: number, draw: (p: Painter) => void, x: number, y: number, dirty = false): void {
    let entry = this.layers.get(key);
    if (!entry || dirty || entry.width !== width || entry.height !== height) {
      const pw = Math.max(1, Math.round(width * this.pixelRatio)), ph = Math.max(1, Math.round(height * this.pixelRatio));
      const canvas = entry && entry.width === width && entry.height === height ? entry.canvas : createCanvas(pw, ph);
      const cctx = canvas.getContext("2d") as Context2D | null;
      if (!cctx) throw new Error("2D context unavailable for layer");
      const child = new Canvas2DPainter(cctx, width, height, this.pixelRatio);
      child.clear();
      draw(child);
      entry = { canvas, width, height };
      this.layers.set(key, entry);
    }
    this.ctx.drawImage(entry.canvas, x, y, width, height);
  }

  /** Drop a cached layer (e.g. when a component unmounts). */
  dropLayer(key: string): void { this.layers.delete(key); }
}

/** Pointer capture may be refused (synthetic events, some embedders); the gesture must still run. */
export function capture(el: Element, e: PointerEvent): void { try { el.setPointerCapture(e.pointerId); } catch { /* not capturable */ } }
