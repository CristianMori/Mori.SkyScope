// Mori.SkyScope — A painter that records every call as JSON ops with resolved styles, for drawing parity between the two cores.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { resolveFill, resolveStroke, resolveText, type Fill, isRasterImage, type ImageHandle, type Painter, type Stroke, type TextMetrics, type TextStyle } from "./painter.js";
import { fnv1a } from "../charts/colormaps.js";

/** One recorded primitive: `op` names it and the remaining keys are its arguments, with numbers rounded by `r3` and styles resolved. */
export interface PaintOp { op: string; [key: string]: unknown }

/** Identical rounding in both cores so recorded geometry compares exactly. */
export const r3 = (x: number): number => Math.floor(x * 1000 + 0.5) / 1000;

/**
 * A Painter that records what it is asked to draw. Component drawing logic is tested by comparing
 * recordings across the two cores (see spec/fixtures). Text metrics are synthetic: 0.6 × size per char.
 */
export class RecordingPainter implements Painter {
  /** Operations in call order. A `layer` call also records a `layerDraw` op holding the child painter's ops whenever it (re)draws. */
  readonly ops: PaintOp[] = [];
  private readonly layers = new Map<string, PaintOp[]>();

  /** `width`/`height` in CSS pixels; `charWidth` is the synthetic glyph width as a fraction of the font size. */
  constructor(readonly width: number, readonly height: number, readonly pixelRatio = 1, private readonly charWidth = 0.6) {}

  private push(op: PaintOp): void { this.ops.push(op); }
  private pts(points: ArrayLike<number>, offset = 0, count?: number): number[] {
    const n = count === undefined ? (points.length - offset) / 2 : count;
    const out: number[] = [];
    for (let i = 0; i < n; i++) out.push(r3(points[offset + 2 * i]!), r3(points[offset + 2 * i + 1]!));
    return out;
  }

  /** Records a `save` op. */
  save(): void { this.push({ op: "save" }); }
  /** Records a `restore` op. */
  restore(): void { this.push({ op: "restore" }); }
  /** Records a `translate` op. */
  translate(x: number, y: number): void { this.push({ op: "translate", x: r3(x), y: r3(y) }); }
  /** Records a `scale` op. */
  scale(sx: number, sy: number): void { this.push({ op: "scale", sx: r3(sx), sy: r3(sy) }); }
  /** Records a `rotate` op. */
  rotate(radians: number): void { this.push({ op: "rotate", radians: r3(radians) }); }
  /** Records a `clipRect` op. */
  clipRect(x: number, y: number, w: number, h: number): void { this.push({ op: "clipRect", x: r3(x), y: r3(y), w: r3(w), h: r3(h) }); }
  /** Records a `clear` op; a missing colour is recorded as null. */
  clear(color?: string): void { this.push({ op: "clear", color: color ?? null }); }
  /** Records a `line` op with the resolved stroke. */
  line(x1: number, y1: number, x2: number, y2: number, stroke: Stroke): void {
    this.push({ op: "line", x1: r3(x1), y1: r3(y1), x2: r3(x2), y2: r3(y2), stroke: resolveStroke(stroke) });
  }
  /** Records a `polyline` op with the selected points copied out and rounded. */
  polyline(points: ArrayLike<number>, stroke: Stroke, offset = 0, count?: number): void {
    this.push({ op: "polyline", points: this.pts(points, offset, count), stroke: resolveStroke(stroke) });
  }
  /** Records a `polygon` op; a missing fill or stroke is recorded as null. */
  polygon(points: ArrayLike<number>, fill?: Fill, stroke?: Stroke): void {
    this.push({ op: "polygon", points: this.pts(points), fill: fill ? resolveFill(fill) : null, stroke: stroke ? resolveStroke(stroke) : null });
  }
  /** Records a `rect` op; a missing fill or stroke is recorded as null. */
  rect(x: number, y: number, w: number, h: number, fill?: Fill, stroke?: Stroke, radius = 0): void {
    this.push({ op: "rect", x: r3(x), y: r3(y), w: r3(w), h: r3(h), radius: r3(radius), fill: fill ? resolveFill(fill) : null, stroke: stroke ? resolveStroke(stroke) : null });
  }
  /** Records a `circle` op; a missing fill or stroke is recorded as null. */
  circle(cx: number, cy: number, r: number, fill?: Fill, stroke?: Stroke): void {
    this.push({ op: "circle", cx: r3(cx), cy: r3(cy), r: r3(r), fill: fill ? resolveFill(fill) : null, stroke: stroke ? resolveStroke(stroke) : null });
  }
  /** Records an `arc` op with the resolved stroke. */
  arc(cx: number, cy: number, r: number, startAngle: number, endAngle: number, stroke: Stroke): void {
    this.push({ op: "arc", cx: r3(cx), cy: r3(cy), r: r3(r), start: r3(startAngle), end: r3(endAngle), stroke: resolveStroke(stroke) });
  }
  /** Records a `sector` op; a missing fill or stroke is recorded as null. */
  sector(cx: number, cy: number, innerRadius: number, outerRadius: number, startAngle: number, endAngle: number, fill?: Fill, stroke?: Stroke): void {
    this.push({ op: "sector", cx: r3(cx), cy: r3(cy), inner: r3(innerRadius), outer: r3(outerRadius), start: r3(startAngle), end: r3(endAngle), fill: fill ? resolveFill(fill) : null, stroke: stroke ? resolveStroke(stroke) : null });
  }
  /** Records a `text` op with the resolved style. */
  text(text: string, x: number, y: number, style: TextStyle): void {
    this.push({ op: "text", text, x: r3(x), y: r3(y), style: resolveText(style) });
  }
  /** Synthetic metrics: `charWidth` × size per character, height = size. */
  measureText(text: string, style: TextStyle): TextMetrics {
    const size = resolveText(style).size;
    return { width: r3(text.length * size * this.charWidth), height: size };
  }
  /** Records an `image` op with the image size; raster images also get a FNV-1a hash of their pixels. */
  image(image: ImageHandle, x: number, y: number, w: number, h: number, opacity = 1): void {
    const op: PaintOp = { op: "image", imageWidth: image.width, imageHeight: image.height, x: r3(x), y: r3(y), w: r3(w), h: r3(h), opacity: r3(opacity) };
    if (isRasterImage(image)) (op as Record<string, unknown>).hash = fnv1a(image.rgba);
    this.push(op);
  }
  /** Draws into a child painter the first time or when `dirty`, recording its ops as `layerDraw`; always records a `layer` reference. */
  layer(key: string, width: number, height: number, draw: (p: Painter) => void, x: number, y: number, dirty = false): void {
    let cached = this.layers.get(key);
    if (!cached || dirty) {
      const child = new RecordingPainter(width, height, this.pixelRatio, this.charWidth);
      draw(child);
      cached = child.ops;
      this.layers.set(key, cached);
      this.push({ op: "layerDraw", key, width: r3(width), height: r3(height), ops: cached });
    }
    this.push({ op: "layer", key, x: r3(x), y: r3(y) });
  }
}
