// Mori.SkyScope — Fixture drivers for the signal store and the synthetic source.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SignalStore } from "../sources/signal-store.js";
import { synthesize, type SynthSpec } from "../sources/synth.js";
import { frameFromJson, type FrameJson } from "../streaming/frame-json.js";
import type { ChannelInfo } from "../sources/contracts.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;

interface StoreState { store: SignalStore; notifications: number[][]; queries: unknown[] }

/** Signal store: `setup` gives retention and default capacity; listener notifications are collected. */
export const signalStoreDriver: FixtureDriver<StoreState> = {
  component: "signal-store",
  create(setup) {
    const store = new SignalStore({
      retentionSeconds: setup.retentionSeconds === undefined ? undefined : num(setup.retentionSeconds),
      defaultCapacity: setup.defaultCapacity === undefined ? undefined : num(setup.defaultCapacity),
    });
    const s: StoreState = { store, notifications: [], queries: [] };
    store.subscribe((ids) => s.notifications.push([...ids]));
    return s;
  },
  /**
   * Steps: declare (the step itself is the `ChannelInfo`), pushFrame (frame JSON); queries: channels (sorted
   * summaries), samples (every [t, v] of one channel).
   */
  step(s, step: Step) {
    const { store, queries } = s;
    switch (step.type) {
      case "declare": {
        const { type: _t, ...info } = step;
        store.declareChannel(info as unknown as ChannelInfo);
        return s;
      }
      case "pushFrame": store.pushFrame(frameFromJson(step.frame as FrameJson)); return s;
      case "query":
        if ("channels" in step) {
          queries.push([...store.channels.values()].sort((a, b) => a.info.id - b.info.id).map(({ info, buffer }) => ({
            id: info.id, name: info.name, unit: info.unit ?? null, kind: info.kind ?? null, timing: info.timing ?? "regular", rate: info.rate ?? null,
            capacity: buffer.capacity, length: buffer.length, latestTime: buffer.isEmpty ? null : buffer.latestTime(),
          })));
        } else if ("samples" in step) {
          const ch = store.get(num(step.samples));
          const out: number[][] = [];
          ch?.buffer.forEach(ch.buffer.firstSeq, ch.buffer.headSeq, (_s, t, v) => out.push([t, v]));
          queries.push(out);
        } else throw new Error(`unknown query ${JSON.stringify(step)}`);
        return s;
    }
    throw new Error(`unknown step ${String(step.type)}`);
  },
  /** Channel count, drop counters, the notifications and the query answers. */
  snapshot({ store, notifications, queries }) {
    return { channelCount: store.channels.size, dropped: store.dropped, lastDropReason: store.lastDropReason, notifications, queries };
  },
};

interface SynthState { queries: unknown[] }

/** Synthetic waveform generator. */
export const synthDriver: FixtureDriver<SynthState> = {
  component: "synthetic",
  create() { return { queries: [] }; },
  /** Step synth: a `SynthSpec` plus tStart, dt, count and startIndex; pushes the generated samples. */
  step(s, step: Step) {
    if (step.type !== "synth") throw new Error(`unknown step ${String(step.type)}`);
    s.queries.push(Array.from(synthesize(step.spec as SynthSpec, num(step.tStart), num(step.dt), num(step.count), step.startIndex === undefined ? 0 : num(step.startIndex))));
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
