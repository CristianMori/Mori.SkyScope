// Mori.SkyScope — The plain legend shared by the analytic charts (cartesian, pie, polar): one row per item, click to toggle.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import type { Rect } from "../scene/geometry.js";
import { OVERLAY_LEGENDS, legendRowHeight, type ChartStyle, type ChartTheme, type LegendPosition } from "./trend-config.js";

/**
 * The plain legend shared by the analytic charts (cartesian, pie, polar): one row per item, click to
 * toggle. Mirrors `Mori.SkyScope.Core.Charts.SimpleLegend`; pinned through the chart fixtures.
 */
export interface LegendItem { /** Key returned by `legendItemAt` and matched against the hover id. */ id: string; /** Text shown next to the swatch. */ name: string; /** Swatch colour (CSS). */ color: string; /** Toggled off by the user; drawn dimmed. */ hidden: boolean; /** Swatch form: a stroke, a filled box or a filled circle. */ shape: "line" | "box" | "circle" }

/** The slice of a chart config the legend needs; the cartesian, pie and polar configs all satisfy it. */
export interface LegendConfigLike { /** Placement; corner values float over the plot. */ legend: LegendPosition; /** Width in pixels of "right" and overlay legends. */ legendWidth: number; /** Gap in pixels between the legend and the plot. */ margin: number; /** Colours and font. */ theme: ChartTheme; /** Paddings, row height and swatch length. */ style: ChartStyle }

/** Vertical list height for `count` items. */
export function simpleLegendHeight(c: LegendConfigLike, count: number): number {
  return 2 * c.style.legendPadding + count * legendRowHeight(c);
}

/** Height reserved above the plot for a "top" legend strip. */
export function topLegendHeight(c: LegendConfigLike): number { return legendRowHeight(c) + 2 * c.style.legendPadding - 4; }

/**
 * Reserves space for "right"/"top" legends inside `outer` (returning the remaining rect and the legend
 * rect) — corner overlays are placed later with `overlayLegendRect` once the plot is known.
 */
export function reserveLegend(c: LegendConfigLike, outer: Rect, count: number): { inner: Rect; legend: Rect | null } {
  if (count === 0 || c.legend === "none" || OVERLAY_LEGENDS.includes(c.legend)) return { inner: outer, legend: null };
  if (c.legend === "right") {
    const w = Math.min(c.legendWidth, outer.w);
    return { inner: { x: outer.x, y: outer.y, w: outer.w - w - c.margin, h: outer.h }, legend: { x: outer.x + outer.w - w, y: outer.y, w, h: outer.h } };
  }
  const h = topLegendHeight(c);
  return { inner: { x: outer.x, y: outer.y + h + c.margin, w: outer.w, h: outer.h - h - c.margin }, legend: { x: outer.x, y: outer.y, w: outer.w, h } };
}

/** Rectangle of a corner overlay legend inside `plot`, inset by the margin and clipped to the plot size; null for non-overlay positions or no items. */
export function overlayLegendRect(c: LegendConfigLike, plot: Rect, count: number): Rect | null {
  if (count === 0 || !OVERLAY_LEGENDS.includes(c.legend)) return null;
  const m = c.margin;
  const w = Math.min(c.legendWidth, plot.w), h = Math.min(simpleLegendHeight(c, count), plot.h);
  const right = c.legend.endsWith("right"), bottom = c.legend.startsWith("bottom");
  return { x: right ? plot.x + plot.w - w - m : plot.x + m, y: bottom ? plot.y + plot.h - h - m : plot.y + m, w, h };
}

function swatch(p: Painter, item: LegendItem, x: number, cy: number, len: number, opacity: number): void {
  if (item.shape === "line") p.line(x, cy, x + len, cy, { color: item.color, width: 2, opacity });
  else if (item.shape === "circle") p.circle(x + len / 2, cy, Math.min(5, len / 2), { color: item.color, opacity });
  else p.rect(x, cy - 5, len, 10, { color: item.color, opacity }, undefined, 2);
}

/** Paints the legend box and its rows; hidden items are dimmed. */
export function drawSimpleLegend(p: Painter, c: LegendConfigLike, r: Rect, items: LegendItem[], hoverId: string | null = null): void {
  const th = c.theme, st = c.style, pad = st.legendPadding, sw = st.legendSwatchLength, rowH = legendRowHeight(c);
  const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize, baseline: "middle" };
  const muted: TextStyle = { ...text, color: th.mutedText };
  p.rect(r.x, r.y, r.w, r.h, { color: th.legendBackground, opacity: st.legendOpacity }, { color: th.legendBorder, width: 1 }, st.legendRadius);
  if (c.legend === "top") {
    let x = r.x + pad;
    const cy = r.y + r.h / 2;
    for (const it of items) {
      const w = p.measureText(it.name, text).width;
      if (x + sw + 6 + w > r.x + r.w - pad + 1) break;
      swatch(p, it, x, cy, sw, it.hidden ? 0.3 : 1);
      p.text(it.name, x + sw + 6, cy, it.hidden ? muted : text);
      x += sw + 6 + w + 16;
    }
    return;
  }
  let y = r.y + pad;
  for (const it of items) {
    if (y + rowH > r.y + r.h + 1) return;
    const cy = y + rowH / 2;
    if (hoverId === it.id) p.rect(r.x + 2, y, r.w - 4, rowH, { color: th.hover, opacity: 0.15 }, undefined, 2);
    swatch(p, it, r.x + pad, cy, sw, it.hidden ? 0.3 : 1);
    p.text(it.name, r.x + pad + sw + 6, cy, it.hidden ? muted : text);
    y += rowH;
  }
}

/** Item under (x, y) or null. The "top" strip uses the painter-independent estimate `charWidth × size`. */
export function legendItemAt(c: LegendConfigLike, r: Rect | null, items: LegendItem[], x: number, y: number, measure: (text: string) => number): string | null {
  if (!r || x < r.x || x > r.x + r.w || y < r.y || y > r.y + r.h) return null;
  const pad = c.style.legendPadding, sw = c.style.legendSwatchLength, rowH = legendRowHeight(c);
  if (c.legend === "top") {
    let cx = r.x + pad;
    for (const it of items) { const w = sw + 6 + measure(it.name); if (x >= cx && x <= cx + w) return it.id; cx += w + 16; }
    return null;
  }
  const i = Math.floor((y - r.y - pad) / rowH);
  return i >= 0 && i < items.length ? items[i]!.id : null;
}
