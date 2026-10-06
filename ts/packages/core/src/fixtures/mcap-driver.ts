// Mori.SkyScope — Fixture driver for MCAP recording and playback: a scripted stream goes through the recorder (byte identity across cores is checked with a…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { ManualClock, type ChannelInfo, type LayerSink, type SourceContext } from "../sources/contracts.js";
import { SignalStore } from "../sources/signal-store.js";
import { PlaybackSource, type Recording } from "../sources/playback.js";
import { McapRecorder, readRecording } from "../recording/recorder.js";
import { readMcap } from "../recording/mcap.js";
import { lz4DecodeBlock, lz4Decompress } from "../recording/lz4.js";

const fromBase64 = (s: string): Uint8Array => Uint8Array.from(atob(s), (c) => c.charCodeAt(0));
const toHex = (b: Uint8Array): string => Array.from(b, (v) => v.toString(16).padStart(2, "0")).join("");
import { fnv1a } from "../charts/colormaps.js";
import type { SkyScopeFrame } from "../streaming/frame.js";
import { r9 } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

/**
 * Fixture driver for MCAP recording and playback: a scripted stream goes through the recorder (byte identity across
 * cores is checked with a hash), the file is parsed back, and the recording is replayed into a store and a
 * layer-event log. Mirrored by `McapDriver.cs`; the golden file `spec/mcap/sample.mcap` comes from the same script.
 */
type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;

/** Records layer calls as strings so both cores can compare them. */
class LayerLog implements LayerSink {
  readonly log: string[] = [];
  declareLayer(id: string, kind: string): void { this.log.push(`declare:${id}:${kind}`); }
  push(id: string, payload: unknown): void { this.log.push(`push:${id}:${JSON.stringify(payload)}`); }
  reset(): void { this.log.push("reset"); }
}

interface State { clock: ManualClock; store: SignalStore; layers: LayerLog; rec: McapRecorder; bytes: Uint8Array | null; play: { src: PlaybackSource; store: SignalStore; layers: LayerLog } | null; queries: unknown[] }

function frameFromJson(f: Step): SkyScopeFrame {
  return { seq: num(f.seq), t0: num(f.t0), channels: (f.channels as Step[]).map((c) => ({ id: num(c.id), encoding: "regular" as const, tStart: num(c.tStart), dt: num(c.dt), values: Float32Array.from(c.values as number[]) })) };
}

/**
 * MCAP recorder and playback: `setup` gives the clock time, an optional size limit and, optionally, a file as base64.
 */
export const mcapDriver: FixtureDriver<State> = {
  component: "mcap",
  create(setup) {
    const clock = new ManualClock(num(setup.time ?? 0)), store = new SignalStore({ retentionSeconds: 60 }), layers = new LayerLog();
    const rec = new McapRecorder({ signals: store, layers }, clock, { maxBytes: setup.maxBytes as number | undefined, library: "Mori.SkyScope 0.1.0" });
    return { clock, store, layers, rec, bytes: typeof setup.base64 === "string" ? fromBase64(setup.base64) : null, play: null, queries: [] };
  },
  /**
   * Steps: clock, start, stop, declareChannel, pushFrame, declareLayer, push, reset, load (parse the bytes and start
   * playback), tick, seek; queries: bytes (length and hash), stats, file (parsed summary), recording, lz4Block, lz4,
   * readError, innerLog, playLog, playBuffer, playState.
   */
  step(s, step: Step) {
    const { rec, queries } = s;
    switch (step.type) {
      case "clock": s.clock.set(num(step.t)); break;
      case "start": rec.start(); break;
      case "stop": s.bytes = rec.stop(); break;
      case "declareChannel": rec.declareChannel(step.info as ChannelInfo); break;
      case "pushFrame": rec.pushFrame(frameFromJson(step.frame as Step)); break;
      case "declareLayer": rec.declareLayer(step.id as string, step.kind as string, step.meta as Record<string, unknown> | undefined); break;
      case "push": rec.push(step.id as string, step.payload); break;
      case "reset": rec.reset(); break;
      case "load": {
        const recording = readRecording(s.bytes!);
        const store = new SignalStore({ retentionSeconds: 60 }), layers = new LayerLog(), src = new PlaybackSource();
        const ctx: SourceContext = { signals: store, layers, clock: src.clock, log: () => {} };
        src.start(ctx, { recording, autoplay: true, loop: step.loop === true });
        s.play = { src, store, layers };
        break;
      }
      case "tick": queries.push(s.play!.src.tick(num(step.dt))); break;
      case "seek": s.play!.src.seek(num(step.t)); break;
      case "query":
        if ("bytes" in step) { const b = s.bytes ?? rec.lastFile; queries.push(b ? { length: b.length, hash: fnv1a(b) } : null); }
        else if ("stats" in step) { const st = rec.stats(); queries.push({ recording: st.recording, messages: st.messages, started: st.started === null ? null : r9(st.started) }); }
        else if ("file" in step) {
          const f = readMcap(s.bytes!);
          const byTopic: Record<string, number> = {};
          for (const m of f.messages) { const t = f.channels.get(m.channelId)!.topic; byTopic[t] = (byTopic[t] ?? 0) + 1; }
          queries.push({ profile: f.profile, library: f.library, schemas: [...f.schemas.values()].map((x) => ({ id: x.id, name: x.name, encoding: x.encoding })), channels: [...f.channels.values()].map((c) => ({ id: c.id, schemaId: c.schemaId, topic: c.topic, messageEncoding: c.messageEncoding, metadata: c.metadata })), messages: f.messages.length, byTopic, start: r9(f.messageStart), end: r9(f.messageEnd) });
        } else if ("recording" in step) {
          const r: Recording = readRecording(s.bytes!);
          queries.push({ channels: r.channels, frames: r.frames.map((f) => ({ seq: f.seq, t0: r9(f.t0), ids: f.channels.map((c) => c.id), first: r9((f.channels[0] as { values: Float32Array }).values[0]!) })), layerEvents: r.layerEvents.map((e) => ({ t: r9(e.t), id: e.id, kind: e.kind ?? null, meta: e.meta ?? null, payload: e.payload === undefined ? null : e.payload })), start: r9(r.start), end: r9(r.end) });
        } else if ("lz4Block" in step) { const { data, size } = step.lz4Block as { data: string; size: number }; const src = fromBase64(data), dst = new Uint8Array(size); let out: string; try { out = toHex(dst.subarray(0, lz4DecodeBlock(src, 0, src.length, dst, 0))); } catch { out = "error"; } queries.push(out); }
        else if ("lz4" in step) { const { data, size } = step.lz4 as { data: string; size: number }; queries.push(toHex(lz4Decompress(fromBase64(data), size))); }
        else if ("readError" in step) { try { readMcap(s.bytes!); queries.push(null); } catch (e) { queries.push((e as Error).message); } }
        else if ("innerLog" in step) queries.push([...s.layers.log]);
        else if ("playLog" in step) queries.push([...s.play!.layers.log]);
        else if ("playBuffer" in step) { const b = s.play!.store.get(num(step.playBuffer))?.buffer; queries.push(b ? { length: b.length, latest: b.isEmpty ? null : r9(b.valueAt(b.headSeq - 1)) } : null); }
        else if ("playState" in step) { const p = s.play!.src; queries.push({ position: r9(p.position), finished: p.finished, playing: p.playing, framesPushed: p.framesPushed }); }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
