// Mori.SkyScope — Template source plugin: declares one regular-rate channel and pushes a counting ramp from a timer.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Source, SourceContext, SourceFactory } from "@mori/skyscope-core";

/**
 * Template source plugin: declares one regular-rate channel and pushes a counting ramp from a timer.
 * Copy this package, rename it, and replace the timer with your protocol. See docs/PLUGINS.md.
 */
/** Configuration of `CounterSource`; every field has a default. */
export interface CounterConfig {
  /** Id of the declared channel (default 1). */
  channelId?: number | undefined;
  /** Samples per second. */
  rate?: number | undefined;
  /** Milliseconds of samples per frame. */
  batchMs?: number | undefined;
}

/** The template source: one channel named "counter" whose values are 0, 1, 2, … at `rate` Hz, pushed one frame per `batchMs`. */
export class CounterSource implements Source {
  /** Registry type id of this source. */
  readonly type = "counter";
  private timer: ReturnType<typeof setInterval> | null = null;
  private n = 0;
  private seq = 0;
  /** Exposed so hosts (and tests) can drive the source without a timer. */
  push: (() => void) | null = null;

  /** Declares the channel, builds `push` and starts the timer; each frame starts at `ctx.clock.now()`. */
  start(ctx: SourceContext, config: CounterConfig = {}): void {
    const id = config.channelId ?? 1, rate = config.rate ?? 100, batchMs = config.batchMs ?? 50, dt = 1 / rate;
    ctx.signals.declareChannel({ id, name: "counter", unit: "count", timing: "regular", rate });
    const count = Math.max(1, Math.round(rate * batchMs / 1000));
    this.push = () => {
      const tStart = ctx.clock.now();
      const values = new Float32Array(count);
      for (let i = 0; i < count; i++) values[i] = this.n++;
      ctx.signals.pushFrame({ seq: this.seq++, t0: tStart, channels: [{ id, encoding: "regular", tStart, dt, values }] });
    };
    this.timer = setInterval(this.push, batchMs);
    ctx.log("info", `counter: ${rate} Hz on channel ${id}`);
  }

  /** Stops the timer and clears `push`; the counter value is kept so a restart continues the ramp. */
  stop(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null; this.push = null;
  }
}

/** Registry entry for `CounterSource`, with a JSON schema for its configuration. */
export const counterFactory: SourceFactory = {
  type: "counter",
  displayName: "Counter (template)",
  capabilities: ["signals"],
  configSchema: { type: "object", properties: { channelId: { type: "integer" }, rate: { type: "number" }, batchMs: { type: "number" } } },
  create: () => new CounterSource(),
};
