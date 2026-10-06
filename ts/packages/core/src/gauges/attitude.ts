// Mori.SkyScope — Artificial horizon: pitch (deg, nose up positive) and roll (deg, right wing down positive).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter, TextStyle } from "../paint/painter.js";
import { GAUGE_LIGHT, SmoothedValue, arcAngle, degToRad, polar, type GaugeTheme } from "./common.js";

/** Resolved settings. `sky`/`ground`/`horizon`/`symbol` are CSS colours; `pitchLadderDeg` is the spacing of pitch lines, `visiblePitchDeg` the pitch span from centre to rim, `damping` the smoothing time constant in seconds. */
export interface AttitudeConfig { sky: string; ground: string; horizon: string; symbol: string; pitchLadderDeg: number; visiblePitchDeg: number; damping: number; theme: GaugeTheme }
/** Constructor options: every setting optional, theme partially overridable. */
export type AttitudeOptions = Partial<Omit<AttitudeConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };

/** Artificial horizon: pitch (deg, nose up positive) and roll (deg, right wing down positive). */
export class AttitudeIndicator {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: AttitudeConfig;
  /** Damped pitch in degrees, nose up positive. */
  readonly pitch: SmoothedValue;
  /** Damped roll in degrees, kept in [0, 360) and damped across the seam. */
  readonly roll: SmoothedValue;
  /** Settings missing from `o` take the defaults; the theme merges over `GAUGE_LIGHT`. */
  constructor(o: AttitudeOptions = {}) {
    const { theme, ...rest } = o;
    this.config = { sky: "#3b82f6", ground: "#92400e", horizon: "#ffffff", symbol: "#facc15", pitchLadderDeg: 10, visiblePitchDeg: 45, damping: 0.12, ...rest, theme: { ...GAUGE_LIGHT, ...theme } };
    this.pitch = new SmoothedValue(0, this.config.damping); this.roll = new SmoothedValue(0, this.config.damping, 360);
  }
  /** New target attitude in degrees; roll is normalised to [0, 360). */
  set(pitchDeg: number, rollDeg: number): void { this.pitch.set(pitchDeg); this.roll.set(((rollDeg % 360) + 360) % 360); }
  /** Advances both damped values by `dt` seconds. */
  step(dt: number): void { this.pitch.step(dt); this.roll.step(dt); }
  /** True while either value is still converging; keep calling `step` and redrawing until false. */
  get animating(): boolean { return !this.pitch.settled || !this.roll.settled; }
  /** Centre and radius of the instrument for a control size (92 % of the half-extent). */
  layout(width: number, height: number): { cx: number; cy: number; r: number } { return { cx: width / 2, cy: height / 2, r: Math.min(width, height) / 2 * 0.92 }; }
  /** Pixels per degree of pitch. */
  pxPerDeg(r: number): number { return r / this.config.visiblePitchDeg; }
  /** Ground polygon (screen space) for the displayed attitude: a big rectangle below the horizon, rotated by −roll about the centre and shifted by pitch. */
  groundPolygon(l: { cx: number; cy: number; r: number }): number[] {
    const roll = this.roll.displayed, dy = this.pitch.displayed * this.pxPerDeg(l.r), R = l.r * 3;
    const c = Math.cos(degToRad(-roll)), s = Math.sin(degToRad(-roll));
    const pts = [[-R, dy], [R, dy], [R, dy + R], [-R, dy + R]];
    const out: number[] = [];
    for (const [x, y] of pts) out.push(l.cx + x! * c - y! * s, l.cy + x! * s + y! * c);
    return out;
  }
  /** Renders the displayed (damped) attitude, clearing to the theme background first. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, l = this.layout(width, height);
    const text: TextStyle = { color: c.horizon, family: th.fontFamily, size: th.fontSize };
    p.clear(th.background);
    p.circle(l.cx, l.cy, l.r, { color: c.sky });
    p.save();
    p.clipRect(l.cx - l.r, l.cy - l.r, 2 * l.r, 2 * l.r);
    p.polygon(this.groundPolygon(l), { color: c.ground });
    p.save(); p.translate(l.cx, l.cy); p.rotate(degToRad(-this.roll.displayed)); p.translate(0, this.pitch.displayed * this.pxPerDeg(l.r));
    p.line(-l.r, 0, l.r, 0, { color: c.horizon, width: 2 });
    const first = -Math.floor(c.visiblePitchDeg / c.pitchLadderDeg) * c.pitchLadderDeg;
    for (let d = first; d <= c.visiblePitchDeg; d += c.pitchLadderDeg) {
      if (d === 0) continue;
      const y = -d * this.pxPerDeg(l.r), w = l.r * (d % (2 * c.pitchLadderDeg) === 0 ? 0.3 : 0.18);
      p.line(-w, y, w, y, { color: c.horizon, width: 1 });
      p.text(String(Math.abs(d)), w + 6, y, { ...text, baseline: "middle" });
    }
    p.restore();
    p.restore();
    for (const d of [-60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60]) {
      const o = polar(l.cx, l.cy, l.r, d), i = polar(l.cx, l.cy, l.r * (d % 30 === 0 ? 0.9 : 0.94), d);
      p.line(o.x, o.y, i.x, i.y, { color: c.horizon, width: d === 0 ? 3 : 1.5 });
    }
    const rp = polar(l.cx, l.cy, l.r * 0.88, -this.roll.displayed);
    p.polygon([rp.x, rp.y, rp.x - l.r * 0.05, rp.y + l.r * 0.09, rp.x + l.r * 0.05, rp.y + l.r * 0.09], { color: c.symbol });
    const s = l.r * 0.25;
    p.line(l.cx - s * 1.6, l.cy, l.cx - s * 0.5, l.cy, { color: c.symbol, width: 4 });
    p.line(l.cx + s * 0.5, l.cy, l.cx + s * 1.6, l.cy, { color: c.symbol, width: 4 });
    p.circle(l.cx, l.cy, s * 0.12, { color: c.symbol });
    p.arc(l.cx, l.cy, l.r, arcAngle(0), arcAngle(360), { color: th.track, width: 2 });
  }
}
