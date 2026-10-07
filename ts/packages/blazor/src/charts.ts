// Mori.SkyScope — Blazor bridge for the analytic charts: mounts an XY, pie, polar or heatmap view and takes options and data as JSON.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { CartesianChart, Heatmap, PieChart, PolarChart } from "@cmori/skyscope-core";
import { ChartView, type ChartLike } from "@cmori/skyscope-render";

/** Which analytic chart model `mountChart` builds; also selects which options JSON shape is expected. */
export type ChartKind = "xy" | "pie" | "polar" | "heatmap";

/** What `mountChart` returns to .NET; every method is invoked through JS interop. */
export interface ChartHandle {
  /** Replace the configuration (and data) — rebuilds the model. */
  setOptions(optionsJson: string): void;
  /** Heatmap only: replace the matrix (row-major). */
  setValues(valuesJson: string): void;
  /** Heatmap only: append a column (spectrogram). */
  pushColumn(columnJson: string): void;
  /** Tears down the view and removes it from the element. */
  dispose(): void;
}

function make(kind: ChartKind, o: never): ChartLike {
  switch (kind) {
    case "xy": return new CartesianChart(o);
    case "pie": return new PieChart(o);
    case "polar": return new PolarChart(o);
    case "heatmap": return new Heatmap(o);
  }
}

/** Mounts one of the analytic charts; the .NET side pushes configuration and data as JSON. */
export function mountChart(element: HTMLElement, kind: ChartKind, optionsJson: string | null): ChartHandle {
  const parse = (json: string | null): never => (json ? JSON.parse(json) : {}) as never;
  const view = new ChartView(element, make(kind, parse(optionsJson)));
  return {
    setOptions(json) { view.setChart(make(kind, parse(json))); },
    setValues(json) { const c = view.chart; if (c instanceof Heatmap) { c.setValues(JSON.parse(json) as number[]); view.invalidate(); } },
    pushColumn(json) { const c = view.chart; if (c instanceof Heatmap) { c.pushColumn(JSON.parse(json) as number[]); view.invalidate(); } },
    dispose() { view.dispose(); },
  };
}
