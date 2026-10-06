// Mori.SkyScope — 3D view model: an orbit camera, a scene, a frame tree and the tools on top (orbit, measure, select) plus the HUD every 3D viewer wants — …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { formatNumber } from "../scales/ticks.js";
import type { Vec2, Vec3 } from "../scene/geometry.js";
import type { InputEvent, Modifiers } from "../scene/interaction.js";
import { Scene, type HitResult } from "../scene/scene.js";
import { SCENE_LIGHT, type SceneTheme } from "../scene/controller.js";
import { Camera3D, type Camera3DOptions } from "./camera3d.js";
import { FrameTree } from "./frame-tree.js";
import { sceneBounds3 } from "./layers3d.js";
import type { Painter3D } from "./painter3d.js";
import { v3len, v3sub } from "./math3.js";

/**
 * 3D view model: an orbit camera, a scene, a frame tree and the tools on top (orbit, measure, select) plus the
 * HUD every 3D viewer wants — orientation gizmo, cursor readout, measurement, selection ring. Hosts feed it
 * `InputEvent`s, call `draw3d` with a `Painter3D` and `drawHud` with the 2D painter on top.
 * Mouse: left drag orbits, middle drag or space+left or shift+left pans, right drag dollies, wheel dollies about
 * the cursor, double-click fits all. Keys: F fit all, R reset the view, T top-down, O toggle orthographic.
 * Mirrors `Mori.SkyScope.Core.Scene3D.Scene3DController`; pinned by `spec/fixtures/scene3d-controller.json`.
 */
/** Interactive tool: orbit, pan and dolly the camera, two-click distance measurement, or hit-test selection. */
export type Scene3DTool = "orbit" | "measure" | "select";

/** Construction options for `Scene3DController`; every field is optional. */
export interface Scene3DControllerOptions {
  /** Initial orbit camera state. */
  camera?: Camera3DOptions | undefined;
  /** Initial tool; default "orbit". */
  tool?: Scene3DTool | undefined;
  /** Overrides merged over the default dark theme. */
  theme?: Partial<SceneTheme> | undefined;
  /** World unit label for readouts; default "m". */
  unit?: string | undefined;
  /** The frame the scene is drawn in (default "map"). */
  fixedFrame?: string | undefined;
  /** Frame tree shared with the layers; a new one is created when omitted. */
  frames?: FrameTree | undefined;
  /** Toggle the orientation gizmo and the cursor readout; both default to true. */
  showGizmo?: boolean | undefined; showCursor?: boolean | undefined;
  /** Screen pixels within which hover and selection hit-test; default 6. */
  hitTolerance?: number | undefined;
  /** Radians per pixel for orbit drags. */
  orbitSpeed?: number | undefined;
  /** Wheel: factor = base ^ (deltaY / divisor). */
  wheelBase?: number | undefined; wheelDivisor?: number | undefined;
  /** Pixels of movement before a press becomes a drag; default 3. */
  clickSlop?: number | undefined;
}

type Gesture = "orbit" | "pan" | "dolly";

/**
 * View model for an interactive 3D scene: owns the orbit camera, the scene and the frame tree, interprets input and
 * draws the HUD. The mouse and key bindings are listed in the module notes above.
 */
export class Scene3DController {
  /** The orbit camera the scene is drawn through. */
  readonly camera: Camera3D;
  /** Layers to draw and hit-test; 3D layers draw through the `Painter3D`, 2D ones through the HUD painter. */
  readonly scene = new Scene();
  /** Frame tree shared with the layers. */
  readonly frames: FrameTree;
  /** Frame the scene is drawn in; shown in the cursor readout. */
  fixedFrame: string;
  /** Colours and font for background and HUD. */
  theme: SceneTheme;
  /**
   * Unit label, HUD toggles, hit tolerance, orbit speed, wheel response and click slop (see
   * `Scene3DControllerOptions`).
   */
  unit: string; showGizmo: boolean; showCursor: boolean; hitTolerance: number; orbitSpeed: number; wheelBase: number; wheelDivisor: number; clickSlop: number;
  private _tool: Scene3DTool;
  private press: { x: number; y: number; button: number; gesture: Gesture | null; dragging: boolean; last: Vec2 } | null = null;
  private spaceHeld = false;
  /** Measurement endpoints (world), at most two. */
  measure: Vec3[] = [];
  /** Hit under the last click with the select tool, or null. */
  selection: HitResult | null = null;
  /** Hit under the pointer while the select tool is active, or null. */
  hover: HitResult | null = null;
  /** Ground point under the pointer, or null when the pointer looks past the ground. */
  cursor: Vec3 | null = null;
  /** Chart time handed to layers. */
  now = 0;
  private readonly home: { yaw: number; pitch: number; distance: number };

  /** Builds the camera and remembers its initial orbit as the home view for `resetView`. */
  constructor(o: Scene3DControllerOptions = {}) {
    this.camera = new Camera3D(o.camera);
    this.home = { yaw: this.camera.yaw, pitch: this.camera.pitch, distance: this.camera.distance };
    this.frames = o.frames ?? new FrameTree();
    this.fixedFrame = o.fixedFrame ?? "map";
    this._tool = o.tool ?? "orbit";
    this.theme = { ...SCENE_LIGHT, background: "#0b1220", overlay: "#e2e8f0", overlayText: "#e2e8f0", ...o.theme };
    this.unit = o.unit ?? "m"; this.showGizmo = o.showGizmo ?? true; this.showCursor = o.showCursor ?? true;
    this.hitTolerance = o.hitTolerance ?? 6; this.orbitSpeed = o.orbitSpeed ?? 0.005;
    this.wheelBase = o.wheelBase ?? 2; this.wheelDivisor = o.wheelDivisor ?? 400; this.clickSlop = o.clickSlop ?? 3;
  }

  /** The active tool. */
  get tool(): Scene3DTool { return this._tool; }
  /** Switch tools; clears the measurement unless measuring and the hover unless selecting. */
  setTool(tool: Scene3DTool): void { this._tool = tool; if (tool !== "measure") this.measure = []; if (tool !== "select") this.hover = null; }
  /** Resize the camera's viewport in pixels. */
  setViewport(width: number, height: number): void { this.camera.setViewport(width, height); }

  /** Frame the union of the visible 3D layers' bounds; returns false when no layer has bounds. */
  fitAll(): boolean {
    const b = sceneBounds3(this.scene);
    if (!b) return false;
    this.camera.fitBox(b);
    return true;
  }
  /** Back to the initial orbit angles and distance, keeping the target. */
  resetView(): void { this.camera.setOrbit(this.home.yaw, this.home.pitch); this.camera.setDistance(this.home.distance); }
  /** Straight down, x to the right. */
  topDown(): void { this.camera.setOrbit(-Math.PI / 2, Math.PI / 2); }
  /** Distance between the two measurement points in world units, or null with fewer than two. */
  measureDistance(): number | null { return this.measure.length < 2 ? null : v3len(v3sub(this.measure[1]!, this.measure[0]!)); }
  /** Topmost hit within `hitTolerance` pixels of a screen point, at chart time `now`. */
  hitTest(sx: number, sy: number): HitResult | null { return this.scene.hitTest(sx, sy, this.camera, this.camera.width, this.camera.height, this.hitTolerance, this.now); }

  private gestureFor(button: number, m: Modifiers | undefined): Gesture | null {
    if (button === 1 || (button === 0 && (this.spaceHeld || m?.shift))) return "pan";
    if (button === 2) return "dolly";
    if (button === 0) return "orbit";
    return null;
  }

  /** Feed a platform event; returns true when something changed that needs a redraw. */
  handle(ev: InputEvent): boolean {
    switch (ev.type) {
      case "keydown":
        if (ev.key === " ") { this.spaceHeld = true; return false; }
        if (ev.key === "f" || ev.key === "F") return this.fitAll();
        if (ev.key === "r" || ev.key === "R") { this.resetView(); return true; }
        if (ev.key === "t" || ev.key === "T") { this.topDown(); return true; }
        if (ev.key === "o" || ev.key === "O") { this.camera.setOrtho(!this.camera.ortho); return true; }
        if (ev.key === "Escape") { const had = this.measure.length > 0 || this.selection !== null; this.measure = []; this.selection = null; this.press = null; return had; }
        return false;
      case "keyup": if (ev.key === " ") this.spaceHeld = false; return false;
      case "pointerdown":
        if (this.press) return false;
        this.press = { x: ev.x, y: ev.y, button: ev.button, gesture: this.gestureFor(ev.button, ev.modifiers), dragging: false, last: { x: ev.x, y: ev.y } };
        return false;
      case "pointermove": {
        const p = this.press;
        if (!p) {
          this.cursor = this.camera.groundPoint(ev.x, ev.y);
          if (this._tool === "select") this.hover = this.hitTest(ev.x, ev.y);
          return true;
        }
        if (!p.dragging && Math.abs(ev.x - p.x) <= this.clickSlop && Math.abs(ev.y - p.y) <= this.clickSlop) return false;
        p.dragging = true;
        const dx = ev.x - p.last.x, dy = ev.y - p.last.y;
        p.last = { x: ev.x, y: ev.y };
        switch (p.gesture) {
          case "orbit": this.camera.orbit(-dx * this.orbitSpeed, dy * this.orbitSpeed); return true;
          case "pan": this.camera.pan(dx, dy); return true;
          case "dolly": this.camera.dolly(Math.pow(this.wheelBase, dy / this.wheelDivisor * 2)); return true;
          default: return false;
        }
      }
      case "pointerup": {
        const p = this.press;
        this.press = null;
        if (!p || p.dragging || p.button !== 0) return false;
        if (this._tool === "measure") {
          const w = this.hitTest(ev.x, ev.y)?.world ?? this.camera.groundPoint(ev.x, ev.y);
          if (!w) return false;
          if (this.measure.length >= 2) this.measure = [w]; else this.measure.push(w);
          return true;
        }
        if (this._tool === "select") { this.selection = this.hitTest(ev.x, ev.y); return true; }
        return false;
      }
      case "pointercancel": this.press = null; return false;
      case "wheel": this.camera.dollyAt(ev.x, ev.y, Math.pow(this.wheelBase, ev.deltaY / this.wheelDivisor)); return true;
      case "dblclick": return this.fitAll();
    }
  }

  /** The 3D pass: clear, then every 3D layer. */
  draw3d(p3: Painter3D, width: number, height: number, p2: Painter): void {
    this.setViewport(width, height);
    p3.begin(this.camera.view(), this.camera.projection(), this.theme.background);
    this.scene.draw(p2, this.camera, this.now, p3);
    p3.end();
  }

  /** The 2D pass on top: rings, measurement, gizmo, readouts. */
  drawHud(p: Painter, width: number, height: number): void {
    const th = this.theme, cam = this.camera, text: TextStyle = { color: th.overlayText, family: th.fontFamily, size: th.fontSize };
    const ring = (h: HitResult, color: string): void => { const s = cam.project3(h.world.x, h.world.y, h.world.z); if (s.visible) p.circle(s.x, s.y, 9, undefined, { color, width: 2 }); };
    if (this.hover && (!this.selection || this.hover.layerId !== this.selection.layerId || this.hover.index !== this.selection.index)) ring(this.hover, th.overlay);
    if (this.selection) ring(this.selection, th.selection);
    if (this.measure.length > 0) {
      const a = cam.project3(this.measure[0]!.x, this.measure[0]!.y, this.measure[0]!.z);
      p.circle(a.x, a.y, 4, { color: th.measure });
      if (this.measure.length > 1) {
        const b = cam.project3(this.measure[1]!.x, this.measure[1]!.y, this.measure[1]!.z);
        p.line(a.x, a.y, b.x, b.y, { color: th.measure, width: 1.5, dash: [6, 4] });
        p.circle(b.x, b.y, 4, { color: th.measure });
        const d = this.measureDistance()!, label = `${formatNumber(d, d >= 100 ? 0 : d >= 10 ? 1 : d >= 1 ? 2 : 3)} ${this.unit}`;
        const mx = (a.x + b.x) / 2, my = (a.y + b.y) / 2, w = p.measureText(label, text).width + 10;
        p.rect(mx - w / 2, my - 20, w, 16, { color: th.background, opacity: 0.9 }, { color: th.measure, width: 1 }, 3);
        p.text(label, mx, my - 12, { ...text, color: th.measure, align: "center", baseline: "middle" });
      }
    }
    if (this.showGizmo) this.drawGizmo(p, width - 40, height - 40, 26);
    if (this.showCursor) {
      const c = this.cursor;
      const s = c ? `${formatNumber(c.x, 2)}, ${formatNumber(c.y, 2)}, ${formatNumber(c.z, 2)} ${this.unit}` : "";
      const mode = `${this.fixedFrame} · ${cam.ortho ? "ortho" : "persp"}`;
      p.text(s ? `${s}  ·  ${mode}` : mode, 10, height - 8, { ...text, align: "left", baseline: "bottom" });
    }
  }

  /** Orientation triad: world axes projected with the camera's rotation only. */
  private drawGizmo(p: Painter, cx: number, cy: number, r: number): void {
    const v = this.camera.view();
    const axes: [string, number, number, number][] = [["x", v[0]!, v[1]!, v[2]!], ["y", v[4]!, v[5]!, v[6]!], ["z", v[8]!, v[9]!, v[10]!]];
    const colors: Record<string, string> = { x: "#dc2626", y: "#16a34a", z: "#2563eb" };
    axes.sort((a, b) => a[3] - b[3]);
    p.circle(cx, cy, r + 8, { color: this.theme.background, opacity: 0.6 });
    for (const [name, ax, ay] of axes) {
      const ex = cx + ax * r, ey = cy - ay * r;
      p.line(cx, cy, ex, ey, { color: colors[name]!, width: 2 });
      p.circle(ex, ey, 5, { color: colors[name]! });
      p.text(name, ex, ey, { color: "#ffffff", family: this.theme.fontFamily, size: 9, align: "center", baseline: "middle", weight: "bold" });
    }
  }
}
