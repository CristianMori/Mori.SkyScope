// Mori.SkyScope — Turns MQTT messages into samples.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { ChannelInfo } from "./contracts.js";
import type { SkyScopeFrame } from "../streaming/frame.js";

/**
 * Turns MQTT messages into samples. A rule matches a topic filter (`+` and `#` wildcards) and extracts a number
 * from the payload: the whole payload as text, or a JSON path (`a.b[2].c`). Time comes from a JSON field when
 * named, otherwise from the receive time. Mirrors `Mori.SkyScope.Core.Sources.MqttMapping`; pinned by
 * `spec/fixtures/mqtt-mapping.json`. The MQTT clients (`@cmori/skyscope-sources`, `Mori.SkyScope.Sources.Mqtt`)
 * are thin wrappers around this.
 */
export interface MqttRule {
  /** Topic filter, e.g. `robot/+/battery` or `sensors/#`. */
  topic: string;
  /** Channel the extracted value is pushed to; several rules may share one. */
  channelId: number;
  /** Channel name (defaults to the topic filter) and unit reported by `mqttChannels`. */
  name?: string | undefined; unit?: string | undefined;
  /** JSON path into the payload; omit for a plain numeric payload. */
  path?: string | undefined;
  /** JSON path to a timestamp (seconds unless `timeScale` says otherwise). */
  timePath?: string | undefined;
  /** Multiplier applied to the extracted timestamp, e.g. 1e-3 for milliseconds; default 1. */
  timeScale?: number | undefined;
  /** Linear correction `value × scale + offset` (defaults 1 and 0). */
  scale?: number | undefined; offset?: number | undefined;
}

/** One extracted sample: channel, time in seconds and value. */
export interface MqttSample { channelId: number; t: number; value: number }

/** MQTT topic filter match (`+` one level, `#` the rest). */
export function topicMatches(filter: string, topic: string): boolean {
  const f = filter.split("/"), t = topic.split("/");
  for (let i = 0; i < f.length; i++) {
    const seg = f[i]!;
    if (seg === "#") return true;
    if (i >= t.length) return false;
    if (seg !== "+" && seg !== t[i]) return false;
  }
  return f.length === t.length;
}

/** `a.b[2].c` / `a/b/2` style lookup into parsed JSON. */
export function jsonPath(value: unknown, path: string): unknown {
  let cur = value;
  for (const raw of path.split(/[./]/).filter((s) => s.length > 0)) {
    const parts = raw.match(/^([^[]*)((?:\[\d+\])*)$/);
    const key = parts ? parts[1]! : raw;
    if (key.length > 0) { if (cur === null || typeof cur !== "object") return undefined; cur = (cur as Record<string, unknown>)[key]; }
    const idx = parts ? parts[2]! : "";
    for (const m of idx.matchAll(/\[(\d+)\]/g)) { if (!Array.isArray(cur)) return undefined; cur = cur[Number(m[1])]; }
  }
  return cur;
}

const toNumber = (v: unknown): number => (typeof v === "number" ? v : typeof v === "boolean" ? (v ? 1 : 0) : typeof v === "string" ? Number(v.trim()) : NaN);

/** Samples produced by one message under `rules`; unmatched or non-numeric extractions yield nothing. */
export function mapMqttMessage(rules: readonly MqttRule[], topic: string, payload: string, receivedAt: number): MqttSample[] {
  const out: MqttSample[] = [];
  let parsed: unknown, parsedOk = false;
  const json = (): unknown => { if (!parsedOk) { try { parsed = JSON.parse(payload); } catch { parsed = undefined; } parsedOk = true; } return parsed; };
  for (const r of rules) {
    if (!topicMatches(r.topic, topic)) continue;
    let v = r.path ? toNumber(jsonPath(json(), r.path)) : toNumber(payload);
    if (!Number.isFinite(v) && !r.path) { const j = json(); if (typeof j === "number") v = j; }
    if (!Number.isFinite(v)) continue;
    let t = receivedAt;
    if (r.timePath) { const tv = toNumber(jsonPath(json(), r.timePath)); if (Number.isFinite(tv)) t = tv * (r.timeScale ?? 1); }
    out.push({ channelId: r.channelId, t, value: v * (r.scale ?? 1) + (r.offset ?? 0) });
  }
  return out;
}

/** One timestamped `ChannelInfo` per distinct `channelId`, in first-rule order. */
export function mqttChannels(rules: readonly MqttRule[]): ChannelInfo[] {
  const seen = new Map<number, ChannelInfo>();
  for (const r of rules) if (!seen.has(r.channelId)) seen.set(r.channelId, { id: r.channelId, name: r.name ?? r.topic, unit: r.unit, timing: "timestamped" });
  return [...seen.values()];
}

/** Batch samples into one timestamped frame per channel (sorted by time within a channel). */
export function samplesToFrame(seq: number, samples: readonly MqttSample[]): SkyScopeFrame | null {
  if (samples.length === 0) return null;
  const byChannel = new Map<number, MqttSample[]>();
  for (const s of samples) { const list = byChannel.get(s.channelId); if (list) list.push(s); else byChannel.set(s.channelId, [s]); }
  let t0 = Infinity;
  const channels = [...byChannel.entries()].sort((a, b) => a[0] - b[0]).map(([id, list]) => {
    list.sort((a, b) => a.t - b.t);
    t0 = Math.min(t0, list[0]!.t);
    return { id, encoding: "timestamped" as const, times: Float64Array.from(list, (s) => s.t), values: Float32Array.from(list, (s) => s.value) };
  });
  return { seq, t0, channels };
}
