// Mori.SkyScope — Compass gauge: needle or rotating card, damped across the 0/360 seam.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { GAUGE_LIGHT, SmoothedValue, arcAngle, polar, type GaugeTheme } from "./common.js";

/** Configuration of a compass gauge: heading presentation, caption, damping and theme. */
export interface CompassConfig {
  /** `label` is drawn above the centre; `damping` is the smoothing time constant in seconds; `showValue` toggles the numeric heading readout; `theme` is resolved. */
  /** "needle": fixed card, rotating needle. "card": rotating card, fixed lubber line (aircraft style). */
  mode: "needle" | "card"; label?: string | undefined; damping: number; showValue: boolean; theme: GaugeTheme;
}
/** Constructor options: every setting optional, theme partially overridable. */
export type CompassOptions = Partial<Omit<CompassConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };

/** Heading indicator, 0–360° with wrap-aware damping. */
export class Compass {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: CompassConfig;
  /** Damped heading in degrees, kept in [0, 360). */
  readonly heading: SmoothedValue;
  /** `initial` is the starting heading in degrees; other settings missing from `o` take the defaults. */
  constructor(o: CompassOptions = {}, initial = 0) {
    const { theme, ...rest } = o;
    this.config = { mode: "needle", damping: 0.2, showValue: true, ...rest, theme: { ...GAUGE_LIGHT, ...theme } };
    this.heading = new SmoothedValue(initial, this.config.damping, 360);
  }
  /** New target heading in degrees; any value is normalised to [0, 360). */
  setHeading(deg: number): void { this.heading.set(((deg % 360) + 360) % 360); }
  /** Advances the damping by `dt` seconds and returns the displayed heading. */
  step(dt: number): number { return this.heading.step(dt); }
  /** True while the heading is still converging; keep calling `step` and redrawing until false. */
  get animating(): boolean { return !this.heading.settled; }
  /** Centre and radius of the card for a control size (92 % of the half-extent). */
  layout(width: number, height: number): { cx: number; cy: number; r: number } { return { cx: width / 2, cy: height / 2, r: Math.min(width, height) / 2 * 0.92 }; }

  /** Renders the card, needle or lubber line, readout and label for the displayed heading. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, l = this.layout(width, height), h = this.heading.displayed;
    const text: TextStyle = { color: th.text, family: th.fontFamily, size: th.fontSize };
    const rot = c.mode === "card" ? -h : 0;
    p.clear(th.background);
    p.circle(l.cx, l.cy, l.r, { color: th.face }, { color: th.track, width: 2 });
    for (let d = 0; d < 360; d += 10) {
      const a = d + rot, major = d % 30 === 0;
      const o = polar(l.cx, l.cy, l.r * 0.96, a), i = polar(l.cx, l.cy, l.r * (major ? 0.84 : 0.9), a);
      p.line(o.x, o.y, i.x, i.y, { color: major ? th.tick : th.minorTick, width: major ? 2 : 1 });
      if (d % 90 === 0) { const lp = polar(l.cx, l.cy, l.r * 0.7, a); p.text("NESW"[d / 90]!, lp.x, lp.y, { ...text, size: th.fontSize * 1.4, weight: "bold", align: "center", baseline: "middle", color: d === 0 ? th.needle : th.text }); }
      else if (major) { const lp = polar(l.cx, l.cy, l.r * 0.72, a); p.text(String(d / 10), lp.x, lp.y, { ...text, color: th.mutedText, align: "center", baseline: "middle" }); }
    }
    if (c.mode === "needle") {
      const tip = polar(l.cx, l.cy, l.r * 0.8, h), tail = polar(l.cx, l.cy, l.r * 0.8, h + 180), w = l.r * 0.07;
      const left = polar(l.cx, l.cy, w, h - 90), right = polar(l.cx, l.cy, w, h + 90);
      p.polygon([tip.x, tip.y, right.x, right.y, left.x, left.y], { color: th.needle });
      p.polygon([tail.x, tail.y, left.x, left.y, right.x, right.y], { color: th.hub });
      p.circle(l.cx, l.cy, l.r * 0.06, { color: th.hub });
    } else {
      const t = polar(l.cx, l.cy, l.r, 0);
      p.polygon([t.x, t.y - 2, t.x - l.r * 0.06, t.y - l.r * 0.14, t.x + l.r * 0.06, t.y - l.r * 0.14], { color: th.needle });
      p.line(l.cx, l.cy - l.r * 0.5, l.cx, l.cy + l.r * 0.5, { color: th.accent, width: 2 });
      p.line(l.cx - l.r * 0.3, l.cy, l.cx + l.r * 0.3, l.cy, { color: th.accent, width: 2 });
    }
    p.arc(l.cx, l.cy, l.r * 0.97, arcAngle(0), arcAngle(360), { color: th.track, width: 1 });
    if (c.showValue) p.text(`${Math.round(h) % 360}°`, l.cx, l.cy + l.r * 0.45, { ...text, color: th.value, size: th.fontSize * 1.5, weight: "bold", align: "center", baseline: "middle" });
    if (c.label) p.text(c.label, l.cx, l.cy - l.r * 0.42, { ...text, color: th.mutedText, align: "center", baseline: "middle" });
  }
}
