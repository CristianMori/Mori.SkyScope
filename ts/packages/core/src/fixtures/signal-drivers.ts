// Mori.SkyScope — Fixture drivers for the signal ring buffer and the M4 decimation.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SignalBuffer, OUT_OF_ORDER, type TimeKind } from "../signals/signal-buffer.js";
import { BucketSeries } from "../signals/bucket-series.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const nums = (v: unknown): number[] => v as number[];
const nanToNull = (v: number): number | null => (Number.isNaN(v) ? null : v);

/** Stable error codes shared with the C# drivers. */
function errorCode(e: unknown): string {
  if (e instanceof Error) {
    if (e.message === OUT_OF_ORDER) return OUT_OF_ORDER;
    if (e instanceof TypeError) return "wrong-kind";
    return e.message;
  }
  return String(e);
}

function applyAppend(buf: SignalBuffer, step: Step, errors: string[]): boolean {
  try {
    if (step.type === "appendRegular") { buf.appendRegular(num(step.t0), num(step.dt), nums(step.values)); return true; }
    if (step.type === "appendTimestamped") { buf.appendTimestamped(nums(step.times), nums(step.values)); return true; }
  } catch (e) { errors.push(errorCode(e)); return true; }
  return false;
}

function samples(buf: SignalBuffer): number[][] {
  const out: number[][] = [];
  buf.forEach(buf.firstSeq, buf.headSeq, (_s, t, v) => out.push([t, v]));
  return out;
}

interface BufState { buf: SignalBuffer; queries: unknown[]; errors: string[] }

/** Signal ring buffer: `setup` gives capacity and time kind. */
export const signalBufferDriver: FixtureDriver<BufState> = {
  component: "signal-buffer",
  create(setup) {
    return { buf: new SignalBuffer(num(setup.capacity), (setup.kind as TimeKind) ?? "regular"), queries: [], errors: [] };
  },
  /**
   * Steps: appendRegular, appendTimestamped (errors are collected, not thrown); queries: indexOfTime, indexAfterTime,
   * timeAt, valueAt, window.
   */
  step(s, step) {
    const { buf, queries } = s;
    if (applyAppend(buf, step, s.errors)) return s;
    if (step.type === "query") {
      if ("indexOfTime" in step) queries.push(buf.indexOfTime(num(step.indexOfTime)));
      else if ("indexAfterTime" in step) queries.push(buf.indexAfterTime(num(step.indexAfterTime)));
      else if ("timeAt" in step) queries.push(buf.timeAt(num(step.timeAt)));
      else if ("valueAt" in step) queries.push(buf.valueAt(num(step.valueAt)));
      else if ("window" in step) { const [a, b] = nums(step.window); queries.push(buf.window(a!, b!)); }
      else throw new Error(`unknown query ${JSON.stringify(step)}`);
      return s;
    }
    throw new Error(`unknown step ${String(step.type)}`);
  },
  /** Buffer metadata, every sample as [t, v], the query answers and the error codes. */
  snapshot({ buf, queries, errors }) {
    return {
      kind: buf.kind, capacity: buf.capacity, head: buf.headSeq, length: buf.length, firstSeq: buf.firstSeq,
      earliestTime: nanToNull(buf.earliestTime()), latestTime: nanToNull(buf.latestTime()),
      runs: buf.kind === "regular" ? buf.runCount : null, samples: samples(buf), queries, errors,
    };
  },
};

interface DecState { buf: SignalBuffer; series: BucketSeries; queries: unknown[]; errors: string[] }

/** M4 bucket series over a buffer: `setup` gives capacity, kind, quantum and the bucket cap. */
export const decimationDriver: FixtureDriver<DecState> = {
  component: "decimation",
  create(setup) {
    const buf = new SignalBuffer(num(setup.capacity), (setup.kind as TimeKind) ?? "regular");
    const series = new BucketSeries(buf, num(setup.quantum), setup.maxBuckets === undefined ? undefined : num(setup.maxBuckets));
    return { buf, series, queries: [], errors: [] };
  },
  /**
   * Steps: the append steps, update (pushes its result), rebuild, setQuantum; queries: buckets (a range), bucket
   * (one), polyline (a range).
   */
  step(s, step) {
    const { buf, series, queries } = s;
    if (applyAppend(buf, step, s.errors)) return s;
    switch (step.type) {
      case "update": queries.push(series.update()); return s;
      case "rebuild": series.rebuild(); return s;
      case "setQuantum": series.setQuantum(num(step.quantum)); return s;
      case "query":
        if ("buckets" in step) { const [a, b] = nums(step.buckets); queries.push(series.bucketsInRange(a!, b!)); }
        else if ("bucket" in step) queries.push(series.bucket(num(step.bucket)));
        else if ("polyline" in step) { const [a, b] = nums(step.polyline); queries.push(Array.from(series.polyline(a!, b!))); }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        return s;
    }
    throw new Error(`unknown step ${String(step.type)}`);
  },
  /** Quantum, top bucket index, the query answers and the error codes. */
  snapshot({ series, queries, errors }) {
    return { quantum: series.quantum, top: series.topIndex, queries, errors };
  },
};
