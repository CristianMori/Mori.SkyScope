// Mori.SkyScope — Blazor bridge for the chart editor panel, attached to a mounted trend chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { ChartEditorPanel, type TrendChartView } from "@mori/skyscope-render";

/** Options of `mountChartEditor`: `hideChartSettings` hides the chart-level section. */
export interface ChartEditorMountOptions { hideChartSettings?: boolean | undefined }

/** What `mountChartEditor` returns to .NET; every method is invoked through JS interop. */
export interface ChartEditorHandle {
  /** Attach the TrendChart (the handle `mountTrendChart` returned) to edit. */
  attach(chart: { view?: TrendChartView } | null): void;
  /** Re-read the chart's configuration (after a change made from .NET). */
  refresh(): void;
  /** Tears down the panel. */
  dispose(): void;
}

/** Mounts the chart editor in `element`; the .NET side attaches it to a TrendChart handle. */
export function mountChartEditor(element: HTMLElement, options: ChartEditorMountOptions): ChartEditorHandle {
  const panel = new ChartEditorPanel(element, { hideChartSettings: options.hideChartSettings });
  return {
    attach: (chart) => panel.attach(chart?.view ?? null),
    refresh: () => panel.refresh(),
    dispose: () => panel.dispose(),
  };
}
