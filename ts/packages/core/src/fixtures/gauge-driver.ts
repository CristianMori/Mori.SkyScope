// Mori.SkyScope — Fixture driver for the gauges: configuration parsing, stepping, layout and drawing-parity queries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { RadialGauge } from "../gauges/radial.js";
import { LinearGauge } from "../gauges/linear.js";
import { Led, LedArray } from "../gauges/led.js";
import { NumericDisplay, sevenSegmentMask } from "../gauges/numeric.js";
import { Compass } from "../gauges/compass.js";
import { AttitudeIndicator } from "../gauges/attitude.js";
import { Knob } from "../gauges/knob.js";
import { Slider, Switch } from "../gauges/switch-slider.js";
import { RecordingPainter } from "../paint/recording-painter.js";
import { r9, rRect } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
type Any = RadialGauge | LinearGauge | Led | LedArray | NumericDisplay | Compass | AttitudeIndicator | Knob | Switch | Slider;
interface State { kind: string; g: Any; width: number; height: number; queries: unknown[] }
const num = (v: unknown): number => v as number;
const arr9 = (a: ArrayLike<number>): number[] => Array.from(a, r9);

/**
 * Gauges: `setup.gauge` picks the type (radial, linear, led, ledArray, numeric, compass, attitude, knob, switch,
 * slider), with options, initial value and size.
 */
export const gaugeDriver: FixtureDriver<State> = {
  component: "gauges",
  create(setup) {
    const o = (setup.options ?? {}) as never, init = setup.initial as number | undefined, kind = setup.gauge as string;
    const g: Any = kind === "radial" ? new RadialGauge(o, init) : kind === "linear" ? new LinearGauge(o, init) : kind === "led" ? new Led(o) : kind === "ledArray" ? new LedArray(o)
      : kind === "numeric" ? new NumericDisplay(o) : kind === "compass" ? new Compass(o, init ?? 0) : kind === "attitude" ? new AttitudeIndicator(o) : kind === "knob" ? new Knob(o, init)
      : kind === "switch" ? new Switch(o) : new Slider(o, init);
    return { kind, g, width: num(setup.width), height: num(setup.height), queries: [] };
  },
  /**
   * Steps: set (value, bits, heading, pitch and roll, or on, by gauge type), step (animation dt), setMode,
   * pointerDown, pointerMove, pointerUp, nudge, toggle; queries: angleFor, ticks, needle, format, layout, displayed,
   * settled, posFor, mask, text, lit, valueFromPoint, value, groundPolygon, on, draw.
   */
  step(s, step: Step) {
    const { g, queries, width: w, height: h } = s;
    switch (step.type) {
      case "set":
        if (g instanceof RadialGauge || g instanceof LinearGauge) g.setValue(num(step.value));
        else if (g instanceof LedArray) { if ("bits" in step) g.setBits(num(step.bits)); else g.setValue(num(step.value)); }
        else if (g instanceof NumericDisplay) g.setValue(step.value as number | null);
        else if (g instanceof Compass) g.setHeading(num(step.heading));
        else if (g instanceof AttitudeIndicator) g.set(num(step.pitch), num(step.roll));
        else if (g instanceof Knob || g instanceof Slider) g.setValue(num(step.value));
        else if (g instanceof Switch) g.set(step.on as boolean);
        else if (g instanceof Led) g.on = step.on as boolean;
        break;
      case "step": if ("step" in g) (g as { step(dt: number): unknown }).step(num(step.dt)); break;
      case "setMode": (g as LedArray).config.mode = step.mode as "level" | "bits"; break;
      case "pointerDown": if (g instanceof Knob) g.pointerDown(g.layout(w, h), num(step.x), num(step.y)); else (g as Slider).pointerDown(w, h, num(step.x), num(step.y)); break;
      case "pointerMove": if (g instanceof Knob) g.pointerMove(g.layout(w, h), num(step.x), num(step.y)); else (g as Slider).pointerMove(w, h, num(step.x), num(step.y)); break;
      case "pointerUp": (g as Knob | Slider).pointerUp(); break;
      case "nudge": (g as Knob | Slider).nudge(num(step.dir)); break;
      case "toggle": (g as Switch).toggle(); break;
      case "query":
        if ("angleFor" in step) queries.push(r9((g as RadialGauge | Knob).angleFor(num(step.angleFor))));
        else if ("ticks" in step) queries.push((g as RadialGauge | LinearGauge).ticks().map((t) => ({ value: r9(t.value), major: t.major, label: t.label })));
        else if ("needle" in step) { const rg = g as RadialGauge; queries.push(arr9(rg.needle(rg.layout(w, h)))); }
        else if ("format" in step) queries.push((g as RadialGauge | LinearGauge).formatValue(num(step.format)));
        else if ("layout" in step) { const l = (g as RadialGauge | LinearGauge).layout(w, h) as { cx?: number; cy?: number; r?: number; track?: { x: number; y: number; w: number; h: number }; horizontal?: boolean }; queries.push(l.track ? { track: rRect(l.track), horizontal: l.horizontal } : { cx: r9(l.cx!), cy: r9(l.cy!), r: r9(l.r!) }); }
        else if ("displayed" in step) queries.push(r9(g instanceof Compass ? g.heading.displayed : (g as RadialGauge | LinearGauge).value.displayed));
        else if ("settled" in step) queries.push(g instanceof Compass ? g.heading.settled : (g as RadialGauge | LinearGauge).value.settled);
        else if ("posFor" in step) { const lg = g as LinearGauge; queries.push(r9(lg.posFor(lg.layout(w, h), num(step.posFor)))); }
        else if ("mask" in step) queries.push(sevenSegmentMask(step.mask as string));
        else if ("text" in step) queries.push((g as NumericDisplay).text());
        else if ("lit" in step) queries.push((g as LedArray).lit());
        else if ("valueFromPoint" in step) { const [x, y] = step.valueFromPoint as [number, number]; queries.push(r9(g instanceof Knob ? g.valueFromPoint(g.layout(w, h), x, y) : (g as Slider).valueFromPoint(w, h, x, y))); }
        else if ("value" in step) queries.push(r9((g as Knob | Slider).value));
        else if ("groundPolygon" in step) { const a = g as AttitudeIndicator; queries.push(arr9(a.groundPolygon(a.layout(w, h)))); }
        else if ("on" in step) queries.push((g as Switch | Led).on);
        else if ("draw" in step) { const p = new RecordingPainter(w, h); g.draw(p, w, h); queries.push(p.ops); }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
