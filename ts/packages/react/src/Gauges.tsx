// Mori.SkyScope — Mounts a gauge model in a GaugeView for the component's lifetime; apply pushes props into the model on every render.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useMemo, useRef, type CSSProperties } from "react";
import { AttitudeIndicator, Compass, Knob, Led, LedArray, LinearGauge, NumericDisplay, RadialGauge, Slider, Switch,
  type AttitudeOptions, type CompassOptions, type KnobOptions, type LedArrayOptions, type LedOptions, type LinearGaugeOptions, type NumericDisplayOptions, type RadialGaugeOptions, type SliderOptions, type SwitchOptions } from "@mori/skyscope-core";
import { GaugeView, bindKnob, bindSlider, bindSwitch, type GaugeLike } from "@mori/skyscope-render";
import { cx } from "./cx.js";

interface HostProps { className?: string | undefined; style?: CSSProperties | undefined }

/** Mounts a gauge model in a GaugeView for the component's lifetime; `apply` pushes props into the model on every render. */
function useGauge<G extends GaugeLike>(make: () => G, optionsKey: string, apply: (g: G) => void, bind?: (view: GaugeView<G>) => void) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<GaugeView<G> | null>(null);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  const gauge = useMemo(make, [optionsKey]);
  useEffect(() => {
    const v = new GaugeView(host.current!, gauge);
    bind?.(v);
    view.current = v;
    return () => { v.dispose(); view.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [gauge]);
  apply(gauge);
  useEffect(() => { view.current?.invalidate(); });
  return host;
}

const Host = ({ host, className, style }: { host: React.RefObject<HTMLDivElement | null> } & HostProps) => (
  <div ref={host} className={cx("skyscope-gauge-host", className)} style={{ width: "100%", height: "100%", minHeight: 60, ...style }} />
);
const key = (o: unknown): string => JSON.stringify(o ?? {});

/** Round dial with a settling needle. `value` is in the gauge's units (see `options.min`/`max`); the model is rebuilt when `options` change. `className`/`style` go on the host div. */
export function RadialGaugeView({ value, options, className, style }: { value: number; options?: RadialGaugeOptions | undefined } & HostProps) {
  const host = useGauge(() => new RadialGauge(options, value), key(options), (g) => g.setValue(value));
  return <Host host={host} className={className} style={style} />;
}
/** Horizontal or vertical bar gauge. `value` is in the gauge's units; the model is rebuilt when `options` change. */
export function LinearGaugeView({ value, options, className, style }: { value: number; options?: LinearGaugeOptions | undefined } & HostProps) {
  const host = useGauge(() => new LinearGauge(options, value), key(options), (g) => g.setValue(value));
  return <Host host={host} className={className} style={style} />;
}
/** Single indicator lamp; `on` lights it. The model is rebuilt when `options` change. */
export function LedView({ on, options, className, style }: { on: boolean; options?: LedOptions | undefined } & HostProps) {
  const host = useGauge(() => new Led(options, on), key(options), (g) => { g.on = on; });
  return <Host host={host} className={className} style={style} />;
}
/** Row or column of lamps. Drive it either as a bar graph with `value` (lamps lit up to the value in `options.min..max`) or as a bit field with `bits` (one lamp per bit); when both are given, `value` is applied last and wins. */
export function LedArrayView({ value, bits, options, className, style }: { value?: number | undefined; bits?: number | undefined; options?: LedArrayOptions | undefined } & HostProps) {
  const host = useGauge(() => new LedArray(options), key(options), (g) => { if (bits !== undefined) g.setBits(bits); if (value !== undefined) g.setValue(value); });
  return <Host host={host} className={className} style={style} />;
}
/** Seven-segment style readout; `null` shows the blank/invalid state. The model is rebuilt when `options` change. */
export function NumericDisplayView({ value, options, className, style }: { value: number | null; options?: NumericDisplayOptions | undefined } & HostProps) {
  const host = useGauge(() => new NumericDisplay(options), key(options), (g) => g.setValue(value));
  return <Host host={host} className={className} style={style} />;
}
/** Compass rose; `heading` is degrees clockwise from north and may exceed 0..360. */
export function CompassView({ heading, options, className, style }: { heading: number; options?: CompassOptions | undefined } & HostProps) {
  const host = useGauge(() => new Compass(options, heading), key(options), (g) => g.setHeading(heading));
  return <Host host={host} className={className} style={style} />;
}
/** Artificial horizon; `pitch` and `roll` are degrees (nose up and right wing down positive). */
export function AttitudeView({ pitch, roll, options, className, style }: { pitch: number; roll: number; options?: AttitudeOptions | undefined } & HostProps) {
  const host = useGauge(() => new AttitudeIndicator(options), key(options), (g) => g.set(pitch, roll));
  return <Host host={host} className={className} style={style} />;
}
/** Controlled rotary input: drag, wheel or arrow keys call `onChange` with the new value; `value` is pushed into the model except while the user is dragging. */
export function KnobView({ value, onChange, options, className, style }: { value: number; onChange?: ((v: number) => void) | undefined; options?: KnobOptions | undefined } & HostProps) {
  const cb = useRef(onChange); cb.current = onChange;
  const host = useGauge(() => new Knob(options, value), key(options), (g) => { if (!g.dragging) g.setValue(value); g.onChange = (v) => cb.current?.(v); }, bindKnob);
  return <Host host={host} className={className} style={style} />;
}
/** Controlled toggle: a click, space or Enter calls `onChange` with the new state; `on` is pushed into the model on every render. */
export function SwitchView({ on, onChange, options, className, style }: { on: boolean; onChange?: ((on: boolean) => void) | undefined; options?: SwitchOptions | undefined } & HostProps) {
  const cb = useRef(onChange); cb.current = onChange;
  const host = useGauge(() => new Switch(options, on), key(options), (g) => { g.on = on; g.onChange = (v) => cb.current?.(v); }, bindSwitch);
  return <Host host={host} className={className} style={style} />;
}
/** Controlled linear input: drag, wheel or arrow keys call `onChange` with the new value; `value` is pushed into the model except while the user is dragging. */
export function SliderView({ value, onChange, options, className, style }: { value: number; onChange?: ((v: number) => void) | undefined; options?: SliderOptions | undefined } & HostProps) {
  const cb = useRef(onChange); cb.current = onChange;
  const host = useGauge(() => new Slider(options, value), key(options), (g) => { if (!g.dragging) g.setValue(value); g.onChange = (v) => cb.current?.(v); }, bindSlider);
  return <Host host={host} className={className} style={style} />;
}
