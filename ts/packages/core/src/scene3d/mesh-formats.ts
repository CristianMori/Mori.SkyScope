// Mori.SkyScope — Mesh resource loaders for marker and robot-model meshes: binary and ASCII STL, and glTF 2.0 binary (GLB) with triangle primitives (float …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { createMesh, type Mesh3D } from "./painter3d.js";

/**
 * Mesh resource loaders for marker and robot-model meshes: binary and ASCII STL, and glTF 2.0 binary (GLB) with
 * triangle primitives (float positions, optional normals, u8/u16/u32 indices, node transforms applied, one merged
 * mesh). Mirrors `Mori.SkyScope.Core.Scene3D.MeshFormats`; pinned by `spec/fixtures/mesh-formats.json`.
 */
/**
 * Triangle mesh as flat float32 xyz positions, optional per-vertex normals and optional uint32 triangle indices
 * (unindexed when null: three consecutive vertices per triangle).
 */
export interface ParsedMesh { positions: Float32Array; normals: Float32Array | null; indices: Uint32Array | null }

const dec = new TextDecoder();

/** STL, binary or ASCII (auto-detected). Normals are recomputed per face so both cores agree. */
export function parseStl(bytes: Uint8Array): ParsedMesh {
  const head = dec.decode(bytes.subarray(0, Math.min(80, bytes.length)));
  const isAscii = head.trimStart().startsWith("solid") && (bytes.length < 84 || dec.decode(bytes.subarray(0, Math.min(bytes.length, 2048))).includes("facet"));
  if (isAscii) return parseAsciiStl(dec.decode(bytes));
  if (bytes.length < 84) throw new Error("stl: truncated");
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const n = dv.getUint32(80, true);
  if (84 + n * 50 > bytes.length) throw new Error("stl: truncated");
  const pos = new Float32Array(n * 9);
  for (let i = 0; i < n; i++) { const o = 84 + i * 50 + 12; for (let k = 0; k < 9; k++) pos[i * 9 + k] = dv.getFloat32(o + k * 4, true); }
  return { positions: pos, normals: faceNormals(pos), indices: null };
}

function parseAsciiStl(text: string): ParsedMesh {
  const out: number[] = [];
  const re = /vertex\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text))) out.push(Number(m[1]), Number(m[2]), Number(m[3]));
  const pos = Float32Array.from(out.slice(0, out.length - (out.length % 9)));
  return { positions: pos, normals: faceNormals(pos), indices: null };
}

/** Flat per-face normals (unit), computed in double and stored as float32. */
export function faceNormals(pos: Float32Array, indices?: Uint32Array | null): Float32Array {
  if (indices) {
    // indexed: average face normals into vertices
    const acc = new Float64Array(pos.length);
    for (let t = 0; t + 2 < indices.length; t += 3) {
      const a = indices[t]! * 3, b = indices[t + 1]! * 3, c = indices[t + 2]! * 3;
      const ux = pos[b]! - pos[a]!, uy = pos[b + 1]! - pos[a + 1]!, uz = pos[b + 2]! - pos[a + 2]!;
      const vx = pos[c]! - pos[a]!, vy = pos[c + 1]! - pos[a + 1]!, vz = pos[c + 2]! - pos[a + 2]!;
      const nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
      for (const i of [a, b, c]) { acc[i] = acc[i]! + nx; acc[i + 1] = acc[i + 1]! + ny; acc[i + 2] = acc[i + 2]! + nz; }
    }
    const out = new Float32Array(pos.length);
    for (let i = 0; i < pos.length; i += 3) { const ax = acc[i]!, ay = acc[i + 1]!, az = acc[i + 2]!; const l = Math.sqrt(ax * ax + ay * ay + az * az) || 1; out[i] = ax / l; out[i + 1] = ay / l; out[i + 2] = az / l; }
    return out;
  }
  const out = new Float32Array(pos.length);
  for (let o = 0; o + 8 < pos.length; o += 9) {
    const ux = pos[o + 3]! - pos[o]!, uy = pos[o + 4]! - pos[o + 1]!, uz = pos[o + 5]! - pos[o + 2]!;
    const vx = pos[o + 6]! - pos[o]!, vy = pos[o + 7]! - pos[o + 1]!, vz = pos[o + 8]! - pos[o + 2]!;
    let nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
    const l = Math.sqrt(nx * nx + ny * ny + nz * nz) || 1; nx /= l; ny /= l; nz /= l;
    for (let k = 0; k < 3; k++) { out[o + 3 * k] = nx; out[o + 3 * k + 1] = ny; out[o + 3 * k + 2] = nz; }
  }
  return out;
}

interface GltfDoc {
  scenes?: { nodes?: number[] }[]; scene?: number;
  nodes?: { mesh?: number; children?: number[]; matrix?: number[]; translation?: number[]; rotation?: number[]; scale?: number[] }[];
  meshes?: { primitives: { attributes: Record<string, number>; indices?: number; mode?: number }[] }[];
  accessors?: { bufferView?: number; byteOffset?: number; componentType: number; count: number; type: string; normalized?: boolean }[];
  bufferViews?: { buffer: number; byteOffset?: number; byteLength: number; byteStride?: number }[];
}

/** glTF 2.0 binary (.glb). Triangle primitives of every node in the default scene, transformed and merged. */
export function parseGlb(bytes: Uint8Array): ParsedMesh {
  const dv = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  if (bytes.length < 20 || dv.getUint32(0, true) !== 0x46546c67) throw new Error("glb: bad magic");
  let p = 12, json: GltfDoc | null = null, bin: Uint8Array | null = null;
  while (p + 8 <= bytes.length) {
    const len = dv.getUint32(p, true), type = dv.getUint32(p + 4, true);
    const chunk = bytes.subarray(p + 8, p + 8 + len);
    if (type === 0x4e4f534a) json = JSON.parse(dec.decode(chunk)) as GltfDoc; else if (type === 0x004e4942) bin = chunk;
    p += 8 + len;
  }
  if (!json) throw new Error("glb: no JSON chunk");
  return assembleGltf(json, bin ?? new Uint8Array(0));
}

function assembleGltf(doc: GltfDoc, bin: Uint8Array): ParsedMesh {
  const pos: number[] = [], nor: number[] = [], idx: number[] = [];
  let hasNormals = true;
  const accessor = (i: number): { data: Float64Array; comps: number } => {
    const a = doc.accessors![i]!, bv = doc.bufferViews![a.bufferView!]!;
    const comps = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 }[a.type] ?? 1;
    const size = { 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 }[a.componentType] ?? 4;
    const stride = bv.byteStride ?? comps * size, base = (bv.byteOffset ?? 0) + (a.byteOffset ?? 0);
    const dv = new DataView(bin.buffer, bin.byteOffset, bin.byteLength);
    const out = new Float64Array(a.count * comps);
    for (let k = 0; k < a.count; k++) for (let c = 0; c < comps; c++) {
      const o = base + k * stride + c * size;
      out[k * comps + c] = a.componentType === 5126 ? dv.getFloat32(o, true) : a.componentType === 5125 ? dv.getUint32(o, true) : a.componentType === 5123 ? dv.getUint16(o, true) : a.componentType === 5121 ? dv.getUint8(o) : a.componentType === 5122 ? dv.getInt16(o, true) : dv.getInt8(o);
    }
    return { data: out, comps };
  };
  const nodeMatrix = (n: NonNullable<GltfDoc["nodes"]>[number]): number[] => {
    if (n.matrix) return n.matrix;
    const t = n.translation ?? [0, 0, 0], q = n.rotation ?? [0, 0, 0, 1], s = n.scale ?? [1, 1, 1];
    const [x, y, z, w] = q as [number, number, number, number];
    const xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
    return [(1 - 2 * (yy + zz)) * s[0]!, 2 * (xy + wz) * s[0]!, 2 * (xz - wy) * s[0]!, 0, 2 * (xy - wz) * s[1]!, (1 - 2 * (xx + zz)) * s[1]!, 2 * (yz + wx) * s[1]!, 0, 2 * (xz + wy) * s[2]!, 2 * (yz - wx) * s[2]!, (1 - 2 * (xx + yy)) * s[2]!, 0, t[0]!, t[1]!, t[2]!, 1];
  };
  const mul = (a: number[], b: number[]): number[] => { const o = new Array<number>(16); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) o[c * 4 + r] = a[r]! * b[c * 4]! + a[4 + r]! * b[c * 4 + 1]! + a[8 + r]! * b[c * 4 + 2]! + a[12 + r]! * b[c * 4 + 3]!; return o; };
  const visit = (ni: number, parent: number[]): void => {
    const n = doc.nodes![ni]!, m = mul(parent, nodeMatrix(n));
    if (n.mesh !== undefined) for (const prim of doc.meshes![n.mesh]!.primitives) {
      if ((prim.mode ?? 4) !== 4 || prim.attributes.POSITION === undefined) continue;
      const base = pos.length / 3;
      const P = accessor(prim.attributes.POSITION);
      for (let k = 0; k < P.data.length; k += 3) {
        const x = P.data[k]!, y = P.data[k + 1]!, z = P.data[k + 2]!;
        pos.push(m[0]! * x + m[4]! * y + m[8]! * z + m[12]!, m[1]! * x + m[5]! * y + m[9]! * z + m[13]!, m[2]! * x + m[6]! * y + m[10]! * z + m[14]!);
      }
      if (prim.attributes.NORMAL !== undefined) {
        const N = accessor(prim.attributes.NORMAL);
        for (let k = 0; k < N.data.length; k += 3) { const x = N.data[k]!, y = N.data[k + 1]!, z = N.data[k + 2]!; nor.push(m[0]! * x + m[4]! * y + m[8]! * z, m[1]! * x + m[5]! * y + m[9]! * z, m[2]! * x + m[6]! * y + m[10]! * z); }
      } else hasNormals = false;
      if (prim.indices !== undefined) { const I = accessor(prim.indices); for (const v of I.data) idx.push(base + v); }
      else for (let k = 0; k < P.data.length / 3; k++) idx.push(base + k);
    }
    for (const c of n.children ?? []) visit(c, m);
  };
  const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  const roots = doc.scenes?.[doc.scene ?? 0]?.nodes ?? (doc.nodes ?? []).map((_, i) => i);
  for (const r of roots) visit(r, identity);
  const positions = Float32Array.from(pos), indices = Uint32Array.from(idx);
  return { positions, normals: hasNormals && nor.length === pos.length ? Float32Array.from(nor) : faceNormals(positions, indices), indices };
}

/** Pick the parser by extension or content. */
export function parseMeshResource(bytes: Uint8Array, name = ""): ParsedMesh {
  const lower = name.toLowerCase();
  if (lower.endsWith(".glb") || (bytes.length >= 4 && bytes[0] === 0x67 && bytes[1] === 0x6c && bytes[2] === 0x54 && bytes[3] === 0x46)) return parseGlb(bytes);
  if (lower.endsWith(".stl") || lower === "") return parseStl(bytes);
  throw new Error(`mesh: unsupported resource ${name}`);
}

/** A renderer mesh from a parsed resource (normals present, so markers can be lit). */
export function meshFromParsed(key: string, p: ParsedMesh): Mesh3D {
  return createMesh(p.positions, { key, normals: p.normals ?? faceNormals(p.positions, p.indices), ...(p.indices ? { indices: p.indices } : {}) });
}
