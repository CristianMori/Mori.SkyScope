// Mori.SkyScope — Fixture drivers for the analytic charts (spec/fixtures/{cartesian,pie,polar,heatmap}.json).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { CartesianChart, type CartesianChartOptions, type CartesianLayout } from "../charts/cartesian.js";
import { histogram, type HistogramOptions } from "../charts/histogram.js";
import { PieChart, type PieChartOptions } from "../charts/pie.js";
import { PolarChart, type PolarChartOptions } from "../charts/polar.js";
import { Heatmap, type HeatmapOptions } from "../charts/heatmap.js";
import { colormapHex, colormapStops, fnv1a, type ColormapName } from "../charts/colormaps.js";
import { RecordingPainter } from "../paint/recording-painter.js";
import type { Rect } from "../scene/geometry.js";
import { r9, rRect } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

/**
 * Fixture drivers for the analytic charts (`spec/fixtures/{cartesian,pie,polar,heatmap}.json`).
 * Mirrored by `AnalyticDrivers.cs`. Every query answer is rounded to 9 decimals.
 */
type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const arr9 = (a: ArrayLike<number>): number[] => Array.from(a, r9);
const rOpt = (r: Rect | null): unknown => (r ? rRect(r) : null);
const draw = (g: { draw(p: RecordingPainter, w: number, h: number): void }, w: number, h: number): unknown => { const p = new RecordingPainter(w, h); g.draw(p, w, h); return p.ops; };

interface CartState { g: CartesianChart; w: number; h: number; queries: unknown[] }
const cartLayout = (l: CartesianLayout): unknown => ({ plot: rRect(l.plot), yAxis: rRect(l.yAxis), y2Axis: rOpt(l.y2Axis), xAxis: rRect(l.xAxis), title: rOpt(l.title), legend: rOpt(l.legend) });

/** Cartesian chart: `setup` gives the options and the size. */
export const cartesianDriver: FixtureDriver<CartState> = {
  component: "cartesian",
  create(setup) { return { g: new CartesianChart((setup.options ?? {}) as CartesianChartOptions), w: num(setup.width), h: num(setup.height), queries: [] }; },
  /**
   * Steps: toggle, hover, leave, click, zoomBox, wheel, resetZoom; queries: xDomain, yDomain, layout, bars, points,
   * area, stacked, hit, hover, tooltip, legend, xTicks, barSlot, histogram, draw (recorded ops).
   */
  step(s, step: Step) {
    const { g, w, h, queries } = s, l = g.layout(w, h);
    switch (step.type) {
      case "toggle": g.toggleSeries(step.id as string); break;
      case "hover": g.pointerMove(num(step.x), num(step.y), w, h); break;
      case "leave": g.pointerLeave(); break;
      case "click": queries.push(g.click(num(step.x), num(step.y), w, h)); break;
      case "zoomBox": g.zoomTo(num(step.x0), num(step.y0), num(step.x1), num(step.y1), l); break;
      case "wheel": g.wheelZoom(num(step.x), num(step.y), num(step.dy), l); break;
      case "resetZoom": g.resetZoom(); break;
      case "query":
        if ("xDomain" in step) queries.push(arr9(g.xDomain()));
        else if ("yDomain" in step) queries.push(arr9(g.yDomain(step.yDomain as "y" | "y2")));
        else if ("layout" in step) queries.push(cartLayout(l));
        else if ("bars" in step) queries.push(g.bars(l).map((b) => ({ seriesId: b.seriesId, index: b.index, x: r9(b.x), y: r9(b.y), w: r9(b.w), h: r9(b.h) })));
        else if ("points" in step) queries.push(arr9(g.pixelPoints(g.series(step.points as string)!, l)));
        else if ("area" in step) queries.push(arr9(g.areaPolygon(g.series(step.area as string)!, l)));
        else if ("stacked" in step) { const [id, i] = step.stacked as [string, number]; queries.push(arr9(g.stackedValue(g.series(id)!, i))); }
        else if ("hit" in step) { const [x, y] = step.hit as [number, number]; queries.push(g.hitTest(x, y, l)); }
        else if ("hover" in step) queries.push(g.hover);
        else if ("tooltip" in step) { const t = g.hover ? g.tooltip(g.hover, l) : null; queries.push(t ? { x: r9(t.x), y: r9(t.y), title: t.title, lines: t.lines } : null); }
        else if ("legend" in step) queries.push(g.legendItems());
        else if ("xTicks" in step) queries.push(g.xTicks(l).map((v) => ({ v: r9(v), label: g.formatX(v, l) })));
        else if ("barSlot" in step) queries.push({ slot: r9(g.barSlot()), width: r9(g.barWidth()), groups: g.barGroups() });
        else if ("histogram" in step) { const hs = step.histogram as { values: number[]; options?: HistogramOptions }; const r = histogram(hs.values, hs.options ?? {}); queries.push({ edges: arr9(r.edges), counts: arr9(r.counts), centers: arr9(r.centers), binWidth: r9(r.binWidth), total: r.total }); }
        else if ("draw" in step) queries.push(draw(g, w, h));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot(s) { return { queries: s.queries }; },
};

interface PieState { g: PieChart; w: number; h: number; queries: unknown[] }
/** Pie chart: `setup` gives the options and the size. */
export const pieDriver: FixtureDriver<PieState> = {
  component: "pie",
  create(setup) { return { g: new PieChart((setup.options ?? {}) as PieChartOptions), w: num(setup.width), h: num(setup.height), queries: [] }; },
  /**
   * Steps: toggle, hover, leave, click; queries: slices (angles in radians), layout, hit, hover, labels, total,
   * legend, draw.
   */
  step(s, step: Step) {
    const { g, w, h, queries } = s, l = g.layout(w, h);
    switch (step.type) {
      case "toggle": g.toggleSlice(step.id as string); break;
      case "hover": g.pointerMove(num(step.x), num(step.y), w, h); break;
      case "leave": g.pointerLeave(); break;
      case "click": queries.push(g.click(num(step.x), num(step.y), w, h)); break;
      case "query":
        if ("slices" in step) queries.push(g.slices().map((x) => ({ id: x.id, value: r9(x.value), frac: r9(x.frac), start: r9(x.start), end: r9(x.end), mid: r9(x.mid) })));
        else if ("layout" in step) queries.push({ cx: r9(l.cx), cy: r9(l.cy), r: r9(l.r), inner: r9(l.inner), plot: rRect(l.plot), title: rOpt(l.title), legend: rOpt(l.legend) });
        else if ("hit" in step) { const [x, y] = step.hit as [number, number]; queries.push(g.hitTest(x, y, l)); }
        else if ("hover" in step) queries.push(g.hover);
        else if ("labels" in step) queries.push(g.slices().map((x) => g.labelFor(x)));
        else if ("total" in step) queries.push(r9(g.total()));
        else if ("legend" in step) queries.push(g.legendItems());
        else if ("draw" in step) queries.push(draw(g, w, h));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot(s) { return { queries: s.queries }; },
};

interface PolarState { g: PolarChart; w: number; h: number; queries: unknown[] }
/** Polar chart: `setup` gives the options and the size. */
export const polarDriver: FixtureDriver<PolarState> = {
  component: "polar",
  create(setup) { return { g: new PolarChart((setup.options ?? {}) as PolarChartOptions), w: num(setup.width), h: num(setup.height), queries: [] }; },
  /** Steps: toggle, hover, leave; queries: rDomain, layout, points, gridAngles, hit, hover, legend, draw. */
  step(s, step: Step) {
    const { g, w, h, queries } = s, l = g.layout(w, h);
    switch (step.type) {
      case "toggle": g.toggleSeries(step.id as string); break;
      case "hover": g.pointerMove(num(step.x), num(step.y), w, h); break;
      case "leave": g.pointerLeave(); break;
      case "query":
        if ("rDomain" in step) queries.push(arr9(g.rDomain()));
        else if ("layout" in step) queries.push({ cx: r9(l.cx), cy: r9(l.cy), r: r9(l.r), plot: rRect(l.plot), title: rOpt(l.title), legend: rOpt(l.legend) });
        else if ("points" in step) queries.push(arr9(g.pixelPoints(g.series(step.points as string)!, l)));
        else if ("gridAngles" in step) queries.push(g.gridAngles().map((a) => ({ a: r9(a), label: g.angleLabel(a) })));
        else if ("hit" in step) { const [x, y] = step.hit as [number, number]; queries.push(g.hitTest(x, y, l)); }
        else if ("hover" in step) queries.push(g.hover);
        else if ("legend" in step) queries.push(g.legendItems());
        else if ("draw" in step) queries.push(draw(g, w, h));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot(s) { return { queries: s.queries }; },
};

interface HeatState { g: Heatmap; w: number; h: number; queries: unknown[] }
/** Heatmap: `setup` gives the options, the size and optional initial values. */
export const heatmapDriver: FixtureDriver<HeatState> = {
  component: "heatmap",
  create(setup) { return { g: new Heatmap((setup.options ?? {}) as HeatmapOptions, setup.values as number[] | undefined), w: num(setup.width), h: num(setup.height), queries: [] }; },
  /**
   * Steps: push (a column), set (one cell), setValues, hover, leave; queries: range, color, get, hash (raster
   * FNV-1a), pixel (RGBA), layout, cell, extent, colormap, draw.
   */
  step(s, step: Step) {
    const { g, w, h, queries } = s, l = g.layout(w, h);
    switch (step.type) {
      case "push": g.pushColumn(step.column as number[]); break;
      case "set": g.set(num(step.row), num(step.col), num(step.value)); break;
      case "setValues": g.setValues(step.values as number[]); break;
      case "hover": g.pointerMove(num(step.x), num(step.y), w, h); break;
      case "leave": g.pointerLeave(); break;
      case "query":
        if ("range" in step) queries.push(arr9(g.range()));
        else if ("color" in step) queries.push(g.colorAt(num(step.color)));
        else if ("get" in step) { const [r, c] = step.get as [number, number]; const v = g.get(r, c); queries.push(Number.isNaN(v) ? null : r9(v)); }
        else if ("hash" in step) { const img = g.image(); queries.push({ width: img.width, height: img.height, hash: fnv1a(img.rgba) }); }
        else if ("pixel" in step) { const [x, y] = step.pixel as [number, number]; const img = g.image(), o = (y * img.width + x) * 4; queries.push([img.rgba[o], img.rgba[o + 1], img.rgba[o + 2], img.rgba[o + 3]]); }
        else if ("layout" in step) queries.push({ plot: rRect(l.plot), yAxis: rRect(l.yAxis), xAxis: rRect(l.xAxis), colorbar: rOpt(l.colorbar), title: rOpt(l.title) });
        else if ("cell" in step) { const [x, y] = step.cell as [number, number]; const c = g.cellAt(x, y, l); queries.push(c ? { col: c.col, row: c.row, value: r9(c.value) } : null); }
        else if ("extent" in step) queries.push({ xMin: r9(g.config.xMin), xMax: r9(g.config.xMax) });
        else if ("colormap" in step) { const cm = step.colormap as { map: ColormapName | string[]; t: number }; queries.push(colormapHex(colormapStops(cm.map), cm.t)); }
        else if ("draw" in step) queries.push(draw(g, w, h));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot(s) { return { queries: s.queries }; },
};
