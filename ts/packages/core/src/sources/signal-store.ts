// Mori.SkyScope — The SignalSink every chart reads from: one ring buffer per channel, sized from rate × retention.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SignalBuffer } from "../signals/signal-buffer.js";
import { channelValues, type SkyScopeFrame, type FrameChannel } from "../streaming/frame.js";
import type { ChannelInfo, Log, SignalSink } from "./contracts.js";

/** A declared channel together with the buffer holding its samples; `observedRate` is the highest sample rate seen in its batches when no rate was declared. */
export interface StoreChannel { info: ChannelInfo; buffer: SignalBuffer; observedRate?: number | undefined }
/**
 * Retention window in seconds (default 60), the starting buffer capacity for channels without a declared rate
 * (default 65536; such a buffer grows to `rate × retention` once batches reveal the rate), the cap any buffer can
 * grow to (default 1 048 576 samples) and an optional logger.
 */
export interface SignalStoreOptions { retentionSeconds?: number | undefined; defaultCapacity?: number | undefined; maxCapacity?: number | undefined; log?: Log | undefined }
/** Called after each pushed frame with the ids that received samples; on `reset` with every id and an empty frame. */
export type StoreListener = (changedIds: readonly number[], frame: SkyScopeFrame) => void;

/**
 * The SignalSink every chart reads from: one ring buffer per channel, sized from rate × retention.
 * Never throws on bad data — a batch that cannot be applied is counted in `dropped` and logged.
 * Mirrors `Mori.SkyScope.Core.Sources.SignalStore`; pinned by `spec/fixtures/signal-store.json`.
 */
export class SignalStore implements SignalSink {
  /** Declared channels by id, including auto-declared ones. */
  readonly channels = new Map<number, StoreChannel>();
  /** Seconds of history a channel with a known rate keeps. */
  readonly retentionSeconds: number;
  /** Starting capacity for channels with no declared rate, before their batches reveal one. */
  readonly defaultCapacity: number;
  /** Largest capacity a buffer is given, declared or observed. */
  readonly maxCapacity: number;
  /** Batches refused since the last reset. */
  dropped = 0;
  /** Message of the most recent drop, or null. */
  lastDropReason: string | null = null;
  private readonly log: Log;
  private readonly listeners = new Set<StoreListener>();

  /** Applies the defaults described on `SignalStoreOptions`. */
  constructor(options: SignalStoreOptions = {}) {
    this.retentionSeconds = options.retentionSeconds ?? 60;
    this.defaultCapacity = options.defaultCapacity ?? 65536;
    this.maxCapacity = Math.max(16, options.maxCapacity ?? 1048576);
    this.log = options.log ?? (() => {});
  }

  /** Ring size for a channel: `rate × retention` (at least 16, at most `maxCapacity`) when the rate is known, else `defaultCapacity`. */
  capacityFor(info: ChannelInfo): number {
    return info.rate && info.rate > 0 ? this.capacityForRate(info.rate) : this.defaultCapacity;
  }
  private capacityForRate(rate: number): number { return Math.min(this.maxCapacity, Math.max(16, Math.ceil(rate * this.retentionSeconds))); }

  /** Re-declaring keeps the data unless timing or capacity changed. */
  declareChannel(info: ChannelInfo): void {
    const existing = this.channels.get(info.id);
    const merged: ChannelInfo = { ...existing?.info, ...info, timing: info.timing ?? existing?.info.timing ?? "regular" };
    const capacity = this.capacityFor(merged);
    if (existing && existing.buffer.kind === merged.timing && existing.buffer.capacity === capacity) { existing.info = merged; return; }
    this.channels.set(info.id, { info: merged, buffer: new SignalBuffer(capacity, merged.timing) });
  }

  /** The channel entry for an id, or undefined. */
  get(id: number): StoreChannel | undefined { return this.channels.get(id); }
  /** Drop all samples; channel declarations survive. */
  reset(): void {
    for (const entry of this.channels.values()) entry.buffer = new SignalBuffer(entry.buffer.capacity, entry.buffer.kind);
    this.dropped = 0;
    const ids = [...this.channels.keys()];
    if (ids.length > 0) for (const l of this.listeners) l(ids, { seq: 0, t0: 0, channels: [] });
  }

  /**
   * Append each channel's batch; unknown channels are auto-declared from the batch encoding, and a batch that does
   * not fit its buffer is counted in `dropped` rather than thrown.
   */
  pushFrame(frame: SkyScopeFrame): void {
    const changed: number[] = [];
    for (const ch of frame.channels) {
      const entry = this.channels.get(ch.id) ?? this.autoDeclare(ch);
      try {
        this.growForObservedRate(entry, ch);
        append(entry.buffer, ch);
        changed.push(ch.id);
      } catch (e) {
        this.dropped++;
        this.lastDropReason = e instanceof Error ? e.message : String(e);
        this.log("warn", `channel ${ch.id}: dropped ${ch.encoding} batch (${this.lastDropReason})`);
      }
    }
    if (changed.length > 0) for (const l of this.listeners) l(changed, frame);
  }

  /** Register a change listener; returns a function that removes it. */
  subscribe(listener: StoreListener): () => void {
    this.listeners.add(listener);
    return () => { this.listeners.delete(listener); };
  }

  /**
   * A channel declared without a rate starts at `defaultCapacity`; every batch reveals a rate (1/dt for regular and
   * quantized batches, samples per second of span for timestamped ones), and when the highest rate seen asks for a
   * larger buffer than the channel has, the buffer grows to `rate × retention` (capped at `maxCapacity`), keeping its samples.
   */
  private growForObservedRate(entry: StoreChannel, ch: FrameChannel): void {
    if (entry.info.rate && entry.info.rate > 0) return;
    const rate = observedRate(ch);
    if (!(rate > 0) || rate <= (entry.observedRate ?? 0)) return;
    entry.observedRate = rate;
    const wanted = this.capacityForRate(rate);
    if (wanted > entry.buffer.capacity) entry.buffer = entry.buffer.resized(wanted);
  }

  private autoDeclare(ch: FrameChannel): StoreChannel {
    this.declareChannel({ id: ch.id, name: `ch${ch.id}`, timing: ch.encoding === "timestamped" ? "timestamped" : "regular" });
    return this.channels.get(ch.id)!;
  }
}

/** Sample rate a batch implies: 1/dt for regular and quantized batches, (n − 1) / span for timestamped ones with at least two samples; 0 when unknown. */
function observedRate(ch: FrameChannel): number {
  if (ch.encoding !== "timestamped") return ch.dt > 0 ? 1 / ch.dt : 0;
  const n = ch.times.length;
  if (n < 2) return 0;
  const span = ch.times[n - 1]! - ch.times[0]!;
  return span > 0 ? (n - 1) / span : 0;
}

/** Regular batches can be expanded into a timestamped buffer; the reverse has no dt and is refused. */
function append(buffer: SignalBuffer, ch: FrameChannel): void {
  if (ch.encoding === "timestamped") {
    if (buffer.kind !== "timestamped") throw new Error("timing-mismatch");
    buffer.appendTimestamped(ch.times, ch.values);
    return;
  }
  const values = channelValues(ch);
  if (buffer.kind === "regular") { buffer.appendRegular(ch.tStart, ch.dt, values); return; }
  const times = new Float64Array(values.length);
  for (let i = 0; i < times.length; i++) times[i] = ch.tStart + i * ch.dt;
  buffer.appendTimestamped(times, values);
}
