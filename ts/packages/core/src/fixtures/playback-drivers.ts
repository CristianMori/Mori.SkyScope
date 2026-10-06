// Mori.SkyScope — Fixture drivers for playback (CSV → recording → clocked replay) and the MQTT mapping.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { ManualClock, NullLayerSink, type ChannelInfo, type SourceContext } from "../sources/contracts.js";
import { SignalStore } from "../sources/signal-store.js";
import { PlaybackSource, parseCsv, type CsvOptions, type Recording } from "../sources/playback.js";
import { mapMqttMessage, mqttChannels, samplesToFrame, topicMatches, jsonPath, type MqttRule } from "../sources/mqtt-mapping.js";
import { r9 } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

/** Fixture drivers for playback (CSV → recording → clocked replay) and the MQTT mapping. Mirrored by `SourceDrivers2.cs`. */
type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;

interface PlayState { rec: Recording; store: SignalStore; src: PlaybackSource; pushed: number[]; queries: unknown[] }

const recSummary = (r: Recording) => ({
  channels: r.channels.map((c) => ({ id: c.id, name: c.name, timing: c.timing ?? null, rate: c.rate ?? null })),
  frames: r.frames.map((f) => ({ seq: f.seq, t0: r9(f.t0), channels: f.channels.map((c) => ({ id: c.id, encoding: c.encoding, count: c.encoding === "quantized" ? c.q.length : c.values.length, first: r9(c.encoding === "quantized" ? c.q[0]! : c.values[0]!), t: r9(c.encoding === "timestamped" ? c.times[0]! : c.tStart) })) })),
  start: r9(r.start), end: r9(r.end),
});

/**
 * CSV playback: `setup` gives the CSV text, parse options, retention and the playback config; `start: false` leaves
 * the source unstarted.
 */
export const playbackDriver: FixtureDriver<PlayState> = {
  component: "playback",
  create(setup) {
    const rec = parseCsv(setup.csv as string, (setup.csvOptions ?? {}) as CsvOptions);
    const store = new SignalStore({ retentionSeconds: (setup.retentionSeconds as number | undefined) ?? 60 });
    const src = new PlaybackSource();
    const ctx: SourceContext = { signals: store, layers: new NullLayerSink(), clock: src.clock, log: () => {} };
    const s: PlayState = { rec, store, src, pushed: [], queries: [] };
    if (setup.start !== false) src.start(ctx, { recording: rec, speed: setup.speed as number | undefined, loop: setup.loop as boolean | undefined, autoplay: setup.autoplay as boolean | undefined });
    return s;
  },
  /**
   * Steps: play, pause, tick (pushes the frame count), seek, setSpeed; queries: recording (summary), position, buffer
   * (one channel), clock.
   */
  step(s, step: Step) {
    const { src, store, queries } = s;
    switch (step.type) {
      case "play": src.play(); break;
      case "pause": src.pause(); break;
      case "tick": queries.push(src.tick(num(step.dt))); break;
      case "seek": src.seek(num(step.t)); break;
      case "setSpeed": src.speed = num(step.speed); break;
      case "query":
        if ("recording" in step) queries.push(recSummary(s.rec));
        else if ("position" in step) queries.push({ position: r9(src.position), progress: r9(src.progress), playing: src.playing, finished: src.finished, framesPushed: src.framesPushed, duration: r9(src.duration) });
        else if ("buffer" in step) { const b = store.get(num(step.buffer))?.buffer; queries.push(b ? { length: b.length, headSeq: b.headSeq, latestTime: b.isEmpty ? null : r9(b.latestTime()), latest: b.isEmpty ? null : r9(b.valueAt(b.headSeq - 1)) } : null); }
        else if ("clock" in step) queries.push(r9(src.clock.now()));
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

interface MqttState { rules: MqttRule[]; seq: number; queries: unknown[] }

/** MQTT mapping: `setup.rules` is the rule list. */
export const mqttMappingDriver: FixtureDriver<MqttState> = {
  component: "mqtt-mapping",
  create(setup) { return { rules: (setup.rules as MqttRule[] | undefined) ?? [], seq: 0, queries: [] }; },
  /**
   * Queries: matches (topic filter), jsonPath, message (samples for one message), channels, frame (samples batched
   * into a frame).
   */
  step(s, step: Step) {
    const { rules, queries } = s;
    switch (step.type) {
      case "query":
        if ("matches" in step) { const [f, t] = step.matches as [string, string]; queries.push(topicMatches(f, t)); }
        else if ("jsonPath" in step) { const { value, path } = step.jsonPath as { value: unknown; path: string }; const v = jsonPath(value, path); queries.push(v === undefined ? null : v); }
        else if ("message" in step) { const m = step.message as { topic: string; payload: string; receivedAt: number }; queries.push(mapMqttMessage(rules, m.topic, m.payload, m.receivedAt).map((x) => ({ channelId: x.channelId, t: r9(x.t), value: r9(x.value) }))); }
        else if ("channels" in step) queries.push(mqttChannels(rules).map((c: ChannelInfo) => ({ id: c.id, name: c.name, unit: c.unit ?? null, timing: c.timing ?? null })));
        else if ("frame" in step) {
          const samples = (step.frame as { channelId: number; t: number; value: number }[]);
          const f = samplesToFrame(s.seq++, samples);
          queries.push(f ? { seq: f.seq, t0: r9(f.t0), channels: f.channels.map((c) => ({ id: c.id, times: c.encoding === "timestamped" ? Array.from(c.times, r9) : [], values: c.encoding !== "quantized" ? Array.from(c.values, r9) : [] })) } : null);
        } else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

/** Re-exported so fixture consumers can drive playback without importing the sources module. */
export { ManualClock };
