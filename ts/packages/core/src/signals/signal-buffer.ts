// Mori.SkyScope — Append-only ring buffer for one channel.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * Append-only ring buffer for one channel. Mirrors `Mori.SkyScope.Core.Signals.SignalBuffer`;
 * behaviour is pinned by `spec/fixtures/signal-buffer.json`.
 *
 * Samples are addressed by a monotonically increasing sequence number (`seq`). Regular-rate
 * channels do not store a timestamp per sample: time is derived from "runs" (t0 + i·dt), so a
 * 1 kHz channel costs 4 bytes/sample instead of 12.
 */
export type TimeKind = "regular" | "timestamped";

/** Error message used by both cores when time goes backwards. */
export const OUT_OF_ORDER = "out-of-order-time";

interface Run { startSeq: number; t0: number; dt: number }

/** Fixed-capacity ring of f32 samples; once full, each append overwrites the oldest. */
export class SignalBuffer {
  /** Maximum samples retained. */
  readonly capacity: number;
  /** Whether time comes from runs ("regular") or from a stored f64 per sample. */
  readonly kind: TimeKind;
  private readonly values: Float32Array;
  private readonly times: Float64Array | null;
  private readonly runs: Run[] = [];
  private head = 0;
  private runHint = 0;

  /** Throws RangeError when `capacity` < 1; a fractional capacity is floored. */
  constructor(capacity: number, kind: TimeKind = "regular") {
    if (!(capacity >= 1)) throw new RangeError("capacity must be >= 1");
    this.capacity = Math.floor(capacity);
    this.kind = kind;
    this.values = new Float32Array(this.capacity);
    this.times = kind === "timestamped" ? new Float64Array(this.capacity) : null;
  }

  /** Next sequence number to be written (= total samples ever appended). */
  get headSeq(): number { return this.head; }
  /** Samples currently retained, at most `capacity`. */
  get length(): number { return Math.min(this.head, this.capacity); }
  /** Oldest sequence number still in the buffer. */
  get firstSeq(): number { return this.head - this.length; }
  /** True until the first append. */
  get isEmpty(): boolean { return this.head === 0; }
  /** Regular buffers: number of (t0, dt) runs still referenced. Always 0 for timestamped buffers. */
  get runCount(): number { return this.runs.length; }

  /** Sample value by sequence number; throws RangeError outside [firstSeq, headSeq). */
  valueAt(seq: number): number { this.check(seq); return this.values[seq % this.capacity]!; }

  /** Sample time in seconds by sequence number; throws RangeError outside [firstSeq, headSeq). */
  timeAt(seq: number): number {
    this.check(seq);
    if (this.times) return this.times[seq % this.capacity]!;
    const run = this.runs[this.runIndexFor(seq)]!;
    return run.t0 + (seq - run.startSeq) * run.dt;
  }

  /** Time of the oldest retained sample; NaN when empty. */
  earliestTime(): number { return this.head === 0 ? NaN : this.timeAt(this.firstSeq); }
  /** Time of the newest sample; NaN when empty. */
  latestTime(): number { return this.head === 0 ? NaN : this.timeAt(this.head - 1); }

  /** Appends evenly spaced samples starting at `t0` seconds, `dt` seconds apart; a batch that continues the previous run (same dt, contiguous time) extends it instead of starting a new one. Throws TypeError on a timestamped buffer, RangeError when dt ≤ 0 or t0 precedes the last sample (`OUT_OF_ORDER`). */
  appendRegular(t0: number, dt: number, values: ArrayLike<number>): void {
    if (this.times) throw new TypeError("appendRegular on a timestamped buffer");
    if (!(dt > 0)) throw new RangeError("dt must be > 0");
    if (values.length === 0) return;
    const last = this.runs[this.runs.length - 1];
    if (last) {
      const lastT = last.t0 + (this.head - 1 - last.startSeq) * last.dt;
      if (t0 < lastT) throw new RangeError(OUT_OF_ORDER);
      const continues = last.dt === dt && Math.abs(t0 - (lastT + dt)) <= dt * 1e-3;
      if (!continues) this.runs.push({ startSeq: this.head, t0, dt });
    } else {
      this.runs.push({ startSeq: this.head, t0, dt });
    }
    this.write(values);
    this.pruneRuns();
  }

  /** Appends samples with explicit times in seconds, which must be non-decreasing and not precede the last stored sample. Throws TypeError on a regular buffer and RangeError on length mismatch or out-of-order time, writing nothing in that case. */
  appendTimestamped(times: ArrayLike<number>, values: ArrayLike<number>): void {
    if (!this.times) throw new TypeError("appendTimestamped on a regular buffer");
    const n = values.length;
    if (n !== times.length) throw new RangeError("times/values length mismatch");
    if (n === 0) return;
    let prev = this.head === 0 ? Number.NEGATIVE_INFINITY : this.latestTime();
    for (let i = 0; i < n; i++) {
      const t = times[i]!;
      if (t < prev) throw new RangeError(OUT_OF_ORDER);
      prev = t;
    }
    for (let i = 0; i < n; i++) this.times[(this.head + i) % this.capacity] = times[i]!;
    this.write(values);
  }

  /** First seq whose time >= t; `headSeq` if none. */
  indexOfTime(t: number): number {
    let lo = this.firstSeq, hi = this.head;
    while (lo < hi) { const mid = Math.floor((lo + hi) / 2); if (this.timeAt(mid) < t) lo = mid + 1; else hi = mid; }
    return lo;
  }

  /** First seq whose time > t; `headSeq` if none. */
  indexAfterTime(t: number): number {
    let lo = this.firstSeq, hi = this.head;
    while (lo < hi) { const mid = Math.floor((lo + hi) / 2); if (this.timeAt(mid) <= t) lo = mid + 1; else hi = mid; }
    return lo;
  }

  /** Samples with tFrom <= t <= tTo as a half-open seq range. */
  window(tFrom: number, tTo: number): { fromSeq: number; toSeq: number } {
    return { fromSeq: this.indexOfTime(tFrom), toSeq: this.indexAfterTime(tTo) };
  }

  /** Sequential read of [fromSeq, toSeq) — the hot path for decimation; avoids per-sample lookups. */
  forEach(fromSeq: number, toSeq: number, fn: (seq: number, t: number, v: number) => void): void {
    fromSeq = Math.max(fromSeq, this.firstSeq);
    toSeq = Math.min(toSeq, this.head);
    if (this.times) {
      for (let seq = fromSeq; seq < toSeq; seq++) { const s = seq % this.capacity; fn(seq, this.times[s]!, this.values[s]!); }
      return;
    }
    let seq = fromSeq;
    while (seq < toSeq) {
      const ri = this.runIndexFor(seq);
      const run = this.runs[ri]!;
      const runEnd = ri + 1 < this.runs.length ? this.runs[ri + 1]!.startSeq : this.head;
      const end = Math.min(runEnd, toSeq);
      for (; seq < end; seq++) fn(seq, run.t0 + (seq - run.startSeq) * run.dt, this.values[seq % this.capacity]!);
    }
  }

  private write(values: ArrayLike<number>): void {
    const n = values.length;
    for (let i = 0; i < n; i++) this.values[(this.head + i) % this.capacity] = values[i]!;
    this.head += n;
  }

  private pruneRuns(): void {
    const first = this.firstSeq;
    while (this.runs.length > 1 && this.runs[1]!.startSeq <= first) { this.runs.shift(); this.runHint = 0; }
  }

  private runIndexFor(seq: number): number {
    const runs = this.runs;
    const h = this.runHint;
    if (h < runs.length && runs[h]!.startSeq <= seq && (h + 1 >= runs.length || runs[h + 1]!.startSeq > seq)) return h;
    let lo = 0, hi = runs.length - 1;
    while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (runs[mid]!.startSeq <= seq) lo = mid; else hi = mid - 1; }
    this.runHint = lo;
    return lo;
  }

  private check(seq: number): void {
    if (seq < this.firstSeq || seq >= this.head) throw new RangeError(`seq ${seq} out of range [${this.firstSeq}, ${this.head})`);
  }
}
