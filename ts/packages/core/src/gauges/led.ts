// Mori.SkyScope — A single indicator lamp.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { GAUGE_LIGHT, clamp, type GaugeTheme } from "./common.js";

/** Resolved settings. `offColor` undefined falls back to the theme's `ledOff`; `label` is drawn under the lamp. */
export interface LedConfig { label?: string | undefined; onColor: string; offColor?: string | undefined; shape: "round" | "square"; theme: GaugeTheme }
/** Constructor options: every setting optional, theme partially overridable. */
export type LedOptions = Partial<Omit<LedConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };

/** A single indicator lamp. */
export class Led {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: LedConfig;
  /** Lit state; assign directly or pass it to the constructor. */
  on = false;
  /** Settings missing from `o` take the defaults; the theme merges over `GAUGE_LIGHT`. */
  constructor(o: LedOptions = {}, on = false) { const { theme, ...rest } = o; this.config = { onColor: "#16a34a", shape: "round", ...rest, theme: { ...GAUGE_LIGHT, ...theme } }; this.on = on; }
  /** Draws the lamp centred in the control, with a highlight when lit. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme;
    p.clear(th.background);
    const labelRoom = c.label ? th.fontSize + 6 : 0;
    const d = Math.min(width, height - labelRoom) * 0.7, cx = width / 2, cy = (height - labelRoom) / 2;
    const fill = { color: this.on ? c.onColor : c.offColor ?? th.ledOff }, stroke = { color: th.track, width: 1 };
    if (c.shape === "round") p.circle(cx, cy, d / 2, fill, stroke); else p.rect(cx - d / 2, cy - d / 2, d, d, fill, stroke, d * 0.15);
    if (this.on) p.circle(cx - d * 0.18, cy - d * 0.18, d * 0.12, { color: "#ffffff", opacity: 0.55 });
    if (c.label) p.text(c.label, cx, height - 2, { color: th.mutedText, family: th.fontFamily, size: th.fontSize, align: "center", baseline: "bottom" });
  }
}

/** Configuration of an LED array: lamp count and direction, level or bit mode, range, colours and caption. */
export interface LedArrayConfig {
  /** `count` lamps laid out along `orientation`: left to right, or bottom to top. */
  count: number; orientation: "horizontal" | "vertical";
  /** "level": the lowest N segments light up for a value (VU meter). "bits": each LED is a bit of a mask. */
  mode: "level" | "bits";
  /** Value range mapped onto the lamps in "level" mode. */
  min: number; max: number;
  /** `label` is drawn under the lamps; `gap` is the spacing between them in pixels. */
  /** Colour per index; fewer entries repeat the last. Default green → amber → red thirds. */
  colors: string[]; label?: string | undefined; gap: number; theme: GaugeTheme;
}
/** Constructor options: every setting optional, theme partially overridable. */
export type LedArrayOptions = Partial<Omit<LedArrayConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };

/** Row or column of lamps acting as a level meter or as a bit-mask indicator. */
export class LedArray {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: LedArrayConfig;
  private value = 0;
  private bits = 0;
  /** Settings missing from `o` take the defaults (10 lamps, green/amber/red thirds); the theme merges over `GAUGE_LIGHT`. */
  constructor(o: LedArrayOptions = {}) {
    const { theme, ...rest } = o;
    const count = rest.count ?? 10;
    const colors = rest.colors ?? Array.from({ length: count }, (_, i) => (i < count * 0.6 ? "#16a34a" : i < count * 0.85 ? "#d97706" : "#dc2626"));
    this.config = { count, orientation: "horizontal", mode: "level", min: 0, max: 100, gap: 3, ...rest, colors, theme: { ...GAUGE_LIGHT, ...theme } };
  }
  /** Level-mode input; values outside [min, max] are clamped when the lit lamps are computed. */
  setValue(v: number): void { this.value = v; }
  /** Bits-mode input: bit i lights lamp i. Coerced to an unsigned 32-bit mask. */
  setBits(mask: number): void { this.bits = mask >>> 0; }
  /** Which LEDs are lit, index 0 = first (left / bottom). */
  lit(): boolean[] {
    const c = this.config;
    if (c.mode === "bits") return Array.from({ length: c.count }, (_, i) => ((this.bits >>> i) & 1) === 1);
    const n = Math.round(clamp((this.value - c.min) / (c.max - c.min), 0, 1) * c.count);
    return Array.from({ length: c.count }, (_, i) => i < n);
  }
  /** Colour of lamp `i`; indices past the end of `colors` reuse its last entry. */
  colorAt(i: number): string { const cs = this.config.colors; return cs[Math.min(i, cs.length - 1)] ?? "#16a34a"; }
  /** Draws the lamps with `gap` pixels between them, plus the label. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, lit = this.lit();
    const text: TextStyle = { color: th.mutedText, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    const labelRoom = c.label ? th.fontSize + 6 : 0;
    const horizontal = c.orientation === "horizontal";
    const span = horizontal ? width - 8 : height - 8 - labelRoom;
    const cell = (span - c.gap * (c.count - 1)) / c.count;
    for (let i = 0; i < c.count; i++) {
      const fill = { color: lit[i] ? this.colorAt(i) : th.ledOff };
      if (horizontal) p.rect(4 + i * (cell + c.gap), 4, cell, height - 8 - labelRoom, fill, undefined, 2);
      else p.rect(4, height - labelRoom - 4 - (i + 1) * cell - i * c.gap, width - 8, cell, fill, undefined, 2);
    }
    if (c.label) p.text(c.label, width / 2, height - 2, { ...text, align: "center", baseline: "bottom" });
  }
}
