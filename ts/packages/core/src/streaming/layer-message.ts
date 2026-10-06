// Mori.SkyScope — SkyScopeLayer v1 — the binary form of a layer push whose payload carries typed arrays (point clouds, meshes, grids).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * SkyScopeLayer v1 — the binary form of a layer push whose payload carries typed arrays (point clouds, meshes,
 * grids). Mirrors `Mori.SkyScope.Core.Streaming.LayerMessage`; pinned by `spec/fixtures/layer-message.json` and
 * the golden files in `spec/frames/layer-*.bin`. Travels as a binary WebSocket message next to SkyScopeFrames
 * (distinguished by magic) and as MCAP message encoding `skyscope-layer`.
 *
 * Little-endian:
 *   header (16): u32 magic "SKSL" | u16 version=1 | u16 flags=0 | u16 idLen | u16 attachmentCount | u32 jsonLen
 *   id utf8, json utf8 (the payload without the typed-array fields), pad to 8
 *   attachment: u16 nameLen | u8 dtype | u8 reserved | u32 count | name utf8 | pad to 8 | data | pad to 8
 *     dtype 0 f32, 1 f64, 2 u8, 3 u16, 4 i16, 5 u32, 6 i32
 */
export const LAYER_MAGIC = 0x4c534b53;
/** The only version `decodeLayerMessage` accepts. */
export const LAYER_VERSION = 1;
/** Size of the fixed message header. */
export const LAYER_HEADER_BYTES = 16;

/** Typed arrays that may travel as attachments; dtype codes 0–6 in this order. */
export type LayerArray = Float32Array | Float64Array | Uint8Array | Uint16Array | Int16Array | Uint32Array | Int32Array;
const DTYPES = [Float32Array, Float64Array, Uint8Array, Uint16Array, Int16Array, Uint32Array, Int32Array] as const;
function dtypeOf(a: LayerArray): number {
  const i = DTYPES.findIndex((c) => a instanceof c);
  if (i < 0) throw new Error("layer message: unsupported array type");
  return i;
}
const pad8 = (n: number): number => (n + 7) & ~7;
const enc = new TextEncoder(), dec = new TextDecoder();

/** True when the payload is an object with at least one typed-array field at the top level. */
export function hasBinaryPayload(payload: unknown): payload is Record<string, unknown> {
  if (!payload || typeof payload !== "object" || Array.isArray(payload)) return false;
  for (const v of Object.values(payload as Record<string, unknown>)) if (ArrayBuffer.isView(v) && !(v instanceof DataView)) return true;
  return false;
}

/** True when a binary WebSocket/MCAP message is a layer message rather than a frame. */
export function isLayerMessage(bytes: Uint8Array): boolean {
  return bytes.length >= LAYER_HEADER_BYTES && new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint32(0, true) === LAYER_MAGIC;
}

/** Serialises a layer push: typed-array fields become attachments, every other defined field goes into the JSON part. Throws when the id or attachment count exceeds 65535 or an array type is unsupported. */
export function encodeLayerMessage(id: string, payload: Record<string, unknown>): Uint8Array {
  const json: Record<string, unknown> = {};
  const attachments: { name: Uint8Array; dtype: number; data: LayerArray }[] = [];
  for (const [k, v] of Object.entries(payload)) {
    if (ArrayBuffer.isView(v) && !(v instanceof DataView)) attachments.push({ name: enc.encode(k), dtype: dtypeOf(v as LayerArray), data: v as LayerArray });
    else if (v !== undefined) json[k] = v;
  }
  const idBytes = enc.encode(id), jsonBytes = enc.encode(JSON.stringify(json));
  if (idBytes.length > 0xffff || attachments.length > 0xffff) throw new Error("layer message: id or attachment count too large");
  let len = pad8(LAYER_HEADER_BYTES + idBytes.length + jsonBytes.length);
  for (const a of attachments) len += pad8(8 + a.name.length) + pad8(a.data.byteLength);
  const out = new Uint8Array(len), dv = new DataView(out.buffer);
  dv.setUint32(0, LAYER_MAGIC, true); dv.setUint16(4, LAYER_VERSION, true); dv.setUint16(6, 0, true);
  dv.setUint16(8, idBytes.length, true); dv.setUint16(10, attachments.length, true); dv.setUint32(12, jsonBytes.length, true);
  out.set(idBytes, LAYER_HEADER_BYTES); out.set(jsonBytes, LAYER_HEADER_BYTES + idBytes.length);
  let p = pad8(LAYER_HEADER_BYTES + idBytes.length + jsonBytes.length);
  for (const a of attachments) {
    dv.setUint16(p, a.name.length, true); out[p + 2] = a.dtype; out[p + 3] = 0; dv.setUint32(p + 4, a.data.length, true);
    out.set(a.name, p + 8);
    p += pad8(8 + a.name.length);
    const bytes = new Uint8Array(a.data.buffer, a.data.byteOffset, a.data.byteLength);
    if (a.dtype === 2 || isLittleEndian) out.set(bytes, p);
    else { const es = a.data.BYTES_PER_ELEMENT; for (let i = 0; i < a.data.length; i++) for (let b = 0; b < es; b++) out[p + i * es + b] = bytes[i * es + es - 1 - b]!; }
    p += pad8(a.data.byteLength);
  }
  return out;
}

const isLittleEndian = new Uint8Array(new Uint16Array([1]).buffer)[0] === 1;

/** Layer `id` and its payload with attachments restored as typed arrays under their field names. */
export interface DecodedLayerMessage { id: string; payload: Record<string, unknown> }

/** Decode; typed arrays are copied into fresh, aligned buffers so callers may keep them. */
export function decodeLayerMessage(buffer: ArrayBuffer | Uint8Array): DecodedLayerMessage {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.length < LAYER_HEADER_BYTES || dv.getUint32(0, true) !== LAYER_MAGIC) throw new Error("layer message: bad magic");
  const version = dv.getUint16(4, true);
  if (version !== LAYER_VERSION) throw new Error(`layer message: unsupported version ${version}`);
  const idLen = dv.getUint16(8, true), count = dv.getUint16(10, true), jsonLen = dv.getUint32(12, true);
  const id = dec.decode(bytes.subarray(LAYER_HEADER_BYTES, LAYER_HEADER_BYTES + idLen));
  const payload = JSON.parse(dec.decode(bytes.subarray(LAYER_HEADER_BYTES + idLen, LAYER_HEADER_BYTES + idLen + jsonLen))) as Record<string, unknown>;
  let p = pad8(LAYER_HEADER_BYTES + idLen + jsonLen);
  for (let i = 0; i < count; i++) {
    if (p + 8 > bytes.length) throw new Error("layer message: truncated");
    const nameLen = dv.getUint16(p, true), dtype = bytes[p + 2]!, n = dv.getUint32(p + 4, true);
    const ctor = DTYPES[dtype];
    if (!ctor) throw new Error(`layer message: unknown dtype ${dtype}`);
    const name = dec.decode(bytes.subarray(p + 8, p + 8 + nameLen));
    p += pad8(8 + nameLen);
    const byteLen = n * ctor.BYTES_PER_ELEMENT;
    if (p + byteLen > bytes.length) throw new Error("layer message: truncated");
    const copy = bytes.slice(p, p + byteLen);
    if (dtype !== 2 && !isLittleEndian) { const es = ctor.BYTES_PER_ELEMENT; for (let k = 0; k < n; k++) copy.subarray(k * es, k * es + es).reverse(); }
    payload[name] = new ctor(copy.buffer, 0, n);
    p += pad8(byteLen);
  }
  return { id, payload };
}
