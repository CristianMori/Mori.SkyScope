// Mori.SkyScope — SkyScopeFrame v1 — the binary data-plane format.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * SkyScopeFrame v1 — the binary data-plane format. Mirrors `Mori.SkyScope.Core.Streaming.FrameCodec`;
 * pinned by the golden files in `spec/frames/` (decode must match the .json, encode must match the .bin).
 *
 * Little-endian, every channel 8-byte aligned so decoding is zero-copy typed-array views:
 *   header (24):  u32 magic "SKSF" | u16 version=1 | u16 flags=0 | u32 seq | u32 channelCount | f64 t0
 *   channel (8):  u16 id | u8 encoding | u8 reserved | u32 count, then body, then pad to 8
 *     enc 0 timestamped: f64 times[count], f32 values[count]
 *     enc 1 regular:     f64 tStart, f64 dt, f32 values[count]
 *     enc 2 quantized:   f64 tStart, f64 dt, f32 scale, f32 offset, i16 q[count]   (value = q*scale + offset)
 */
export const FRAME_MAGIC = 0x46534b53;
/** The only version `decodeFrame` accepts. */
export const FRAME_VERSION = 1;
/** Size of the fixed frame header. */
export const FRAME_HEADER_BYTES = 24;
/** Size of each channel header, before its body. */
export const CHANNEL_HEADER_BYTES = 8;

/** Channel body layouts; wire codes 0, 1 and 2 in this order. */
export type FrameEncoding = "timestamped" | "regular" | "quantized";
const ENCODING_CODE: Record<FrameEncoding, number> = { timestamped: 0, regular: 1, quantized: 2 };
const ENCODING_NAME: FrameEncoding[] = ["timestamped", "regular", "quantized"];

/** One channel of a frame. `id` is the u16 channel id; `times`/`tStart` are seconds and `dt` is the sample period in seconds. */
export type FrameChannel =
  /** An f64 time per f32 sample; both arrays have the same length. */
  | { id: number; encoding: "timestamped"; times: Float64Array; values: Float32Array }
  /** Evenly spaced f32 samples: time i = tStart + i·dt. */
  | { id: number; encoding: "regular"; tStart: number; dt: number; values: Float32Array }
  /** Evenly spaced int16 samples; value = q·scale + offset in f32 arithmetic (see `channelValues`). */
  | { id: number; encoding: "quantized"; tStart: number; dt: number; scale: number; offset: number; q: Int16Array };

/** A decoded frame: `seq` is a u32 counter, `t0` the batch reference time in seconds, channels in wire order. */
export interface SkyScopeFrame { seq: number; t0: number; channels: FrameChannel[] }

const LITTLE_ENDIAN = new Uint8Array(new Uint16Array([1]).buffer)[0] === 1;
const pad8 = (n: number): number => (n + 7) & ~7;

/** Number of samples in the channel. */
export function channelCount(ch: FrameChannel): number {
  return ch.encoding === "quantized" ? ch.q.length : ch.values.length;
}

/** Encoded size of the channel including its header and padding to 8 bytes. */
export function channelByteLength(ch: FrameChannel): number {
  const n = channelCount(ch);
  const body = ch.encoding === "timestamped" ? 12 * n : ch.encoding === "regular" ? 16 + 4 * n : 24 + 2 * n;
  return pad8(CHANNEL_HEADER_BYTES + body);
}

/** Exact number of bytes `encodeFrame` will produce. */
export function frameByteLength(frame: SkyScopeFrame): number {
  let len = FRAME_HEADER_BYTES;
  for (const ch of frame.channels) len += channelByteLength(ch);
  return len;
}

/** Values of any channel as f32 (dequantizes enc 2; f32 arithmetic to match the C# side bit-for-bit). */
export function channelValues(ch: FrameChannel): Float32Array {
  if (ch.encoding !== "quantized") return ch.values;
  const out = new Float32Array(ch.q.length);
  for (let i = 0; i < out.length; i++) out[i] = Math.fround(Math.fround(ch.q[i]! * ch.scale) + ch.offset);
  return out;
}

/** Serialises into `target` (returning the used prefix; throws RangeError when too small) or into a new buffer. Arrays are written as given, so quantized channels must already be prepared. */
export function encodeFrame(frame: SkyScopeFrame, target?: Uint8Array): Uint8Array {
  const len = frameByteLength(frame);
  if (target && target.byteLength < len) throw new RangeError(`target too small: ${target.byteLength} < ${len}`);
  const out = target ? target.subarray(0, len) : new Uint8Array(len);
  const dv = new DataView(out.buffer, out.byteOffset, len);
  dv.setUint32(0, FRAME_MAGIC, true);
  dv.setUint16(4, FRAME_VERSION, true);
  dv.setUint16(6, 0, true);
  dv.setUint32(8, frame.seq >>> 0, true);
  dv.setUint32(12, frame.channels.length, true);
  dv.setFloat64(16, frame.t0, true);
  let off = FRAME_HEADER_BYTES;
  for (const ch of frame.channels) {
    const n = channelCount(ch);
    dv.setUint16(off, ch.id, true);
    dv.setUint8(off + 2, ENCODING_CODE[ch.encoding]);
    dv.setUint8(off + 3, 0);
    dv.setUint32(off + 4, n, true);
    let p = off + CHANNEL_HEADER_BYTES;
    switch (ch.encoding) {
      case "timestamped":
        p = writeArray(dv, out, p, ch.times, 8); p = writeArray(dv, out, p, ch.values, 4); break;
      case "regular":
        dv.setFloat64(p, ch.tStart, true); dv.setFloat64(p + 8, ch.dt, true);
        p = writeArray(dv, out, p + 16, ch.values, 4); break;
      case "quantized":
        dv.setFloat64(p, ch.tStart, true); dv.setFloat64(p + 8, ch.dt, true);
        dv.setFloat32(p + 16, ch.scale, true); dv.setFloat32(p + 20, ch.offset, true);
        p = writeArray(dv, out, p + 24, ch.q, 2); break;
    }
    const end = off + channelByteLength(ch);
    out.fill(0, p, end);
    off = end;
  }
  return out;
}

/** Options for `decodeFrame`. */
export interface DecodeOptions { /** Copy arrays out of the input buffer instead of viewing into it. */ copy?: boolean }

/** Parses a frame. Without `copy`, typed arrays view the input buffer when alignment allows, so the input must outlive them. Throws RangeError with message "truncated-frame", "bad-magic", "unsupported-version:N" or "bad-encoding:N". */
export function decodeFrame(input: Uint8Array | ArrayBuffer, options: DecodeOptions = {}): SkyScopeFrame {
  const bytes = input instanceof ArrayBuffer ? new Uint8Array(input) : input;
  if (bytes.byteLength < FRAME_HEADER_BYTES) throw new RangeError("truncated-frame");
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (dv.getUint32(0, true) !== FRAME_MAGIC) throw new RangeError("bad-magic");
  const version = dv.getUint16(4, true);
  if (version !== FRAME_VERSION) throw new RangeError(`unsupported-version:${version}`);
  const seq = dv.getUint32(8, true);
  const count = dv.getUint32(12, true);
  const t0 = dv.getFloat64(16, true);
  const channels: FrameChannel[] = [];
  let off = FRAME_HEADER_BYTES;
  for (let i = 0; i < count; i++) {
    if (off + CHANNEL_HEADER_BYTES > bytes.byteLength) throw new RangeError("truncated-frame");
    const id = dv.getUint16(off, true);
    const code = dv.getUint8(off + 2);
    const encoding = ENCODING_NAME[code];
    if (!encoding) throw new RangeError(`bad-encoding:${code}`);
    const n = dv.getUint32(off + 4, true);
    let p = off + CHANNEL_HEADER_BYTES;
    let ch: FrameChannel;
    if (encoding === "timestamped") {
      need(bytes, p, 12 * n);
      const times = readArray(Float64Array, dv, bytes, p, n, options.copy); p += 8 * n;
      const values = readArray(Float32Array, dv, bytes, p, n, options.copy); p += 4 * n;
      ch = { id, encoding, times, values };
    } else if (encoding === "regular") {
      need(bytes, p, 16 + 4 * n);
      ch = { id, encoding, tStart: dv.getFloat64(p, true), dt: dv.getFloat64(p + 8, true), values: readArray(Float32Array, dv, bytes, p + 16, n, options.copy) };
      p += 16 + 4 * n;
    } else {
      need(bytes, p, 24 + 2 * n);
      ch = { id, encoding, tStart: dv.getFloat64(p, true), dt: dv.getFloat64(p + 8, true), scale: dv.getFloat32(p + 16, true), offset: dv.getFloat32(p + 20, true), q: readArray(Int16Array, dv, bytes, p + 24, n, options.copy) };
      p += 24 + 2 * n;
    }
    channels.push(ch);
    off += pad8(p - off);
  }
  return { seq, t0, channels };
}

function need(bytes: Uint8Array, p: number, n: number): void {
  if (p + n > bytes.byteLength) throw new RangeError("truncated-frame");
}

type TA = Float64Array | Float32Array | Int16Array;
interface TACtor<T extends TA> { new (n: number): T; new (buffer: ArrayBufferLike, byteOffset: number, length: number): T; readonly BYTES_PER_ELEMENT: number }

function readArray<T extends TA>(ctor: TACtor<T>, dv: DataView, bytes: Uint8Array, p: number, n: number, copy?: boolean): T {
  const abs = bytes.byteOffset + p;
  if (LITTLE_ENDIAN && abs % ctor.BYTES_PER_ELEMENT === 0) {
    const view = new ctor(bytes.buffer, abs, n);
    return copy ? (view.slice() as T) : view;
  }
  const out = new ctor(n);
  for (let i = 0; i < n; i++) {
    const o = p + i * ctor.BYTES_PER_ELEMENT;
    out[i] = ctor.BYTES_PER_ELEMENT === 8 ? dv.getFloat64(o, true) : ctor.BYTES_PER_ELEMENT === 4 ? dv.getFloat32(o, true) : dv.getInt16(o, true);
  }
  return out;
}

function writeArray(dv: DataView, out: Uint8Array, p: number, arr: TA, size: number): number {
  const abs = out.byteOffset + p;
  if (LITTLE_ENDIAN && abs % size === 0) {
    new (arr.constructor as TACtor<TA>)(out.buffer, abs, arr.length).set(arr);
  } else {
    for (let i = 0; i < arr.length; i++) {
      const o = p + i * size;
      if (size === 8) dv.setFloat64(o, arr[i]!, true); else if (size === 4) dv.setFloat32(o, arr[i]!, true); else dv.setInt16(o, arr[i]!, true);
    }
  }
  return p + arr.length * size;
}
