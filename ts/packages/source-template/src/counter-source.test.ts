// Mori.SkyScope — Runs the template counter source against a signal store with a manual clock.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, expect, it } from "vitest";
import { ManualClock, NullLayerSink, SignalStore, SourceRegistry, type SourceContext } from "@cmori/skyscope-core";
import { CounterSource, counterFactory } from "./index.js";

describe("counter source (template)", () => {
  it("declares its channel and pushes counting frames into a store", () => {
    const store = new SignalStore({ retentionSeconds: 10 });
    const clock = new ManualClock(100);
    const ctx: SourceContext = { signals: store, layers: new NullLayerSink(), clock, log: () => {} };
    const src = new CounterSource();
    src.start(ctx, { channelId: 7, rate: 100, batchMs: 50 });
    src.push!(); clock.advance(0.05); src.push!();
    src.stop();
    const ch = store.get(7)!;
    expect(ch.info.name).toBe("counter");
    expect(ch.buffer.length).toBe(10);
    expect(ch.buffer.valueAt(9)).toBe(9);
    expect(ch.buffer.timeAt(5)).toBeCloseTo(100.05, 9);
    expect(store.dropped).toBe(0);
  });

  it("is discoverable through the registry", () => {
    const registry = new SourceRegistry().register(counterFactory);
    expect(registry.list().map((f) => f.type)).toEqual(["counter"]);
    expect(registry.create("counter")).toBeInstanceOf(CounterSource);
  });
});
