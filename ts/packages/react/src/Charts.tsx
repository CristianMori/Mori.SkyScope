// Mori.SkyScope — Mounts an analytic chart model in a ChartView for the component's lifetime.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useMemo, useRef, type CSSProperties } from "react";
import { CartesianChart, Heatmap, PieChart, PolarChart, type CartesianChartOptions, type HeatmapOptions, type PieChartOptions, type PolarChartOptions } from "@cmori/skyscope-core";
import { ChartView, type ChartLike } from "@cmori/skyscope-render";
import { cx } from "./cx.js";

interface HostProps { className?: string | undefined; style?: CSSProperties | undefined }

/**
 * Mounts an analytic chart model in a ChartView for the component's lifetime. The model is rebuilt when the
 * options change (keyed by their JSON), `onReady` hands it out for imperative updates (e.g. spectrogram pushes).
 */
function useChart<C extends ChartLike>(make: () => C, optionsKey: string, onReady?: ((chart: C, view: ChartView<C>) => void) | undefined) {
  const host = useRef<HTMLDivElement>(null);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const chart = useMemo(make, [optionsKey]);
  const ready = useRef(onReady); ready.current = onReady;
  useEffect(() => {
    const v = new ChartView(host.current!, chart);
    ready.current?.(chart, v);
    return () => v.dispose();
  }, [chart]);
  return host;
}

const Host = ({ host, className, style }: { host: React.RefObject<HTMLDivElement | null> } & HostProps) => (
  <div ref={host} className={cx("skyscope-chart-host", className)} style={{ width: "100%", height: "100%", minHeight: 120, ...style }} />
);
const key = (o: unknown): string => JSON.stringify(o ?? {});

/** Line / step / scatter / area / bar chart. Series data lives in `options.series`. */
export function XYChart({ options, onReady, className, style }: { options: CartesianChartOptions; onReady?: ((chart: CartesianChart, view: ChartView<CartesianChart>) => void) | undefined } & HostProps) {
  const host = useChart(() => new CartesianChart(options), key(options), onReady);
  return <Host host={host} className={className} style={style} />;
}
/** Pie / donut chart; slices live in `options.slices`. `onReady` hands out the model and the view; `className`/`style` go on the host div. */
export function PieChartView({ options, onReady, className, style }: { options: PieChartOptions; onReady?: ((chart: PieChart, view: ChartView<PieChart>) => void) | undefined } & HostProps) {
  const host = useChart(() => new PieChart(options), key(options), onReady);
  return <Host host={host} className={className} style={style} />;
}
/** Radar / polar chart over `options.categories` and `options.series`. `onReady` hands out the model and the view; `className`/`style` go on the host div. */
export function PolarChartView({ options, onReady, className, style }: { options: PolarChartOptions; onReady?: ((chart: PolarChart, view: ChartView<PolarChart>) => void) | undefined } & HostProps) {
  const host = useChart(() => new PolarChart(options), key(options), onReady);
  return <Host host={host} className={className} style={style} />;
}
/** Heatmap; pass `values` (row-major, row 0 at yMin) or push columns through `onReady` for a spectrogram. */
export function HeatmapView({ options, values, onReady, className, style }: { options: HeatmapOptions; values?: ArrayLike<number> | undefined; onReady?: ((chart: Heatmap, view: ChartView<Heatmap>) => void) | undefined } & HostProps) {
  const viewRef = useRef<ChartView<Heatmap> | null>(null);
  const host = useChart(() => new Heatmap(options, values), key(options), (chart, view) => { viewRef.current = view; onReady?.(chart, view); });
  useEffect(() => { if (values && viewRef.current) { viewRef.current.chart.setValues(values); viewRef.current.invalidate(); } }, [values]);
  return <Host host={host} className={className} style={style} />;
}
