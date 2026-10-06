// Mori.SkyScope — Web host of the trend chart: three canvases, render loop, resize, and pointer, wheel and keyboard routing through the model's hit test.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { TrendChartModel, applyTrendOptions, drawTrendChartBackground, drawTrendChartForeground, drawTrendChartSeries, seriesGeometry, initialInteraction, reduceInteraction, LiveClock,
  type InputEvent, type InteractionState, type Modifiers, type Rect, type SignalStore, type TimeSource, type Tool, type TrendChartOptions, type TrendLayout } from "@mori/skyscope-core";
import { Canvas2DPainter, capture } from "./canvas2d-painter.js";
import { WebGLLineRenderer, cssToRgba } from "./webgl-lines.js";

/** Construction options of `TrendChartView`. */
export interface TrendChartViewOptions {
  /** The store whose channels the chart reads; the view never writes to it. */
  store: SignalStore;
  /** Initial chart configuration (lanes, axes, series, theme); defaults apply for anything omitted. */
  config?: TrendChartOptions | undefined;
  /** Initial pointer tool (default "pan"). */
  tool?: Tool | undefined;
  /** Time source for the live window's "now" (default: wall clock); pass a playback clock to follow a recording. */
  clock?: TimeSource | undefined;
  /** Draw series with WebGL (default true when available); otherwise Canvas2D draws everything. */
  webgl?: boolean | undefined;
  /** Frames per second cap for the render loop (default 60). */
  maxFps?: number | undefined;
}

/**
 * DOM host for a TrendChart: three stacked canvases (2D background, WebGL series, 2D foreground),
 * a render loop, ResizeObserver, and pointer/wheel/keyboard → interaction reducer → model effects.
 * Gestures follow ibaAnalyzer: series names (in the plot or the legend) are picked up and dropped onto an axis
 * (common scale), into a lane's free area (own scale) or between lanes (new lane); lane header bars reorder, fold and
 * remove lanes; Y-axes shift and stretch by dragging, zoom with the wheel and autoscale on double-click; the navigator
 * frame moves, resizes, jumps on click and steps with the arrow keys. Ctrl/Shift while picking up adds to a selection.
 */
export class TrendChartView {
  /** The core chart model (config, lanes, axes, cursors, drags); hosts call its methods directly for pause, reset, time span. */
  readonly model: TrendChartModel;
  /** The focusable host div appended to the container; fills it, clips overflow and receives the input events. */
  readonly element: HTMLDivElement;
  private readonly bg: HTMLCanvasElement;
  private readonly fg: HTMLCanvasElement;
  private glCanvas: HTMLCanvasElement | null;
  private gl: WebGLLineRenderer | null = null;
  private readonly clock: TimeSource;
  private interaction: InteractionState;
  private boxPreview: Rect | null = null;
  private layout: TrendLayout | null = null;
  private width = 0; private height = 0;
  private raf = 0; private lastFrame = 0; private readonly minFrameMs: number;
  private readonly observer: ResizeObserver | null;
  private readonly unlisten: (() => void)[] = [];

  /**
   * Builds the model and the three canvases inside `container` and binds the events. WebGL is tried when requested and
   * silently falls back to Canvas2D series. The render loop is not started: call `start()`.
   */
  constructor(container: HTMLElement, options: TrendChartViewOptions) {
    this.model = new TrendChartModel(options.store, options.config ?? {});
    this.clock = options.clock ?? new LiveClock();
    this.interaction = initialInteraction(options.tool ?? "pan");
    this.minFrameMs = 1000 / (options.maxFps ?? 60);
    const el = document.createElement("div");
    el.className = "skyscope-trend"; el.tabIndex = 0;
    Object.assign(el.style, { position: "relative", width: "100%", height: "100%", overflow: "hidden", outline: "none", touchAction: "none", userSelect: "none" });
    const mk = (z: number): HTMLCanvasElement => { const c = document.createElement("canvas"); Object.assign(c.style, { position: "absolute", left: "0", top: "0", zIndex: String(z) }); el.appendChild(c); return c; };
    this.bg = mk(0);
    this.glCanvas = (options.webgl ?? true) ? mk(1) : null;
    if (this.glCanvas) { try { this.gl = new WebGLLineRenderer(this.glCanvas); } catch { this.glCanvas.remove(); this.glCanvas = null; } }
    this.fg = mk(2);
    container.appendChild(el);
    this.element = el;
    this.observer = typeof ResizeObserver !== "undefined" ? new ResizeObserver(() => this.resize()) : null;
    this.observer?.observe(el);
    this.resize();
    this.bindEvents();
  }

  /** The active pointer tool (pan, boxZoom, cursor). */
  get tool(): Tool { return this.interaction.tool; }
  /** Switches the pointer tool; any gesture in progress is discarded. */
  setTool(tool: Tool): void { this.interaction = initialInteraction(tool); }
  /** Theme and style merge field-wise; other keys replace. */
  setConfig(config: TrendChartOptions): void { applyTrendOptions(this.model.config, config); this.model.timeSpan = this.model.config.timeSpan; this.layout = null; }

  /** Starts the continuous render loop (one frame per animation frame, capped at `maxFps`); idempotent. */
  start(): void { if (!this.raf) this.raf = requestAnimationFrame(this.tick); }
  /** Stops the render loop; the last frame stays on screen. */
  stop(): void { if (this.raf) { cancelAnimationFrame(this.raf); this.raf = 0; } }
  /** Stops the loop, stops observing, unbinds the events, frees the WebGL resources and removes the host from the DOM. */
  dispose(): void { this.stop(); this.observer?.disconnect(); for (const u of this.unlisten) u(); this.gl?.dispose(); this.element.remove(); }

  private resize(): void {
    const r = this.element.getBoundingClientRect();
    this.width = Math.max(1, Math.floor(r.width)); this.height = Math.max(1, Math.floor(r.height));
    this.layout = null;
  }

  private tick = (t: number): void => {
    this.raf = requestAnimationFrame(this.tick);
    if (t - this.lastFrame < this.minFrameMs) return;
    this.lastFrame = t;
    this.render();
  };

  /** One frame: advance time, refresh decimation, paint three layers. */
  render(): void {
    const m = this.model;
    m.now = this.clock.now();
    const layout = (this.layout = m.layout(this.width, this.height));
    m.update(layout);
    const dpr = globalThis.devicePixelRatio || 1;
    const bg = Canvas2DPainter.forCanvas(this.bg, this.width, this.height, dpr);
    drawTrendChartBackground(m, bg, layout);
    if (this.gl && this.glCanvas) {
      this.gl.resize(this.width, this.height, dpr);
      Object.assign(this.glCanvas.style, { width: `${this.width}px`, height: `${this.height}px` });
      this.gl.clear();
      for (const g of seriesGeometry(m, layout)) {
        this.gl.setSeries(g.seriesId, g.points, g.points.length / 2, g.xOrigin);
        this.gl.draw(g.seriesId, g.clip, { color: cssToRgba(g.color) }, g.scissor);
      }
    }
    const fg = Canvas2DPainter.forCanvas(this.fg, this.width, this.height, dpr);
    fg.clear();
    if (!this.gl) drawTrendChartSeries(m, fg, layout);
    drawTrendChartForeground(m, fg, layout);
    const th = m.config.theme;
    if (this.boxPreview) fg.rect(this.boxPreview.x, this.boxPreview.y, this.boxPreview.w, this.boxPreview.h, { color: th.cursorA, opacity: 0.12 }, { color: th.cursorA, width: 1, dash: [4, 2] });
    this.element.style.cursor = m.drag || m.laneDrag ? "grabbing" : m.axisDrag ? "ns-resize" : this.hoverCursor;
  }

  private hoverLegendRow: string | null = null;
  /** Series picked with Ctrl/Shift; the next drag moves them together. */
  readonly selection = new Set<string>();
  private externalDrag = false;
  /** Fired after a drop, a lane change or an axis change the host may want to persist. */
  onConfigChanged: (() => void) | null = null;

  /**
   * A drag that started outside the chart (a signal tree): `channelIds` become series when dropped on the chart.
   * Call `externalDragMove`/`externalDrop` with client coordinates; `group` keeps several channels in one lane.
   */
  externalDragStart(channelIds: number[], group = false): void { this.externalDrag = true; this.model.beginDrag([], -1000, -1000, group, channelIds); }
  /** Moves an external drag; viewport (client) coordinates, so the pointer may still be over the tree. No-op without an external drag. */
  externalDragMove(clientX: number, clientY: number): void { if (!this.externalDrag) return; const p = this.clientToLocal(clientX, clientY); this.model.updateDrag(this.currentLayout(), p.x, p.y); }
  /** Finishes an external drag at viewport coordinates; returns true when the drop changed the configuration (and fires `onConfigChanged`). */
  externalDrop(clientX: number, clientY: number): boolean {
    if (!this.externalDrag) return false;
    this.externalDrag = false;
    const p = this.clientToLocal(clientX, clientY);
    const changed = this.model.endDrag(this.currentLayout(), p.x, p.y);
    this.layout = null;
    if (changed) this.onConfigChanged?.();
    return changed;
  }
  /** Abandons an external drag (pointer cancel, Escape) without changing the chart. */
  externalDragCancel(): void { this.externalDrag = false; this.model.cancelDrag(); }
  private clientToLocal(clientX: number, clientY: number): { x: number; y: number } { const r = this.element.getBoundingClientRect(); return { x: clientX - r.left, y: clientY - r.top }; }
  private changed(): void { this.layout = null; this.onConfigChanged?.(); }
  private currentLayout(): TrendLayout { return this.layout ?? (this.layout = this.model.layout(this.width, this.height)); }
  private hoverCursor = "";
  private cursorFor(hit: ReturnType<TrendChartModel["hitTest"]>): string {
    switch (hit.kind) {
      case "label": case "legendRow": return "grab";
      case "header": return hit.part === "grip" ? "grab" : "pointer";
      case "axis": return hit.zone === "middle" ? "ns-resize" : "row-resize";
      case "navigator": return hit.zone === "inside" ? "grab" : hit.zone === "outside" ? "pointer" : "ew-resize";
      default: return "";
    }
  }

  private dispatch(ev: InputEvent): void {
    const { state, effects } = reduceInteraction(this.interaction, ev);
    this.interaction = state;
    const layout = this.layout ?? this.model.layout(this.width, this.height);
    for (const e of effects) {
      if (e.type === "boxZoomPreview") this.boxPreview = e.rect;
      else if (e.type === "boxZoom" || e.type === "boxZoomCancel") this.boxPreview = null;
      if (e.type === "hover") { this.hoverLegendRow = this.model.legendRowAt(layout, e.x, e.y); this.hoverCursor = this.cursorFor(this.model.hitTest(layout, e.x, e.y)); }
      this.model.applyEffect(e, layout);
    }
  }

  private bindEvents(): void {
    const el = this.element;
    const mods = (e: MouseEvent): Modifiers => ({ shift: e.shiftKey, ctrl: e.ctrlKey || e.metaKey, alt: e.altKey });
    const pos = (e: MouseEvent): { x: number; y: number } => { const r = el.getBoundingClientRect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
    const on = <K extends keyof HTMLElementEventMap>(type: K, fn: (e: HTMLElementEventMap[K]) => void, opts?: AddEventListenerOptions): void => {
      el.addEventListener(type, fn, opts); this.unlisten.push(() => el.removeEventListener(type, fn));
    };
    const m = this.model;
    on("pointerdown", (e) => {
      el.focus(); capture(el, e);
      const p = pos(e), layout = this.currentLayout(), hit = m.hitTest(layout, p.x, p.y), group = e.ctrlKey || e.metaKey || e.shiftKey;
      if (e.button === 0) {
        if (hit.kind === "label" || hit.kind === "legendRow") {
          if (group) this.selection.add(hit.seriesId);
          const ids = this.selection.has(hit.seriesId) ? [...this.selection] : [hit.seriesId];
          m.beginDrag(ids, p.x, p.y, group);
          return;
        }
        if (hit.kind === "header") {
          if (hit.part === "collapse") { m.setLaneCollapsed(hit.laneId, !m.lanes().find((l) => l.id === hit.laneId)?.collapsed); this.changed(); return; }
          if (hit.part === "remove") { m.removeLane(hit.laneId); this.changed(); return; }
          m.beginLaneDrag(hit.laneId, p.x, p.y); return;
        }
        if (hit.kind === "axis") { m.beginAxisDrag(layout, hit.axisId, hit.laneId, hit.zone, p.y); return; }
        if (hit.kind === "navigator") { m.beginNavigatorDrag(layout, p.x); return; }
        if (!group) this.selection.clear();
      }
      this.dispatch({ type: "pointerdown", ...p, button: e.button, modifiers: mods(e) });
    });
    on("pointermove", (e) => {
      const p = pos(e);
      if (m.drag) { m.updateDrag(this.currentLayout(), p.x, p.y); return; }
      if (m.laneDrag) { m.updateLaneDrag(this.currentLayout(), p.x, p.y); return; }
      if (m.axisDrag) { m.updateAxisDrag(this.currentLayout(), p.y); return; }
      if (m.navDrag) { m.updateNavigatorDrag(this.currentLayout(), p.x); return; }
      this.dispatch({ type: "pointermove", ...p, modifiers: mods(e) });
    });
    on("pointerup", (e) => {
      const p = pos(e);
      if (m.drag) { if (m.endDrag(this.currentLayout(), p.x, p.y)) this.changed(); else this.layout = null; return; }
      if (m.laneDrag) { if (m.endLaneDrag(this.currentLayout(), p.x, p.y)) this.changed(); return; }
      if (m.axisDrag) { m.endAxisDrag(); this.onConfigChanged?.(); return; }
      if (m.navDrag) { m.endNavigatorDrag(); return; }
      this.dispatch({ type: "pointerup", ...p, button: e.button, modifiers: mods(e) });
    });
    on("pointercancel", () => { m.cancelDrag(); m.cancelLaneDrag(); m.endAxisDrag(); m.endNavigatorDrag(); this.dispatch({ type: "pointercancel" }); });
    on("wheel", (e) => {
      e.preventDefault();
      const p = pos(e), layout = this.currentLayout(), hit = m.hitTest(layout, p.x, p.y);
      if (hit.kind === "axis") { m.axisZoomAt(layout, hit.axisId, hit.laneId, p.y, Math.pow(2, -e.deltaY / 400)); return; }
      if (hit.kind === "navigator") { m.applyEffect({ type: "zoom", x: layout.plot.x + layout.plot.w, y: p.y, factor: Math.pow(2, -e.deltaY / 400) }, layout); return; }
      this.dispatch({ type: "wheel", ...p, deltaY: e.deltaY, modifiers: mods(e) });
    }, { passive: false });
    on("dblclick", (e) => {
      const p = pos(e), layout = this.currentLayout(), hit = m.hitTest(layout, p.x, p.y);
      if (hit.kind === "axis") { m.axisAutoscale(hit.axisId); this.onConfigChanged?.(); return; }
      if (hit.kind === "navigator" || hit.kind === "header" || hit.kind === "label") return;
      this.dispatch({ type: "dblclick", ...p });
    });
    on("keydown", (e) => {
      if (e.key === " ") e.preventDefault();
      if (e.key === "Escape") { m.cancelDrag(); m.cancelLaneDrag(); this.selection.clear(); }
      if ((e.key === "ArrowLeft" || e.key === "ArrowRight") && m.config.navigator) { e.preventDefault(); m.navigatorKey(e.key); return; }
      this.dispatch({ type: "keydown", key: e.key });
    });
    on("keyup", (e) => this.dispatch({ type: "keyup", key: e.key }));
    on("contextmenu", (e) => e.preventDefault());
  }
}
