// Mori.SkyScope — SkyScope recordings in MCAP: one channel of binary frames, one channel with the channel catalog (JSON), and one JSON channel per scene la…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { decodeFrame, encodeFrame, type SkyScopeFrame } from "../streaming/frame.js";
import { decodeLayerMessage, encodeLayerMessage, hasBinaryPayload } from "../streaming/layer-message.js";
import { LiveClock, type ChannelInfo, type LayerSink, type SignalSink, type TimeSource } from "../sources/contracts.js";
import type { LayerEvent, Recording } from "../sources/playback.js";
import { McapWriter, readMcap, secondsToNs, type McapFile, type McapWriterOptions } from "./mcap.js";

/**
 * SkyScope recordings in MCAP: one channel of binary frames, one channel with the channel catalog (JSON), and one
 * JSON channel per scene layer (`/layers/<id>`, messages `{declare:{kind,meta}}` or `{push:payload}`).
 * `McapRecorder` is a tee: it forwards to the sinks a source already feeds and, while recording, writes every message.
 * Mirrors `Mori.SkyScope.Core.Recording`; pinned by `spec/fixtures/mcap.json`.
 */
export const FRAMES_TOPIC = "/skyscope/frames";
/** Topic of the JSON channel catalog; each message is `{"channels":[...]}`. */
export const CHANNELS_TOPIC = "/skyscope/channels";
/** Prefix of per-layer topics; the layer id follows it. */
export const LAYER_TOPIC_PREFIX = "/layers/";
/** MCAP message encoding of frame messages. */
export const FRAME_MESSAGE_ENCODING = "skyscope-frame";
/** MCAP message encoding of binary layer pushes. */
export const LAYER_MESSAGE_ENCODING = "skyscope-layer";

const encoder = new TextEncoder(), decoder = new TextDecoder();

/** Fixed key order and no undefined members, so both cores write identical catalog JSON. */
export function channelInfoJson(c: ChannelInfo): string {
  const parts = [`"id":${c.id}`, `"name":${JSON.stringify(c.name)}`];
  if (c.unit !== undefined) parts.push(`"unit":${JSON.stringify(c.unit)}`);
  if (c.kind !== undefined) parts.push(`"kind":${JSON.stringify(c.kind)}`);
  if (c.timing !== undefined) parts.push(`"timing":${JSON.stringify(c.timing)}`);
  if (c.rate !== undefined) parts.push(`"rate":${JSON.stringify(c.rate)}`);
  if (c.color !== undefined) parts.push(`"color":${JSON.stringify(c.color)}`);
  return `{${parts.join(",")}}`;
}

/** `messages` written and `bytes` produced so far (of the last file once stopped); `started` and `duration` in clock seconds, null and 0 before the first start. */
export interface RecorderStats { recording: boolean; messages: number; bytes: number; started: number | null; duration: number }
/** Options of the recorder: the writer options plus the in-memory size limit. */
export interface McapRecorderOptions extends McapWriterOptions {
  /** Stop automatically once the in-memory file exceeds this many bytes (browser safety); 0 = no limit. */
  maxBytes?: number | undefined;
  /** Called after the recorder stops itself because `maxBytes` was exceeded. */
  onLimit?: (() => void) | undefined;
}

/** Tee between a source and its sinks that can write everything it sees to an MCAP file. */
export class McapRecorder implements SignalSink, LayerSink {
  private writer: McapWriter | null = null;
  private frameChannel = -1; private catalogChannel = -1;
  private readonly layerChannels = new Map<string, number>();
  private readonly binaryLayerChannels = new Map<string, number>();
  private binarySchema: number | null = null;
  private readonly layerSchemas = new Map<string, number>();
  private readonly catalog = new Map<number, ChannelInfo>();
  private readonly layerDecls = new Map<string, { kind: string; meta: Record<string, unknown> | undefined }>();
  private messages = 0; private started: number | null = null;
  private last: Uint8Array | null = null;

  /** Stop automatically once the in-memory file exceeds this many bytes; 0 = no limit. */
  maxBytes: number;
  /** `inner` sinks receive every call whether or not recording; `clock` stamps catalog and layer messages. */
  constructor(private readonly inner: { signals?: SignalSink | undefined; layers?: LayerSink | undefined } = {}, private readonly clock: TimeSource = new LiveClock(), private readonly options: McapRecorderOptions = {}) { this.maxBytes = options.maxBytes ?? 0; }

  /** True between `start` and `stop`. */
  get recording(): boolean { return this.writer !== null; }
  /** Counters of the current recording, or of the last one once stopped. */
  stats(): RecorderStats { return { recording: this.writer !== null, messages: this.messages, bytes: this.writer?.length ?? this.last?.length ?? 0, started: this.started, duration: this.started === null ? 0 : this.clock.now() - this.started }; }
  /** The bytes of the last finished recording. */
  get lastFile(): Uint8Array | null { return this.last; }

  /** Begin a new file: the channels and layers seen so far are written first, so a recording started mid-stream is self-contained. With `sink`, records stream out instead of accumulating in memory. */
  start(sink?: (chunk: Uint8Array) => void): void {
    if (this.writer) return;
    const w = new McapWriter({ ...this.options, sink });
    const frameSchema = w.addSchema("skyscope.Frame", "text", "SkyScopeFrame v1: binary batch of channel samples (see Mori.SkyScope streaming/frame)");
    const catalogSchema = w.addSchema("skyscope.Channels", "jsonschema", "{}");
    this.frameChannel = w.addChannel(FRAMES_TOPIC, frameSchema, FRAME_MESSAGE_ENCODING);
    this.catalogChannel = w.addChannel(CHANNELS_TOPIC, catalogSchema, "json");
    this.layerChannels.clear(); this.layerSchemas.clear(); this.binaryLayerChannels.clear(); this.binarySchema = null;
    this.writer = w; this.messages = 0; this.started = this.clock.now(); this.last = null;
    const now = secondsToNs(this.started);
    if (this.catalog.size > 0) this.write(this.catalogChannel, now, `{"channels":[${[...this.catalog.values()].map(channelInfoJson).join(",")}]}`);
    for (const [id, d] of this.layerDecls) this.write(this.layerChannel(id, d.kind), now, `{"declare":{"kind":${JSON.stringify(d.kind)},"meta":${JSON.stringify(d.meta ?? {})}}}`);
  }
  /** Finish the file and return its bytes (null when not recording; empty when it was streamed to a sink). */
  stop(): Uint8Array | null {
    if (!this.writer) return null;
    const bytes = this.writer.finish();
    this.writer = null; this.last = bytes;
    return bytes;
  }
  /** True when the current recording goes to a sink instead of memory. */
  get streaming(): boolean { return this.writer?.streaming ?? false; }

  private write(channel: number, timeNs: bigint, payload: string | Uint8Array): void {
    const w = this.writer!;
    w.addMessage(channel, timeNs, typeof payload === "string" ? encoder.encode(payload) : payload);
    this.messages++;
    if (this.maxBytes > 0 && !w.streaming && w.length > this.maxBytes) { this.stop(); this.options.onLimit?.(); }
  }
  /** Second channel on the same topic for pushes with typed arrays (message encoding `skyscope-layer`). */
  private binaryLayerChannel(id: string, kind: string): number {
    let ch = this.binaryLayerChannels.get(id);
    if (ch !== undefined) return ch;
    if (this.binarySchema === null) this.binarySchema = this.writer!.addSchema("skyscope.LayerMessage", "text", "SkyScopeLayer v1: layer push with typed-array attachments (see Mori.SkyScope streaming/layer-message)");
    ch = this.writer!.addChannel(LAYER_TOPIC_PREFIX + id, this.binarySchema, LAYER_MESSAGE_ENCODING, { kind });
    this.binaryLayerChannels.set(id, ch);
    return ch;
  }
  private layerChannel(id: string, kind: string): number {
    let ch = this.layerChannels.get(id);
    if (ch !== undefined) return ch;
    let schema = this.layerSchemas.get(kind);
    if (schema === undefined) { schema = this.writer!.addSchema(`skyscope.layer.${kind}`, "jsonschema", "{}"); this.layerSchemas.set(kind, schema); }
    ch = this.writer!.addChannel(LAYER_TOPIC_PREFIX + id, schema, "json", { kind });
    this.layerChannels.set(id, ch);
    return ch;
  }

  // ---- SignalSink ----
  /** Remembers the channel for later `start`s, forwards it, and appends it to the catalog channel while recording. */
  declareChannel(info: ChannelInfo): void {
    this.catalog.set(info.id, info);
    this.inner.signals?.declareChannel(info);
    if (this.writer) this.write(this.catalogChannel, secondsToNs(this.clock.now()), `{"channels":[${channelInfoJson(info)}]}`);
  }
  /** Forwards the frame and, while recording, writes it stamped with its `t0`. */
  pushFrame(frame: SkyScopeFrame): void {
    this.inner.signals?.pushFrame(frame);
    if (this.writer) this.write(this.frameChannel, secondsToNs(frame.t0), encodeFrame(frame));
  }
  /** Resets both inner sinks; the recording itself continues. */
  reset(): void { this.inner.signals?.reset?.(); this.inner.layers?.reset?.(); }

  // ---- LayerSink ----
  /** Remembers the declaration for later `start`s, forwards it, and writes it while recording. */
  declareLayer(id: string, kind: string, meta?: Record<string, unknown>): void {
    this.layerDecls.set(id, { kind, meta });
    this.inner.layers?.declareLayer(id, kind, meta);
    if (this.writer) this.write(this.layerChannel(id, kind), secondsToNs(this.clock.now()), `{"declare":{"kind":${JSON.stringify(kind)},"meta":${JSON.stringify(meta ?? {})}}}`);
  }
  /** Forwards the payload and, while recording, writes it as JSON, or as a binary layer message when it holds typed arrays. */
  push(id: string, payload: unknown): void {
    this.inner.layers?.push(id, payload);
    if (this.writer) {
      const kind = this.layerDecls.get(id)?.kind ?? "unknown";
      if (hasBinaryPayload(payload)) this.write(this.binaryLayerChannel(id, kind), secondsToNs(this.clock.now()), encodeLayerMessage(id, payload));
      else this.write(this.layerChannel(id, kind), secondsToNs(this.clock.now()), `{"push":${JSON.stringify(payload ?? null)}}`);
    }
  }
}

/** A parsed MCAP file → the playback Recording (frames, channel catalog, layer events). Foreign topics are ignored. */
export function recordingFromMcap(file: McapFile): Recording {
  const byTopic = new Map<number, string>();
  const binary = new Set<number>();
  for (const c of file.channels.values()) { byTopic.set(c.id, c.topic); if (c.messageEncoding === LAYER_MESSAGE_ENCODING) binary.add(c.id); }
  const channels = new Map<number, ChannelInfo>();
  const frames: SkyScopeFrame[] = [];
  const layerEvents: LayerEvent[] = [];
  for (const m of file.messages) {
    const topic = byTopic.get(m.channelId);
    if (topic === FRAMES_TOPIC) {
      frames.push(decodeFrame(m.data));
    } else if (topic === CHANNELS_TOPIC) {
      const j = JSON.parse(decoder.decode(m.data)) as { channels?: ChannelInfo[] };
      for (const c of j.channels ?? []) channels.set(c.id, c);
    } else if (topic?.startsWith(LAYER_TOPIC_PREFIX)) {
      const id = topic.slice(LAYER_TOPIC_PREFIX.length);
      if (binary.has(m.channelId)) { layerEvents.push({ t: m.logTime, id, payload: decodeLayerMessage(m.data).payload }); continue; }
      const j = JSON.parse(decoder.decode(m.data)) as { declare?: { kind: string; meta?: Record<string, unknown> }; push?: unknown };
      if (j.declare) layerEvents.push({ t: m.logTime, id, kind: j.declare.kind, meta: j.declare.meta });
      else if ("push" in j) layerEvents.push({ t: m.logTime, id, payload: j.push });
    }
  }
  frames.sort((a, b) => a.t0 - b.t0);
  let start = Infinity, end = -Infinity;
  for (const f of frames) { start = Math.min(start, f.t0); for (const ch of f.channels) end = Math.max(end, ch.encoding === "timestamped" ? ch.times[ch.times.length - 1] ?? f.t0 : ch.encoding === "regular" ? ch.tStart + ch.dt * Math.max(0, ch.values.length - 1) : f.t0); }
  for (const e of layerEvents) { start = Math.min(start, e.t); end = Math.max(end, e.t); }
  if (!Number.isFinite(start)) { start = 0; end = 0; }
  return { channels: [...channels.values()].sort((a, b) => a.id - b.id), frames, layerEvents, start, end: Math.max(start, end) };
}

/** Parses MCAP bytes straight into a playback `Recording`. */
export function readRecording(bytes: Uint8Array | ArrayBuffer): Recording { return recordingFromMcap(readMcap(bytes)); }
