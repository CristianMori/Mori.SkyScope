// Mori.SkyScope — Canvas host for a SceneController: translates pointer/wheel/keyboard events into core InputEvents and redraws on demand (interaction, inv…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SceneController, type InputEvent, type Modifiers, type SceneControllerOptions } from "@mori/skyscope-core";
import { Canvas2DPainter, capture } from "./canvas2d-painter.js";

/** Controller options plus the host's own: `maxFps` caps the redraw rate in frames per second (default 60). */
export interface SceneViewOptions extends SceneControllerOptions { maxFps?: number | undefined }

/**
 * Canvas host for a SceneController: translates pointer/wheel/keyboard events into core InputEvents and
 * redraws on demand (interaction, `invalidate()` after layer updates) at most `maxFps` times per second.
 */
export class SceneView {
  /** The focusable host div appended to the container; fills it, shows a crosshair cursor and receives the input events. */
  readonly element: HTMLDivElement;
  /** The canvas the scene is painted on; sized to the host and the device pixel ratio on every frame. */
  readonly canvas: HTMLCanvasElement;
  /** The core controller that owns the scene, the camera and the tools; the view only feeds it events and paints it. */
  readonly controller: SceneController;
  private dirty = true;
  private raf = 0;
  private last = 0;
  private width = 1; private height = 1;
  private readonly minFrameMs: number;
  private readonly observer: ResizeObserver | null;
  private readonly unlisten: (() => void)[] = [];

  /** Creates the controller and the host inside `container`, observes its size, binds the input events and schedules the first frame. */
  constructor(container: HTMLElement, options: SceneViewOptions = {}) {
    const { maxFps, ...rest } = options;
    this.controller = new SceneController(rest);
    this.minFrameMs = 1000 / (maxFps ?? 60);
    const el = document.createElement("div");
    el.className = "skyscope-scene"; el.tabIndex = 0;
    Object.assign(el.style, { position: "relative", width: "100%", height: "100%", outline: "none", touchAction: "none", userSelect: "none", cursor: "crosshair" });
    this.canvas = document.createElement("canvas");
    Object.assign(this.canvas.style, { position: "absolute", left: "0", top: "0" });
    el.appendChild(this.canvas);
    container.appendChild(el);
    this.element = el;
    this.observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(() => { this.resize(); this.invalidate(); }) : null;
    this.observer?.observe(el);
    this.resize();
    this.bind();
    this.invalidate();
  }

  /** The controller's scene (the layer collection); shorthand for `controller.scene`. */
  get scene() { return this.controller.scene; }
  /** The controller's 2D camera; shorthand for `controller.camera`. */
  get camera() { return this.controller.camera; }
  /** Request a redraw (call after changing layers). */
  invalidate(): void { this.dirty = true; if (!this.raf) this.raf = requestAnimationFrame(this.tick); }
  /** Cancels the pending frame, stops observing, unbinds the events and removes the host from the DOM. */
  dispose(): void { if (this.raf) cancelAnimationFrame(this.raf); this.raf = 0; this.observer?.disconnect(); for (const u of this.unlisten) u(); this.element.remove(); }

  private resize(): void {
    const r = this.element.getBoundingClientRect();
    this.width = Math.max(1, Math.floor(r.width)); this.height = Math.max(1, Math.floor(r.height));
    this.controller.setViewport(this.width, this.height);
  }

  private tick = (t: number): void => {
    this.raf = 0;
    if (!this.dirty) return;
    if (this.last && t - this.last < this.minFrameMs) { this.raf = requestAnimationFrame(this.tick); return; }
    this.render(); this.dirty = false; this.last = t;
  };

  /** Paints one frame synchronously, bypassing the frame-rate cap; the render loop calls this when dirty. */
  render(): void {
    const p = Canvas2DPainter.forCanvas(this.canvas, this.width, this.height, globalThis.devicePixelRatio || 1);
    this.controller.draw(p, this.width, this.height);
  }

  private dispatch(ev: InputEvent): void { if (this.controller.handle(ev)) this.invalidate(); }

  private bind(): void {
    const el = this.element;
    const pos = (e: MouseEvent): [number, number] => { const r = el.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
    const mods = (e: MouseEvent): Modifiers => ({ shift: e.shiftKey, ctrl: e.ctrlKey, alt: e.altKey });
    const on = <K extends keyof HTMLElementEventMap>(type: K, fn: (e: HTMLElementEventMap[K]) => void, opts?: AddEventListenerOptions): void => { el.addEventListener(type, fn, opts); this.unlisten.push(() => el.removeEventListener(type, fn)); };
    on("pointerdown", (e) => { el.focus(); capture(el, e); const [x, y] = pos(e); this.dispatch({ type: "pointerdown", x, y, button: e.button, modifiers: mods(e) }); });
    on("pointermove", (e) => { const [x, y] = pos(e); this.dispatch({ type: "pointermove", x, y, modifiers: mods(e) }); });
    on("pointerup", (e) => { const [x, y] = pos(e); this.dispatch({ type: "pointerup", x, y, button: e.button, modifiers: mods(e) }); });
    on("pointercancel", () => this.dispatch({ type: "pointercancel" }));
    on("dblclick", (e) => { const [x, y] = pos(e); this.dispatch({ type: "dblclick", x, y }); });
    on("wheel", (e) => { e.preventDefault(); const [x, y] = pos(e); this.dispatch({ type: "wheel", x, y, deltaY: e.deltaY, modifiers: mods(e) }); }, { passive: false });
    on("keydown", (e) => { if ([" ", "q", "e", "r", "f", "Q", "E", "R", "F", "Escape"].includes(e.key)) e.preventDefault(); this.dispatch({ type: "keydown", key: e.key }); });
    on("keyup", (e) => this.dispatch({ type: "keyup", key: e.key }));
    on("contextmenu", (e) => e.preventDefault());
  }
}
