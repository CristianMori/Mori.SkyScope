// Mori.SkyScope — React component hosting the chart editor panel beside a trend chart.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useRef, type CSSProperties } from "react";
import { ChartEditorPanel, type TrendChartView } from "@cmori/skyscope-render";
import { cx } from "./cx.js";

/** Props of `ChartEditor`. */
export interface ChartEditorProps {
  /** The chart to edit (the view `TrendChart` hands to `onReady`); null shows an empty panel. */
  chart?: TrendChartView | null | undefined;
  /** Hide the chart-level settings (time span, legend, panels). */
  hideChartSettings?: boolean | undefined;
  /** Class of the host div. */
  className?: string | undefined;
  /** Inline style of the host div; it fills its parent. */
  style?: CSSProperties | undefined;
}

/**
 * A runtime editor for a trend chart: lanes, axes, series, thresholds and markers as a tree with add, remove and move,
 * and a property form for the selected item. Changes go through the chart's model and fire its `onConfigChanged`.
 */
export function ChartEditor({ chart, hideChartSettings, className, style }: ChartEditorProps) {
  const host = useRef<HTMLDivElement>(null);
  const panel = useRef<ChartEditorPanel | null>(null);
  useEffect(() => {
    const p = new ChartEditorPanel(host.current!, { chart, hideChartSettings });
    panel.current = p;
    return () => { p.dispose(); panel.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hideChartSettings]);
  useEffect(() => { panel.current?.attach(chart ?? null); }, [chart]);
  return <div ref={host} className={cx("skyscope-editor-host", className)} style={{ height: "100%", minHeight: 0, ...style }} />;
}
