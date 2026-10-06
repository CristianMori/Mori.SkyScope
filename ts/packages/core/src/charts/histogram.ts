// Mori.SkyScope — Histogram binning with 1-2-5 "nice" bin widths, so bin edges land on round numbers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { tickSpec } from "../scales/ticks.js";
import type { CartesianSeriesConfig } from "./cartesian.js";

/**
 * Histogram binning with 1-2-5 "nice" bin widths, so bin edges land on round numbers.
 * Mirrors `Mori.SkyScope.Core.Charts.Histogram`; pinned by `spec/fixtures/cartesian.json`.
 */
export interface HistogramOptions {
  /** Target bin count (default 10) when `binWidth` is not given. */
  bins?: number | undefined;
  /** Explicit bin width in data units; takes precedence over `bins`. */
  binWidth?: number | undefined;
  /** Fixed data range; omit to use the finite extent of the values. Values outside the resulting bins are dropped. */
  min?: number | undefined; max?: number | undefined;
  /** Normalise counts to densities (count / (n × width)). */
  density?: boolean | undefined;
}
/** Bins of a histogram; `edges` has one entry more than `counts`, and `counts` holds densities when `density` was requested. */
export interface HistogramResult { /** Bin boundaries, ascending; bin k spans [edges[k], edges[k + 1]]. */ edges: number[]; /** Samples per bin, or densities when normalised. */ counts: number[]; /** Midpoint of each bin. */ centers: number[]; /** Width of every bin in data units. */ binWidth: number; /** Number of finite values that landed in a bin. */ total: number }

/** Bins the finite values into equal-width bins whose edges fall on round numbers; NaN and out-of-range values are skipped, the last bin is closed. */
export function histogram(values: ArrayLike<number>, o: HistogramOptions = {}): HistogramResult {
  let lo = Infinity, hi = -Infinity;
  for (let i = 0; i < values.length; i++) { const v = values[i]!; if (Number.isNaN(v)) continue; if (v < lo) lo = v; if (v > hi) hi = v; }
  if (o.min !== undefined) lo = o.min;
  if (o.max !== undefined) hi = o.max;
  if (!Number.isFinite(lo) || !Number.isFinite(hi)) return { edges: [0, 1], counts: [0], centers: [0.5], binWidth: 1, total: 0 };
  let w = o.binWidth ?? tickSpec(lo, hi, o.bins ?? 10).step;
  if (!(w > 0)) w = 1;
  const start = Math.floor(lo / w) * w;
  // The last bin is closed ([edge, max]) so the maximum lands in it instead of opening a new bin.
  const n = Math.max(1, Math.ceil((hi - start) / w - 1e-9));
  const counts = new Array<number>(n).fill(0);
  let total = 0;
  for (let i = 0; i < values.length; i++) {
    const v = values[i]!;
    if (Number.isNaN(v) || v < start || v > start + n * w) continue;
    let k = Math.floor((v - start) / w);
    if (k >= n) k = n - 1;
    counts[k]!++; total++;
  }
  const edges: number[] = [], centers: number[] = [];
  for (let k = 0; k <= n; k++) edges.push(start + k * w);
  for (let k = 0; k < n; k++) centers.push(start + (k + 0.5) * w);
  if (o.density && total > 0) for (let k = 0; k < n; k++) counts[k] = counts[k]! / (total * w);
  return { edges, counts, centers, binWidth: w, total };
}

/** A histogram as a bar series for `CartesianChart` (bars centred on the bins, width = bin width). */
export function histogramSeries(id: string, values: ArrayLike<number>, o: HistogramOptions = {}, extra: Partial<CartesianSeriesConfig> = {}): CartesianSeriesConfig {
  const h = histogram(values, o);
  return { id, kind: "bar", x: h.centers, y: h.counts, barWidth: h.binWidth, ...extra };
}
