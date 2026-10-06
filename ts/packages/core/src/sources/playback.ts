// Mori.SkyScope — Recorded data played back through a manual clock with a scrubber: play/pause, speed, seek, loop.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { FrameChannel, SkyScopeFrame } from "../streaming/frame.js";
import { ManualClock, type ChannelInfo, type Source, type SourceContext, type SourceFactory } from "./contracts.js";

/**
 * Recorded data played back through a manual clock with a scrubber: play/pause, speed, seek, loop.
 * `parseCsv` turns a CSV export into a Recording; other readers (MCAP, bags) produce the same shape.
 * Mirrors `Mori.SkyScope.Core.Sources.Playback`; pinned by `spec/fixtures/playback.json`.
 */
/** A scene-layer declaration (`kind` present) or push, at recording time `t`. */
export interface LayerEvent { t: number; id: string; kind?: string | undefined; meta?: Record<string, unknown> | undefined; payload?: unknown }

/** Everything a playback source replays: channel declarations, sample frames and layer events, plus the time span. */
export interface Recording {
  /** Declared on the sink before the first frame is pushed. */
  channels: ChannelInfo[];
  /** Frames sorted by `t0`. */
  frames: SkyScopeFrame[];
  /** Sorted by `t`; empty for signal-only recordings such as CSV. */
  layerEvents: LayerEvent[];
  /** Start of the recorded time span, seconds; playback rewinds here. */
  start: number;
  /** End of the recorded time span (last sample or layer event), seconds. */
  end: number;
}

/**
 * Parsing options for `parseCsv`. By default the delimiter is sniffed, and the time column is the first one named t,
 * time, timestamp or seconds, else column 0.
 */
export interface CsvOptions {
  /** Cell separator; auto-detects ";" when the text has no commas. */
  delimiter?: string | undefined;
  /** Name or index of the time column (seconds). Omit with `rate` for regular data without a time column. */
  timeColumn?: string | number | undefined;
  /** Samples per second for data without a time column; makes the channels regular. */
  rate?: number | undefined;
  /** Rows per frame. */
  chunkRows?: number | undefined;
  /** First channel id (columns get consecutive ids). */
  firstChannelId?: number | undefined;
  /** Time value multiplier (e.g. 1e-3 for milliseconds). */
  timeScale?: number | undefined;
}

/** CSV → Recording. The header row names the channels; blank lines and non-numeric cells (→ NaN) are tolerated. */
export function parseCsv(text: string, o: CsvOptions = {}): Recording {
  const delim = o.delimiter ?? (text.includes(";") && !text.includes(",") ? ";" : ",");
  const lines = text.split(/\r?\n/).filter((l) => l.trim().length > 0);
  if (lines.length === 0) return { channels: [], frames: [], layerEvents: [], start: 0, end: 0 };
  const header = lines[0]!.split(delim).map((h) => h.trim().replace(/^"|"$/g, ""));
  let timeIdx = -1;
  if (o.timeColumn !== undefined) timeIdx = typeof o.timeColumn === "number" ? o.timeColumn : header.indexOf(o.timeColumn);
  else if (o.rate === undefined) timeIdx = header.findIndex((h) => /^(t|time|timestamp|seconds?)$/i.test(h));
  if (timeIdx < 0 && o.rate === undefined) timeIdx = 0;
  const valueCols = header.map((_, i) => i).filter((i) => i !== timeIdx);
  const firstId = o.firstChannelId ?? 1, rate = o.rate, timeScale = o.timeScale ?? 1;
  const channels: ChannelInfo[] = valueCols.map((c, k) => ({ id: firstId + k, name: header[c]!, timing: timeIdx >= 0 ? "timestamped" : "regular", rate: timeIdx >= 0 ? undefined : rate }));
  const rows: number[][] = [];
  for (let i = 1; i < lines.length; i++) rows.push(lines[i]!.split(delim).map((c) => { const v = Number(c.trim()); return Number.isFinite(v) ? v : NaN; }));
  const chunk = Math.max(1, o.chunkRows ?? 256), dt = rate ? 1 / rate : 0;
  const frames: SkyScopeFrame[] = [];
  for (let r0 = 0, seq = 0; r0 < rows.length; r0 += chunk, seq++) {
    const slice = rows.slice(r0, r0 + chunk);
    const times = timeIdx >= 0 ? Float64Array.from(slice, (row) => (row[timeIdx] ?? NaN) * timeScale) : null;
    const t0 = times ? times[0]! : r0 * dt;
    const fchannels: FrameChannel[] = valueCols.map((c, k) => {
      const values = Float32Array.from(slice, (row) => row[c] ?? NaN);
      return times ? { id: firstId + k, encoding: "timestamped", times, values } : { id: firstId + k, encoding: "regular", tStart: t0, dt, values };
    });
    frames.push({ seq, t0, channels: fchannels });
  }
  frames.sort((a, b) => a.t0 - b.t0);
  const start = frames.length ? frames[0]!.t0 : 0;
  const last = frames[frames.length - 1];
  let end = start;
  if (last) for (const ch of last.channels) end = Math.max(end, ch.encoding === "timestamped" ? ch.times[ch.times.length - 1] ?? last.t0 : ch.encoding === "regular" ? ch.tStart + ch.dt * Math.max(0, ch.values.length - 1) : last.t0);
  return { channels, frames, layerEvents: [], start, end };
}

/** `PlaybackSource.start` configuration. */
export interface PlaybackConfig {
  /** What to play. */
  recording: Recording;
  /** Playback rate relative to wall time; default 1. */
  speed?: number | undefined;
  /** Restart from the beginning when the end is reached; default false. */
  loop?: boolean | undefined;
  /** Start playing immediately. */
  autoplay?: boolean | undefined;
}

/**
 * Plays a Recording: `tick(wallDt)` advances the manual clock by `wallDt × speed` and pushes every frame whose
 * time was crossed. Seeking backwards resets the sink and replays from the start up to the target, so ring
 * buffers always hold exactly what a live source would have produced by then.
 */
export class PlaybackSource implements Source {
  /** Always "playback". */
  readonly type = "playback";
  /** The manual clock playback drives; hand it to charts as their `TimeSource`. */
  readonly clock = new ManualClock();
  private ctx: SourceContext | null = null;
  private recording_: Recording | null = null;
  private cursor = 0;
  private layerCursor = 0;
  /** True while `tick` advances time. */
  playing = false;
  /** Playback rate relative to wall time; may be changed while playing. */
  speed = 1;
  /** Restart at the end instead of pausing. */
  loop = false;

  /** The recording given to `start`, or null before it. */
  get recording(): Recording | null { return this.recording_; }
  /** Current playback time in recording seconds. */
  get position(): number { return this.clock.now(); }
  /** Recording length in seconds (0 before `start`). */
  get duration(): number { return this.recording_ ? this.recording_.end - this.recording_.start : 0; }
  /** Position as a fraction 0..1 of the recording. */
  get progress(): number { const d = this.duration; return d > 0 && this.recording_ ? Math.min(1, Math.max(0, (this.position - this.recording_.start) / d)) : 0; }
  /** Frames pushed since the last start or backwards seek. */
  get framesPushed(): number { return this.cursor; }
  /** True once every frame and layer event has been pushed. */
  get finished(): boolean { return !!this.recording_ && this.cursor >= this.recording_.frames.length && this.layerCursor >= this.recording_.layerEvents.length; }

  /** Declare the channels, rewind the clock to the recording start and begin playing when `autoplay` is set. */
  start(ctx: SourceContext, config: PlaybackConfig): void {
    this.ctx = ctx; this.recording_ = config.recording; this.speed = config.speed ?? 1; this.loop = config.loop ?? false;
    for (const c of config.recording.channels) ctx.signals.declareChannel(c);
    this.clock.set(config.recording.start); this.cursor = 0; this.layerCursor = 0;
    this.playing = config.autoplay ?? false;
  }
  /** Pause and detach from the context. */
  stop(): void { this.playing = false; this.ctx = null; }
  /** Resume; at the end of a non-looping recording, rewind first. */
  play(): void { if (this.finished && !this.loop) this.seek(this.recording_?.start ?? 0); this.playing = true; }
  /** Stop advancing time. */
  pause(): void { this.playing = false; }

  /** Advance by `wallDt` seconds of wall time (host timer); returns the number of frames pushed. */
  tick(wallDt: number): number {
    if (!this.playing || !this.recording_ || !this.ctx) return 0;
    let target = this.clock.now() + wallDt * this.speed;
    if (target > this.recording_.end) {
      if (this.loop && this.recording_.frames.length > 0) { const pushed = this.pushUntil(this.recording_.end); this.seek(this.recording_.start); return pushed + this.tick(Math.max(0, (target - this.recording_.end) / this.speed)); }
      target = this.recording_.end; this.playing = false;
    }
    return this.pushUntil(target);
  }

  /** Jump to time `t`: forwards pushes the skipped frames, backwards resets the sink and replays. */
  seek(t: number): void {
    if (!this.recording_ || !this.ctx) return;
    const target = Math.min(this.recording_.end, Math.max(this.recording_.start, t));
    if (target < this.clock.now()) { this.ctx.signals.reset?.(); this.ctx.layers.reset?.(); this.cursor = 0; this.layerCursor = 0; }
    this.pushUntil(target);
  }

  /** Frames and layer events are applied in time order (frames first on ties). Returns the number of frames pushed. */
  private pushUntil(t: number): number {
    const r = this.recording_!, sink = this.ctx!.signals, layers = this.ctx!.layers;
    let n = 0;
    for (;;) {
      const f = this.cursor < r.frames.length ? r.frames[this.cursor]! : null;
      const e = this.layerCursor < r.layerEvents.length ? r.layerEvents[this.layerCursor]! : null;
      const ft = f && f.t0 <= t ? f.t0 : Infinity, et = e && e.t <= t ? e.t : Infinity;
      if (ft === Infinity && et === Infinity) break;
      if (ft <= et) { sink.pushFrame(f!); this.cursor++; n++; }
      else { if (e!.kind !== undefined) layers.declareLayer(e!.id, e!.kind, e!.meta); else layers.push(e!.id, e!.payload); this.layerCursor++; }
    }
    this.clock.set(t);
    return n;
  }
}

/** Registry entry for `PlaybackSource`. */
export const playbackFactory: SourceFactory = { type: "playback", displayName: "Playback (CSV, recordings)", capabilities: ["signals", "playback"], create: () => new PlaybackSource() };
