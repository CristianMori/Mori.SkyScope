// Mori.SkyScope — The rendering contract.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * The rendering contract. The core computes geometry and calls these primitives; a renderer
 * (Canvas2D, WebGL, SkiaSharp, SVG) implements them. Mirrors `Mori.SkyScope.Core.Paint.IPainter`.
 *
 * Conventions: pixels in CSS units (the painter applies `pixelRatio`), y down, angles in radians
 * from +x increasing clockwise on screen (Canvas convention). Polylines/polygons take interleaved
 * [x0, y0, x1, y1, …] so decimated series can be drawn without allocation.
 */
export type LineCap = "butt" | "round" | "square";
/** Corner style where polyline segments meet, as in Canvas `lineJoin`. */
export type LineJoin = "miter" | "round" | "bevel";

/** Outline style. Missing members take the defaults applied by `resolveStroke`: width 1, solid, butt cap, miter join, opacity 1. */
export interface Stroke {
  /** Any CSS colour string. */
  color: string;
  /** Line width in CSS pixels. */
  width?: number | undefined;
  /** Dash and gap lengths in CSS pixels; empty or undefined draws solid. */
  dash?: readonly number[] | undefined;
  /** End-cap style. */
  cap?: LineCap | undefined;
  /** Corner style. */
  join?: LineJoin | undefined;
  /** 0–1, multiplied with the colour's own alpha. */
  opacity?: number | undefined;
}

/** Solid fill: `color` is any CSS colour, `opacity` 0–1 defaults to 1. */
export interface Fill { color: string; opacity?: number | undefined }

/** Horizontal anchoring of text about its x. */
export type TextAlign = "left" | "center" | "right";
/** Vertical anchoring of text about its y, as in Canvas `textBaseline`. */
export type TextBaseline = "top" | "middle" | "bottom" | "alphabetic";

/** Font and placement of a text run. Missing members take the defaults applied by `resolveText`: system font, 12 px, normal weight, left, alphabetic. */
export interface TextStyle {
  /** Any CSS colour string. */
  color: string;
  /** CSS font-family list. */
  family?: string | undefined;
  /** Font size in CSS pixels. */
  size?: number | undefined;
  /** CSS font weight, numeric or keyword. */
  weight?: number | "normal" | "bold" | undefined;
  /** Horizontal anchor; default left. */
  align?: TextAlign | undefined;
  /** Vertical anchor; default alphabetic. */
  baseline?: TextBaseline | undefined;
  /** Radians, clockwise, around (x, y). */
  rotation?: number | undefined;
  /** 0–1, multiplied with the colour's own alpha. */
  opacity?: number | undefined;
}

/** Extent of a text run in CSS pixels; `height` is the font size on painters without real glyph metrics. */
export interface TextMetrics { width: number; height: number }

/** `width`/`height` are the image's intrinsic size in pixels. */
/** Opaque platform image (HTMLImageElement, ImageBitmap, SKImage…). */
export interface ImageHandle { readonly width: number; readonly height: number }

/** `rgba` holds width × height × 4 bytes, row-major from the top-left; bump `version` after changing the pixels so painters re-upload. */
/**
 * A core-produced RGBA raster (heatmaps). Painters upload it to a platform image, re-uploading only when
 * `version` changes. Mirrors `Mori.SkyScope.Core.Paint.RasterImage`.
 */
export interface RasterImage extends ImageHandle { readonly rgba: Uint8ClampedArray; readonly version: number }
/** Type guard: true for images whose pixels the core owns. */
export const isRasterImage = (i: ImageHandle): i is RasterImage => "rgba" in i;

/** Font family used when a `TextStyle` has none. */
export const DEFAULT_FONT_FAMILY = "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif";
/** Font size in CSS pixels used when a `TextStyle` has none. */
export const DEFAULT_FONT_SIZE = 12;

/** The rendering contract every renderer implements: about fifteen primitives in CSS pixels with y down, plus clipping, transforms and layer caching. */
export interface Painter {
  /** Surface width in CSS pixels. */
  readonly width: number;
  /** Surface height in CSS pixels. */
  readonly height: number;
  /** Device pixels per CSS pixel; coordinates are nonetheless always given in CSS pixels. */
  readonly pixelRatio: number;

  /** Pushes the current transform and clip. */
  save(): void;
  /** Pops the state pushed by the matching `save`. */
  restore(): void;
  /** Moves the origin by (x, y). */
  translate(x: number, y: number): void;
  /** Scales subsequent coordinates about the origin. */
  scale(sx: number, sy: number): void;
  /** Rotates subsequent coordinates about the origin; positive is clockwise on screen. */
  rotate(radians: number): void;
  /** Restricts drawing to the rectangle until the enclosing `restore`. */
  clipRect(x: number, y: number, w: number, h: number): void;

  /** Fills the whole surface with `color`, or makes it transparent when omitted. */
  clear(color?: string): void;
  /** Straight segment between two points. */
  line(x1: number, y1: number, x2: number, y2: number, stroke: Stroke): void;
  /** `count` points starting at element `offset` of the interleaved array. */
  polyline(points: ArrayLike<number>, stroke: Stroke, offset?: number, count?: number): void;
  /** Closed shape from interleaved points; fill and stroke may each be omitted. */
  polygon(points: ArrayLike<number>, fill?: Fill, stroke?: Stroke): void;
  /** Axis-aligned rectangle; `radius` rounds the corners (0 or omitted = square). */
  rect(x: number, y: number, w: number, h: number, fill?: Fill, stroke?: Stroke, radius?: number): void;
  /** Disc centred at (cx, cy). */
  circle(cx: number, cy: number, r: number, fill?: Fill, stroke?: Stroke): void;
  /** Stroked circular arc from `startAngle` to `endAngle`, radians clockwise from +x. */
  arc(cx: number, cy: number, r: number, startAngle: number, endAngle: number, stroke: Stroke): void;
  /** Filled annular sector between two radii and two angles (a pie slice when `innerRadius` is 0). */
  sector(cx: number, cy: number, innerRadius: number, outerRadius: number, startAngle: number, endAngle: number, fill?: Fill, stroke?: Stroke): void;
  /** One line of text anchored at (x, y) per the style's align and baseline. */
  text(text: string, x: number, y: number, style: TextStyle): void;
  /** Size the text would occupy drawn with `style`. */
  measureText(text: string, style: TextStyle): TextMetrics;
  /** Draws an image scaled into the rectangle; `opacity` 0–1 defaults to 1. */
  image(image: ImageHandle, x: number, y: number, w: number, h: number, opacity?: number): void;
  /**
   * Cached offscreen surface: `draw` runs only the first time or when `dirty`; the surface is then
   * composited at (x, y). This is how static layers (grids, bitmaps, paused series) cost one blit.
   */
  layer(key: string, width: number, height: number, draw: (p: Painter) => void, x: number, y: number, dirty?: boolean): void;
}

/** Fully-resolved styles, identical in both cores — what RecordingPainter records and what renderers consume. */
export interface ResolvedStroke { color: string; width: number; dash: number[] | null; cap: LineCap; join: LineJoin; opacity: number }
/** `Fill` with every default applied. */
export interface ResolvedFill { color: string; opacity: number }
/** `TextStyle` with every default applied; `weight` is stringified. */
export interface ResolvedTextStyle { color: string; family: string; size: number; weight: string; align: TextAlign; baseline: TextBaseline; rotation: number; opacity: number }

/** Applies the stroke defaults; the dash array is copied, and an empty one becomes null. */
export function resolveStroke(s: Stroke): ResolvedStroke {
  return { color: s.color, width: s.width ?? 1, dash: s.dash && s.dash.length > 0 ? [...s.dash] : null, cap: s.cap ?? "butt", join: s.join ?? "miter", opacity: s.opacity ?? 1 };
}
/** Applies the fill defaults. */
export function resolveFill(f: Fill): ResolvedFill { return { color: f.color, opacity: f.opacity ?? 1 }; }
/** Applies the text defaults. */
export function resolveText(t: TextStyle): ResolvedTextStyle {
  return { color: t.color, family: t.family ?? DEFAULT_FONT_FAMILY, size: t.size ?? DEFAULT_FONT_SIZE, weight: String(t.weight ?? "normal"), align: t.align ?? "left", baseline: t.baseline ?? "alphabetic", rotation: t.rotation ?? 0, opacity: t.opacity ?? 1 };
}
