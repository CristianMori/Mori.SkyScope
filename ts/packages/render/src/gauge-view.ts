// Mori.SkyScope — Canvas host for any gauge model: redraws only when invalidated or while the needle is settling, so a dashboard of idle gauges costs nothing.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Knob, Painter, Slider, Switch } from "@mori/skyscope-core";
import { Canvas2DPainter, capture } from "./canvas2d-painter.js";

/** The minimum a gauge model must offer the host: `draw` paints it into a `width` x `height` CSS-pixel area. */
export interface GaugeLike { draw(p: Painter, width: number, height: number): void }
/** A gauge with a settling animation: `step(dt)` advances it by `dt` seconds and `animating` is true while it is still moving. */
export interface AnimatedGauge extends GaugeLike { step(dt: number): unknown; readonly animating: boolean }
const isAnimated = (g: GaugeLike): g is AnimatedGauge => "step" in g && "animating" in g;

/** Host options for `GaugeView`. `maxFps` caps the redraw rate in frames per second (default 60). */
export interface GaugeViewOptions { maxFps?: number | undefined }

/**
 * Canvas host for any gauge model: redraws only when invalidated or while the needle is settling,
 * so a dashboard of idle gauges costs nothing. Pointer/wheel/keyboard hooks let input gauges bind.
 */
export class GaugeView<G extends GaugeLike = GaugeLike> {
  /** The focusable host div appended to the container; fills it and receives the pointer and keyboard events. */
  readonly element: HTMLDivElement;
  /** The canvas the gauge is painted on; sized to the host and the device pixel ratio on every frame. */
  readonly canvas: HTMLCanvasElement;
  /** Pointer hook for input gauges: `type` is down/move/up (cancel reports as up), x/y are CSS pixels in the host. Null leaves pointer events unhandled. */
  onPointer: ((type: "down" | "move" | "up", x: number, y: number, width: number, height: number) => void) | null = null;
  /** Wheel hook: receives the raw `deltaY` (positive = scroll down). When set, wheel events are consumed. */
  onWheel: ((deltaY: number) => void) | null = null;
  /** Keyboard hook: receives `KeyboardEvent.key`; arrows, space and Enter are prevented from scrolling the page. */
  onKey: ((key: string) => void) | null = null;
  private dirty = true;
  private raf = 0;
  private last = 0;
  private width = 1; private height = 1;
  private readonly minFrameMs: number;
  private readonly observer: ResizeObserver | null;
  private readonly unlisten: (() => void)[] = [];

  /** Creates the host inside `container`, observes its size, binds the input events and starts the loop. `gauge` stays public so hosts can push values and call `invalidate`. */
  constructor(container: HTMLElement, public gauge: G, options: GaugeViewOptions = {}) {
    this.minFrameMs = 1000 / (options.maxFps ?? 60);
    const el = document.createElement("div");
    el.className = "skyscope-gauge"; el.tabIndex = 0;
    Object.assign(el.style, { position: "relative", width: "100%", height: "100%", outline: "none", touchAction: "none", userSelect: "none" });
    this.canvas = document.createElement("canvas");
    Object.assign(this.canvas.style, { position: "absolute", left: "0", top: "0" });
    el.appendChild(this.canvas);
    container.appendChild(el);
    this.element = el;
    this.observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(() => { this.resize(); this.invalidate(); }) : null;
    this.observer?.observe(el);
    this.resize();
    this.bind();
    this.start();
  }

  /** Swaps the model and redraws; the input hooks are left untouched. */
  setGauge(gauge: G): void { this.gauge = gauge; this.invalidate(); }
  /** Request a redraw (call after changing the model). */
  invalidate(): void { this.dirty = true; this.start(); }
  /** Schedules the next animation frame if none is pending; the loop stops by itself once idle and settled. */
  start(): void { if (!this.raf) this.raf = requestAnimationFrame(this.tick); }
  /** Cancels the pending frame; `invalidate` or `start` resumes. */
  stop(): void { if (this.raf) { cancelAnimationFrame(this.raf); this.raf = 0; } }
  /** Stops the loop, stops observing, unbinds the events and removes the host from the DOM. */
  dispose(): void { this.stop(); this.observer?.disconnect(); for (const u of this.unlisten) u(); this.element.remove(); }

  private resize(): void {
    const r = this.element.getBoundingClientRect();
    this.width = Math.max(1, Math.floor(r.width)); this.height = Math.max(1, Math.floor(r.height));
  }

  private tick = (t: number): void => {
    this.raf = 0;
    const dt = this.last ? Math.min(0.1, (t - this.last) / 1000) : 0;
    if (isAnimated(this.gauge) && this.gauge.animating && dt > 0) { this.gauge.step(dt); this.dirty = true; }
    if (this.dirty && (!this.last || t - this.last >= this.minFrameMs)) { this.render(); this.dirty = false; this.last = t; }
    else if (!this.dirty) this.last = t;
    if (this.dirty || (isAnimated(this.gauge) && this.gauge.animating)) this.raf = requestAnimationFrame(this.tick);
  };

  /** Paints one frame synchronously, bypassing the frame-rate cap and without stepping the animation. */
  render(): void {
    const p = Canvas2DPainter.forCanvas(this.canvas, this.width, this.height, globalThis.devicePixelRatio || 1);
    this.gauge.draw(p, this.width, this.height);
  }

  private bind(): void {
    const el = this.element;
    const pos = (e: MouseEvent): [number, number] => { const r = el.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
    const on = <K extends keyof HTMLElementEventMap>(type: K, fn: (e: HTMLElementEventMap[K]) => void, opts?: AddEventListenerOptions): void => { el.addEventListener(type, fn, opts); this.unlisten.push(() => el.removeEventListener(type, fn)); };
    on("pointerdown", (e) => { if (!this.onPointer) return; el.focus(); capture(el, e); this.onPointer("down", ...pos(e), this.width, this.height); this.invalidate(); });
    on("pointermove", (e) => { if (!this.onPointer) return; this.onPointer("move", ...pos(e), this.width, this.height); this.invalidate(); });
    on("pointerup", (e) => { if (!this.onPointer) return; this.onPointer("up", ...pos(e), this.width, this.height); this.invalidate(); });
    on("pointercancel", (e) => { if (!this.onPointer) return; this.onPointer("up", ...pos(e), this.width, this.height); this.invalidate(); });
    on("wheel", (e) => { if (!this.onWheel) return; e.preventDefault(); this.onWheel(e.deltaY); this.invalidate(); }, { passive: false });
    on("keydown", (e) => { if (!this.onKey) return; if (["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", " ", "Enter"].includes(e.key)) e.preventDefault(); this.onKey(e.key); this.invalidate(); });
  }
}

/** Wires a Knob to its view: drag to turn, wheel and arrow keys nudge one step (wheel up = increase). Replaces the view's hooks. */
export function bindKnob(view: GaugeView<Knob>): void {
  const k = view.gauge;
  view.onPointer = (type, x, y, w, h) => { const l = k.layout(w, h); if (type === "down") k.pointerDown(l, x, y); else if (type === "move") k.pointerMove(l, x, y); else k.pointerUp(); };
  view.onWheel = (dy) => k.nudge(-dy);
  view.onKey = (key) => { if (key === "ArrowUp" || key === "ArrowRight") k.nudge(1); else if (key === "ArrowDown" || key === "ArrowLeft") k.nudge(-1); };
}
/** Wires a Slider to its view: drag the thumb, wheel and arrow keys nudge one step. Replaces the view's hooks. */
export function bindSlider(view: GaugeView<Slider>): void {
  const s = view.gauge;
  view.onPointer = (type, x, y, w, h) => { if (type === "down") s.pointerDown(w, h, x, y); else if (type === "move") s.pointerMove(w, h, x, y); else s.pointerUp(); };
  view.onWheel = (dy) => s.nudge(-dy);
  view.onKey = (key) => { if (key === "ArrowUp" || key === "ArrowRight") s.nudge(1); else if (key === "ArrowDown" || key === "ArrowLeft") s.nudge(-1); };
}
/** Wires a Switch to its view: a press, space or Enter toggles it. Replaces the view's hooks. */
export function bindSwitch(view: GaugeView<Switch>): void {
  view.onPointer = (type) => { if (type === "down") view.gauge.toggle(); };
  view.onKey = (key) => { if (key === " " || key === "Enter") view.gauge.toggle(); };
}
