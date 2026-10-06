// Mori.SkyScope — Seven-segment numeric display with sign, decimals, unit and label.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter } from "../paint/painter.js";
import { formatNumber } from "../scales/ticks.js";
import { GAUGE_LIGHT, type GaugeTheme } from "./common.js";

/** Seven-segment masks, bits a..g = 1..64 (a top, b upper-right, c lower-right, d bottom, e lower-left, f upper-left, g middle). */
const SEG: Record<string, number> = { "0": 0x3f, "1": 0x06, "2": 0x5b, "3": 0x4f, "4": 0x66, "5": 0x6d, "6": 0x7d, "7": 0x07, "8": 0x7f, "9": 0x6f, "-": 0x40, " ": 0x00, "E": 0x79, "r": 0x50, "o": 0x5c, "L": 0x38, "H": 0x76, "A": 0x77, "C": 0x39, "F": 0x71, "P": 0x73, "U": 0x3e, "b": 0x7c, "d": 0x5e, "n": 0x54, "t": 0x78 };
/** Segment bit mask for one character; unknown characters give 0 (blank). */
export const sevenSegmentMask = (ch: string): number => SEG[ch] ?? 0x00;

/** Polygons for the lit segments of one digit cell (x, y, w, h), as interleaved point lists. */
export function segmentPolygons(x: number, y: number, w: number, h: number, mask: number, thickness = 0.16): number[][] {
  const t = w * thickness, g = t * 0.18; // segment thickness and gap
  const out: number[][] = [];
  const hz = (yy: number): number[] => [x + t / 2 + g, yy, x + t + g, yy - t / 2, x + w - t - g, yy - t / 2, x + w - t / 2 - g, yy, x + w - t - g, yy + t / 2, x + t + g, yy + t / 2];
  const vt = (xx: number, y0: number, y1: number): number[] => [xx, y0 + g, xx + t / 2, y0 + t / 2 + g, xx + t / 2, y1 - t / 2 - g, xx, y1 - g, xx - t / 2, y1 - t / 2 - g, xx - t / 2, y0 + t / 2 + g];
  const mid = y + h / 2;
  if (mask & 0x01) out.push(hz(y + t / 2));
  if (mask & 0x02) out.push(vt(x + w - t / 2, y + t / 2, mid));
  if (mask & 0x04) out.push(vt(x + w - t / 2, mid, y + h - t / 2));
  if (mask & 0x08) out.push(hz(y + h - t / 2));
  if (mask & 0x10) out.push(vt(x + t / 2, mid, y + h - t / 2));
  if (mask & 0x20) out.push(vt(x + t / 2, y + t / 2, mid));
  if (mask & 0x40) out.push(hz(mid));
  return out;
}

/** Configuration of a seven-segment display: digit cells, decimals, caption, unit, segment colours and slant. */
export interface NumericDisplayConfig {
  /** `decimals` is the fixed fraction length; `label` is drawn below, `unit` beside the digits. */
  /** Digit cells including sign and integer part; the decimal point lives between cells. */
  digits: number; decimals: number; label?: string | undefined; unit?: string | undefined;
  /** `segmentColor` for lit segments, `offOpacity` (0–1) for unlit ones, `slant` as a fraction of the cell height shearing to the right. */
  segmentColor: string; offOpacity: number; slant: number; theme: GaugeTheme;
}
/** Constructor options: every setting optional, theme partially overridable. */
export type NumericDisplayOptions = Partial<Omit<NumericDisplayConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };

/** Seven-segment readout; unlit segments show faintly like a real LED display. */
export class NumericDisplay {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: NumericDisplayConfig;
  /** Shown value; null or a non-finite number renders as a single dash. */
  value: number | null = 0;
  /** Settings missing from `o` take the defaults; the theme merges over `GAUGE_LIGHT`. */
  constructor(o: NumericDisplayOptions = {}) { const { theme, ...rest } = o; this.config = { digits: 5, decimals: 1, segmentColor: "#16a34a", offOpacity: 0.08, slant: 0.08, ...rest, theme: { ...GAUGE_LIGHT, ...theme } }; }
  /** Replaces the shown value; null blanks the display to a dash. */
  setValue(v: number | null): void { this.value = v; }
  /** Right-aligned text in the digit cells; "-" for null, "E" fill on overflow. */
  text(): string {
    const c = this.config;
    if (this.value === null || !Number.isFinite(this.value)) return "-".padStart(c.digits, " ");
    let s = formatNumber(this.value, c.decimals);
    const cells = s.replace(".", "").length;
    if (cells > c.digits) return "E".repeat(c.digits);
    while (s.replace(".", "").length < c.digits) s = " " + s;
    return s;
  }
  /** Renders the digit cells (unlit segments faintly), decimal point, unit and label. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme;
    p.clear(th.background);
    const labelRoom = c.label ? th.fontSize + 6 : 0;
    const unitRoom = c.unit ? th.fontSize * 2.2 : 0;
    const pad = 6, availW = width - 2 * pad - unitRoom, availH = height - 2 * pad - labelRoom;
    const cellW = Math.min(availW / (c.digits + 0.25 * (c.digits - 1)), availH * 0.6), cellH = Math.min(availH, cellW / 0.6), gap = cellW * 0.25;
    const x0 = pad + (availW - (c.digits * cellW + (c.digits - 1) * gap)) / 2, y0 = pad + (availH - cellH) / 2;
    const text = this.text();
    let cell = 0;
    p.save(); p.translate(cellH * c.slant, 0);
    for (let i = 0; i < text.length; i++) {
      const ch = text[i]!;
      if (ch === ".") { const cx = x0 + cell * (cellW + gap) - gap / 2, cy = y0 + cellH - cellW * 0.08; p.circle(cx - cellH * c.slant, cy, cellW * 0.07, { color: c.segmentColor }); continue; }
      const cx = x0 + cell * (cellW + gap);
      const mask = sevenSegmentMask(ch);
      p.save(); p.translate(-(cellH * c.slant) * ((y0 + cellH / 2) / cellH), 0);
      for (const poly of segmentPolygons(cx, y0, cellW, cellH, 0x7f)) p.polygon(poly, { color: c.segmentColor, opacity: c.offOpacity });
      for (const poly of segmentPolygons(cx, y0, cellW, cellH, mask)) p.polygon(poly, { color: c.segmentColor });
      p.restore();
      cell++;
    }
    p.restore();
    if (c.unit) p.text(c.unit, width - pad, y0 + cellH, { color: th.mutedText, family: th.fontFamily, size: th.fontSize * 1.2, align: "right", baseline: "bottom" });
    if (c.label) p.text(c.label, width / 2, height - 2, { color: th.mutedText, family: th.fontFamily, size: th.fontSize, align: "center", baseline: "bottom" });
  }
}
