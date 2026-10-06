// Mori.SkyScope — Pure trend chart layout: legend, lane header column, axis columns, lanes with analog area and logic stack, labels, time axis, navigator.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Rect } from "../scene/geometry.js";
import { OVERLAY_LEGENDS, legendRowHeight, type AxisLayout, type LabelLayout, type LaneLayout, type TrendChartConfig, type TrendLayout } from "./trend-config.js";

/** What the layout needs to know about one lane; `TrendChartModel.layout` derives it from the config. */
export interface LaneLayoutInput {
  /** Lane id, copied into the output. */ laneId: string; /** Relative height among open lanes. */ weight: number; /** Axis ids of the left columns, innermost first. */ leftAxes: string[]; /** Axis ids of the right columns, innermost first. */ rightAxes: string[];
  /** Folded to `collapsedLaneHeight`; no axes or labels are laid out. */
  collapsed?: boolean | undefined;
  /** Number of digital (logic-analyzer) tracks stacked at the bottom of the lane. */
  tracks?: number | undefined;
  /** Whether the lane has analog series (the stack then takes at most `digitalStackShare`). */
  analog?: boolean | undefined;
  /** Series shown as in-plot labels, in draw order. */
  labels?: { seriesId: string; name: string; digital?: boolean | undefined }[] | undefined;
}

/** Height of an overlay legend listing `rows` series across `groups` lanes. */
export function legendHeight(config: TrendChartConfig, rows: number, groups: number, deltaRows = 0): number {
  const rowH = legendRowHeight(config);
  return 2 * config.style.legendPadding + rows * rowH + deltaRows * (rowH - 4) + (deltaRows > 0 ? rowH - 4 : 0) + Math.max(0, groups - 1) * 4;
}

/** Painter-free text width: 0.6 × fontSize per character (the RecordingPainter's metric), identical in both cores. */
export const estimateTextWidth = (text: string, fontSize: number): number => text.length * fontSize * 0.6;

/**
 * In-plot label boxes along the top of a lane: a swatch, the name, 6 px gaps; labels that would overflow wrap to the next
 * row. Rows that would run under an overlay legend (`avoid`) start right of it when it sits on the left, else stop before it.
 */
export function layoutLabels(config: TrendChartConfig, lane: Rect, labels: { seriesId: string; name: string }[], avoid: Rect | null = null): LabelLayout[] {
  const out: LabelLayout[] = [];
  const h = config.theme.fontSize + 6, sw = config.style.legendSwatchLength;
  const bounds = (y: number): { x0: number; x1: number } => {
    let x0 = lane.x + 6, x1 = lane.x + lane.w - 4;
    if (avoid && y < avoid.y + avoid.h && y + h > avoid.y && avoid.x < x1 && avoid.x + avoid.w > x0) {
      if (avoid.x + avoid.w / 2 < lane.x + lane.w / 2) x0 = Math.max(x0, avoid.x + avoid.w + 6); else x1 = Math.min(x1, avoid.x - 6);
    }
    return { x0, x1 };
  };
  let y = lane.y + 4, b = bounds(y), x = b.x0;
  for (const l of labels) {
    const w = sw + 4 + estimateTextWidth(l.name, config.theme.fontSize) + 8;
    if (x + w > b.x1 && x > b.x0) { y += h + 2; b = bounds(y); x = b.x0; }
    if (y + h > lane.y + lane.h) break;
    out.push({ seriesId: l.seriesId, rect: { x, y, w, h } });
    x += w + 6;
  }
  return out;
}

/**
 * Pure layout: margins → legend → lane header column → y-axis columns → stacked lanes → time axis → navigator.
 * Every lane gets the same axis column widths so plots line up vertically; collapsed lanes take a fixed thin height.
 * Mirrors `TrendLayoutEngine` in C#.
 */
export function layoutTrendChart(config: TrendChartConfig, width: number, height: number, lanes: LaneLayoutInput[], legendRows = 0, legendDeltaRows = 0): TrendLayout {
  const m = config.margin;
  let x0 = m, x1 = width - m, y0 = m, y1 = height - m;
  let legend: Rect | null = null;
  if (config.legend === "right") { legend = { x: x1 - config.legendWidth, y: y0, w: config.legendWidth, h: y1 - y0 }; x1 = legend.x - m; }
  else if (config.legend === "top") { const h = 24; legend = { x: x0, y: y0, w: x1 - x0, h }; y0 += h + m; }

  let navigator: Rect | null = null;
  if (config.navigator) { navigator = { x: x0, y: y1 - config.navigatorHeight, w: Math.max(0, x1 - x0), h: config.navigatorHeight }; y1 = navigator.y - m; }

  const headerW = config.laneHeaders ? config.laneHeaderWidth : 0;
  const leftCols = Math.max(0, ...lanes.map((l) => l.leftAxes.length));
  const rightCols = Math.max(0, ...lanes.map((l) => l.rightAxes.length));
  const plotX = x0 + headerW + leftCols * config.yAxisWidth;
  const plotRight = x1 - rightCols * config.yAxisWidth;
  const timeAxis: Rect = { x: plotX, y: y1 - config.timeAxisHeight, w: Math.max(0, plotRight - plotX), h: config.timeAxisHeight };
  const plotTop = y0, plotBottom = timeAxis.y;
  if (navigator) { navigator.x = plotX; navigator.w = Math.max(0, plotRight - plotX); }

  const plot: Rect = { x: plotX, y: plotTop, w: Math.max(0, plotRight - plotX), h: Math.max(0, plotBottom - plotTop) };
  if (OVERLAY_LEGENDS.includes(config.legend) && legendRows > 0) {
    const lw = Math.min(config.legendWidth, plot.w), lh = Math.min(legendHeight(config, legendRows, lanes.length, legendDeltaRows), plot.h);
    const right = config.legend.endsWith("right"), bottom = config.legend.startsWith("bottom");
    legend = { x: right ? plot.x + plot.w - lw - m : plot.x + m, y: bottom ? plot.y + plot.h - lh - m : plot.y + m, w: lw, h: lh };
  }
  const avoid = OVERLAY_LEGENDS.includes(config.legend) ? legend : null;

  const n = Math.max(1, lanes.length);
  const open = lanes.filter((l) => !l.collapsed), collapsedCount = lanes.length - open.length;
  const totalWeight = open.reduce((s, l) => s + Math.max(0.01, l.weight), 0) || 1;
  const avail = Math.max(0, plotBottom - plotTop - config.laneGap * (n - 1) - collapsedCount * config.collapsedLaneHeight);
  const out: LaneLayout[] = [];
  let y = plotTop;
  for (const lane of lanes) {
    const h = lane.collapsed ? config.collapsedLaneHeight : avail * Math.max(0.01, lane.weight) / totalWeight;
    const rect: Rect = { x: plotX, y, w: Math.max(0, plotRight - plotX), h };
    // logic-analyzer tracks sit at the bottom; a digital-only lane is all stack
    const tracks = lane.tracks ?? 0, hasAnalog = lane.analog ?? true;
    let analog: Rect = rect, stack: Rect | null = null;
    if (!lane.collapsed && tracks > 0) {
      const sh = hasAnalog ? Math.min(tracks * config.digitalTrackHeight, h * config.digitalStackShare) : h;
      stack = { x: rect.x, y: y + h - sh, w: rect.w, h: sh };
      analog = { x: rect.x, y, w: rect.w, h: h - sh };
    }
    const axes: AxisLayout[] = [];
    if (!lane.collapsed) {
      lane.leftAxes.forEach((axisId, i) => axes.push({ axisId, side: "left", rect: { x: plotX - (i + 1) * config.yAxisWidth, y: analog.y, w: config.yAxisWidth, h: analog.h } }));
      lane.rightAxes.forEach((axisId, i) => axes.push({ axisId, side: "right", rect: { x: plotRight + i * config.yAxisWidth, y: analog.y, w: config.yAxisWidth, h: analog.h } }));
    }
    const header: Rect | null = headerW > 0 ? { x: x0, y, w: headerW, h } : null;
    // analog labels sit along the top of the analog area; a digital track's label is its name at the left of its band
    let labels: LabelLayout[] = [];
    if (!lane.collapsed && config.plotLabels && lane.labels) {
      const analogLabels = lane.labels.filter((l) => !l.digital), digitalLabels = lane.labels.filter((l) => l.digital);
      labels = layoutLabels(config, analog, analogLabels, avoid);
      if (stack && digitalLabels.length > 0) {
        const th = stack.h / digitalLabels.length, lh = config.theme.fontSize + 6, sw = config.style.legendSwatchLength;
        digitalLabels.forEach((l, i) => labels.push({ seriesId: l.seriesId, rect: { x: stack.x + 4, y: stack.y + i * th + th / 2 - lh / 2, w: sw + 4 + estimateTextWidth(l.name, config.theme.fontSize) + 8, h: lh } }));
      }
    }
    out.push({ laneId: lane.laneId, rect, analog, stack, axes, header, labels, collapsed: !!lane.collapsed });
    y += h + config.laneGap;
  }
  return { width, height, plot, lanes: out, timeAxis, legend, navigator };
}
