// Mori.SkyScope — DOM panel that edits a trend chart's structure at runtime: lanes, axes, series, thresholds and markers as a tree, a property form for the selected item, and the chart-level settings.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { DARK_THEME, LIGHT_THEME, type EditorRow, type LegendPosition, type TimeFormat } from "@mori/skyscope-core";
import type { TrendChartView } from "./trend-chart-view.js";

/** Options for `ChartEditorPanel`. */
export interface ChartEditorPanelOptions {
  /** The chart to edit; can be attached later with `attach`. */
  chart?: TrendChartView | null | undefined;
  /** Hide the chart-level settings section (time span, legend, panels). */
  hideChartSettings?: boolean | undefined;
}

const CSS = `
.skyscope-editor{display:flex;flex-direction:column;height:100%;min-height:0;font:12px/1.4 var(--skyscope-font-family,system-ui,sans-serif);color:var(--skyscope-color-text,#0f172a);user-select:none}
.skyscope-editor-bar{display:flex;flex-wrap:wrap;gap:3px;margin-bottom:4px}
.skyscope-editor-bar button,.skyscope-editor-form button{padding:2px 7px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff);color:inherit;font:inherit;cursor:pointer}
.skyscope-editor-bar button:disabled{opacity:.4;cursor:default}
.skyscope-editor-rows{flex:1;min-height:0;overflow:auto;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff)}
.skyscope-editor-row{display:flex;align-items:center;gap:6px;padding:2px 6px;cursor:pointer;white-space:nowrap}
.skyscope-editor-row:hover{background:color-mix(in srgb,var(--skyscope-color-primary,#2563eb) 8%,transparent)}
.skyscope-editor-row[data-selected=true]{background:color-mix(in srgb,var(--skyscope-color-primary,#2563eb) 18%,transparent)}
.skyscope-editor-row[data-kind=lane]{font-weight:600}
.skyscope-editor-kind{width:14px;color:var(--skyscope-color-text-secondary,#64748b);font-size:11px;text-align:center}
.skyscope-editor-label{flex:1;overflow:hidden;text-overflow:ellipsis}
.skyscope-editor-detail{color:var(--skyscope-color-text-secondary,#64748b);font-size:11px;overflow:hidden;text-overflow:ellipsis;max-width:45%}
.skyscope-editor-form{display:grid;grid-template-columns:auto 1fr;gap:4px 8px;align-items:center;padding:6px;margin-top:4px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff)}
.skyscope-editor-form h4{grid-column:1/3;margin:0 0 2px;font-size:12px;color:var(--skyscope-color-text-secondary,#64748b);font-weight:600}
.skyscope-editor-form label{color:var(--skyscope-color-text-secondary,#64748b)}
.skyscope-editor-form input,.skyscope-editor-form select{width:100%;box-sizing:border-box;padding:2px 4px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:3px;background:var(--skyscope-color-bg,#fff);color:inherit;font:inherit}
.skyscope-editor-form input[type=checkbox]{width:auto;justify-self:start}
.skyscope-editor-form input[type=color]{padding:0;height:22px}
.skyscope-editor-empty{padding:8px;color:var(--skyscope-color-text-secondary,#64748b)}
`;

const KIND_GLYPH: Record<EditorRow["kind"], string> = { lane: "▤", axis: "┃", stack: "▦", series: "〜", threshold: "―", marker: "│" };
const LEGENDS: LegendPosition[] = ["top-left", "top-right", "bottom-left", "bottom-right", "right", "top", "none"];

/**
 * A runtime editor for a trend chart: the structure as a tree (lanes → axes → series, the logic stack, unused axes,
 * thresholds, markers), buttons to add and remove items and to move lanes, and a property form for the selected item
 * (or the chart's own settings when nothing is selected). Every change goes through the model's editor commands and
 * fires the chart's `onConfigChanged`, so it round-trips with layout files and the signal tree.
 */
export class ChartEditorPanel {
  private chart: TrendChartView | null;
  private readonly root: HTMLDivElement;
  private readonly bar: HTMLDivElement;
  private readonly rows: HTMLDivElement;
  private readonly form: HTMLDivElement;
  private readonly timer: ReturnType<typeof setInterval>;
  private unlisten: (() => void) | null = null;
  private signature = "";
  private selected: { kind: EditorRow["kind"]; id: string } | null = null;
  private readonly hideChart: boolean;
  /** Undo history: configuration snapshots before each change (from the panel or from a gesture on the chart), and the redo side. */
  private readonly history: { undo: string[]; redo: string[]; last: string | null } = { undo: [], redo: [], last: null };
  /** How many undo steps are kept. */
  historyLimit = 100;

  /** Builds the panel inside `element`, injects the stylesheet once per document and follows the chart's configuration changes. */
  constructor(readonly element: HTMLElement, options: ChartEditorPanelOptions = {}) {
    this.chart = null;
    this.hideChart = options.hideChartSettings === true;
    if (!document.getElementById("skyscope-editor-css")) { const st = document.createElement("style"); st.id = "skyscope-editor-css"; st.textContent = CSS; document.head.appendChild(st); }
    this.root = document.createElement("div"); this.root.className = "skyscope-editor";
    this.bar = document.createElement("div"); this.bar.className = "skyscope-editor-bar";
    this.rows = document.createElement("div"); this.rows.className = "skyscope-editor-rows";
    this.form = document.createElement("div"); this.form.className = "skyscope-editor-form";
    this.root.append(this.bar, this.rows, this.form);
    element.appendChild(this.root);
    this.rows.tabIndex = 0;
    this.rows.addEventListener("keydown", (e) => this.onKey(e));
    this.form.addEventListener("keydown", (e) => { if ((e.ctrlKey || e.metaKey) && (e.key === "z" || e.key === "y") && !(e.target instanceof HTMLInputElement && e.target.type === "text")) this.onKey(e); });
    this.rows.addEventListener("click", (e) => {
      const el = (e.target as HTMLElement).closest<HTMLElement>(".skyscope-editor-row");
      if (!el) { this.selected = null; this.refresh(); return; }
      const kind = el.dataset.kind as EditorRow["kind"], id = el.dataset.id!;
      this.selected = this.selected?.id === id && this.selected.kind === kind ? null : { kind, id };
      this.refresh();
    });
    this.attach(options.chart ?? null);
    this.timer = setInterval(() => { if (this.currentSignature() !== this.signature) this.refresh(); }, 500);
  }

  /** The chart this panel edits (null detaches); the undo history starts afresh. */
  attach(chart: TrendChartView | null): void {
    this.unlisten?.(); this.unlisten = null;
    this.chart = chart;
    this.history.undo = []; this.history.redo = []; this.history.last = chart ? chart.model.snapshotConfig() : null;
    if (chart) this.unlisten = chart.addConfigListener(() => { this.recordChange(); this.refresh(); });
    this.selected = null;
    this.refresh();
  }

  /** After any configuration change: the previous state goes on the undo stack (a restore lands on the state it restored, so nothing is pushed then). */
  private recordChange(): void {
    const m = this.chart?.model; if (!m) return;
    const cur = m.snapshotConfig();
    if (cur === this.history.last) return;
    if (this.history.last !== null) { this.history.undo.push(this.history.last); if (this.history.undo.length > this.historyLimit) this.history.undo.shift(); }
    this.history.redo = [];
    this.history.last = cur;
  }
  /** Undo the last change (panel or gesture); no-op without history. */
  undo(): void { const prev = this.history.undo.pop(); if (prev === undefined || !this.chart) return; if (this.history.last !== null) this.history.redo.push(this.history.last); this.history.last = prev; this.chart.model.restoreConfig(prev); this.chart.notifyConfigChanged(); }
  /** Redo the last undone change. */
  redo(): void { const next = this.history.redo.pop(); if (next === undefined || !this.chart) return; if (this.history.last !== null) this.history.undo.push(this.history.last); this.history.last = next; this.chart.model.restoreConfig(next); this.chart.notifyConfigChanged(); }
  /** True when `undo` has something to undo. */
  get canUndo(): boolean { return this.history.undo.length > 0; }
  /** True when `redo` has something to redo. */
  get canRedo(): boolean { return this.history.redo.length > 0; }

  /** Keyboard: arrows move the selection, Delete removes, Ctrl+Z / Ctrl+Y undo and redo, Escape clears the selection. */
  private onKey(e: KeyboardEvent): void {
    const m = this.chart?.model; if (!m) return;
    if ((e.ctrlKey || e.metaKey) && e.key === "z") { e.preventDefault(); this.undo(); return; }
    if ((e.ctrlKey || e.metaKey) && e.key === "y") { e.preventDefault(); this.redo(); return; }
    if (e.target !== this.rows) return;
    const rows = m.editorRows();
    const i = this.selected ? rows.findIndex((r) => r.id === this.selected!.id && r.kind === this.selected!.kind) : -1;
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      const next = rows[Math.max(0, Math.min(rows.length - 1, i + (e.key === "ArrowDown" ? 1 : -1)))];
      if (next) { this.selected = { kind: next.kind, id: next.id }; this.refresh(); this.rows.querySelector<HTMLElement>("[data-selected=true]")?.scrollIntoView({ block: "nearest" }); }
    } else if (e.key === "Delete" || e.key === "Backspace") { e.preventDefault(); this.removeSelected(); }
    else if (e.key === "Escape") { this.selected = null; this.refresh(); }
  }

  /** Removes the selected item (the toolbar's remove button and the Delete key).*/
  private removeSelected(): void {
    const m = this.chart?.model, sel = this.selected; if (!m || !sel || sel.kind === "stack") return;
    if (sel.kind === "lane") m.removeLane(sel.id);
    else if (sel.kind === "axis") m.removeAxis(sel.id);
    else if (sel.kind === "series") m.removeSeries(sel.id);
    else if (sel.kind === "threshold") m.removeThreshold(sel.id);
    else if (sel.kind === "marker") m.removeMarker(sel.id);
    this.selected = null; this.changed();
  }

  /** The series row before or after the selected one under the same axis or stack, for reordering. */
  private seriesNeighbour(delta: -1 | 1): EditorRow | null {
    const m = this.chart?.model, sel = this.selected; if (!m || !sel || sel.kind !== "series") return null;
    const rows = m.editorRows();
    const me = rows.find((r) => r.kind === "series" && r.id === sel.id); if (!me) return null;
    const siblings = rows.filter((r) => r.kind === "series" && r.parentId === me.parentId);
    return siblings[siblings.indexOf(me) + delta] ?? null;
  }

  private currentSignature(): string {
    const m = this.chart?.model;
    return m ? JSON.stringify(m.editorRows()) + m.store.channels.size : "";
  }

  /** Applies a command's result: repaints the chart, notifies its listeners and re-renders the panel. */
  private changed(): void { this.chart?.notifyConfigChanged(); this.refresh(); }

  /** Rebuilds the toolbar, the rows and the form from the model. */
  refresh(): void {
    this.signature = this.currentSignature();
    this.renderBar();
    this.renderRows();
    this.renderForm();
  }

  private renderBar(): void {
    const m = this.chart?.model;
    this.bar.replaceChildren();
    if (!m) return;
    const sel = this.selected;
    const laneId = sel?.kind === "lane" ? sel.id : sel ? m.editorRows().find((r) => r.id === sel.id && r.kind === sel.kind)?.parentId ?? null : null;
    const laneOf = (id: string | null): string | null => { if (!id) return null; const row = m.editorRows().find((r) => r.id === id); return !row ? null : row.kind === "lane" ? row.id : laneOf(row.parentId); };
    const targetLane = sel?.kind === "lane" ? sel.id : laneOf(laneId);
    const btn = (label: string, title: string, enabled: boolean, onClick: () => void): void => { const b = document.createElement("button"); b.textContent = label; b.title = title; b.disabled = !enabled; b.addEventListener("click", onClick); this.bar.appendChild(b); };
    btn("+ lane", "add a lane at the end", true, () => { const id = m.addLane(); this.selected = { kind: "lane", id }; this.changed(); });
    btn("+ signal", "add a channel of the store to the selected lane (or the first)", m.store.channels.size > 0, () => this.pickChannel(targetLane));
    btn("+ axis", "add an axis definition (assign it to a series afterwards)", true, () => { const id = m.addAxis(); this.selected = { kind: "axis", id }; this.changed(); });
    btn("+ threshold", "add a threshold on the selected axis (or the first axis)", true, () => {
      const axisId = sel?.kind === "axis" ? sel.id : sel?.kind === "series" ? m.axisIdOf(m.config.series.find((s) => s.id === sel.id)!) : m.axesIn(m.lanes()[0]!.id)[0]?.id ?? `axis:${m.lanes()[0]!.id}`;
      const id = m.addThreshold(axisId.startsWith("stack:") ? `axis:${m.lanes()[0]!.id}` : axisId, 0); this.selected = { kind: "threshold", id }; this.changed();
    });
    btn("+ marker", "add an event marker at the right edge of the window", true, () => { const id = m.addMarker(m.window().t1); this.selected = { kind: "marker", id }; this.changed(); });
    const laneIndex = sel?.kind === "lane" ? m.lanes().findIndex((l) => l.id === sel.id) : -1;
    const up = this.seriesNeighbour(-1), down = this.seriesNeighbour(1);
    const reorder = (to: EditorRow): void => { m.reorderSeries(sel!.id, m.config.series.findIndex((s) => s.id === to.id)); this.changed(); };
    btn("▲", "move the lane or the signal up", laneIndex > 0 || !!up, () => { if (sel?.kind === "lane") m.moveLane(sel.id, laneIndex - 1); else if (up) { reorder(up); return; } this.changed(); });
    btn("▼", "move the lane or the signal down", (laneIndex >= 0 && laneIndex < m.lanes().length - 1) || !!down, () => { if (sel?.kind === "lane") m.moveLane(sel.id, laneIndex + 2); else if (down) { reorder(down); return; } this.changed(); });
    btn("remove", "remove the selected item (Delete)", !!sel && sel.kind !== "stack", () => this.removeSelected());
    btn("undo", "undo the last change (Ctrl+Z)", this.canUndo, () => this.undo());
    btn("redo", "redo (Ctrl+Y)", this.canRedo, () => this.redo());
  }

  /** A small inline chooser of store channels not yet in the chart; the pick is added to `laneId` (own axis, or the logic stack for digital). */
  private pickChannel(laneId: string | null): void {
    const m = this.chart!.model;
    const inChart = new Set(m.config.series.map((s) => s.channelId));
    const options = [...m.store.channels.values()].map((c) => c.info).filter((i) => !inChart.has(i.id)).sort((a, b) => a.name.localeCompare(b.name));
    this.form.replaceChildren();
    const h = document.createElement("h4"); h.textContent = `add a signal to ${laneId ?? m.lanes()[0]!.id}`; this.form.appendChild(h);
    const label = document.createElement("label"); label.textContent = "channel";
    const select = document.createElement("select");
    for (const i of options) { const o = document.createElement("option"); o.value = String(i.id); o.textContent = `${i.name}${i.unit ? ` [${i.unit}]` : ""}${i.kind === "digital" ? " (digital)" : ""}`; select.appendChild(o); }
    if (options.length === 0) { const o = document.createElement("option"); o.textContent = "every channel is in the chart"; o.disabled = true; select.appendChild(o); }
    const add = document.createElement("button"); add.textContent = "add"; add.disabled = options.length === 0;
    add.addEventListener("click", () => { const ids = m.addChannels([Number(select.value)], { kind: "ownAxis", laneId: laneId ?? m.lanes()[0]!.id }); if (ids[0]) this.selected = { kind: "series", id: ids[0] }; this.changed(); });
    const cancel = document.createElement("button"); cancel.textContent = "cancel"; cancel.addEventListener("click", () => this.refresh());
    this.form.append(label, select, document.createElement("span"), add, document.createElement("span"), cancel);
  }

  private renderRows(): void {
    const m = this.chart?.model;
    this.rows.replaceChildren();
    if (!m) { const e = document.createElement("div"); e.className = "skyscope-editor-empty"; e.textContent = "no chart attached"; this.rows.appendChild(e); return; }
    const list = m.editorRows();
    if (this.selected && !list.some((r) => r.id === this.selected!.id && r.kind === this.selected!.kind)) this.selected = null;
    for (const r of list) {
      const el = document.createElement("div");
      el.className = "skyscope-editor-row"; el.dataset.kind = r.kind; el.dataset.id = r.id; el.dataset.selected = String(this.selected?.id === r.id && this.selected.kind === r.kind);
      el.style.paddingLeft = `${6 + r.depth * 14}px`;
      const k = document.createElement("span"); k.className = "skyscope-editor-kind"; k.textContent = KIND_GLYPH[r.kind]; k.title = r.kind;
      const l = document.createElement("span"); l.className = "skyscope-editor-label"; l.textContent = r.label; l.title = `${r.kind} ${r.id}`;
      const d = document.createElement("span"); d.className = "skyscope-editor-detail"; d.textContent = r.detail;
      el.append(k, l, d);
      this.rows.appendChild(el);
    }
  }

  // ---- the property form -------------------------------------------------------------
  private field(label: string, input: HTMLElement): void { const l = document.createElement("label"); l.textContent = label; this.form.append(l, input); }
  private text(value: string | undefined | null, onChange: (v: string | null) => void, placeholder = ""): HTMLInputElement {
    const i = document.createElement("input"); i.type = "text"; i.value = value ?? ""; i.placeholder = placeholder;
    i.addEventListener("change", () => onChange(i.value.trim() === "" ? null : i.value)); return i;
  }
  private number(value: number | undefined | null, onChange: (v: number | null) => void, placeholder = "", step = "any"): HTMLInputElement {
    const i = document.createElement("input"); i.type = "number"; i.step = step; i.value = value === undefined || value === null ? "" : String(value); i.placeholder = placeholder;
    i.addEventListener("change", () => { const v = i.value.trim() === "" ? null : Number(i.value); onChange(v !== null && Number.isFinite(v) ? v : null); }); return i;
  }
  private check(value: boolean, onChange: (v: boolean) => void): HTMLInputElement {
    const i = document.createElement("input"); i.type = "checkbox"; i.checked = value; i.addEventListener("change", () => onChange(i.checked)); return i;
  }
  private select(value: string, options: { value: string; label: string }[], onChange: (v: string) => void): HTMLSelectElement {
    const s = document.createElement("select");
    for (const o of options) { const e = document.createElement("option"); e.value = o.value; e.textContent = o.label; e.selected = o.value === value; s.appendChild(e); }
    s.addEventListener("change", () => onChange(s.value)); return s;
  }
  private color(value: string | undefined | null, fallback: string, onChange: (v: string | null) => void): HTMLElement {
    const wrap = document.createElement("div"); wrap.style.display = "flex"; wrap.style.gap = "4px";
    const i = document.createElement("input"); i.type = "color"; i.value = toHex(value ?? fallback); i.style.width = "40px";
    i.addEventListener("change", () => onChange(i.value));
    const clear = document.createElement("button"); clear.textContent = "default"; clear.title = "back to the palette or theme colour"; clear.addEventListener("click", () => onChange(null));
    wrap.append(i, clear); return wrap;
  }
  private heading(text: string): void { const h = document.createElement("h4"); h.textContent = text; this.form.appendChild(h); }

  private renderForm(): void {
    const chart = this.chart, m = chart?.model;
    this.form.replaceChildren();
    if (!chart || !m) return;
    const sel = this.selected;
    const apply = (ok: boolean): void => { if (ok) this.changed(); };
    if (!sel) {
      if (this.hideChart) return;
      const c = m.config;
      this.heading("chart");
      this.field("time span (s)", this.number(c.timeSpan, (v) => { if (v !== null && v > 0) { chart.setConfig({ timeSpan: v }); this.changed(); } }));
      this.field("time format", this.select(c.timeFormat, [{ value: "utc", label: "wall clock (UTC)" }, { value: "relative", label: "seconds back" }], (v) => { chart.setConfig({ timeFormat: v as TimeFormat }); this.changed(); }));
      this.field("legend", this.select(c.legend, LEGENDS.map((l) => ({ value: l, label: l })), (v) => { chart.setConfig({ legend: v as LegendPosition }); this.changed(); }));
      this.field("signal labels", this.check(c.plotLabels, (v) => { chart.setConfig({ plotLabels: v }); this.changed(); }));
      this.field("lane headers", this.check(c.laneHeaders, (v) => { chart.setConfig({ laneHeaders: v }); this.changed(); }));
      this.field("navigator", this.check(c.navigator, (v) => { chart.setConfig({ navigator: v }); this.changed(); }));
      this.field("measurements", this.check(c.measurePanel, (v) => { chart.setConfig({ measurePanel: v }); this.changed(); }));
      this.heading("look");
      const preset = c.theme.background === DARK_THEME.background ? "dark" : c.theme.background === LIGHT_THEME.background ? "light" : "custom";
      this.field("theme", this.select(preset, [{ value: "light", label: "light" }, { value: "dark", label: "dark" }, ...(preset === "custom" ? [{ value: "custom", label: "custom" }] : [])], (v) => { if (v === "light" || v === "dark") { chart.setConfig({ theme: v === "dark" ? DARK_THEME : LIGHT_THEME }); this.changed(); } }));
      this.field("signal width", this.number(c.style.seriesWidth, (v) => { if (v !== null && v > 0) { chart.setConfig({ style: { seriesWidth: v } }); this.changed(); } }, "1.5", "0.5"));
      this.field("font size", this.number(c.theme.fontSize, (v) => { if (v !== null && v >= 6) { chart.setConfig({ theme: { fontSize: v } }); this.changed(); } }, "12", "1"));
      this.field("value grid", this.check(c.style.showValueGrid, (v) => { chart.setConfig({ style: { showValueGrid: v } }); this.changed(); }));
      this.field("time grid", this.check(c.style.showTimeGrid, (v) => { chart.setConfig({ style: { showTimeGrid: v } }); this.changed(); }));
      return;
    }
    if (sel.kind === "lane") {
      const lane = m.lanes().find((l) => l.id === sel.id); if (!lane) return;
      this.heading(`lane ${lane.id}`);
      this.field("label", this.text(lane.label, (v) => apply(m.updateLane(lane.id, { label: v })), lane.id));
      this.field("weight", this.number(lane.weight ?? 1, (v) => apply(m.updateLane(lane.id, { weight: v })), "1", "0.1"));
      this.field("folded", this.check(lane.collapsed === true, (v) => apply(m.updateLane(lane.id, { collapsed: v }))));
      this.field("keep when empty", this.check(lane.keep === true, (v) => apply(m.updateLane(lane.id, { keep: v }))));
      return;
    }
    if (sel.kind === "axis") {
      const a = m.axis(sel.id);
      this.heading(`axis ${a.id}`);
      this.field("label", this.text(a.label, (v) => apply(m.updateAxis(a.id, { label: v })), a.id));
      this.field("unit", this.text(a.unit, (v) => apply(m.updateAxis(a.id, { unit: v }))));
      this.field("min", this.number(a.min, (v) => apply(m.updateAxis(a.id, { min: v })), "auto"));
      this.field("max", this.number(a.max, (v) => apply(m.updateAxis(a.id, { max: v })), "auto"));
      this.field("side", this.select(a.side ?? "left", [{ value: "left", label: "left" }, { value: "right", label: "right" }], (v) => apply(m.updateAxis(a.id, { side: v as "left" | "right" }))));
      this.field("colour", this.color(a.color, m.config.theme.axis, (v) => apply(m.updateAxis(a.id, { color: v }))));
      return;
    }
    if (sel.kind === "series") {
      const s = m.config.series.find((x) => x.id === sel.id); if (!s) return;
      this.heading(`signal ${s.id} (channel ${s.channelId})`);
      this.field("name", this.text(s.name, (v) => apply(m.updateSeries(s.id, { name: v })), m.seriesName({ ...s, name: undefined })));
      this.field("lane", this.select(m.laneIdOf(s), m.lanes().map((l) => ({ value: l.id, label: l.label ?? l.id })), (v) => apply(m.updateSeries(s.id, { laneId: v }))));
      if (s.kind !== "digital") {
        const laneId = m.laneIdOf(s), defaultAxis = `axis:${laneId}`;
        const axes = [{ value: defaultAxis, label: "lane default" }, ...m.config.axes.filter((a) => a.id !== defaultAxis).map((a) => ({ value: a.id, label: a.label ?? a.id })), { value: "__new__", label: "new axis…" }];
        this.field("axis", this.select(m.axisIdOf(s), axes, (v) => { if (v === "__new__") { const id = m.addAxis(); m.updateSeries(s.id, { axisId: id }); this.selected = { kind: "axis", id }; this.changed(); } else apply(m.updateSeries(s.id, { axisId: v })); }));
      }
      this.field("digital", this.check(s.kind === "digital", (v) => apply(m.updateSeries(s.id, { kind: v ? "digital" : "analog" }))));
      this.field("visible", this.check(s.visible !== false, (v) => apply(m.updateSeries(s.id, { visible: v }))));
      this.field("colour", this.color(s.color, m.seriesColor(s), (v) => apply(m.updateSeries(s.id, { color: v }))));
      this.field("width", this.number(s.width, (v) => apply(m.updateSeries(s.id, { width: v })), String(m.config.style.seriesWidth), "0.5"));
      return;
    }
    if (sel.kind === "stack") { this.heading("logic stack"); const p = document.createElement("span"); p.textContent = "digital signals of this lane; select one to edit it"; p.style.gridColumn = "1/3"; this.form.appendChild(p); return; }
    if (sel.kind === "threshold") {
      const t = m.config.thresholds.find((x) => x.id === sel.id); if (!t) return;
      this.heading(`threshold ${t.id}`);
      const axes = this.allAxisIds(m).map((id) => ({ value: id, label: m.axis(id).label ?? id }));
      if (!axes.some((a) => a.value === t.axisId)) axes.unshift({ value: t.axisId, label: t.axisId });
      this.field("axis", this.select(t.axisId, axes, (v) => apply(m.updateThreshold(t.id, { axisId: v }))));
      this.field("from", this.number(t.from, (v) => apply(m.updateThreshold(t.id, { from: v }))));
      this.field("to", this.number(t.to, (v) => apply(m.updateThreshold(t.id, { to: v })), "line"));
      this.field("label", this.text(t.label, (v) => apply(m.updateThreshold(t.id, { label: v }))));
      this.field("colour", this.color(t.color, t.color, (v) => apply(m.updateThreshold(t.id, { color: v ?? "#dc2626" }))));
      return;
    }
    if (sel.kind === "marker") {
      const mk = m.config.markers.find((x) => x.id === sel.id); if (!mk) return;
      this.heading(`marker ${mk.id}`);
      this.field("time (s)", this.number(mk.time, (v) => apply(m.updateMarker(mk.id, { time: v }))));
      this.field("label", this.text(mk.label, (v) => apply(m.updateMarker(mk.id, { label: v }))));
      this.field("colour", this.color(mk.color, m.config.theme.marker, (v) => apply(m.updateMarker(mk.id, { color: v }))));
    }
  }

  /** Every axis id a threshold may use: each lane's default axis plus the defined axes. */
  private allAxisIds(m: TrendChartView["model"]): string[] {
    const ids = m.lanes().map((l) => `axis:${l.id}`);
    for (const a of m.config.axes) if (!ids.includes(a.id)) ids.push(a.id);
    return ids;
  }

  /** Stops following the chart and removes the panel from the DOM; the host `element` itself is left in place. */
  dispose(): void { clearInterval(this.timer); this.unlisten?.(); this.root.remove(); }
}

/** A CSS colour as the six-digit hex an `<input type=color>` accepts; unknown forms fall back to black. */
function toHex(css: string): string {
  if (/^#[0-9a-f]{6}$/i.test(css)) return css.toLowerCase();
  if (/^#[0-9a-f]{3}$/i.test(css)) return `#${css[1]}${css[1]}${css[2]}${css[2]}${css[3]}${css[3]}`.toLowerCase();
  const m = /^rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)/.exec(css);
  if (m) return `#${[m[1], m[2], m[3]].map((v) => Number(v).toString(16).padStart(2, "0")).join("")}`;
  return "#000000";
}
