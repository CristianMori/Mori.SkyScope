// Mori.SkyScope — LZ4 decompression (block format and frame format) for MCAP chunks with compression "lz4".
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * LZ4 decompression (block format and frame format) for MCAP chunks with compression "lz4". Checksums are not verified.
 * Mirrors `Mori.SkyScope.Core.Mcap.Lz4`; pinned by `spec/fixtures/mcap.json` and `spec/mcap/sample-lz4.mcap`.
 */
const FRAME_MAGIC = 0x184d2204;
const SKIPPABLE_MIN = 0x184d2a50, SKIPPABLE_MAX = 0x184d2a5f;

/** Decode one LZ4 block from `src[start, end)` into `dst` at `dstPos`; returns the new destination position. */
export function lz4DecodeBlock(src: Uint8Array, start: number, end: number, dst: Uint8Array, dstPos: number): number {
  let i = start, o = dstPos;
  while (i < end) {
    const token = src[i++]!;
    let lit = token >>> 4;
    if (lit === 15) { let b: number; do { b = src[i++]!; lit += b; } while (b === 255); }
    if (o + lit > dst.length || i + lit > end) throw new Error("lz4: block overruns its output or input");
    dst.set(src.subarray(i, i + lit), o); i += lit; o += lit;
    if (i >= end) break;                                   // the last sequence carries literals only
    const offset = src[i]! | (src[i + 1]! << 8); i += 2;
    if (offset === 0 || offset > o - dstPos) throw new Error("lz4: bad match offset");
    let len = token & 15;
    if (len === 15) { let b: number; do { b = src[i++]!; len += b; } while (b === 255); }
    len += 4;
    if (o + len > dst.length) throw new Error("lz4: match overruns the output");
    let from = o - offset;
    if (offset >= len) { dst.copyWithin(o, from, from + len); o += len; }
    else for (let k = 0; k < len; k++) dst[o++] = dst[from++]!;   // overlapping copy, byte by byte
  }
  return o;
}

/** Decode an LZ4 frame (possibly several, skippable frames allowed) into a buffer of `expectedSize` bytes. */
export function lz4DecodeFrame(bytes: Uint8Array, expectedSize: number): Uint8Array {
  const dst = new Uint8Array(expectedSize);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let i = 0, o = 0;
  while (i + 4 <= bytes.length) {
    const magic = view.getUint32(i, true); i += 4;
    if (magic >= SKIPPABLE_MIN && magic <= SKIPPABLE_MAX) { const n = view.getUint32(i, true); i += 4 + n; continue; }
    if (magic !== FRAME_MAGIC) throw new Error("lz4: not a frame");
    const flg = bytes[i]!, bd = bytes[i + 1]!; i += 2;
    if ((flg >>> 6) !== 1) throw new Error("lz4: unsupported frame version");
    const blockChecksum = (flg & 0x10) !== 0, contentSize = (flg & 0x08) !== 0, contentChecksum = (flg & 0x04) !== 0, dictId = (flg & 0x01) !== 0;
    void bd;
    if (contentSize) i += 8;
    if (dictId) i += 4;
    i += 1;                                                 // header checksum (not verified)
    for (;;) {
      const size = view.getUint32(i, true); i += 4;
      if (size === 0) break;                                // end mark
      const uncompressed = (size & 0x80000000) !== 0, n = size & 0x7fffffff;
      if (uncompressed) { dst.set(bytes.subarray(i, i + n), o); o += n; }
      else o = lz4DecodeBlock(bytes, i, i + n, dst, o);
      i += n;
      if (blockChecksum) i += 4;
    }
    if (contentChecksum) i += 4;
  }
  return o === expectedSize ? dst : dst.subarray(0, o);
}

/** MCAP chunk payload → records: frame format when it starts with the frame magic, otherwise a raw block. */
export function lz4Decompress(bytes: Uint8Array, uncompressedSize: number): Uint8Array {
  if (bytes.length >= 4 && new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getUint32(0, true) === FRAME_MAGIC) return lz4DecodeFrame(bytes, uncompressedSize);
  const dst = new Uint8Array(uncompressedSize);
  const n = lz4DecodeBlock(bytes, 0, bytes.length, dst, 0);
  return n === uncompressedSize ? dst : dst.subarray(0, n);
}
