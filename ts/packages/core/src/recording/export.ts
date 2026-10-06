// Mori.SkyScope — Export a time range of a store's channels to CSV or to an MCAP recording: the cursor span of a chart, or any explicit range.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { SignalStore, StoreChannel } from "../sources/signal-store.js";
import { encodeFrame, type FrameChannel, type SkyScopeFrame } from "../streaming/frame.js";
import { McapWriter, secondsToNs } from "./mcap.js";
import { CHANNELS_TOPIC, FRAMES_TOPIC, FRAME_MESSAGE_ENCODING, channelInfoJson } from "./recorder.js";

/**
 * Export a time range of a store's channels: `exportRangeCsv` writes the wide CSV that `parseCsv` reads back (one row
 * per distinct timestamp, a cell empty when a channel has no sample there), `exportRangeMcap` the MCAP file that
 * `readRecording` reads back, laid out like an `McapRecorder` recording of that span. Both cores produce identical text
 * and bytes. Mirrors `Mori.SkyScope.Core.Mcap.RangeExport`; pinned by `spec/fixtures/trend-chart.json`.
 */
/** Options of `exportRangeCsv`: decimals of the time column (default 6) and of the value cells (default 6). */
export interface CsvExportOptions { decimals?: number | undefined; valueDecimals?: number | undefined }

/** Samples per frame in an MCAP export. */
export const EXPORT_BATCH_SAMPLES = 1000;

const POW10 = [1, 10, 100, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];
/** 2^53: integer parts up to here print exactly in both cores. */
const EXACT_LIMIT = 9007199254740992;
const encoder = new TextEncoder();

/**
 * Fixed-point formatting shared with the C# core: half away from zero on the double arithmetic both languages share
 * (`floor(fraction × 10^decimals + 0.5)`, carried into the integer part), invariant digits and never "-0". `toFixed`
 * and C# "F" disagree on exact halves, so neither is used. NaN and the infinities print as `NaN`, `Infinity` and
 * `-Infinity`; magnitudes of 2^53 and above fall back to the shortest round-trip form. `decimals` is clamped to 0–15.
 */
export function formatFixed(v: number, decimals: number): string {
  if (!Number.isFinite(v)) return Number.isNaN(v) ? "NaN" : v > 0 ? "Infinity" : "-Infinity";
  const d = Math.max(0, Math.min(15, Math.floor(decimals)));
  const abs = Math.abs(v);
  if (abs >= EXACT_LIMIT) return String(v);
  let whole = Math.floor(abs);
  const p = POW10[d]!;
  let frac = Math.floor((abs - whole) * p + 0.5);
  if (frac >= p) { whole += 1; frac = 0; }
  const digits = d === 0 ? String(whole) : `${whole}.${String(frac).padStart(d, "0")}`;
  return v < 0 && (whole > 0 || frac > 0) ? `-${digits}` : digits;
}

/** The store entries of the requested ids, without duplicates and in the given order; ids the store does not know are skipped. */
function selectChannels(store: SignalStore, channelIds: readonly number[]): StoreChannel[] {
  const seen = new Set<number>(), out: StoreChannel[] = [];
  for (const id of channelIds) {
    if (seen.has(id)) continue;
    seen.add(id);
    const c = store.get(id);
    if (c) out.push(c);
  }
  return out;
}

/** Column headers: the channel name (`ch<id>` when blank), `#<id>` appended where two selected channels share a name; commas, quotes and line breaks become spaces. */
export function csvColumnNames(channels: readonly { info: { id: number; name: string } }[]): string[] {
  const clean = channels.map((c) => { const n = c.info.name.replace(/[,"\r\n]/g, " ").trim(); return n.length > 0 ? n : `ch${c.info.id}`; });
  const counts = new Map<string, number>();
  for (const n of clean) counts.set(n, (counts.get(n) ?? 0) + 1);
  return clean.map((n, i) => (counts.get(n)! > 1 ? `${n}#${channels[i]!.info.id}` : n));
}

/**
 * Wide CSV of the samples with `t0 ≤ t ≤ t1` (either order) of the given channels: header `t,<name>[,<name>…]`, one row
 * per distinct timestamp across the channels in ascending order, a cell empty when a channel has no sample at that
 * time (the last of several samples at one time wins), `\n` line endings and a trailing newline. Times and values are
 * written with `formatFixed`. Channels the store does not know are skipped.
 */
export function exportRangeCsv(store: SignalStore, channelIds: readonly number[], t0: number, t1: number, options: CsvExportOptions = {}): string {
  const from = Math.min(t0, t1), to = Math.max(t0, t1);
  const td = options.decimals ?? 6, vd = options.valueDecimals ?? 6;
  const channels = selectChannels(store, channelIds);
  const columns = channels.map((c) => {
    const times: number[] = [], values: number[] = [];
    const w = c.buffer.window(from, to);
    c.buffer.forEach(w.fromSeq, w.toSeq, (_seq, t, v) => { times.push(t); values.push(v); });
    return { times, values, at: 0 };
  });
  const all: number[] = [];
  for (const c of columns) for (const t of c.times) all.push(t);
  all.sort((a, b) => a - b);
  const lines = [["t", ...csvColumnNames(channels)].join(",")];
  let prev = NaN;
  for (const t of all) {
    if (t === prev) continue;
    prev = t;
    const cells = [formatFixed(t, td)];
    for (const c of columns) {
      let j = c.at;
      if (j < c.times.length && c.times[j] === t) {
        while (j + 1 < c.times.length && c.times[j + 1] === t) j++;
        cells.push(formatFixed(c.values[j]!, vd));
        c.at = j + 1;
      } else cells.push("");
    }
    lines.push(cells.join(","));
  }
  return lines.join("\n") + "\n";
}

/**
 * MCAP file of the samples with `t0 ≤ t ≤ t1` (either order) of the given channels, in the layout `McapRecorder`
 * writes: the frame and catalog channels, one catalog message with the selected channels (ascending id) stamped at the
 * range start, then one `SkyScopeFrame` message per batch of at most `EXPORT_BATCH_SAMPLES` samples, channel by channel
 * in ascending id. Regular channels keep their encoding (one regular batch per run, cut at the range and at the batch
 * size), timestamped channels write timestamped batches; every frame is stamped with its first sample time.
 * `readRecording` reads it back; channels the store does not know are skipped.
 */
export function exportRangeMcap(store: SignalStore, channelIds: readonly number[], t0: number, t1: number): Uint8Array {
  const from = Math.min(t0, t1), to = Math.max(t0, t1);
  const channels = selectChannels(store, channelIds).sort((a, b) => a.info.id - b.info.id);
  const w = new McapWriter();
  const frameSchema = w.addSchema("skyscope.Frame", "text", "SkyScopeFrame v1: binary batch of channel samples (see Mori.SkyScope streaming/frame)");
  const catalogSchema = w.addSchema("skyscope.Channels", "jsonschema", "{}");
  const frameChannel = w.addChannel(FRAMES_TOPIC, frameSchema, FRAME_MESSAGE_ENCODING);
  const catalogChannel = w.addChannel(CHANNELS_TOPIC, catalogSchema, "json");
  if (channels.length > 0) w.addMessage(catalogChannel, secondsToNs(from), encoder.encode(`{"channels":[${channels.map((c) => channelInfoJson(c.info)).join(",")}]}`));
  let seq = 0;
  const emit = (ch: FrameChannel, tStart: number): void => {
    const frame: SkyScopeFrame = { seq: seq++, t0: tStart, channels: [ch] };
    w.addMessage(frameChannel, secondsToNs(tStart), encodeFrame(frame));
  };
  for (const c of channels) {
    const buf = c.buffer, id = c.info.id;
    const { fromSeq, toSeq } = buf.window(from, to);
    if (toSeq <= fromSeq) continue;
    if (buf.kind === "regular") {
      for (const run of buf.runsIn(fromSeq, toSeq)) {
        for (let s = run.fromSeq; s < run.toSeq; s += EXPORT_BATCH_SAMPLES) {
          const n = Math.min(EXPORT_BATCH_SAMPLES, run.toSeq - s);
          const values = new Float32Array(n);
          for (let i = 0; i < n; i++) values[i] = buf.valueAt(s + i);
          const tStart = buf.timeAt(s);
          emit({ id, encoding: "regular", tStart, dt: run.dt, values }, tStart);
        }
      }
    } else {
      for (let s = fromSeq; s < toSeq; s += EXPORT_BATCH_SAMPLES) {
        const n = Math.min(EXPORT_BATCH_SAMPLES, toSeq - s);
        const times = new Float64Array(n), values = new Float32Array(n);
        for (let i = 0; i < n; i++) { times[i] = buf.timeAt(s + i); values[i] = buf.valueAt(s + i); }
        emit({ id, encoding: "timestamped", times, values }, times[0]!);
      }
    }
  }
  return w.finish();
}
