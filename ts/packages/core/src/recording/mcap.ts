// Mori.SkyScope — Minimal MCAP (https://mcap.dev) writer and reader — enough for SkyScope recordings and for reading unchunked or uncompressed-chunk files …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * Minimal MCAP (https://mcap.dev) writer and reader — enough for SkyScope recordings and for reading unchunked or
 * uncompressed-chunk files written by other tools. Writes: magic, Header, Schema/Channel/Message records, DataEnd, a
 * summary (schemas, channels, statistics, summary offsets) and the Footer; no chunks, no CRCs (zero = "not computed").
 * Reads: everything above plus Chunk records with compression "" or "lz4" (zstd chunks raise a clear error).
 * Mirrors `Mori.SkyScope.Core.Recording.Mcap`; pinned by `spec/fixtures/mcap.json` and `spec/mcap/*.mcap`.
 */
import { lz4Decompress } from "./lz4.js";

/** The 8-byte magic that opens and closes every MCAP file. */
export const MCAP_MAGIC = Uint8Array.from([0x89, 0x4d, 0x43, 0x41, 0x50, 0x30, 0x0d, 0x0a]);

/** Record opcodes from the MCAP specification. */
export const enum McapOp { Header = 1, Footer = 2, Schema = 3, Channel = 4, Message = 5, Chunk = 6, MessageIndex = 7, ChunkIndex = 8, Attachment = 9, AttachmentIndex = 10, Statistics = 11, Metadata = 12, MetadataIndex = 13, SummaryOffset = 14, DataEnd = 15 }

const NS = 1_000_000_000n;
/** Seconds (double) → nanoseconds (bigint), exact to the nanosecond for epoch-scale times. */
export function secondsToNs(t: number): bigint { const whole = Math.floor(t); return BigInt(whole) * NS + BigInt(Math.round((t - whole) * 1e9)); }
/** Nanoseconds (bigint) → seconds (double). */
export function nsToSeconds(ns: bigint): number { return Number(ns / NS) + Number(ns % NS) / 1e9; }

const encoder = new TextEncoder(), decoder = new TextDecoder();

/** Growable little-endian byte writer. */
export class ByteWriter {
  private buf = new Uint8Array(4096);
  private view = new DataView(this.buf.buffer);
  /** Bytes written so far. */
  length = 0;
  private ensure(n: number): void {
    if (this.length + n <= this.buf.length) return;
    let size = this.buf.length * 2;
    while (size < this.length + n) size *= 2;
    const next = new Uint8Array(size); next.set(this.buf.subarray(0, this.length)); this.buf = next; this.view = new DataView(next.buffer);
  }
  /** Appends one byte. */
  u8(v: number): void { this.ensure(1); this.buf[this.length++] = v; }
  /** Appends a little-endian u16. */
  u16(v: number): void { this.ensure(2); this.view.setUint16(this.length, v, true); this.length += 2; }
  /** Appends a little-endian u32. */
  u32(v: number): void { this.ensure(4); this.view.setUint32(this.length, v >>> 0, true); this.length += 4; }
  /** Appends a little-endian u64. */
  u64(v: bigint | number): void { this.ensure(8); this.view.setBigUint64(this.length, typeof v === "bigint" ? v : BigInt(v), true); this.length += 8; }
  /** Appends raw bytes. */
  bytes(b: Uint8Array): void { this.ensure(b.length); this.buf.set(b, this.length); this.length += b.length; }
  /** u32 length + UTF-8. */
  str(s: string): void { const b = encoder.encode(s); this.u32(b.length); this.bytes(b); }
  /** u32 byte length + bytes. */
  data(b: Uint8Array): void { this.u32(b.length); this.bytes(b); }
  /** Writes a record: opcode, u64 length, content. */
  record(op: McapOp, content: ByteWriter): void { this.u8(op); this.u64(content.length); this.bytes(content.toBytes()); }
  /** Copy of everything written. */
  toBytes(): Uint8Array { return this.buf.slice(0, this.length); }
}

/** Little-endian cursor over `bytes[start, end)`. Reads are not checked against `end`; callers consult `remaining`. */
export class ByteReader {
  /** DataView over the whole underlying array. */
  readonly view: DataView;
  /** Absolute read position in `bytes`. */
  pos = 0;
  /** `start` and `end` are absolute indices into `bytes`. */
  constructor(readonly bytes: Uint8Array, start = 0, readonly end = bytes.length) { this.view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength); this.pos = start; }
  /** Bytes left before `end`. */
  get remaining(): number { return this.end - this.pos; }
  /** Reads one byte and advances. */
  u8(): number { return this.bytes[this.pos++]!; }
  /** Reads a little-endian u16 and advances. */
  u16(): number { const v = this.view.getUint16(this.pos, true); this.pos += 2; return v; }
  /** Reads a little-endian u32 and advances. */
  u32(): number { const v = this.view.getUint32(this.pos, true); this.pos += 4; return v; }
  /** Reads a little-endian u64 and advances. */
  u64(): bigint { const v = this.view.getBigUint64(this.pos, true); this.pos += 8; return v; }
  /** Next `n` bytes as a view (not a copy) and advances. */
  take(n: number): Uint8Array { const b = this.bytes.subarray(this.pos, this.pos + n); this.pos += n; return b; }
  /** u32 length followed by UTF-8. */
  str(): string { return decoder.decode(this.take(this.u32())); }
  /** u32 byte length followed by the bytes, as a view. */
  data(): Uint8Array { return this.take(this.u32()); }
}

/** Schema record: `id` is 1-based (0 = no schema); `data` is the schema text or bytes in `encoding`. */
export interface McapSchema { id: number; name: string; encoding: string; data: Uint8Array }
/** Channel record: `id` is 0-based; `metadata` is written with its keys sorted. */
export interface McapChannel { id: number; schemaId: number; topic: string; messageEncoding: string; metadata: Record<string, string> }
/** Message record: `logTime`/`publishTime` in seconds; `data` views the file buffer. */
export interface McapMessage { channelId: number; sequence: number; logTime: number; publishTime: number; data: Uint8Array }
/** Parsed file: schemas and channels keyed by id, messages sorted by log time, `messageStart`/`messageEnd` in seconds (both 0 when there are none). */
export interface McapFile { profile: string; library: string; schemas: Map<number, McapSchema>; channels: Map<number, McapChannel>; messages: McapMessage[]; messageStart: number; messageEnd: number }

/** Options of the MCAP writer: header fields and chunking behaviour. */
export interface McapWriterOptions {
  /** Header fields; default "skyscope" and "Mori.SkyScope 0.1.0". */
  profile?: string | undefined; library?: string | undefined;
  /** Stream every record here instead of buffering the file in memory (the File System Access API, a socket…). */
  sink?: ((chunk: Uint8Array) => void) | undefined;
}

/** Where a writer puts its records: an in-memory ByteWriter or a counting pass-through to a sink. */
interface Output { readonly length: number; bytes(b: Uint8Array): void; record(op: McapOp, content: ByteWriter): void; toBytes(): Uint8Array }
class SinkOutput implements Output {
  length = 0;
  constructor(private readonly sink: (chunk: Uint8Array) => void) {}
  bytes(b: Uint8Array): void { this.sink(b); this.length += b.length; }
  record(op: McapOp, content: ByteWriter): void { const h = new ByteWriter(); h.u8(op); h.u64(content.length); this.bytes(h.toBytes()); this.bytes(content.toBytes()); }
  toBytes(): Uint8Array { return new Uint8Array(0); }
}

/** Deterministic writer: the same calls produce the same bytes in both cores. */
export class McapWriter {
  private readonly out: Output;
  /** True when records go to a sink instead of memory; `finish` then returns an empty array. */
  readonly streaming: boolean;
  private readonly schemas: McapSchema[] = [];
  private readonly channels: McapChannel[] = [];
  private readonly sequences = new Map<number, number>();
  private readonly counts = new Map<number, bigint>();
  private messageCount = 0n;
  private start = -1n; private end = -1n;
  private finished = false;

  /** Writes the magic and the Header record immediately. */
  constructor(o: McapWriterOptions = {}) {
    this.out = o.sink ? new SinkOutput(o.sink) : new ByteWriter();
    this.streaming = !!o.sink;
    this.out.bytes(MCAP_MAGIC);
    const h = new ByteWriter(); h.str(o.profile ?? "skyscope"); h.str(o.library ?? "Mori.SkyScope 0.1.0");
    this.out.record(McapOp.Header, h);
  }

  /** Bytes written so far. */
  get length(): number { return this.out.length; }

  /** Registers a schema and writes its record; returns its 1-based id. */
  addSchema(name: string, encoding: string, data: Uint8Array | string): number {
    const id = this.schemas.length + 1;
    const s: McapSchema = { id, name, encoding, data: typeof data === "string" ? encoder.encode(data) : data };
    this.schemas.push(s);
    this.out.record(McapOp.Schema, McapWriter.schemaRecord(s));
    return id;
  }
  /** Registers a channel and writes its record; returns its 0-based id. */
  addChannel(topic: string, schemaId: number, messageEncoding: string, metadata: Record<string, string> = {}): number {
    const id = this.channels.length;
    const c: McapChannel = { id, schemaId, topic, messageEncoding, metadata };
    this.channels.push(c); this.sequences.set(id, 0); this.counts.set(id, 0n);
    this.out.record(McapOp.Channel, McapWriter.channelRecord(c));
    return id;
  }
  /** Writes a message; times are unix nanoseconds. Throws after `finish` or for an unknown channel. */
  addMessage(channelId: number, logTimeNs: bigint, data: Uint8Array, publishTimeNs: bigint = logTimeNs): void {
    if (this.finished) throw new Error("McapWriter: already finished");
    const seq = this.sequences.get(channelId);
    if (seq === undefined) throw new Error(`McapWriter: unknown channel ${channelId}`);
    this.sequences.set(channelId, seq + 1); this.counts.set(channelId, this.counts.get(channelId)! + 1n); this.messageCount++;
    if (this.start < 0n || logTimeNs < this.start) this.start = logTimeNs;
    if (logTimeNs > this.end) this.end = logTimeNs;
    const m = new ByteWriter(); m.u16(channelId); m.u32(seq); m.u64(logTimeNs); m.u64(publishTimeNs); m.bytes(data);
    this.out.record(McapOp.Message, m);
  }

  /** DataEnd, summary section (schemas, channels, statistics), summary offsets, footer, magic. Returns the file (empty when streaming). */
  finish(): Uint8Array {
    if (this.finished) return this.out.toBytes();
    this.finished = true;
    const de = new ByteWriter(); de.u32(0); this.out.record(McapOp.DataEnd, de);
    const summaryStart = BigInt(this.out.length);
    const groups: { op: McapOp; start: bigint; length: bigint }[] = [];
    const group = (op: McapOp, write: () => void): void => { const s = this.out.length; write(); if (this.out.length > s) groups.push({ op, start: BigInt(s), length: BigInt(this.out.length - s) }); };
    group(McapOp.Schema, () => { for (const s of this.schemas) this.out.record(McapOp.Schema, McapWriter.schemaRecord(s)); });
    group(McapOp.Channel, () => { for (const c of this.channels) this.out.record(McapOp.Channel, McapWriter.channelRecord(c)); });
    group(McapOp.Statistics, () => {
      const st = new ByteWriter();
      st.u64(this.messageCount); st.u16(this.schemas.length); st.u32(this.channels.length); st.u32(0); st.u32(0); st.u32(0);
      st.u64(this.start < 0n ? 0n : this.start); st.u64(this.end < 0n ? 0n : this.end);
      const cm = new ByteWriter(); for (const c of this.channels) { cm.u16(c.id); cm.u64(this.counts.get(c.id)!); }
      st.data(cm.toBytes());
      this.out.record(McapOp.Statistics, st);
    });
    const summaryOffsetStart = BigInt(this.out.length);
    for (const g of groups) { const so = new ByteWriter(); so.u8(g.op); so.u64(g.start); so.u64(g.length); this.out.record(McapOp.SummaryOffset, so); }
    const f = new ByteWriter(); f.u64(summaryStart); f.u64(summaryOffsetStart); f.u32(0);
    this.out.record(McapOp.Footer, f);
    this.out.bytes(MCAP_MAGIC);
    return this.out.toBytes();
  }

  private static schemaRecord(s: McapSchema): ByteWriter { const w = new ByteWriter(); w.u16(s.id); w.str(s.name); w.str(s.encoding); w.data(s.data); return w; }
  private static channelRecord(c: McapChannel): ByteWriter {
    const w = new ByteWriter(); w.u16(c.id); w.u16(c.schemaId); w.str(c.topic); w.str(c.messageEncoding);
    const md = new ByteWriter(); for (const k of Object.keys(c.metadata).sort()) { md.str(k); md.str(c.metadata[k]!); }
    w.data(md.toBytes());
    return w;
  }
}

/** Parse a whole file from memory. Summary records are ignored; chunks must be uncompressed. */
export function readMcap(input: Uint8Array | ArrayBuffer): McapFile {
  const bytes = input instanceof Uint8Array ? input : new Uint8Array(input);
  if (bytes.length < 16 || !MCAP_MAGIC.every((b, i) => bytes[i] === b)) throw new Error("not an MCAP file (bad magic)");
  const file: McapFile = { profile: "", library: "", schemas: new Map(), channels: new Map(), messages: [], messageStart: Infinity, messageEnd: -Infinity };
  const r = new ByteReader(bytes, MCAP_MAGIC.length);
  const readRecords = (rr: ByteReader, nested: boolean): boolean => {
    while (rr.remaining >= 9) {
      const op = rr.u8(); const len = Number(rr.u64()); const start = rr.pos, end = start + len;
      if (end > rr.end) throw new Error(`MCAP: truncated record (opcode ${op})`);
      const body = new ByteReader(rr.bytes, start, end);
      switch (op) {
        case McapOp.Header: file.profile = body.str(); file.library = body.str(); break;
        case McapOp.Schema: { const id = body.u16(); if (!file.schemas.has(id)) file.schemas.set(id, { id, name: body.str(), encoding: body.str(), data: body.data() }); break; }
        case McapOp.Channel: {
          const id = body.u16();
          if (!file.channels.has(id)) {
            const schemaId = body.u16(), topic = body.str(), messageEncoding = body.str();
            const md = new ByteReader(rr.bytes, body.pos + 4, body.pos + 4 + body.u32()); body.pos = md.end;
            const metadata: Record<string, string> = {}; while (md.remaining > 0) { const k = md.str(); metadata[k] = md.str(); }
            file.channels.set(id, { id, schemaId, topic, messageEncoding, metadata });
          }
          break;
        }
        case McapOp.Message: {
          const channelId = body.u16(), sequence = body.u32(), logTime = nsToSeconds(body.u64()), publishTime = nsToSeconds(body.u64());
          file.messages.push({ channelId, sequence, logTime, publishTime, data: rr.bytes.subarray(body.pos, end) });
          if (logTime < file.messageStart) file.messageStart = logTime;
          if (logTime > file.messageEnd) file.messageEnd = logTime;
          break;
        }
        case McapOp.Chunk: {
          if (nested) throw new Error("MCAP: nested chunk");
          body.u64(); body.u64(); const uncompressedSize = Number(body.u64()); body.u32();
          const compression = body.str(); const recordsLength = Number(body.u64());
          if (compression === "") {
            if (recordsLength !== uncompressedSize && uncompressedSize !== 0) throw new Error("MCAP: chunk size mismatch");
            readRecords(new ByteReader(rr.bytes, body.pos, body.pos + recordsLength), true);
          } else if (compression === "lz4") {
            const decoded = lz4Decompress(rr.bytes.subarray(body.pos, body.pos + recordsLength), uncompressedSize);
            readRecords(new ByteReader(decoded, 0, decoded.length), true);
          } else throw new Error(`MCAP: chunk compression "${compression}" is not supported (uncompressed and lz4 chunks are)`);
          break;
        }
        case McapOp.DataEnd: return false;
        case McapOp.Footer: return false;
        default: break; // indexes, attachments, metadata, statistics: skipped
      }
      rr.pos = end;
    }
    return true;
  };
  readRecords(r, false);
  if (file.messages.length === 0) { file.messageStart = 0; file.messageEnd = 0; }
  file.messages.sort((a, b) => a.logTime - b.logTime);
  return file;
}
