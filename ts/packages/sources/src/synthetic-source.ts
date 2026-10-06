// Mori.SkyScope — Synthetic source: deterministic waveforms pushed as frames from a timer, for demos and benches.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { synthesize, type ChannelKind, type SynthSpec, type Source, type SourceContext, type SourceFactory, type FrameChannel } from "@mori/skyscope-core";

/** One generated channel: the waveform spec plus its channel `id` (required, unique) and optional `name` (default `ch<id>`), `unit` and `kind`. */
export interface SyntheticChannelConfig extends SynthSpec { id: number; name?: string | undefined; unit?: string | undefined; kind?: ChannelKind | undefined }

/** Configuration of `SyntheticSource`; every field has a default. */
export interface SyntheticSourceConfig {
  /** Explicit channels, or a count to auto-generate mixed waveforms at staggered frequencies. */
  channels?: SyntheticChannelConfig[] | number | undefined;
  /** Samples per second per channel. */
  rate?: number | undefined;
  /** Frame period. */
  batchMs?: number | undefined;
  /** Noise seed; auto-generated channels use `seed + index` so each has its own stream. */
  seed?: number | undefined;
  /** Send as quantized i16 (enc 2) instead of f32 — halves bandwidth, exercises the dequantizer. */
  quantized?: boolean | undefined;
}

/** Builds `count` channels (ids 1..count) cycling through the five waveforms with staggered frequency, amplitude, offset, phase and noise. */
export function autoChannels(count: number, seed = 0): SyntheticChannelConfig[] {
  const waves: SynthSpec["waveform"][] = ["sine", "triangle", "sawtooth", "square", "noise"];
  return Array.from({ length: count }, (_, i) => ({
    id: i + 1, name: `ch${i + 1}`, waveform: waves[i % waves.length]!,
    frequency: 0.1 + (i % 10) * 0.37, amplitude: 1 + (i % 4), offset: (i % 3) - 1, phase: i * 0.5, noise: 0.05 + (i % 5) * 0.02, seed: seed + i,
  }));
}

/** Generates frames on a timer — the demo/bench source. Deterministic for a given seed and start time. */
export class SyntheticSource implements Source {
  /** Registry type id of this source. */
  readonly type = "synthetic";
  private timer: ReturnType<typeof setInterval> | null = null;
  private seq = 0;
  private nextT = 0;
  private index = 0;

  /** Declares the channels and starts the frame timer. Frame times continue from `ctx.clock.now()` at the sample rate, independent of timer jitter. Restarts if already running. */
  start(ctx: SourceContext, config: SyntheticSourceConfig = {}): void {
    const rate = config.rate ?? 1000, batchMs = config.batchMs ?? 20, dt = 1 / rate;
    const channels = typeof config.channels === "number" ? autoChannels(config.channels, config.seed) : config.channels ?? autoChannels(8, config.seed);
    for (const c of channels) ctx.signals.declareChannel({ id: c.id, name: c.name ?? `ch${c.id}`, unit: c.unit, kind: c.kind, rate, timing: "regular" });
    const count = Math.max(1, Math.round(rate * batchMs / 1000));
    this.nextT = ctx.clock.now();
    this.stop();
    this.timer = setInterval(() => {
      const tStart = this.nextT;
      const frame = { seq: this.seq++, t0: tStart, channels: channels.map((c): FrameChannel => {
        const values = synthesize(c, tStart, dt, count, this.index);
        if (!config.quantized) return { id: c.id, encoding: "regular", tStart, dt, values };
        const amp = (c.amplitude ?? 1) + (c.noise ?? 0), offset = Math.fround(c.offset ?? 0), scale = Math.fround(amp / 32767);
        const q = Int16Array.from(values, (v) => Math.max(-32768, Math.min(32767, Math.round((v - offset) / scale))));
        return { id: c.id, encoding: "quantized", tStart, dt, scale, offset, q };
      }) };
      this.nextT = tStart + count * dt;
      this.index += count;
      ctx.signals.pushFrame(frame);
    }, batchMs);
  }

  /** Stops the frame timer; the sample index and sequence number are kept so a restart continues the signal. */
  stop(): void {
    if (this.timer !== null) { clearInterval(this.timer); this.timer = null; }
  }
}

/** Registry entry for `SyntheticSource`, with a JSON schema for its configuration. */
export const syntheticSourceFactory: SourceFactory = {
  type: "synthetic",
  displayName: "Synthetic signals",
  capabilities: ["signals"],
  configSchema: {
    type: "object",
    properties: {
      channels: { oneOf: [{ type: "integer", minimum: 1 }, { type: "array" }] },
      rate: { type: "number", minimum: 1, default: 1000 },
      batchMs: { type: "number", minimum: 1, default: 20 },
      seed: { type: "integer", default: 0 },
      quantized: { type: "boolean", default: false },
    },
  },
  create: () => new SyntheticSource(),
};
