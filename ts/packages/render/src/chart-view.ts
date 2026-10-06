// Mori.SkyScope — Canvas host for the analytic chart models: hover, legend clicks, box zoom, wheel zoom, double-click reset.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter } from "@mori/skyscope-core";
import { Canvas2DPainter, capture } from "./canvas2d-painter.js";

/**
 * What the analytic chart models (CartesianChart, PieChart, PolarChart, Heatmap) expose to a host. Zoom methods
 * are optional — only the cartesian chart has them.
 */
export interface ChartLike {
  /** Paints the whole chart into a `width` x `height` CSS-pixel area; called once per invalidated frame. */
  draw(p: Painter, width: number, height: number): void;
  /** Pointer position in CSS pixels relative to the host; updates hover/tooltip state. */
  pointerMove(x: number, y: number, width: number, height: number): void;
  /** The pointer left the host; clears hover state. */
  pointerLeave(): void;
  /** A primary-button press; returns true when consumed (e.g. a legend toggle), which suppresses a box-zoom start. */
  click?(x: number, y: number, width: number, height: number): boolean;
  /** Starts a box-zoom drag at the given host coordinates. */
  beginBox?(x: number, y: number): void;
  /** Ends the box-zoom drag; returns true when the zoom was applied. */
  endBox?(width: number, height: number): boolean;
  /** Zooms about (x, y) by a wheel delta (positive = zoom out) using the `layout` computed by `layout()`. */
  wheelZoom?(x: number, y: number, deltaY: number, layout: unknown): void;
  /** Computes the chart layout for a size; its result is only ever handed back to `wheelZoom`. */
  layout?(width: number, height: number): unknown;
  /** Restores the unzoomed axes (double-click). */
  resetZoom?(): void;
}

/** Host options for `ChartView`. `maxFps` caps the redraw rate in frames per second (default 60). */
export interface ChartViewOptions { maxFps?: number | undefined }

/**
 * Canvas host for an analytic chart: hover tooltips, legend clicks, drag = box zoom, wheel = zoom, double-click = reset.
 * Redraws only when invalidated (a hover, a click, new data), so idle charts cost nothing.
 */
export class ChartView<C extends ChartLike = ChartLike> {
  /** The focusable host div appended to the container; fills it and receives the pointer and keyboard events. */
  readonly element: HTMLDivElement;
  /** The single canvas the chart is painted on; sized to the host and the device pixel ratio on every frame. */
  readonly canvas: HTMLCanvasElement;
  private dirty = true;
  private raf = 0;
  private last = 0;
  private width = 1; private height = 1;
  private readonly minFrameMs: number;
  private readonly observer: ResizeObserver | null;
  private readonly unlisten: (() => void)[] = [];

  /** Creates the host inside `container`, observes its size, binds the input events and schedules the first frame. `chart` stays public so hosts can mutate the model and call `invalidate`. */
  constructor(container: HTMLElement, public chart: C, options: ChartViewOptions = {}) {
    this.minFrameMs = 1000 / (options.maxFps ?? 60);
    const el = document.createElement("div");
    el.className = "skyscope-chart"; el.tabIndex = 0;
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
    this.invalidate();
  }

  /** Swaps the model (e.g. after rebuilding it from new options) and redraws. Event handlers read `this.chart`, so the new model receives input. */
  setChart(chart: C): void { this.chart = chart; this.invalidate(); }
  /** Request a redraw (call after changing the model or its data). */
  invalidate(): void { this.dirty = true; if (!this.raf) this.raf = requestAnimationFrame(this.tick); }
  /** Cancels the pending frame, stops observing, unbinds the events and removes the host from the DOM. */
  dispose(): void { if (this.raf) cancelAnimationFrame(this.raf); this.raf = 0; this.observer?.disconnect(); for (const u of this.unlisten) u(); this.element.remove(); }

  private resize(): void {
    const r = this.element.getBoundingClientRect();
    this.width = Math.max(1, Math.floor(r.width)); this.height = Math.max(1, Math.floor(r.height));
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
    this.chart.draw(p, this.width, this.height);
  }

  private bind(): void {
    const el = this.element, c = this.chart;
    const pos = (e: MouseEvent): [number, number] => { const r = el.getBoundingClientRect(); return [e.clientX - r.left, e.clientY - r.top]; };
    const on = <K extends keyof HTMLElementEventMap>(type: K, fn: (e: HTMLElementEventMap[K]) => void, opts?: AddEventListenerOptions): void => { el.addEventListener(type, fn, opts); this.unlisten.push(() => el.removeEventListener(type, fn)); };
    let dragging = false;
    on("pointermove", (e) => { const [x, y] = pos(e); this.chart.pointerMove(x, y, this.width, this.height); this.invalidate(); });
    on("pointerleave", () => { this.chart.pointerLeave(); this.invalidate(); });
    on("pointerdown", (e) => {
      if (e.button !== 0) return;
      el.focus();
      const [x, y] = pos(e);
      if (this.chart.click?.(x, y, this.width, this.height)) { this.invalidate(); return; }
      if (this.chart.beginBox) { capture(el, e); dragging = true; this.chart.beginBox(x, y); }
    });
    on("pointerup", () => { if (!dragging) return; dragging = false; this.chart.endBox?.(this.width, this.height); this.invalidate(); });
    on("pointercancel", () => { dragging = false; this.chart.endBox?.(this.width, this.height); this.invalidate(); });
    on("dblclick", () => { if (this.chart.resetZoom) { this.chart.resetZoom(); this.invalidate(); } });
    on("wheel", (e) => {
      if (!c.wheelZoom || !c.layout) return;
      e.preventDefault();
      const [x, y] = pos(e);
      this.chart.wheelZoom!(x, y, e.deltaY, this.chart.layout!(this.width, this.height));
      this.invalidate();
    }, { passive: false });
  }
}
