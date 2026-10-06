// Mori.SkyScope — SceneView model: a camera, a scene and the tools on top of it (pan, box zoom, measure, select), plus the overlays every map view wants — …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { tickSpec } from "../scales/ticks.js";
import { formatNumber } from "../scales/ticks.js";
import { Camera2D, type Camera2DOptions } from "./camera.js";
import type { Rect, Vec2 } from "./geometry.js";
import { applyCameraEffect, initialInteraction, reduceInteraction, type InputEvent, type InteractionState, type Tool } from "./interaction.js";
import { Scene, type HitResult } from "./scene.js";

/**
 * SceneView model: a camera, a scene and the tools on top of it (pan, box zoom, measure, select), plus the
 * overlays every map view wants — box-zoom preview, measurement, selection ring, scale bar, cursor readout.
 * Hosts feed it `InputEvent`s and call `draw`. Mirrors `Mori.SkyScope.Core.Scene.SceneController`;
 * pinned by `spec/fixtures/scene-controller.json`.
 */
/**
 * Interactive tool selected on a `SceneController`: pan the camera, rubber-band zoom, two-click distance measurement,
 * or hit-test selection.
 */
export type SceneTool = "pan" | "boxZoom" | "measure" | "select";

/** Colours and font used for the scene background and the controller's overlays. */
export interface SceneTheme {
  /**
   * CSS colours: canvas background, overlay strokes, overlay text, selection ring, measurement, box-zoom rubber band.
   */
  background: string; overlay: string; overlayText: string; selection: string; measure: string; boxZoom: string;
  /** Font family and size in CSS pixels for the overlay labels. */
  fontFamily: string; fontSize: number;
}
/** Default light theme (slate palette). */
export const SCENE_LIGHT: SceneTheme = { background: "#f8fafc", overlay: "#0f172a", overlayText: "#0f172a", selection: "#d97706", measure: "#dc2626", boxZoom: "#2563eb", fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", fontSize: 11 };
/** Dark variant of `SCENE_LIGHT`: near-black background with light overlays. */
export const SCENE_DARK: SceneTheme = { ...SCENE_LIGHT, background: "#020617", overlay: "#f8fafc", overlayText: "#f8fafc", selection: "#fbbf24", measure: "#f87171", boxZoom: "#60a5fa" };

/** Construction options for `SceneController`; every field is optional. */
export interface SceneControllerOptions {
  /** Initial 2D camera state. */
  camera?: Camera2DOptions | undefined;
  /** Initial tool; default "pan". */
  tool?: SceneTool | undefined;
  /** Overrides merged over `SCENE_LIGHT`. */
  theme?: Partial<SceneTheme> | undefined;
  /** World unit label for the scale bar and readouts. */
  unit?: string | undefined;
  /** Toggle the scale bar (bottom-left) and the cursor readout (bottom-right); both default to true. */
  showScaleBar?: boolean | undefined; showCursor?: boolean | undefined;
  /** Screen pixels within which hover and selection hit-test; default 6. */
  hitTolerance?: number | undefined;
  /** Pixels of padding for fit-all. */
  fitPadding?: number | undefined;
}

/**
 * View model for an interactive 2D map or scene: owns the camera, the scene and the interaction state, and draws the
 * overlays. Mirrors `Mori.SkyScope.Core.Scene.SceneController`.
 */
export class SceneController {
  /** The camera the scene is drawn through; moved by gestures and by the fit and rotate helpers. */
  readonly camera: Camera2D;
  /** Layers to draw and hit-test; hosts add and remove layers on it directly. */
  readonly scene = new Scene();
  /** Colours and font for background and overlays. */
  theme: SceneTheme;
  /**
   * World unit label, overlay toggles, hit tolerance in pixels and fit-all padding in pixels (see
   * `SceneControllerOptions`).
   */
  unit: string; showScaleBar: boolean; showCursor: boolean; hitTolerance: number; fitPadding: number;
  private interaction: InteractionState;
  private _tool: SceneTool;
  /** Box-zoom rubber band (screen). */
  boxPreview: Rect | null = null;
  /** Measurement endpoints (world), at most two. */
  measure: Vec2[] = [];
  /** Hit under the last click with the select tool, or null. */
  selection: HitResult | null = null;
  /** Hit under the pointer while the select tool is active, or null. */
  hover: HitResult | null = null;
  /** World position of the pointer from the last hover event, or null before any movement. */
  cursor: Vec2 | null = null;
  /** Chart time handed to layers. */
  now = 0;

  /** Builds the camera and the interaction state from options; see `SceneControllerOptions` for the defaults. */
  constructor(o: SceneControllerOptions = {}) {
    this.camera = new Camera2D(o.camera);
    this._tool = o.tool ?? "pan";
    this.interaction = initialInteraction(SceneController.gestureFor(this._tool));
    this.theme = { ...SCENE_LIGHT, ...o.theme };
    this.unit = o.unit ?? "m"; this.showScaleBar = o.showScaleBar ?? true; this.showCursor = o.showCursor ?? true;
    this.hitTolerance = o.hitTolerance ?? 6; this.fitPadding = o.fitPadding ?? 24;
  }

  /** The active tool. */
  get tool(): SceneTool { return this._tool; }
  /** Switch tools; restarts the gesture state and clears the measurement unless the measure tool stays active. */
  setTool(tool: SceneTool): void {
    this._tool = tool;
    this.interaction = initialInteraction(SceneController.gestureFor(tool));
    if (tool !== "measure") this.measure = [];
  }
  private static gestureFor(tool: SceneTool): Tool { return tool === "measure" ? "cursor" : tool === "select" ? "select" : tool; }

  /** Resize the camera's viewport in pixels. */
  setViewport(width: number, height: number): void { this.camera.setViewport(width, height); }
  /** Rotate the view by `radians` (positive = clockwise on screen). */
  rotateBy(radians: number): void { this.camera.setRotation(this.camera.rotation + radians); }
  /** Set the absolute view rotation in radians. */
  setRotation(radians: number): void { this.camera.setRotation(radians); }
  /** Fit the union of the visible layers' bounds with `fitPadding`; returns false when no layer has bounds. */
  fitAll(): boolean {
    const b = this.scene.bounds();
    if (!b) return false;
    this.camera.fitBounds(b, this.fitPadding);
    return true;
  }
  /** Distance between the two measurement points (world units) or null. */
  measureDistance(): number | null {
    if (this.measure.length < 2) return null;
    const [a, b] = this.measure as [Vec2, Vec2];
    return Math.hypot(b.x - a.x, b.y - a.y);
  }
  /** Topmost hit within `hitTolerance` pixels of a screen point, at chart time `now`. */
  hitTest(sx: number, sy: number): HitResult | null { return this.scene.hitTest(sx, sy, this.camera, this.camera.width, this.camera.height, this.hitTolerance, this.now); }

  /** Feed a platform event; returns true when something changed that needs a redraw. */
  handle(ev: InputEvent): boolean {
    if (ev.type === "keydown") {
      if (ev.key === "q" || ev.key === "Q") { this.rotateBy(-Math.PI / 12); return true; }
      if (ev.key === "e" || ev.key === "E") { this.rotateBy(Math.PI / 12); return true; }
      if (ev.key === "r" || ev.key === "R") { this.camera.setRotation(0); return true; }
      if (ev.key === "f" || ev.key === "F") { return this.fitAll(); }
      if (ev.key === "Escape") { this.measure = []; this.selection = null; }
    }
    const r = reduceInteraction(this.interaction, ev);
    this.interaction = r.state;
    let changed = false;
    for (const e of r.effects) {
      if (applyCameraEffect(this.camera, e)) { changed = true; if (e.type === "boxZoom") this.boxPreview = null; continue; }
      switch (e.type) {
        case "boxZoomPreview": this.boxPreview = e.rect; changed = true; break;
        case "boxZoomCancel": this.boxPreview = null; changed = true; break;
        case "hover": {
          const w = this.camera.unproject(e.x, e.y);
          this.cursor = w;
          if (this._tool === "select") this.hover = this.hitTest(e.x, e.y);
          changed = true;
          break;
        }
        case "click": {
          if (e.button !== 0) break;
          if (this._tool === "measure") {
            const w = this.camera.unproject(e.x, e.y);
            if (this.measure.length >= 2) this.measure = [w]; else this.measure.push(w);
            changed = true;
          } else if (this._tool === "select") { this.selection = this.hitTest(e.x, e.y); changed = true; }
          break;
        }
        case "reset": changed = this.fitAll() || changed; break;
        case "dragStart": case "drag": case "dragEnd": break;
      }
    }
    return changed;
  }

  /** Scale-bar length: a 1-2-5 world length that spans about `targetPx` pixels. */
  scaleBar(targetPx = 100): { world: number; px: number } {
    const worldPerPx = 1 / this.camera.zoom;
    const step = tickSpec(0, worldPerPx * targetPx, 1).step;
    const world = step > 0 ? step : worldPerPx * targetPx;
    return { world, px: world * this.camera.zoom };
  }

  /** Draws background, scene and overlays into `p` after syncing the viewport to `width` × `height` pixels. */
  draw(p: Painter, width: number, height: number): void {
    this.setViewport(width, height);
    const th = this.theme, text: TextStyle = { color: th.overlayText, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    this.scene.draw(p, this.camera, this.now);
    // hover / selection rings
    const ring = (h: HitResult, color: string): void => { const s = this.camera.project(h.world.x, h.world.y); p.circle(s.x, s.y, 9, undefined, { color, width: 2 }); };
    if (this.hover && (!this.selection || this.hover.layerId !== this.selection.layerId || this.hover.index !== this.selection.index)) ring(this.hover, th.overlay);
    if (this.selection) ring(this.selection, th.selection);
    // measurement
    if (this.measure.length > 0) {
      const a = this.camera.project(this.measure[0]!.x, this.measure[0]!.y);
      p.circle(a.x, a.y, 4, { color: th.measure });
      if (this.measure.length > 1) {
        const b = this.camera.project(this.measure[1]!.x, this.measure[1]!.y);
        p.line(a.x, a.y, b.x, b.y, { color: th.measure, width: 1.5, dash: [6, 4] });
        p.circle(b.x, b.y, 4, { color: th.measure });
        const d = this.measureDistance()!, label = `${formatNumber(d, d >= 100 ? 0 : d >= 10 ? 1 : d >= 1 ? 2 : 3)} ${this.unit}`;
        const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2, w = p.measureText(label, text).width + 10;
        p.rect(mx - w / 2, my - 20, w, 16, { color: th.background, opacity: 0.9 }, { color: th.measure, width: 1 }, 3);
        p.text(label, mx, my - 12, { ...text, color: th.measure, align: "center", baseline: "middle" });
      }
    }
    if (this.boxPreview) { const r = this.boxPreview; p.rect(r.x, r.y, r.w, r.h, { color: th.boxZoom, opacity: 0.1 }, { color: th.boxZoom, width: 1, dash: [4, 3] }); }
    if (this.showScaleBar) {
      const sb = this.scaleBar(), x = 12, y = height - 14;
      p.line(x, y, x + sb.px, y, { color: th.overlay, width: 2 });
      p.line(x, y - 4, x, y + 4, { color: th.overlay, width: 2 });
      p.line(x + sb.px, y - 4, x + sb.px, y + 4, { color: th.overlay, width: 2 });
      p.text(`${formatNumber(sb.world, sb.world < 1 ? 2 : 0)} ${this.unit}`, x + sb.px / 2, y - 6, { ...text, align: "center", baseline: "bottom" });
    }
    if (this.showCursor && this.cursor) {
      p.text(`${formatNumber(this.cursor.x, 2)}, ${formatNumber(this.cursor.y, 2)} ${this.unit}`, width - 10, height - 8, { ...text, align: "right", baseline: "bottom" });
    }
  }
}
