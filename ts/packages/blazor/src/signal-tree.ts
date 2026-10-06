// Mori.SkyScope — Blazor bridge for the signal tree panel, attached to a mounted trend chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { acquire } from "./shared.js";
import { SignalTreePanel, type TrendChartView } from "@mori/skyscope-render";

/** Options of `mountSignalTree`: `wsUrl` and `retentionSeconds` select the shared store as for the trend chart; `separators` split channel names into groups; `placeholder` is the search box hint. */
export interface SignalTreeMountOptions { wsUrl?: string | undefined; retentionSeconds?: number | undefined; separators?: string | undefined; placeholder?: string | undefined }

/** What `mountSignalTree` returns to .NET; every method is invoked through JS interop. */
export interface SignalTreeHandle {
  /** Attach the TrendChart (the handle `mountTrendChart` returned) rows are dragged onto. */
  attach(chart: { view?: TrendChartView } | null): void;
  /** Sets the search filter programmatically (the box itself is not updated). */
  setQuery(query: string): void;
  /** Tears down the panel and releases the shared socket reference. */
  dispose(): void;
}

/** Mounts the signal tree on the shared store of `wsUrl`; the .NET side attaches it to a TrendChart handle. */
export function mountSignalTree(element: HTMLElement, options: SignalTreeMountOptions): SignalTreeHandle {
  const { store, release } = acquire(options.wsUrl, options.retentionSeconds ?? 60);
  const panel = new SignalTreePanel(element, store, { separators: options.separators, placeholder: options.placeholder });
  return {
    attach: (chart) => panel.attach(chart?.view ?? null),
    setQuery: (q) => { panel.model.setQuery(q); panel.refresh(); },
    dispose: () => { panel.dispose(); release(); },
  };
}
