// Mori.SkyScope — Fixture driver for the signal tree: channels in, rows / selection / drag payloads out.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { FixtureDriver } from "./drivers.js";
import { SignalStore } from "../sources/signal-store.js";
import type { ChannelInfo } from "../sources/contracts.js";
import { SignalTreeModel } from "../charts/signal-tree.js";

interface State { model: SignalTreeModel; queries: unknown[] }

/** Fixture driver for the signal tree: channels in, rows / selection / drag payloads out. C# mirror: `SignalTreeDriver`. */
export const signalTreeDriver: FixtureDriver<State> = {
  component: "signal-tree",
  create(setup) {
    const store = new SignalStore({ retentionSeconds: 10 });
    for (const c of (setup.channels as ChannelInfo[] | undefined) ?? []) store.declareChannel(c);
    const model = new SignalTreeModel(store, { separators: setup.separators as string | undefined });
    return { model, queries: [] };
  },
  /**
   * Steps: setQuery, toggleGroup, click (with ctrl and shift), clearSelection, declare; queries: rows, selection
   * (sorted ids), dragIds, split.
   */
  step(s, step) {
    const m = s.model;
    switch (step.type) {
      case "setQuery": m.setQuery(step.query as string); break;
      case "toggleGroup": m.toggleGroup(step.prefix as string); break;
      case "click": m.click(step.rowId as string, { ctrl: step.ctrl as boolean | undefined, shift: step.shift as boolean | undefined }); break;
      case "clearSelection": m.clearSelection(); break;
      case "declare": m.store.declareChannel(step.channel as ChannelInfo); break;
      case "query":
        if ("rows" in step) s.queries.push(m.rows().map((r) => ({ kind: r.kind, id: r.id, name: r.name, depth: r.depth, expanded: r.expanded ?? null, count: r.count ?? null, channelId: r.channelId ?? null, unit: r.unit ?? null, channelKind: r.channelKind ?? null, selected: r.selected })));
        else if ("selection" in step) s.queries.push([...m.selection].sort((a, b) => a - b));
        else if ("dragIds" in step) s.queries.push(m.dragIds(step.dragIds as number));
        else if ("split" in step) s.queries.push(m.split(step.split as string));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot(s) { return { queries: s.queries }; },
};
