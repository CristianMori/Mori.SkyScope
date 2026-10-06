// Mori.SkyScope — The signal tree beside a chart (ibaAnalyzer's signal tree): every channel of a store, grouped by the prefix of its name (amr-1/pose/x → g…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { ChannelInfo, ChannelKind } from "../sources/contracts.js";
import type { SignalStore } from "../sources/signal-store.js";

/**
 * The signal tree beside a chart (ibaAnalyzer's signal tree): every channel of a store, grouped by the prefix of its
 * name (`amr-1/pose/x` → group `amr-1/pose`, leaf `x`; `motor.speed` → group `motor`), searchable, with a selection
 * that is dragged onto the chart. Ctrl toggles, Shift selects a range over the visible rows; plain click selects one.
 * Pure state: identical in TS and C#, pinned by `spec/fixtures/signal-tree.json`.
 */
export interface SignalTreeRow {
  /** A folder row or a channel leaf. */
  kind: "group" | "channel";
  /** `g:<prefix>` for groups, `c:<channelId>` for channels. */
  id: string;
  /** Display text: the prefix for groups, the leaf part for channels inside a group, the full name for ungrouped channels. */
  name: string;
  /** Indent level: 0 for groups and ungrouped channels, 1 for channels inside a group. */
  depth: number;
  /** Groups: open or folded. */
  expanded?: boolean;
  /** Groups: channels inside (after the search filter). */
  count?: number;
  /** Channels: the store channel id. */
  channelId?: number;
  /** Channels: engineering unit, when the channel declares one. */
  unit?: string | undefined;
  /** Channels: analog or digital. */
  channelKind?: ChannelKind | undefined;
  /** Channels: part of the selection. Groups: every channel of the group is selected. */
  selected: boolean;
}

/** Construction options for `SignalTreeModel`. */
export interface SignalTreeOptions { /** Characters that split a channel name into group and leaf (default "/.:"); the last occurrence cuts. */ separators?: string | undefined }

/** Grouping, search, fold and selection state of the signal tree; `rows()` reads the store on every call, so it is never stale. */
export class SignalTreeModel {
  /** Current search text, matched case-insensitively against channel names and units; non-empty opens every group. */
  query = "";
  /** Selected channel ids, mutated in place by `click` and `clearSelection`. */
  readonly selection = new Set<number>();
  private readonly folded = new Set<string>();
  private anchor: number | null = null;
  /** Separator characters in use (see `SignalTreeOptions.separators`). */
  readonly separators: string;

  /** Creates a tree over `store`; the store is kept by reference, not copied. */
  constructor(readonly store: SignalStore, options: SignalTreeOptions = {}) { this.separators = options.separators ?? "/.:"; }

  /** Group prefix and leaf name of a channel name. */
  split(name: string): { group: string; leaf: string } {
    let cut = -1;
    for (let i = name.length - 1; i >= 0; i--) if (this.separators.includes(name[i]!)) { cut = i; break; }
    return cut < 0 ? { group: "", leaf: name } : { group: name.slice(0, cut), leaf: name.slice(cut + 1) };
  }

  private matches(c: ChannelInfo): boolean {
    const q = this.query.trim().toLowerCase();
    return q === "" || c.name.toLowerCase().includes(q) || (c.unit?.toLowerCase().includes(q) ?? false);
  }

  /** Visible rows top to bottom: ungrouped channels first, then groups (sorted) with their channels (sorted) when open. */
  rows(): SignalTreeRow[] {
    const channels = [...this.store.channels.values()].map((c) => c.info).filter((c) => this.matches(c));
    const groups = new Map<string, ChannelInfo[]>();
    for (const c of channels) { const g = this.split(c.name).group; (groups.get(g) ?? groups.set(g, []).get(g)!).push(c); }
    const searching = this.query.trim() !== "";
    const out: SignalTreeRow[] = [];
    const push = (c: ChannelInfo, depth: number): void => { out.push({ kind: "channel", id: `c:${c.id}`, name: depth === 0 ? c.name : this.split(c.name).leaf, depth, channelId: c.id, unit: c.unit, channelKind: c.kind, selected: this.selection.has(c.id) }); };
    const byName = (a: ChannelInfo, b: ChannelInfo): number => a.name < b.name ? -1 : a.name > b.name ? 1 : a.id - b.id;
    for (const c of (groups.get("") ?? []).sort(byName)) push(c, 0);
    for (const g of [...groups.keys()].filter((k) => k !== "").sort()) {
      const list = groups.get(g)!.sort(byName);
      const expanded = searching || !this.folded.has(g);
      out.push({ kind: "group", id: `g:${g}`, name: g, depth: 0, expanded, count: list.length, selected: list.every((c) => this.selection.has(c.id)) });
      if (expanded) for (const c of list) push(c, 1);
    }
    return out;
  }

  /** Replaces the search text. */
  setQuery(q: string): void { this.query = q; }
  /** Folds an open group or opens a folded one; folds are overridden while a search is active. */
  toggleGroup(prefix: string): void { if (this.folded.has(prefix)) this.folded.delete(prefix); else this.folded.add(prefix); }
  /** True when the group is folded, regardless of the search override. */
  isFolded(prefix: string): boolean { return this.folded.has(prefix); }

  /**
   * Click on a row. Channels: plain = only this one, Ctrl = toggle, Shift = range from the last plain/ctrl click over the
   * visible channel rows. Groups: plain toggles the fold, Ctrl adds/removes all its channels, Shift selects them.
   */
  click(rowId: string, modifiers: { ctrl?: boolean | undefined; shift?: boolean | undefined } = {}): void {
    const rows = this.rows();
    const row = rows.find((r) => r.id === rowId);
    if (!row) return;
    if (row.kind === "group") {
      const prefix = row.id.slice(2);
      const all = [...this.store.channels.values()].map((c) => c.info).filter((c) => this.matches(c) && this.split(c.name).group === prefix).map((c) => c.id);
      if (modifiers.ctrl) { const every = all.every((id) => this.selection.has(id)); for (const id of all) { if (every) this.selection.delete(id); else this.selection.add(id); } }
      else if (modifiers.shift) { for (const id of all) this.selection.add(id); }
      else this.toggleGroup(prefix);
      return;
    }
    const id = row.channelId!;
    if (modifiers.shift && this.anchor !== null) {
      const visible = rows.filter((r) => r.kind === "channel").map((r) => r.channelId!);
      const a = visible.indexOf(this.anchor), b = visible.indexOf(id);
      if (a >= 0 && b >= 0) { if (!modifiers.ctrl) this.selection.clear(); for (let i = Math.min(a, b); i <= Math.max(a, b); i++) this.selection.add(visible[i]!); return; }
    }
    if (modifiers.ctrl) { if (this.selection.has(id)) this.selection.delete(id); else this.selection.add(id); }
    else { this.selection.clear(); this.selection.add(id); }
    this.anchor = id;
  }

  /** What a drag starting on `channelId` carries: the selection when it is part of it, else that channel alone. */
  dragIds(channelId: number): number[] {
    if (this.selection.has(channelId)) {
      const order = this.rows().filter((r) => r.kind === "channel").map((r) => r.channelId!);
      return order.filter((id) => this.selection.has(id));
    }
    return [channelId];
  }
  /** Empties the selection and forgets the shift-range anchor. */
  clearSelection(): void { this.selection.clear(); this.anchor = null; }
}
