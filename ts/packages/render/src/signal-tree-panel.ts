// Mori.SkyScope — DOM panel for the signal tree: search box, grouped rows, selection, and drag of channels onto a trend chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SignalTreeModel, type SignalStore, type SignalTreeRow } from "@mori/skyscope-core";
import { capture } from "./canvas2d-painter.js";
import type { TrendChartView } from "./trend-chart-view.js";

/** Options for `SignalTreePanel`. */
export interface SignalTreePanelOptions {
  /** The chart channels are dragged onto; can be attached later with `attach`. */
  chart?: TrendChartView | null | undefined;
  /** Placeholder text of the search box (default "search signals…"). */
  placeholder?: string | undefined;
  /** Characters that split channel names into groups (passed to `SignalTreeModel`); default per the model. */
  separators?: string | undefined;
}

const CSS = `
.skyscope-tree{display:flex;flex-direction:column;height:100%;min-height:0;font:12px/1.4 var(--skyscope-font-family,system-ui,sans-serif);color:var(--skyscope-color-text,#0f172a);user-select:none}
.skyscope-tree input{margin:0 0 4px;padding:4px 6px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff);color:inherit;font:inherit;outline:none}
.skyscope-tree input:focus{border-color:var(--skyscope-color-primary,#2563eb)}
.skyscope-tree-list{flex:1;min-height:0;overflow:auto;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff)}
.skyscope-tree-row{display:flex;align-items:center;gap:6px;padding:2px 6px;cursor:grab;white-space:nowrap}
.skyscope-tree-row[data-kind=group]{cursor:pointer;font-weight:600}
.skyscope-tree-row:hover{background:color-mix(in srgb,var(--skyscope-color-primary,#2563eb) 8%,transparent)}
.skyscope-tree-row[data-selected=true]{background:color-mix(in srgb,var(--skyscope-color-primary,#2563eb) 18%,transparent)}
.skyscope-tree-caret{width:10px;color:var(--skyscope-color-text-secondary,#64748b)}
.skyscope-tree-name{flex:1;overflow:hidden;text-overflow:ellipsis}
.skyscope-tree-unit,.skyscope-tree-count{color:var(--skyscope-color-text-secondary,#64748b);font-size:11px}
.skyscope-tree-dot{width:6px;height:6px;border-radius:3px;background:var(--skyscope-color-primary,#2563eb)}
.skyscope-tree-empty{padding:8px;color:var(--skyscope-color-text-secondary,#64748b)}
`;

/**
 * A searchable tree of every channel in a store (grouped by name prefix) whose rows are dragged onto a TrendChart:
 * the drop obeys the chart's rules (axis strip → shared scale, lane → own scale, time axis → new lane). Ctrl/Shift
 * while picking up selects several; a group drag keeps them together. Double-click adds a channel to the first lane.
 */
export class SignalTreePanel {
  /** The core tree model (query, expansion, selection); mutate it and call `refresh` to re-render. */
  readonly model: SignalTreeModel;
  private chart: TrendChartView | null;
  private readonly root: HTMLDivElement;
  private readonly input: HTMLInputElement;
  private readonly list: HTMLDivElement;
  private readonly timer: ReturnType<typeof setInterval>;
  private signature = "";
  private press: { x: number; y: number; channelId: number; group: boolean; rowId: string } | null = null;
  private dragging = false;

  /**
   * Builds the panel inside `element` over `store`, injects the stylesheet once per document and starts a 500 ms poll
   * that re-renders when the channel count or the chart's series change.
   */
  constructor(readonly element: HTMLElement, readonly store: SignalStore, options: SignalTreePanelOptions = {}) {
    this.model = new SignalTreeModel(store, { separators: options.separators });
    this.chart = options.chart ?? null;
    if (!document.getElementById("skyscope-tree-css")) { const st = document.createElement("style"); st.id = "skyscope-tree-css"; st.textContent = CSS; document.head.appendChild(st); }
    this.root = document.createElement("div"); this.root.className = "skyscope-tree";
    this.input = document.createElement("input"); this.input.type = "search"; this.input.placeholder = options.placeholder ?? "search signals…";
    this.input.addEventListener("input", () => { this.model.setQuery(this.input.value); this.refresh(); });
    this.list = document.createElement("div"); this.list.className = "skyscope-tree-list"; this.list.tabIndex = 0;
    this.root.append(this.input, this.list);
    element.appendChild(this.root);
    this.bind();
    this.refresh();
    this.timer = setInterval(() => { if (this.currentSignature() !== this.signature) this.refresh(); }, 500);
  }

  /** The chart rows are dropped on. */
  attach(chart: TrendChartView | null): void { this.chart = chart; this.refresh(); }

  private currentSignature(): string {
    return `${this.store.channels.size}:${this.chart?.model.config.series.map((s) => s.channelId).join(",") ?? ""}`;
  }

  /** Rebuilds the row elements from the model; rows whose channel is already in the chart get a dot. Cheap enough to call after every model change. */
  refresh(): void {
    this.signature = this.currentSignature();
    const inChart = new Set(this.chart?.model.config.series.map((s) => s.channelId) ?? []);
    const rows = this.model.rows();
    this.list.replaceChildren();
    if (rows.length === 0) { const e = document.createElement("div"); e.className = "skyscope-tree-empty"; e.textContent = this.store.channels.size === 0 ? "no signals yet" : "no match"; this.list.appendChild(e); return; }
    for (const r of rows) this.list.appendChild(this.rowElement(r, inChart));
  }

  private rowElement(r: SignalTreeRow, inChart: Set<number>): HTMLDivElement {
    const el = document.createElement("div");
    el.className = "skyscope-tree-row"; el.dataset.id = r.id; el.dataset.kind = r.kind; el.dataset.selected = String(r.selected);
    el.style.paddingLeft = `${6 + r.depth * 14}px`;
    const caret = document.createElement("span"); caret.className = "skyscope-tree-caret"; caret.textContent = r.kind === "group" ? (r.expanded ? "▾" : "▸") : "";
    const name = document.createElement("span"); name.className = "skyscope-tree-name"; name.textContent = r.name; name.title = r.name;
    el.append(caret, name);
    if (r.kind === "group") { const c = document.createElement("span"); c.className = "skyscope-tree-count"; c.textContent = String(r.count ?? 0); el.appendChild(c); }
    else {
      if (r.unit) { const u = document.createElement("span"); u.className = "skyscope-tree-unit"; u.textContent = r.unit; el.appendChild(u); }
      if (r.channelId !== undefined && inChart.has(r.channelId)) { const d = document.createElement("span"); d.className = "skyscope-tree-dot"; d.title = "in chart"; el.appendChild(d); }
    }
    return el;
  }

  private rowAt(e: Event): SignalTreeRow | null {
    const el = (e.target as HTMLElement).closest<HTMLElement>(".skyscope-tree-row");
    const id = el?.dataset.id;
    return id ? this.model.rows().find((r) => r.id === id) ?? null : null;
  }

  private bind(): void {
    const list = this.list;
    list.addEventListener("pointerdown", (e) => {
      if (e.button !== 0) return;
      const r = this.rowAt(e);
      if (!r) return;
      const group = e.ctrlKey || e.metaKey || e.shiftKey;
      if (r.kind === "group") { this.model.click(r.id, { ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey }); this.refresh(); return; }
      this.press = { x: e.clientX, y: e.clientY, channelId: r.channelId!, group, rowId: r.id };
      capture(list, e);
    });
    list.addEventListener("pointermove", (e) => {
      if (!this.press) return;
      if (this.dragging) { this.chart?.externalDragMove(e.clientX, e.clientY); return; }
      if (Math.hypot(e.clientX - this.press.x, e.clientY - this.press.y) < 4 || !this.chart) return;
      // a drag on an unselected row carries that row alone; on a selected row it carries the selection
      const ids = this.model.dragIds(this.press.channelId);
      this.dragging = true;
      this.chart.externalDragStart(ids, this.press.group || ids.length > 1);
      this.chart.externalDragMove(e.clientX, e.clientY);
    });
    const finish = (e: PointerEvent, drop: boolean): void => {
      const press = this.press; this.press = null;
      if (!press) return;
      if (this.dragging) { this.dragging = false; if (drop) this.chart?.externalDrop(e.clientX, e.clientY); else this.chart?.externalDragCancel(); }
      else if (drop) this.model.click(press.rowId, { ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey });
      this.refresh();
    };
    list.addEventListener("pointerup", (e) => finish(e, true));
    list.addEventListener("pointercancel", (e) => finish(e, false));
    list.addEventListener("dblclick", (e) => {
      const r = this.rowAt(e);
      if (!r || r.kind !== "channel" || !this.chart) return;
      const m = this.chart.model;
      const s = m.addSeriesForChannel(r.channelId!);
      if (s) { m.applyGroupDrop([s.id], { kind: "ownAxis", laneId: m.lanes()[0]!.id }, false); this.chart.onConfigChanged?.(); this.refresh(); }
    });
    list.addEventListener("keydown", (e) => { if (e.key === "Escape") { this.model.clearSelection(); this.refresh(); } });
  }

  /** Stops the poll and removes the panel from the DOM; the host `element` itself is left in place. */
  dispose(): void { clearInterval(this.timer); this.root.remove(); }
}
