// Mori.SkyScope — Mesh resource loaders for marker and robot-model meshes: binary and ASCII STL, glTF 2.0 binary (GLB) and COLLADA (.dae).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { createMesh, type Mesh3D } from "./painter3d.js";
import { parseXml, type XmlElement } from "./urdf.js";

/**
 * Mesh resource loaders for marker and robot-model meshes: binary and ASCII STL, glTF 2.0 (binary `.glb` and JSON
 * `.gltf`, buffers embedded, as `data:` URIs or external files through a resolver) with triangle primitives (float
 * positions, optional normals, u8/u16/u32 indices, node transforms applied, one merged mesh, the first material's
 * base colour) and COLLADA (.dae: triangles and polylists, node transforms, up axis, the first bound material's
 * diffuse colour). Mirrors `Mori.SkyScope.Core.Scene3D.MeshFormats`; pinned by `spec/fixtures/mesh-formats.json`.
 */
/**
 * Triangle mesh as flat float32 xyz positions, optional per-vertex normals and optional uint32 triangle indices
 * (unindexed when null: three consecutive vertices per triangle). `color` is the material colour the format carried
 * for its first primitive (RGBA 0..1), used by mesh markers that set no colour of their own.
 */
export interface ParsedMesh { positions: Float32Array; normals: Float32Array | null; indices: Uint32Array | null; color?: [number, number, number, number] | undefined }

/** How a parser finds files a resource refers to (glTF external buffers): `resolve` gets the URI as written and returns its bytes, or undefined when unknown. */
export interface MeshResourceOptions { resolve?: ((uri: string) => Uint8Array | undefined) | undefined }

/**
 * Resolves `relative` against `base` like a URL: absolute references (a scheme such as `package:` or `file:`, or a
 * leading `/`) are returned as given; otherwise the last path segment of `base` is replaced and `.`/`..` segments are
 * collapsed (never above the scheme or the root). Used to find a mesh's sibling files from its own URI.
 */
export function joinUri(base: string, relative: string): string {
  if (/^[a-zA-Z][a-zA-Z0-9+.-]*:/.test(relative) || relative.startsWith("/")) return relative;
  const scheme = /^[a-zA-Z][a-zA-Z0-9+.-]*:\/\/[^/]*\/?/.exec(base)?.[0] ?? "";
  const rest = base.slice(scheme.length);
  const dir = rest.includes("/") ? rest.slice(0, rest.lastIndexOf("/") + 1) : "";
  const out: string[] = [];
  for (const seg of (dir + relative).split("/")) {
    if (seg === "." || seg === "") continue;
    if (seg === "..") { if (out.length) out.pop(); continue; }
    out.push(seg);
  }
  return (scheme.endsWith("/") || scheme === "" ? scheme : scheme + "/") + out.join("/");
}

/** `#rrggbb` of an RGBA 0..1 colour (alpha ignored), channels clamped and rounded. */
export function hexOfColor(c: [number, number, number, number]): string {
  const h = (v: number): string => Math.round(Math.min(1, Math.max(0, v)) * 255).toString(16).padStart(2, "0");
  return `#${h(c[0])}${h(c[1])}${h(c[2])}`;
}

const dec = new TextDecoder();
const fromBase64 = (s: string): Uint8Array => Uint8Array.from(atob(s), (c) => c.charCodeAt(0));

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
  meshes?: { primitives: { attributes: Record<string, number>; indices?: number; mode?: number; material?: number }[] }[];
  materials?: { pbrMetallicRoughness?: { baseColorFactor?: number[] } }[];
  accessors?: { bufferView?: number; byteOffset?: number; componentType: number; count: number; type: string; normalized?: boolean }[];
  bufferViews?: { buffer: number; byteOffset?: number; byteLength: number; byteStride?: number }[];
  buffers?: { uri?: string; byteLength?: number }[];
}

/** glTF 2.0 binary (.glb). Triangle primitives of every node in the default scene, transformed and merged; buffers come from the BIN chunk, `data:` URIs or `options.resolve`. */
export function parseGlb(bytes: Uint8Array, options: MeshResourceOptions = {}): ParsedMesh {
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
  return assembleGltf(json, bin, options);
}

/** glTF 2.0 JSON (.gltf). Like `parseGlb`, with every buffer a `data:` URI (decoded here) or an external file fetched through `options.resolve`. */
export function parseGltf(bytes: Uint8Array, options: MeshResourceOptions = {}): ParsedMesh {
  let json: GltfDoc;
  try { json = JSON.parse(dec.decode(bytes)) as GltfDoc; } catch { throw new Error("gltf: not JSON"); }
  if (!json || typeof json !== "object" || Array.isArray(json)) throw new Error("gltf: not JSON");
  return assembleGltf(json, null, options);
}

/** Bytes of `doc.buffers[i]`: the GLB BIN chunk when it has no URI, a decoded `data:` URI, or the resolver's answer. */
function gltfBuffer(doc: GltfDoc, i: number, bin: Uint8Array | null, options: MeshResourceOptions, cache: Map<number, Uint8Array>): Uint8Array {
  const cached = cache.get(i);
  if (cached) return cached;
  const b = doc.buffers?.[i];
  let out: Uint8Array | undefined;
  if (!b || b.uri === undefined) out = bin ?? (b ? undefined : new Uint8Array(0));
  else if (b.uri.startsWith("data:")) { const comma = b.uri.indexOf(","); out = comma < 0 ? undefined : fromBase64(b.uri.slice(comma + 1)); }
  else out = options.resolve?.(b.uri);
  if (!out) throw new Error(`gltf: cannot resolve buffer ${b?.uri ?? i}`);
  cache.set(i, out);
  return out;
}

function assembleGltf(doc: GltfDoc, bin: Uint8Array | null, options: MeshResourceOptions): ParsedMesh {
  const pos: number[] = [], nor: number[] = [], idx: number[] = [];
  let hasNormals = true, color: [number, number, number, number] | undefined;
  const buffers = new Map<number, Uint8Array>();
  const accessor = (i: number): { data: Float64Array; comps: number } => {
    const a = doc.accessors![i]!, bv = doc.bufferViews![a.bufferView!]!;
    const comps = { SCALAR: 1, VEC2: 2, VEC3: 3, VEC4: 4, MAT4: 16 }[a.type] ?? 1;
    const size = { 5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4 }[a.componentType] ?? 4;
    const stride = bv.byteStride ?? comps * size, base = (bv.byteOffset ?? 0) + (a.byteOffset ?? 0);
    const buf = gltfBuffer(doc, bv.buffer, bin, options, buffers);
    const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
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
      if (!color && prim.material !== undefined) {
        const f = doc.materials?.[prim.material]?.pbrMetallicRoughness?.baseColorFactor;
        color = f && f.length >= 3 ? [f[0]!, f[1]!, f[2]!, f[3] ?? 1] : [1, 1, 1, 1];
      }
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
  return { positions, normals: hasNormals && nor.length === pos.length ? Float32Array.from(nor) : faceNormals(positions, indices), indices, ...(color ? { color } : {}) };
}

const xmlChild = (e: XmlElement, name: string): XmlElement | undefined => e.children.find((c) => c.name === name);
const xmlChildren = (e: XmlElement, name: string): XmlElement[] => e.children.filter((c) => c.name === name);
const xmlNums = (s: string): number[] => { const t = s.trim(); return t.length === 0 ? [] : t.split(/\s+/).map(Number); };
const xmlRef = (s: string | undefined): string => (s ?? "").startsWith("#") ? (s ?? "").slice(1) : (s ?? "");

/** Direction `(x, y, z)` through the upper 3×3 of `m`, renormalised (unit when the input is unit; zero stays zero). */
function transformNormal(m: number[], x: number, y: number, z: number): [number, number, number] {
  const nx = m[0]! * x + m[4]! * y + m[8]! * z, ny = m[1]! * x + m[5]! * y + m[9]! * z, nz = m[2]! * x + m[6]! * y + m[10]! * z;
  const l = Math.sqrt(nx * nx + ny * ny + nz * nz) || 1;
  return [nx / l, ny / l, nz / l];
}

/**
 * COLLADA 1.4/1.5 (.dae). Reads `library_geometries` meshes (`<source>` float arrays with accessor stride, `<vertices>`,
 * `<triangles>` and `<polylist>` with VERTEX/NORMAL inputs and offsets; polygons are triangulated as fans), places
 * every `instance_geometry` of the visual scene through its node `<matrix>`/`<translate>`/`<rotate>`/`<scale>` chain
 * (all geometries at the origin when no scene instances any), converts `<up_axis>Y_UP</up_axis>` to Z up as ROS does
 * (x, y, z → x, −z, y) and merges everything into one unindexed mesh. Primitives without normals get flat face normals.
 * The first primitive whose bound material (`instance_material` symbol → `library_materials` → `instance_effect` →
 * `profile_COMMON` phong/lambert/blinn/constant `<diffuse><color>`) has a colour gives the mesh its `color`.
 * Textures, controllers and animations are ignored.
 */
export function parseCollada(bytes: Uint8Array): ParsedMesh {
  const root = xmlChild(parseXml(dec.decode(bytes)), "COLLADA");
  if (!root) throw new Error("collada: no COLLADA root");
  const upAxis = (xmlChild(xmlChild(root, "asset") ?? root, "up_axis")?.text ?? "Z_UP").trim().toUpperCase();
  const up = upAxis === "Y_UP" ? [1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1] : [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  const mul = (a: number[], b: number[]): number[] => { const o = new Array<number>(16); for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) o[c * 4 + r] = a[r]! * b[c * 4]! + a[4 + r]! * b[c * 4 + 1]! + a[8 + r]! * b[c * 4 + 2]! + a[12 + r]! * b[c * 4 + 3]!; return o; };

  const geometries = new Map<string, XmlElement>();
  for (const lib of xmlChildren(root, "library_geometries")) for (const g of xmlChildren(lib, "geometry")) if (g.attrs.id) geometries.set(g.attrs.id, g);
  const nodesById = new Map<string, XmlElement>();
  for (const lib of xmlChildren(root, "library_nodes")) for (const n of xmlChildren(lib, "node")) if (n.attrs.id) nodesById.set(n.attrs.id, n);
  // materials: effect id → diffuse colour, material id → effect id
  const effectColors = new Map<string, [number, number, number, number]>();
  for (const lib of xmlChildren(root, "library_effects")) for (const e of xmlChildren(lib, "effect")) {
    if (!e.attrs.id) continue;
    for (const profile of xmlChildren(e, "profile_COMMON")) for (const tech of xmlChildren(profile, "technique")) for (const shader of tech.children) {
      if (shader.name !== "phong" && shader.name !== "lambert" && shader.name !== "blinn") continue;
      const diffuse = xmlChild(shader, "diffuse");
      const v = diffuse ? xmlNums(xmlChild(diffuse, "color")?.text ?? "") : [];
      if (v.length >= 3 && !effectColors.has(e.attrs.id)) effectColors.set(e.attrs.id, [v[0]!, v[1]!, v[2]!, v[3] ?? 1]);
    }
  }
  const materialEffects = new Map<string, string>();
  for (const lib of xmlChildren(root, "library_materials")) for (const m of xmlChildren(lib, "material")) { const fx = xmlChild(m, "instance_effect"); if (m.attrs.id && fx) materialEffects.set(m.attrs.id, xmlRef(fx.attrs.url)); }
  const bindings = (instance: XmlElement | null): Map<string, string> => {
    const out = new Map<string, string>();
    if (!instance) return out;
    for (const bm of xmlChildren(instance, "bind_material")) for (const tc of xmlChildren(bm, "technique_common")) for (const im of xmlChildren(tc, "instance_material")) if (im.attrs.symbol) out.set(im.attrs.symbol, xmlRef(im.attrs.target));
    return out;
  };
  let color: [number, number, number, number] | undefined;

  const pos: number[] = [], nor: number[] = [];
  const emitGeometry = (g: XmlElement, world: number[], bound: Map<string, string>): void => {
    const mesh = xmlChild(g, "mesh");
    if (!mesh) return;
    const sources = new Map<string, { data: number[]; stride: number }>();
    for (const s of xmlChildren(mesh, "source")) {
      const fa = xmlChild(s, "float_array");
      if (!s.attrs.id || !fa) continue;
      const acc = xmlChild(xmlChild(s, "technique_common") ?? s, "accessor");
      sources.set(s.attrs.id, { data: xmlNums(fa.text), stride: Math.max(1, Number(acc?.attrs.stride ?? 3) || 3) });
    }
    const verts = new Map<string, { position: string; normal: string | null }>();
    for (const v of xmlChildren(mesh, "vertices")) {
      const p = xmlChildren(v, "input").find((i) => i.attrs.semantic === "POSITION"), n = xmlChildren(v, "input").find((i) => i.attrs.semantic === "NORMAL");
      if (v.attrs.id && p) verts.set(v.attrs.id, { position: xmlRef(p.attrs.source), normal: n ? xmlRef(n.attrs.source) : null });
    }
    for (const prim of mesh.children) {
      if (prim.name !== "triangles" && prim.name !== "polylist") continue;
      let posSrc: { data: number[]; stride: number } | undefined, norSrc: { data: number[]; stride: number } | undefined, posOff = 0, norOff = 0, stride = 1;
      for (const inp of xmlChildren(prim, "input")) {
        const off = Number(inp.attrs.offset ?? 0) || 0; stride = Math.max(stride, off + 1);
        if (inp.attrs.semantic === "VERTEX") {
          const v = verts.get(xmlRef(inp.attrs.source)); if (!v) continue;
          posSrc = sources.get(v.position); posOff = off;
          if (v.normal && !norSrc) { norSrc = sources.get(v.normal); norOff = off; }
        } else if (inp.attrs.semantic === "NORMAL") { norSrc = sources.get(xmlRef(inp.attrs.source)); norOff = off; }
      }
      if (!posSrc) continue;
      if (!color && prim.attrs.material) { const mat = bound.get(prim.attrs.material) ?? prim.attrs.material; color = effectColors.get(materialEffects.get(mat) ?? ""); }
      const p = xmlNums(xmlChild(prim, "p")?.text ?? "");
      const corners = Math.floor(p.length / stride);
      const polys: number[] = prim.name === "polylist" ? xmlNums(xmlChild(prim, "vcount")?.text ?? "") : [];
      if (prim.name === "triangles") for (let t = 0; t + 2 < corners; t += 3) polys.push(3);
      const start = pos.length;
      let c = 0;
      const corner = (k: number): void => {
        const pi = p[k * stride + posOff]!, s = posSrc!.stride;
        const x = posSrc!.data[pi * s] ?? 0, y = posSrc!.data[pi * s + 1] ?? 0, z = posSrc!.data[pi * s + 2] ?? 0;
        pos.push(world[0]! * x + world[4]! * y + world[8]! * z + world[12]!, world[1]! * x + world[5]! * y + world[9]! * z + world[13]!, world[2]! * x + world[6]! * y + world[10]! * z + world[14]!);
        if (norSrc) { const ni = p[k * stride + norOff]!, ns = norSrc.stride; nor.push(...transformNormal(world, norSrc.data[ni * ns] ?? 0, norSrc.data[ni * ns + 1] ?? 0, norSrc.data[ni * ns + 2] ?? 0)); }
      };
      for (const vc of polys) {
        if (c + vc > corners) break;
        for (let i = 1; i + 1 < vc; i++) { corner(c); corner(c + i); corner(c + i + 1); }
        c += vc;
      }
      if (!norSrc) { const flat = faceNormals(Float32Array.from(pos.slice(start))); for (let i = 0; i < flat.length; i++) nor.push(flat[i]!); }
    }
  };

  const nodeMatrix = (n: XmlElement): number[] => {
    let m = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
    for (const e of n.children) {
      const v = e.name === "matrix" || e.name === "translate" || e.name === "rotate" || e.name === "scale" ? xmlNums(e.text) : null;
      if (!v) continue;
      if (e.name === "matrix" && v.length >= 16) m = mul(m, [v[0]!, v[4]!, v[8]!, v[12]!, v[1]!, v[5]!, v[9]!, v[13]!, v[2]!, v[6]!, v[10]!, v[14]!, v[3]!, v[7]!, v[11]!, v[15]!]);
      else if (e.name === "translate" && v.length >= 3) m = mul(m, [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, v[0]!, v[1]!, v[2]!, 1]);
      else if (e.name === "scale" && v.length >= 3) m = mul(m, [v[0]!, 0, 0, 0, 0, v[1]!, 0, 0, 0, 0, v[2]!, 0, 0, 0, 0, 1]);
      else if (e.name === "rotate" && v.length >= 4) {
        const l = Math.sqrt(v[0]! * v[0]! + v[1]! * v[1]! + v[2]! * v[2]!) || 1, x = v[0]! / l, y = v[1]! / l, z = v[2]! / l;
        const a = v[3]! * Math.PI / 180, s = Math.sin(a), co = Math.cos(a), t = 1 - co;
        m = mul(m, [t * x * x + co, t * x * y + s * z, t * x * z - s * y, 0, t * x * y - s * z, t * y * y + co, t * y * z + s * x, 0, t * x * z + s * y, t * y * z - s * x, t * z * z + co, 0, 0, 0, 0, 1]);
      }
    }
    return m;
  };
  let instanced = 0;
  const visit = (n: XmlElement, parent: number[], depth: number): void => {
    if (depth > 64) return;
    const m = mul(parent, nodeMatrix(n));
    for (const e of n.children) {
      if (e.name === "instance_geometry") { const g = geometries.get(xmlRef(e.attrs.url)); if (g) { instanced++; emitGeometry(g, m, bindings(e)); } }
      else if (e.name === "instance_node") { const ref = nodesById.get(xmlRef(e.attrs.url)); if (ref) visit(ref, m, depth + 1); }
      else if (e.name === "node") visit(e, m, depth + 1);
    }
  };
  const sceneUrl = xmlRef(xmlChild(xmlChild(root, "scene") ?? root, "instance_visual_scene")?.attrs.url);
  const scenes = xmlChildren(root, "library_visual_scenes").flatMap((l) => xmlChildren(l, "visual_scene"));
  const scene = scenes.find((s) => s.attrs.id === sceneUrl) ?? scenes[0];
  if (scene) for (const n of xmlChildren(scene, "node")) visit(n, up, 0);
  if (instanced === 0) for (const g of geometries.values()) emitGeometry(g, up, bindings(null));
  return { positions: Float32Array.from(pos), normals: Float32Array.from(nor), indices: null, ...(color ? { color } : {}) };
}

/** Pick the parser by extension or content (`.glb`, `.gltf`, `.dae`, `.stl`); `options.resolve` serves glTF external buffers. */
export function parseMeshResource(bytes: Uint8Array, name = "", options: MeshResourceOptions = {}): ParsedMesh {
  const lower = name.toLowerCase();
  if (lower.endsWith(".glb") || (bytes.length >= 4 && bytes[0] === 0x67 && bytes[1] === 0x6c && bytes[2] === 0x54 && bytes[3] === 0x46)) return parseGlb(bytes, options);
  if (lower.endsWith(".gltf")) return parseGltf(bytes, options);
  if (lower.endsWith(".dae") || (bytes.length > 0 && bytes[0] === 0x3c && dec.decode(bytes.subarray(0, Math.min(bytes.length, 1024))).includes("COLLADA"))) return parseCollada(bytes);
  if (lower.endsWith(".stl") || lower === "") return parseStl(bytes);
  throw new Error(`mesh: unsupported resource ${name}`);
}

/** A renderer mesh from a parsed resource (normals present, so markers can be lit). */
export function meshFromParsed(key: string, p: ParsedMesh): Mesh3D {
  return createMesh(p.positions, { key, normals: p.normals ?? faceNormals(p.positions, p.indices), ...(p.indices ? { indices: p.indices } : {}) });
}
