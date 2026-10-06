// Mori.SkyScope — Blazor bridge for the gauges and inputs: mounts a gauge view and relays value changes to .NET.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { AttitudeIndicator, Compass, Knob, Led, LedArray, LinearGauge, NumericDisplay, RadialGauge, Slider, Switch } from "@mori/skyscope-core";
import { GaugeView, bindKnob, bindSlider, bindSwitch, type GaugeLike } from "@mori/skyscope-render";

/** Which gauge model `mountGauge` builds; knob, switch and slider are inputs that report changes back to .NET. */
export type GaugeKind = "radial" | "linear" | "led" | "ledArray" | "numeric" | "compass" | "attitude" | "knob" | "switch" | "slider";

/** .NET object reference with a [JSInvokable] OnChange(double) method (inputs only). */
interface DotNetRef { invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown> }

/** What `mountGauge` returns to .NET; every method is invoked through JS interop. */
export interface GaugeHandle {
  /** Push state: {value} | {bits} | {heading} | {pitch, roll} | {on} depending on the gauge kind. */
  set(stateJson: string): void;
  /** Tears down the view and removes it from the element. */
  dispose(): void;
}

/**
 * Mounts one gauge in `element`. `optionsJson` is the gauge's options object (null = defaults). For inputs, `dotnet`
 * receives `OnChange(value)` on every user change; it is ignored for display-only kinds.
 */
export function mountGauge(element: HTMLElement, kind: GaugeKind, optionsJson: string | null, dotnet: DotNetRef | null): GaugeHandle {
  const o = optionsJson ? (JSON.parse(optionsJson) as never) : ({} as never);
  const notify = (v: unknown): void => { void dotnet?.invokeMethodAsync("OnChange", v); };
  let gauge: GaugeLike;
  let view: GaugeView;
  switch (kind) {
    case "radial": gauge = new RadialGauge(o); break;
    case "linear": gauge = new LinearGauge(o); break;
    case "led": gauge = new Led(o); break;
    case "ledArray": gauge = new LedArray(o); break;
    case "numeric": gauge = new NumericDisplay(o); break;
    case "compass": gauge = new Compass(o); break;
    case "attitude": gauge = new AttitudeIndicator(o); break;
    case "knob": { const k = new Knob(o); k.onChange = notify; gauge = k; break; }
    case "switch": { const s = new Switch(o); s.onChange = notify; gauge = s; break; }
    case "slider": { const s = new Slider(o); s.onChange = notify; gauge = s; break; }
  }
  view = new GaugeView(element, gauge);
  if (gauge instanceof Knob) bindKnob(view as GaugeView<Knob>);
  else if (gauge instanceof Slider) bindSlider(view as GaugeView<Slider>);
  else if (gauge instanceof Switch) bindSwitch(view as GaugeView<Switch>);
  return {
    set(stateJson) {
      const s = JSON.parse(stateJson) as { value?: number | null; bits?: number; heading?: number; pitch?: number; roll?: number; on?: boolean };
      const g = gauge;
      if (g instanceof RadialGauge || g instanceof LinearGauge) g.setValue(s.value ?? 0);
      else if (g instanceof LedArray) { if (s.bits !== undefined) g.setBits(s.bits); else g.setValue(s.value ?? 0); }
      else if (g instanceof NumericDisplay) g.setValue(s.value ?? null);
      else if (g instanceof Compass) g.setHeading(s.heading ?? 0);
      else if (g instanceof AttitudeIndicator) g.set(s.pitch ?? 0, s.roll ?? 0);
      else if (g instanceof Knob || g instanceof Slider) { if (!g.dragging) g.setValue(s.value ?? 0); }
      else if (g instanceof Switch) g.set(s.on ?? false);
      else if (g instanceof Led) g.on = s.on ?? false;
      view.invalidate();
    },
    dispose() { view.dispose(); },
  };
}
