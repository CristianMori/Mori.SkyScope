// Mori.SkyScope — Incremental M4 decimation over a SignalBuffer.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { SignalBuffer } from "./signal-buffer.js";

/**
 * Incremental M4 decimation over a `SignalBuffer`. Mirrors `Mori.SkyScope.Core.Signals.BucketSeries`;
 * pinned by `spec/fixtures/decimation.json`.
 *
 * Time is divided into buckets of `quantum` seconds (bucket index = floor(t / quantum)). Each bucket
 * keeps first/min/max/last with their times, so drawing a window costs at most 4 points per bucket
 * regardless of how many samples arrived. `update()` is O(new samples); `setQuantum()` rebuilds.
 */
export interface Bucket {
  /** `index` = floor(t / quantum); `t` is the bucket's start time in seconds; `count` is how many samples fell in it. */
  index: number; t: number; count: number;
  /** Sample values: `first`/`last` in arrival order, `min`/`max` the extremes. */
  first: number; min: number; max: number; last: number;
  /** Times in seconds of those samples; `tMin`/`tMax` belong to the first occurrence of each extreme. */
  tFirst: number; tMin: number; tMax: number; tLast: number;
}

/** Ring of the most recent `maxBuckets` buckets over a buffer; older buckets are forgotten as time advances. */
export class BucketSeries {
  /** Source of samples; only ever read. */
  readonly buffer: SignalBuffer;
  /** Ring size: buckets at or below `topIndex − maxBuckets` are evicted. */
  readonly maxBuckets: number;
  private q: number;
  private readonly count: Int32Array;
  private readonly vFirst: Float32Array; private readonly vMin: Float32Array;
  private readonly vMax: Float32Array;   private readonly vLast: Float32Array;
  private readonly tFirst: Float64Array; private readonly tMin: Float64Array;
  private readonly tMax: Float64Array;   private readonly tLast: Float64Array;
  private top: number | null = null;   // highest bucket index seen
  private cursor = 0;                  // next seq to process

  /** Throws RangeError unless `quantum` > 0 and `maxBuckets` ≥ 1. Nothing is folded until `update()` is called. */
  constructor(buffer: SignalBuffer, quantum: number, maxBuckets = 4096) {
    if (!(quantum > 0)) throw new RangeError("quantum must be > 0");
    if (!(maxBuckets >= 1)) throw new RangeError("maxBuckets must be >= 1");
    this.buffer = buffer; this.q = quantum; this.maxBuckets = Math.floor(maxBuckets);
    const m = this.maxBuckets;
    this.count = new Int32Array(m);
    this.vFirst = new Float32Array(m); this.vMin = new Float32Array(m); this.vMax = new Float32Array(m); this.vLast = new Float32Array(m);
    this.tFirst = new Float64Array(m); this.tMin = new Float64Array(m); this.tMax = new Float64Array(m); this.tLast = new Float64Array(m);
  }

  /** Bucket width in seconds. */
  get quantum(): number { return this.q; }
  /** Highest bucket index seen so far, or null before any sample. */
  get topIndex(): number | null { return this.top; }

  /** Changes the bucket width and rebuilds from the buffer when it differs. Throws RangeError for a non-positive value. */
  setQuantum(quantum: number): void {
    if (!(quantum > 0)) throw new RangeError("quantum must be > 0");
    if (quantum === this.q) return;
    this.q = quantum;
    this.rebuild();
  }

  /** Discard all buckets and re-derive them from whatever the buffer still holds. */
  rebuild(): void {
    this.count.fill(0);
    this.top = null;
    this.cursor = this.buffer.firstSeq;
    this.update();
  }

  /** Fold samples appended since the last call. Returns how many were processed. */
  update(): number {
    const from = Math.max(this.cursor, this.buffer.firstSeq);
    const to = this.buffer.headSeq;
    if (to <= from) { this.cursor = to; return 0; }
    this.buffer.forEach(from, to, (_seq, t, v) => this.add(t, v));
    this.cursor = to;
    return to - from;
  }

  /** Snapshot of one bucket, or null when it is empty, beyond the top or already evicted. */
  bucket(index: number): Bucket | null {
    if (this.top === null || index > this.top || index <= this.top - this.maxBuckets) return null;
    const s = this.slot(index);
    if (this.count[s] === 0) return null;
    return {
      index, t: index * this.q, count: this.count[s]!,
      first: this.vFirst[s]!, min: this.vMin[s]!, max: this.vMax[s]!, last: this.vLast[s]!,
      tFirst: this.tFirst[s]!, tMin: this.tMin[s]!, tMax: this.tMax[s]!, tLast: this.tLast[s]!,
    };
  }

  /** Non-empty buckets whose span intersects [tFrom, tTo]. */
  bucketsInRange(tFrom: number, tTo: number): Bucket[] {
    const out: Bucket[] = [];
    if (this.top === null) return out;
    const lo = Math.max(Math.floor(tFrom / this.q), this.top - this.maxBuckets + 1);
    const hi = Math.min(Math.floor(tTo / this.q), this.top);
    for (let i = lo; i <= hi; i++) { const b = this.bucket(i); if (b) out.push(b); }
    return out;
  }

  /**
   * M4 polyline for [tFrom, tTo] as interleaved [t, v, t, v, …]: per bucket first, then min/max in
   * time order (skipping any that coincide with first/last), then last.
   */
  polyline(tFrom: number, tTo: number): Float64Array {
    const pts: number[] = [];
    for (const b of this.bucketsInRange(tFrom, tTo)) {
      pts.push(b.tFirst, b.first);
      if (b.count > 1) {
        const minFirst = b.tMin <= b.tMax;
        const t1 = minFirst ? b.tMin : b.tMax, v1 = minFirst ? b.min : b.max;
        const t2 = minFirst ? b.tMax : b.tMin, v2 = minFirst ? b.max : b.min;
        if (t1 !== b.tFirst && t1 !== b.tLast) pts.push(t1, v1);
        if (t2 !== b.tFirst && t2 !== b.tLast) pts.push(t2, v2);
        pts.push(b.tLast, b.last);
      }
    }
    return Float64Array.from(pts);
  }

  private slot(index: number): number { const m = this.maxBuckets; return ((index % m) + m) % m; }

  private add(t: number, v: number): void {
    const b = Math.floor(t / this.q);
    if (this.top === null || b > this.top) {
      // advance: every bucket between the old top and b starts empty
      const start = this.top === null ? b : this.top + 1;
      const from = Math.max(start, b - this.maxBuckets + 1);
      for (let i = from; i <= b; i++) this.count[this.slot(i)] = 0;
      this.top = b;
    } else if (b <= this.top - this.maxBuckets) {
      return; // older than the ring remembers
    }
    const s = this.slot(b);
    if (this.count[s] === 0) {
      this.count[s] = 1;
      this.vFirst[s] = this.vMin[s] = this.vMax[s] = this.vLast[s] = v;
      this.tFirst[s] = this.tMin[s] = this.tMax[s] = this.tLast[s] = t;
      return;
    }
    this.count[s]!++;
    this.vLast[s] = v; this.tLast[s] = t;
    if (v < this.vMin[s]!) { this.vMin[s] = v; this.tMin[s] = t; }
    if (v > this.vMax[s]!) { this.vMax[s] = v; this.tMax[s] = t; }
  }
}
