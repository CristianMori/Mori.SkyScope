// Mori.SkyScope — Toggle switch and slider inputs.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Painter } from "../paint/painter.js";
import { formatNumber } from "../scales/ticks.js";
import { GAUGE_LIGHT, autoDecimals, clamp, type GaugeTheme } from "./common.js";
import { snapValue } from "./knob.js";

/** Resolved settings. `onLabel`/`offLabel` are printed inside the toggle; `label` is the caption below. */
export interface SwitchConfig { label?: string | undefined; onLabel: string; offLabel: string; theme: GaugeTheme }
/** Constructor options: every setting optional, theme partially overridable. */
export type SwitchOptions = Partial<Omit<SwitchConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };

/** Toggle switch. Click (or Space/Enter) flips it. */
export class Switch {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: SwitchConfig;
  /** Current state. Assigning it directly bypasses `onChange`; use `set` or `toggle` to notify. */
  on: boolean;
  /** Called with the new state after `toggle` or a `set` that changes it. */
  onChange: ((on: boolean) => void) | null = null;
  /** Settings missing from `o` take the defaults; the theme merges over `GAUGE_LIGHT`. */
  constructor(o: SwitchOptions = {}, on = false) { const { theme, ...rest } = o; this.config = { onLabel: "ON", offLabel: "OFF", ...rest, theme: { ...GAUGE_LIGHT, ...theme } }; this.on = on; }
  /** Flips the state and notifies `onChange`. */
  toggle(): void { this.on = !this.on; this.onChange?.(this.on); }
  /** Sets the state, notifying only when it actually changes. */
  set(on: boolean): void { if (on !== this.on) this.toggle(); }
  /** Draws the pill toggle centred in the control, plus the label. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, labelRoom = c.label ? th.fontSize + 6 : 0;
    const w = Math.min(width - 8, 64), h = Math.min(height - labelRoom - 8, w / 2), x = (width - w) / 2, y = (height - labelRoom - h) / 2, r = h / 2;
    p.clear(th.background);
    p.rect(x, y, w, h, { color: this.on ? th.accent : th.track }, undefined, r);
    p.circle(this.on ? x + w - r : x + r, y + r, r - 3, { color: "#ffffff" }, { color: th.minorTick, width: 1 });
    p.text(this.on ? c.onLabel : c.offLabel, this.on ? x + r * 0.6 : x + w - r * 0.6, y + r, { color: this.on ? "#ffffff" : th.mutedText, family: th.fontFamily, size: th.fontSize * 0.9, weight: "bold", align: this.on ? "left" : "right", baseline: "middle" });
    if (c.label) p.text(c.label, width / 2, height - 2, { color: th.mutedText, family: th.fontFamily, size: th.fontSize, align: "center", baseline: "bottom" });
  }
}

/** Resolved settings. `step` 0 means continuous; "vertical" puts min at the bottom; `decimals` undefined picks `autoDecimals`. */
export interface SliderConfig { min: number; max: number; step: number; orientation: "horizontal" | "vertical"; label?: string | undefined; unit?: string | undefined; decimals?: number | undefined; theme: GaugeTheme }
/** Constructor options: every setting optional, theme partially overridable. */
export type SliderOptions = Partial<Omit<SliderConfig, "theme">> & { theme?: Partial<GaugeTheme> | undefined };
/** Track ends along the drag axis (`x0` at min, `x1` at max; x0 > x1 for a vertical slider) and `y`, its position on the cross axis (an x coordinate when vertical). */
export interface SliderTrack { x0: number; x1: number; y: number; horizontal: boolean }

/** Linear input with a drag handle; the track maps to [min, max]. */
export class Slider {
  /** Settings resolved against the defaults and `GAUGE_LIGHT`. */
  readonly config: SliderConfig;
  /** Current value, always snapped and clamped to the configured range. */
  value: number;
  /** True between `pointerDown` and `pointerUp`. */
  dragging = false;
  /** Called with the new value whenever it changes, by pointer, `nudge` or `setValue`. */
  onChange: ((v: number) => void) | null = null;
  /** `initial` defaults to `min`. */
  constructor(o: SliderOptions = {}, initial?: number) { const { theme, ...rest } = o; this.config = { min: 0, max: 100, step: 0, orientation: "horizontal", ...rest, theme: { ...GAUGE_LIGHT, ...theme } }; this.value = initial ?? this.config.min; }
  /** Track end points along the drag axis (x0 = min end, x1 = max end) and its cross position. */
  track(width: number, height: number): SliderTrack {
    const labelRoom = this.config.label ? this.config.theme.fontSize + 6 : 0;
    return this.config.orientation === "horizontal" ? { x0: 14, x1: width - 14, y: (height - labelRoom) / 2, horizontal: true } : { x0: height - 14 - labelRoom, x1: 14, y: width / 2, horizontal: false };
  }
  /** Value for a pointer position in control pixels, projected onto the track, clamped and snapped. */
  valueFromPoint(width: number, height: number, x: number, y: number): number {
    const c = this.config, t = this.track(width, height), pos = t.horizontal ? x : y;
    return snapValue(c.min + clamp((pos - t.x0) / (t.x1 - t.x0), 0, 1) * (c.max - c.min), c.min, c.max, c.step);
  }
  private update(v: number): void { if (v !== this.value) { this.value = v; this.onChange?.(v); } }
  /** Programmatic set (snapped and clamped). */
  setValue(v: number): void { this.update(snapValue(v, this.config.min, this.config.max, this.config.step)); }
  /** Starts a drag and jumps the value to the pointer; coordinates are control pixels. */
  pointerDown(width: number, height: number, x: number, y: number): void { this.dragging = true; this.update(this.valueFromPoint(width, height, x, y)); }
  /** Updates the value while dragging; ignored otherwise. */
  pointerMove(width: number, height: number, x: number, y: number): void { if (this.dragging) this.update(this.valueFromPoint(width, height, x, y)); }
  /** Ends the drag. */
  pointerUp(): void { this.dragging = false; }
  /** Wheel or arrow keys: one step (or 1 % of the range) per notch, in the sign of `direction`. */
  nudge(direction: number): void { const c = this.config, s = c.step > 0 ? c.step : (c.max - c.min) / 100; this.update(snapValue(this.value + Math.sign(direction) * s, c.min, c.max, c.step)); }
  /** Draws the track, the filled part up to the value, the handle, the readout and the label. */
  draw(p: Painter, width: number, height: number): void {
    const c = this.config, th = c.theme, t = this.track(width, height), f = (this.value - c.min) / (c.max - c.min), pos = t.x0 + f * (t.x1 - t.x0);
    p.clear(th.background);
    const seg = (a: number, b: number, color: string): void => { if (t.horizontal) p.line(a, t.y, b, t.y, { color, width: 6, cap: "round" }); else p.line(t.y, a, t.y, b, { color, width: 6, cap: "round" }); };
    seg(t.x0, t.x1, th.track);
    seg(t.x0, pos, th.accent);
    if (t.horizontal) p.circle(pos, t.y, 9, { color: "#ffffff" }, { color: th.accent, width: 2 }); else p.circle(t.y, pos, 9, { color: "#ffffff" }, { color: th.accent, width: 2 });
    const label = formatNumber(this.value, c.decimals ?? autoDecimals(c.min, c.max)) + (c.unit ? ` ${c.unit}` : "");
    if (t.horizontal) p.text(label, pos, t.y - 14, { color: th.value, family: th.fontFamily, size: th.fontSize, weight: "bold", align: "center", baseline: "bottom" });
    else p.text(label, t.y + 14, pos, { color: th.value, family: th.fontFamily, size: th.fontSize, weight: "bold", baseline: "middle" });
    if (c.label) p.text(c.label, width / 2, height - 2, { color: th.mutedText, family: th.fontFamily, size: th.fontSize, align: "center", baseline: "bottom" });
  }
}
