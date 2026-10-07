// Mori.SkyScope — Web host of the trend chart: three canvases, render loop, resize, and pointer, wheel and keyboard routing through the model's hit test.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { TrendChartModel, applyTrendOptions, drawTrendChartBackground, drawTrendChartForeground, drawTrendChartSeries, seriesGeometry, initialInteraction, reduceInteraction, LiveClock, formatFixed,
  CHANNEL_DRAG_MIME, CHANNEL_DRAG_DIGITAL_MIME, parseChannelDrag, channelDragIsDigital, type ChannelDragPayload, type DropTarget,
  type InputEvent, type InteractionState, type Modifiers, type Rect, type SignalStore, type TimeSource, type Tool, type TrendChartOptions, type TrendLayout } from "@cmori/skyscope-core";
import { Canvas2DPainter, capture } from "./canvas2d-painter.js";
import { saveFile } from "./download.js";
import { showSeriesMenu } from "./series-menu.js";
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
        this.gl.draw(g.seriesId, g.clip, { color: cssToRgba(g.color), width: g.width }, g.scissor);
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
  /** True while a native drag (HTML5 drag and drop) hovers the chart; the model's drag then only carries the preview. */
  private nativeDrag = false;
  /** Fired after a drop, a lane change or an axis change the host may want to persist. */
  onConfigChanged: (() => void) | null = null;
  /**
   * Fired when channels are dropped on the chart (HTML5 drag and drop with `CHANNEL_DRAG_MIME`, a JSON array of ids or
   * plain text ids), before anything is applied. Set `cancel` to refuse the drop, change `target` to redirect it, or
   * set `handled` after adding the signals yourself. Otherwise the chart calls `addChannels(ids, target, group)`.
   */
  onChannelDrop: ((e: ChannelDropEvent) => void) | null = null;

  /**
   * Add channels from code: a series per channel not in the chart yet, placed at `target` with the drop rules (default:
   * the first lane, analog on an own axis, digital in the logic stack). Returns the series ids and fires `onConfigChanged`.
   */
  addChannels(channelIds: number[], target?: DropTarget, group = false): string[] {
    const ids = this.model.addChannels(channelIds, target, group);
    if (ids.length) this.changed();
    return ids;
  }
  /** Where a payload would land if dropped at viewport coordinates (for a custom preview); `none` outside the plot. */
  dropTargetAt(clientX: number, clientY: number, digital = false): DropTarget { const p = this.clientToLocal(clientX, clientY); return this.model.dropTarget(this.currentLayout(), p.x, p.y, digital); }

  private static dragTypes(dt: DataTransfer | null): boolean { return !!dt && Array.from(dt.types).some((t) => t === CHANNEL_DRAG_MIME || t === "text/plain" || t === "Text"); }
  private static readPayload(dt: DataTransfer | null): ChannelDragPayload | null { return dt ? parseChannelDrag(dt.getData(CHANNEL_DRAG_MIME)) ?? parseChannelDrag(dt.getData("text/plain")) : null; }
  /** The arrangement as a JSON layout file (theme and style excluded). */
  exportLayout(): string { return this.model.exportLayout(); }
  /** Replace the arrangement with a layout file; theme and style are kept. */
  importLayout(json: string): void { this.model.importLayout(json); this.selection.clear(); this.changed(); }
  /**
   * The visible signals between cursors A and B as a CSV or MCAP download (`exportCursorsCsv` / `exportCursorsMcap` on the
   * model, named `skyscope-<t0>-<t1>.<format>`). Returns false, saving nothing, unless both cursors are set.
   */
  exportCursors(format: "csv" | "mcap"): boolean {
    const r = this.model.cursorRange();
    if (!r) return false;
    const name = `skyscope-${formatFixed(r.t0, 3)}-${formatFixed(r.t1, 3)}.${format}`;
    if (format === "csv") saveFile(new Blob([this.model.exportCursorsCsv()!], { type: "text/csv" }), name, "text/csv");
    else saveFile(this.model.exportCursorsMcap()!, name);
    return true;
  }
  /** Where a drag on a label or legend row started, to tell a click (hide/show) from a drag (move). */
  private press: { x: number; y: number; seriesId: string } | null = null;
  private clientToLocal(clientX: number, clientY: number): { x: number; y: number } { const r = this.element.getBoundingClientRect(); return { x: clientX - r.left, y: clientY - r.top }; }

  /** Native drag and drop: drag-over previews the drop target (the payload is not readable until the drop, so the digital hint travels as a MIME type), drop applies it through `onChannelDrop`. */
  private bindDragDrop(on: <K extends keyof HTMLElementEventMap>(type: K, fn: (e: HTMLElementEventMap[K]) => void) => void): void {
    const m = this.model;
    const over = (e: DragEvent): void => {
      if (!TrendChartView.dragTypes(e.dataTransfer)) return;
      e.preventDefault();
      if (e.dataTransfer) e.dataTransfer.dropEffect = "copy";
      const p = this.clientToLocal(e.clientX, e.clientY);
      if (!m.drag || !this.nativeDrag) { m.cancelDrag(); this.nativeDrag = true; const digital = !!e.dataTransfer && Array.from(e.dataTransfer.types).includes(CHANNEL_DRAG_DIGITAL_MIME); m.beginDrag([], p.x, p.y, false, [], digital); }
      m.updateDrag(this.currentLayout(), p.x, p.y);
    };
    on("dragenter", over);
    on("dragover", over);
    on("dragleave", (e) => { if (this.nativeDrag && !(e.relatedTarget instanceof Node && this.element.contains(e.relatedTarget))) { this.nativeDrag = false; m.cancelDrag(); } });
    on("drop", (e) => {
      if (!this.nativeDrag && !TrendChartView.dragTypes(e.dataTransfer)) return;
      e.preventDefault();
      this.nativeDrag = false; m.cancelDrag();
      const payload = TrendChartView.readPayload(e.dataTransfer);
      if (!payload) return;
      const p = this.clientToLocal(e.clientX, e.clientY);
      const ids = payload.channels.map((c) => c.id);
      const digital = channelDragIsDigital(payload, this.model.store);
      const ev: ChannelDropEvent = { payload, channelIds: ids, target: m.dropTarget(this.currentLayout(), p.x, p.y, digital), x: p.x, y: p.y, group: payload.group, cancel: false, handled: false };
      this.onChannelDrop?.(ev);
      if (ev.cancel) { this.layout = null; return; }
      if (ev.handled) { this.changed(); return; }
      if (ev.target.kind === "none") { this.layout = null; return; }
      this.addChannels(ids, ev.target, ev.group);
    });
  }
  private readonly configListeners = new Set<() => void>();
  /** Follow configuration changes from any source (gestures, drops, layout files, an editor panel); returns a function that stops following. */
  addConfigListener(fn: () => void): () => void { this.configListeners.add(fn); return () => { this.configListeners.delete(fn); }; }
  /** Tell the view the configuration was changed from outside (an editor panel calling model commands): relayouts and fires `onConfigChanged` and the listeners. */
  notifyConfigChanged(): void { this.changed(); }
  private changed(): void { this.layout = null; this.onConfigChanged?.(); for (const fn of this.configListeners) fn(); }
  private currentLayout(): TrendLayout { return this.layout ?? (this.layout = this.model.layout(this.width, this.height)); }
  private hoverCursor = "";
  private cursorFor(hit: ReturnType<TrendChartModel["hitTest"]>): string {
    switch (hit.kind) {
      case "label": case "legendRow": return "grab";
      case "header": return hit.part === "grip" ? "grab" : "pointer";
      case "axis": return hit.zone === "middle" ? "ns-resize" : "row-resize";
      case "navigator": return hit.zone === "inside" ? "grab" : hit.zone === "outside" ? "pointer" : "ew-resize";
      case "laneGap": return "row-resize";
      case "cursor": return "ew-resize";
      case "measure": return "default";
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
    this.bindDragDrop(on);
    on("pointerdown", (e) => {
      el.focus(); capture(el, e);
      const p = pos(e), layout = this.currentLayout(), hit = m.hitTest(layout, p.x, p.y), group = e.ctrlKey || e.metaKey || e.shiftKey;
      if (e.button === 0) {
        if (hit.kind === "label" || hit.kind === "legendRow") {
          if (group) this.selection.add(hit.seriesId);
          const ids = this.selection.has(hit.seriesId) ? [...this.selection] : [hit.seriesId];
          this.press = { x: p.x, y: p.y, seriesId: hit.seriesId };
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
        if (hit.kind === "laneGap") { m.beginLaneResize(layout, hit.aboveLaneId, hit.belowLaneId, p.y); return; }
        if (hit.kind === "cursor") { m.beginCursorDrag(hit.which); return; }
        if (hit.kind === "measure") return;
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
      if (m.laneResize) { if (m.updateLaneResize(this.currentLayout(), p.y)) this.layout = null; return; }
      if (m.cursorDrag) { m.updateCursorDrag(this.currentLayout(), p.x); this.layout = null; return; }
      this.dispatch({ type: "pointermove", ...p, modifiers: mods(e) });
    });
    on("pointerup", (e) => {
      const p = pos(e);
      if (m.drag) {
        // a press without movement on a label or legend row is a click: hide or show that signal
        const press = this.press; this.press = null;
        if (press && !m.drag.channelIds.length && Math.hypot(p.x - press.x, p.y - press.y) < 4 && !(e.ctrlKey || e.metaKey || e.shiftKey)) { m.cancelDrag(); m.toggleSeries(press.seriesId); this.changed(); return; }
        if (m.endDrag(this.currentLayout(), p.x, p.y)) this.changed(); else this.layout = null;
        return;
      }
      if (m.laneDrag) { if (m.endLaneDrag(this.currentLayout(), p.x, p.y)) this.changed(); return; }
      if (m.axisDrag) { m.endAxisDrag(); this.changed(); return; }
      if (m.navDrag) { m.endNavigatorDrag(); return; }
      if (m.laneResize) { m.endLaneResize(); this.changed(); return; }
      if (m.cursorDrag) { m.endCursorDrag(); return; }
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
      if (hit.kind === "axis") { m.axisAutoscale(hit.axisId); this.changed(); return; }
      if (hit.kind === "navigator" || hit.kind === "header" || hit.kind === "label" || hit.kind === "laneGap" || hit.kind === "cursor" || hit.kind === "measure") return;
      this.dispatch({ type: "dblclick", ...p });
    });
    on("keydown", (e) => {
      if (e.key === " ") e.preventDefault();
      if (e.key === "Escape") { m.cancelDrag(); m.cancelLaneDrag(); this.selection.clear(); this.nativeDrag = false; }
      if ((e.key === "ArrowLeft" || e.key === "ArrowRight") && m.config.navigator) { e.preventDefault(); m.navigatorKey(e.key); return; }
      this.dispatch({ type: "keydown", key: e.key });
    });
    on("keyup", (e) => this.dispatch({ type: "keyup", key: e.key }));
    on("contextmenu", (e) => {
      e.preventDefault();
      const p = pos(e), hit = m.hitTest(this.currentLayout(), p.x, p.y);
      if (hit.kind === "label" || hit.kind === "legendRow") showSeriesMenu(m, hit.seriesId, e.clientX, e.clientY, () => this.changed());
    });
  }
}

/** What `TrendChartView.onChannelDrop` receives: the payload, the drop target the chart resolved, the point in chart pixels, and the verdict fields. */
export interface ChannelDropEvent {
  /** The dragged payload as read from the data transfer. */
  payload: ChannelDragPayload;
  /** The channel ids, in drag order. */
  channelIds: number[];
  /** Where the drop lands by the chart's rules; replace it to redirect the drop. */
  target: DropTarget;
  /** Drop point in chart pixels. */ x: number; /** Drop point in chart pixels. */ y: number;
  /** Keep the channels together (one axis, one lane) as a Ctrl/Shift group; from the payload, may be changed. */
  group: boolean;
  /** Set to refuse the drop. */
  cancel: boolean;
  /** Set after adding the signals yourself (the chart then only repaints and fires `onConfigChanged`). */
  handled: boolean;
}
