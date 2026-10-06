// Mori.SkyScope — Host for the 3D scene view: a WebGL2 canvas for the scene and a 2D canvas for the HUD, pointer and keyboard routing.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { Scene3DController, type InputEvent, type Modifiers, type Scene3DControllerOptions } from "@mori/skyscope-core";
import { Canvas2DPainter, capture } from "./canvas2d-painter.js";
import { WebGLPainter3D } from "./webgl-painter3d.js";

/** Controller options plus the host's own loop settings. */
export interface Scene3DViewOptions extends Scene3DControllerOptions {
  /** Frames per second cap for the render loop (default 60); also bounds the continuous loop when `animate` is set. */
  maxFps?: number | undefined;
  /** Redraw every frame (live scenes whose layers depend on `now`); default redraws on demand. */
  animate?: boolean | undefined;
  /** Chart time source for `controller.now`; default: seconds since the page loaded. */
  clock?: (() => number) | undefined;
}

/**
 * Host for a Scene3DController: a WebGL2 canvas for the scene and a 2D canvas on top for the HUD. Translates
 * pointer/wheel/keyboard events into core InputEvents and redraws on demand (or continuously with `animate`).
 */
export class Scene3DView {
  /** The focusable host div appended to the container; fills it, clips overflow and receives the input events. */
  readonly element: HTMLDivElement;
  /** The WebGL2 canvas the 3D scene is rendered on (bottom layer). */
  readonly glCanvas: HTMLCanvasElement;
  /** The 2D canvas stacked on top for labels and the HUD; it ignores pointer events so input reaches the host. */
  readonly hudCanvas: HTMLCanvasElement;
  /** The core controller that owns the scene, camera, frame tree and tools; the view only feeds it events and paints it. */
  readonly controller: Scene3DController;
  /** The WebGL2 painter bound to `glCanvas`; resized with the host and disposed with the view. */
  readonly painter: WebGLPainter3D;
  private dirty = true;
  private raf = 0;
  private last = 0;
  private width = 1; private height = 1;
  private readonly minFrameMs: number;
  private readonly animate: boolean;
  private readonly clock: () => number;
  private readonly t0 = typeof performance !== "undefined" ? performance.now() : Date.now();
  private readonly observer: ResizeObserver | null;
  private readonly unlisten: (() => void)[] = [];

  /** Creates the controller, both canvases and the painter inside `container`, observes its size, binds the input events and schedules the first frame. Throws when WebGL2 is unavailable. */
  constructor(container: HTMLElement, options: Scene3DViewOptions = {}) {
    const { maxFps, animate, clock, ...rest } = options;
    this.controller = new Scene3DController(rest);
    this.minFrameMs = 1000 / (maxFps ?? 60);
    this.animate = animate ?? false;
    this.clock = clock ?? (() => ((typeof performance !== "undefined" ? performance.now() : Date.now()) - this.t0) / 1000);
    const el = document.createElement("div");
    el.className = "skyscope-scene3d"; el.tabIndex = 0;
    Object.assign(el.style, { position: "relative", width: "100%", height: "100%", outline: "none", touchAction: "none", userSelect: "none", cursor: "grab", overflow: "hidden" });
    this.glCanvas = document.createElement("canvas");
    this.hudCanvas = document.createElement("canvas");
    for (const c of [this.glCanvas, this.hudCanvas]) { Object.assign(c.style, { position: "absolute", left: "0", top: "0" }); el.appendChild(c); }
    this.hudCanvas.style.pointerEvents = "none";
    container.appendChild(el);
    this.element = el;
    this.painter = new WebGLPainter3D(this.glCanvas);
    this.observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(() => { this.resize(); this.invalidate(); }) : null;
    this.observer?.observe(el);
    this.resize();
    this.bind();
    this.invalidate();
  }

  /** The controller's scene (the layer collection); shorthand for `controller.scene`. */
  get scene() { return this.controller.scene; }
  /** The controller's 3D camera; shorthand for `controller.camera`. */
  get camera() { return this.controller.camera; }
  /** The controller's transform tree (frame id → parent pose); shorthand for `controller.frames`. */
  get frames() { return this.controller.frames; }
  /** Request a redraw (call after changing layers). */
  invalidate(): void { this.dirty = true; if (!this.raf) this.raf = requestAnimationFrame(this.tick); }
  /** Cancels the pending frame, stops observing, unbinds the events, frees the GPU resources and removes the host from the DOM. */
  dispose(): void {
    if (this.raf) cancelAnimationFrame(this.raf); this.raf = 0;
    this.observer?.disconnect(); for (const u of this.unlisten) u();
    this.painter.dispose(); this.element.remove();
  }

  private resize(): void {
    const r = this.element.getBoundingClientRect();
    this.width = Math.max(1, Math.floor(r.width)); this.height = Math.max(1, Math.floor(r.height));
    const pr = globalThis.devicePixelRatio || 1;
    this.painter.resize(this.width, this.height, pr);
    this.glCanvas.style.width = `${this.width}px`; this.glCanvas.style.height = `${this.height}px`;
    this.controller.setViewport(this.width, this.height);
  }

  private tick = (t: number): void => {
    this.raf = 0;
    if (!this.dirty && !this.animate) return;
    if (this.last && t - this.last < this.minFrameMs) { this.raf = requestAnimationFrame(this.tick); return; }
    this.render(); this.dirty = false; this.last = t;
    if (this.animate) this.raf = requestAnimationFrame(this.tick);
  };

  /** Paints one frame synchronously: samples the clock into `controller.now`, clears the HUD, renders the 3D pass and then the HUD on top. */
  render(): void {
    const c = this.controller;
    c.now = this.clock();
    const hud = Canvas2DPainter.forCanvas(this.hudCanvas, this.width, this.height, globalThis.devicePixelRatio || 1);
    hud.clear();
    c.draw3d(this.painter, this.width, this.height, hud);
    c.drawHud(hud, this.width, this.height);
  }

  private dispatch(ev: InputEvent): void { if (this.controller.handle(ev)) this.invalidate(); }

  private bind(): void {
    const el = this.element;
    const pos = (e: MouseEvent): [number, number] => { const r = el.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
    const mods = (e: MouseEvent): Modifiers => ({ shift: e.shiftKey, ctrl: e.ctrlKey, alt: e.altKey });
    const on = <K extends keyof HTMLElementEventMap>(type: K, fn: (e: HTMLElementEventMap[K]) => void, opts?: AddEventListenerOptions): void => { el.addEventListener(type, fn, opts); this.unlisten.push(() => el.removeEventListener(type, fn)); };
    on("pointerdown", (e) => { el.focus(); capture(el, e); el.style.cursor = "grabbing"; const [x, y] = pos(e); this.dispatch({ type: "pointerdown", x, y, button: e.button, modifiers: mods(e) }); });
    on("pointermove", (e) => { const [x, y] = pos(e); this.dispatch({ type: "pointermove", x, y, modifiers: mods(e) }); });
    on("pointerup", (e) => { el.style.cursor = "grab"; const [x, y] = pos(e); this.dispatch({ type: "pointerup", x, y, button: e.button, modifiers: mods(e) }); });
    on("pointercancel", () => { el.style.cursor = "grab"; this.dispatch({ type: "pointercancel" }); });
    on("dblclick", (e) => { const [x, y] = pos(e); this.dispatch({ type: "dblclick", x, y }); });
    on("wheel", (e) => { e.preventDefault(); const [x, y] = pos(e); this.dispatch({ type: "wheel", x, y, deltaY: e.deltaY, modifiers: mods(e) }); }, { passive: false });
    on("keydown", (e) => { if ([" ", "f", "r", "t", "o", "F", "R", "T", "O", "Escape"].includes(e.key)) e.preventDefault(); this.dispatch({ type: "keydown", key: e.key }); });
    on("keyup", (e) => this.dispatch({ type: "keyup", key: e.key }));
    on("contextmenu", (e) => e.preventDefault());
  }
}
