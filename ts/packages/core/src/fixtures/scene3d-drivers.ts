// Mori.SkyScope — Fixture drivers for the 3D stack: math, camera, frame tree, layer messages, layers, controller, mesh formats and URDF.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Vec3 } from "../scene/geometry.js";
import { Scene, type HitResult } from "../scene/scene.js";
import type { InputEvent } from "../scene/interaction.js";
import { Scene3DController, type Scene3DTool } from "../scene3d/controller3d.js";
import { RecordingPainter } from "../paint/recording-painter.js";
import { fnv1a } from "../charts/colormaps.js";
import { Camera3D, type Camera3DOptions } from "../scene3d/camera3d.js";
import { FrameTree } from "../scene3d/frame-tree.js";
import { RecordingPainter3D, meshHash } from "../scene3d/painter3d.js";
import { joinUri, meshFromParsed, parseMeshResource } from "../scene3d/mesh-formats.js";
import { parseUrdf, urdfMarkers, urdfMeshUris, urdfRootLink, urdfTransforms, type RobotModel } from "../scene3d/urdf.js";
import { sceneBounds3, type FrameTransformMessage } from "../scene3d/layers3d.js";
import { SceneLayerSink, applyLayerPayload, type LayerMeta } from "../scene/layer-json.js";
import { decodeLayerMessage, encodeLayerMessage, hasBinaryPayload, type LayerArray } from "../streaming/layer-message.js";
import * as M from "../scene3d/math3.js";
import { r9 } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const nums = (v: unknown): number[] => v as number[];
const rV = (v: Vec3) => ({ x: r9(v.x), y: r9(v.y), z: r9(v.z) });
const rQ = (q: M.Quat) => ({ x: r9(q.x), y: r9(q.y), z: r9(q.z), w: r9(q.w) });
const rM = (m: M.Mat4 | null) => (m ? m.map(r9) : null);
const rBox = (b: M.Box3 | null) => (b ? { min: rV(b.min), max: rV(b.max) } : null);
const vec = (a: unknown): Vec3 => { const n = nums(a); return M.v3(n[0]!, n[1]!, n[2]!); };
const quat = (a: unknown): M.Quat => { const n = nums(a); return { x: n[0]!, y: n[1]!, z: n[2]!, w: n[3]! }; };
const box = (b: unknown): M.Box3 => { const o = b as { min: number[]; max: number[] }; return { min: vec(o.min), max: vec(o.max) }; };
const ray = (r: unknown): M.Ray => { const o = r as { origin: number[]; dir: number[] }; return { origin: vec(o.origin), dir: M.v3norm(vec(o.dir)) }; };

function mat(spec: unknown): M.Mat4 {
  if (Array.isArray(spec)) return spec as number[];
  const s = spec as Step;
  if ("translation" in s) { const n = nums(s.translation); return M.mat4Translation(n[0]!, n[1]!, n[2]!); }
  if ("scaling" in s) { const n = nums(s.scaling); return M.mat4Scaling(n[0]!, n[1]!, n[2]!); }
  if ("quat" in s) return M.mat4FromQuat(quat(s.quat));
  if ("euler" in s) { const n = nums(s.euler); return M.mat4FromQuat(M.quatFromEuler(n[0]!, n[1]!, n[2]!)); }
  if ("pose" in s) { const [t, q] = s.pose as unknown[]; return M.mat4FromPose(vec(t), quat(q)); }
  if ("mul" in s) { const [a, b] = s.mul as unknown[]; return M.mat4Mul(mat(a), mat(b)); }
  if ("invert" in s) return M.mat4Invert(mat(s.invert)) ?? M.MAT4_IDENTITY;
  if ("transpose" in s) return M.mat4Transpose(mat(s.transpose));
  if ("lookAt" in s) { const [e, t, u] = s.lookAt as unknown[]; return M.mat4LookAt(vec(e), vec(t), vec(u)); }
  if ("perspective" in s) { const n = nums(s.perspective); return M.mat4Perspective(n[0]!, n[1]!, n[2]!, n[3]!); }
  if ("ortho" in s) { const n = nums(s.ortho); return M.mat4Ortho(n[0]!, n[1]!, n[2]!, n[3]!, n[4]!, n[5]!); }
  throw new Error(`unknown matrix ${JSON.stringify(spec)}`);
}

/**
 * 3D math; matrices in steps are arrays or `translation`, `scaling`, `quat`, `euler`, `pose`, `mul`, `invert`,
 * `transpose`, `lookAt`, `perspective` and `ortho` specs.
 */
export const math3Driver: FixtureDriver<{ queries: unknown[] }> = {
  component: "math3",
  create() { return { queries: [] }; },
  /**
   * Queries: quatFromEuler, quatToEuler, quatFromAxisAngle, quatMul, quatRotate, quatSlerp, mat4, invert, point, dir,
   * box3FromPositions, box3Sphere, box3Union, rayPlane, rayBox, raySphere.
   */
  step(s, step: Step) {
    const q = s.queries;
    if ("quatFromEuler" in step) { const n = nums(step.quatFromEuler); q.push(rQ(M.quatFromEuler(n[0]!, n[1]!, n[2]!))); }
    else if ("quatToEuler" in step) { const e = M.quatToEuler(quat(step.quatToEuler)); q.push({ roll: r9(e.roll), pitch: r9(e.pitch), yaw: r9(e.yaw) }); }
    else if ("quatFromAxisAngle" in step) { const [a, r] = step.quatFromAxisAngle as unknown[]; q.push(rQ(M.quatFromAxisAngle(vec(a), num(r)))); }
    else if ("quatMul" in step) { const [a, b] = step.quatMul as unknown[]; q.push(rQ(M.quatMul(quat(a), quat(b)))); }
    else if ("quatRotate" in step) { const [a, v] = step.quatRotate as unknown[]; q.push(rV(M.quatRotate(quat(a), vec(v)))); }
    else if ("quatSlerp" in step) { const [a, b, t] = step.quatSlerp as unknown[]; q.push(rQ(M.quatSlerp(quat(a), quat(b), num(t)))); }
    else if ("mat4" in step) q.push(rM(mat(step.mat4)));
    else if ("invert" in step) q.push(rM(M.mat4Invert(mat(step.invert))));
    else if ("point" in step) { const [m, x, y, z] = step.point as unknown[]; q.push(rV(M.mat4Point(mat(m), num(x), num(y), num(z)))); }
    else if ("dir" in step) { const [m, x, y, z] = step.dir as unknown[]; q.push(rV(M.mat4Dir(mat(m), num(x), num(y), num(z)))); }
    else if ("box3FromPositions" in step) { const [p, m] = step.box3FromPositions as unknown[]; q.push(rBox(M.box3FromPositions(nums(p), m === undefined || m === null ? undefined : mat(m)))); }
    else if ("box3Sphere" in step) { const sp = M.box3Sphere(box(step.box3Sphere)); q.push({ center: rV(sp.center), radius: r9(sp.radius) }); }
    else if ("box3Union" in step) { const [a, b] = step.box3Union as unknown[]; q.push(rBox(M.box3Union(a ? box(a) : null, b ? box(b) : null))); }
    else if ("rayPlane" in step) { const [r, n, d] = step.rayPlane as unknown[]; const t = M.rayPlane(ray(r), vec(n), num(d)); q.push(t === null ? null : r9(t)); }
    else if ("rayBox" in step) { const [r, b] = step.rayBox as unknown[]; const t = M.rayBox(ray(r), box(b)); q.push(t === null ? null : r9(t)); }
    else if ("raySphere" in step) { const [r, c, rad] = step.raySphere as unknown[]; const t = M.raySphere(ray(r), vec(c), num(rad)); q.push(t === null ? null : r9(t)); }
    else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

function cameraOptions(setup: Step): Camera3DOptions {
  const o: Camera3DOptions = { ...(setup as Camera3DOptions) };
  if (Array.isArray(setup.target)) o.target = vec(setup.target);
  return o;
}
function cameraStep(camera: Camera3D, step: Step): boolean {
  switch (step.type) {
    case "setViewport": camera.setViewport(num(step.width), num(step.height)); return true;
    case "setTarget": { const v = vec(step.target); camera.setTarget(v.x, v.y, v.z); return true; }
    case "setDistance": camera.setDistance(num(step.distance)); return true;
    case "setOrbit": camera.setOrbit(num(step.yaw), num(step.pitch)); return true;
    case "setFov": camera.setFov(num(step.fov)); return true;
    case "setOrtho": camera.setOrtho(step.ortho === true); return true;
    case "orbit": camera.orbit(num(step.dyaw), num(step.dpitch)); return true;
    case "pan": camera.pan(num(step.dx), num(step.dy)); return true;
    case "dolly": camera.dolly(num(step.factor)); return true;
    case "dollyAt": camera.dollyAt(num(step.x), num(step.y), num(step.factor)); return true;
    case "fitSphere": camera.fitSphere(vec(step.center), num(step.radius)); return true;
    case "fitBox": camera.fitBox(box(step.box)); return true;
    default: return false;
  }
}
function cameraQuery(camera: Camera3D, step: Step): unknown {
  if ("project3" in step) { const n = nums(step.project3); const p = camera.project3(n[0]!, n[1]!, n[2]!); return { x: r9(p.x), y: r9(p.y), depth: r9(p.depth), visible: p.visible }; }
  if ("unproject3" in step) { const n = nums(step.unproject3); return rV(camera.unproject3(n[0]!, n[1]!, n[2]!)); }
  if ("ray" in step) { const n = nums(step.ray); const r = camera.ray(n[0]!, n[1]!); return { origin: rV(r.origin), dir: rV(r.dir) }; }
  if ("groundPoint" in step) { const n = nums(step.groundPoint); const g = camera.groundPoint(n[0]!, n[1]!); return g ? rV(g) : null; }
  if ("project" in step) { const n = nums(step.project); const p = camera.project(n[0]!, n[1]!); return { x: r9(p.x), y: r9(p.y) }; }
  if ("unproject" in step) { const n = nums(step.unproject); const p = camera.unproject(n[0]!, n[1]!); return { x: r9(p.x), y: r9(p.y) }; }
  if ("eye" in step) return rV(camera.eye());
  if ("view" in step) return rM(camera.view());
  if ("proj" in step) return rM(camera.projection());
  if ("worldPerPixel" in step) return r9(camera.worldPerPixel());
  if ("state" in step) return { target: rV(camera.target), distance: r9(camera.distance), yaw: r9(camera.yaw), pitch: r9(camera.pitch), ortho: camera.ortho, version: camera.version };
  throw new Error(`unknown query ${JSON.stringify(step)}`);
}

/** Orbit camera: `setup` is `Camera3DOptions` with `target` as an array. */
export const camera3dDriver: FixtureDriver<{ camera: Camera3D; queries: unknown[] }> = {
  component: "camera3d",
  create(setup) { return { camera: new Camera3D(cameraOptions(setup)), queries: [] }; },
  /**
   * Steps: setViewport, setTarget, setDistance, setOrbit, setFov, setOrtho, orbit, pan, dolly, dollyAt, fitSphere,
   * fitBox; queries: project3, unproject3, ray, groundPoint, project, unproject, eye, view, proj, worldPerPixel,
   * state.
   */
  step(s, step: Step) {
    if (step.type === "query") s.queries.push(cameraQuery(s.camera, step));
    else if (!cameraStep(s.camera, step)) throw new Error(`unknown step ${String(step.type)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

const opt = (t: unknown): number | undefined => (t === null || t === undefined ? undefined : num(t));
const transformMsg = (m: Step): FrameTransformMessage => ({ child: m.child as string, parent: m.parent as string, t: nums(m.t) as [number, number, number], q: nums(m.q) as [number, number, number, number], time: m.time as number | undefined });

/** Frame tree: `setup.maxSamples` caps the history per frame. */
export const frameTreeDriver: FixtureDriver<{ tree: FrameTree; queries: unknown[] }> = {
  component: "frameTree",
  create(setup) { return { tree: new FrameTree(setup.maxSamples as number | undefined), queries: [] }; },
  /**
   * Steps: set (a transform message), remove, clear; queries: lookup, point, transformAt, frameIds, roots, parent,
   * canTransform, version.
   */
  step(s, step: Step) {
    const { tree, queries } = s;
    switch (step.type) {
      case "set": { const m = transformMsg(step); tree.set(m.child, m.parent, { t: M.v3(...m.t), q: { x: m.q[0], y: m.q[1], z: m.q[2], w: m.q[3] } }, m.time); break; }
      case "remove": tree.remove(step.frame as string); break;
      case "clear": tree.clear(); break;
      case "query":
        if ("lookup" in step) { const [t, src, time] = step.lookup as unknown[]; queries.push(rM(tree.lookup(t as string, src as string, opt(time)))); }
        else if ("point" in step) { const [t, src, time, p] = step.point as unknown[]; const m = tree.lookup(t as string, src as string, opt(time)); const v = vec(p); queries.push(m ? rV(M.mat4Point(m, v.x, v.y, v.z)) : null); }
        else if ("transformAt" in step) { const [f, time] = step.transformAt as unknown[]; const tf = tree.transformAt(f as string, opt(time)); queries.push(tf ? { t: rV(tf.t), q: rQ(tf.q) } : null); }
        else if ("frameIds" in step) queries.push(tree.frameIds());
        else if ("roots" in step) queries.push(tree.roots());
        else if ("parent" in step) queries.push(tree.parent(step.parent as string));
        else if ("canTransform" in step) { const [t, src, time] = step.canTransform as unknown[]; queries.push(tree.canTransform(t as string, src as string, opt(time))); }
        else if ("version" in step) queries.push(tree.version);
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

/** Fixture JSON for typed arrays: `{"$f32":[…]}`, `{"$u8":[…]}`, … at the top level of a payload. */
const ARRAY_TAGS: Record<string, new (a: number[]) => LayerArray> = { $f32: Float32Array, $f64: Float64Array, $u8: Uint8Array, $u16: Uint16Array, $i16: Int16Array, $u32: Uint32Array, $i32: Int32Array };
/** Replace tagged objects such as `{"$f32": [...]}` at the top level of a payload with typed arrays. */
export function payloadFromJson(p: unknown): unknown {
  if (!p || typeof p !== "object" || Array.isArray(p)) return p;
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(p as Record<string, unknown>)) {
    if (v && typeof v === "object" && !Array.isArray(v)) { const keys = Object.keys(v); const tag = keys.length === 1 ? keys[0]! : ""; const ctor = ARRAY_TAGS[tag]; if (ctor) { out[k] = new ctor((v as Record<string, number[]>)[tag]!); continue; } }
    out[k] = v;
  }
  return out;
}
/** Inverse of `payloadFromJson`: typed arrays become tagged objects, float values rounded to 9 decimals. */
export function payloadToJson(p: unknown): unknown {
  if (!p || typeof p !== "object" || Array.isArray(p)) return p;
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(p as Record<string, unknown>)) {
    if (ArrayBuffer.isView(v) && !(v instanceof DataView)) {
      const tag = Object.entries(ARRAY_TAGS).find(([, c]) => v instanceof c)?.[0] ?? "$?";
      const isFloat = v instanceof Float32Array || v instanceof Float64Array;
      out[k] = { [tag]: Array.from(v as unknown as ArrayLike<number>, (x) => (isFloat ? r9(x) : x)) };
    } else out[k] = v;
  }
  return out;
}
const fromBase64 = (s: string): Uint8Array => Uint8Array.from(atob(s), (c) => c.charCodeAt(0));
const toBase64 = (b: Uint8Array): string => btoa(String.fromCharCode(...b));

/** Binary layer message codec. */
export const layerMessageDriver: FixtureDriver<{ queries: unknown[]; last: Uint8Array | null }> = {
  component: "layerMessage",
  create() { return { queries: [], last: null }; },
  /**
   * Queries: encode (length, hash and round-trip; remembers the bytes), decode (from base64), hasBinary, base64 (the
   * last encoded bytes).
   */
  step(s, step: Step) {
    const q = s.queries;
    if ("encode" in step) {
      const e = step.encode as Step;
      const bytes = encodeLayerMessage(e.id as string, payloadFromJson(e.payload) as Record<string, unknown>);
      s.last = bytes;
      q.push({ length: bytes.length, hash: fnv1a(bytes), roundTrip: (() => { const d = decodeLayerMessage(bytes); return { id: d.id, payload: payloadToJson(d.payload) }; })() });
    } else if ("decode" in step) {
      try { const d = decodeLayerMessage(fromBase64(step.decode as string)); q.push({ id: d.id, payload: payloadToJson(d.payload) }); }
      catch (err) { q.push({ error: (err as Error).message }); }
    } else if ("hasBinary" in step) q.push(hasBinaryPayload(payloadFromJson(step.hasBinary)));
    else if ("base64" in step) q.push(s.last ? toBase64(s.last) : null);
    else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

interface Scene3DState { scene: Scene; sink: SceneLayerSink; camera: Camera3D; width: number; height: number; queries: unknown[] }

/** Layers created through the JSON contract, drawn with a RecordingPainter3D under a Camera3D. */
export const scene3dDriver: FixtureDriver<Scene3DState> = {
  component: "scene3d",
  create(setup) {
    const width = num(setup.width ?? 800), height = num(setup.height ?? 600);
    const camera = new Camera3D({ width, height, ...cameraOptions((setup.camera ?? {}) as Step) });
    const scene = new Scene();
    const sink = new SceneLayerSink(scene, undefined, { fixedFrame: setup.fixedFrame as string | undefined });
    for (const l of (setup.layers ?? []) as Step[]) sink.declareLayer(l.id as string, l.kind as string, (l.meta ?? {}) as LayerMeta);
    if (Array.isArray(setup.transforms)) { sink.declareLayer("tf", "frames"); sink.push("tf", { transforms: (setup.transforms as Step[]).map(transformMsg) }); }
    return { scene, sink, camera, width, height, queries: [] };
  },
  /**
   * Steps: declare, push, transforms (into the `tf` layer), reset, camera (a camera step), fit, draw (3D ops plus the
   * 2D op count); queries: hitTest, bounds3, order, frameIds, unknown, otherwise a camera query.
   */
  step(s, step: Step) {
    const { scene, sink, camera, queries } = s;
    switch (step.type) {
      case "declare": sink.declareLayer(step.id as string, step.kind as string, (step.meta ?? {}) as LayerMeta); break;
      case "push": sink.push(step.id as string, payloadFromJson(step.payload)); break;
      case "transforms": sink.push("tf", { transforms: (step.transforms as Step[]).map(transformMsg) }); break;
      case "reset": sink.reset(); break;
      case "camera": if (!cameraStep(camera, step.op as Step)) throw new Error("unknown camera op"); break;
      case "fit": { const b = sceneBounds3(scene); if (b) camera.fitBox(b); break; }
      case "draw": {
        const p2 = new RecordingPainter(s.width, s.height), p3 = new RecordingPainter3D(s.width, s.height);
        p3.begin(camera.view(), camera.projection(), (step.clear as string | undefined) ?? null);
        scene.draw(p2, camera, num(step.now ?? 0), p3);
        p3.end();
        queries.push({ ops3d: p3.ops, ops2d: p2.ops.length });
        break;
      }
      case "query":
        if ("hitTest" in step) { const h = step.hitTest as Step; const r = scene.hitTest(num(h.x), num(h.y), camera, s.width, s.height, h.tolerance === undefined ? 6 : num(h.tolerance), num(h.now ?? 0)); queries.push(r ? { layerId: r.layerId, world: rV(r.world), distance: r9(r.distance), index: r.index ?? null, data: r.data === undefined ? null : (typeof r.data === "number" ? r9(r.data) : r.data) } : null); }
        else if ("bounds3" in step) queries.push(rBox(sceneBounds3(scene)));
        else if ("order" in step) queries.push(scene.order());
        else if ("frameIds" in step) queries.push(sink.frames.frameIds());
        else if ("unknown" in step) queries.push([...sink.unknown].sort());
        else queries.push(cameraQuery(camera, step));
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

interface Ctrl3DState { c: Scene3DController; width: number; height: number; queries: unknown[] }

/** Scene3DController fed with InputEvents (the fixture JSON has the InputEvent shape), drawing HUD ops with a RecordingPainter. */
export const scene3dControllerDriver: FixtureDriver<Ctrl3DState> = {
  component: "scene3dController",
  create(setup) {
    const width = num(setup.width ?? 800), height = num(setup.height ?? 600);
    const c = new Scene3DController({ camera: { width, height, ...cameraOptions((setup.camera ?? {}) as Step) }, tool: setup.tool as Scene3DTool | undefined, fixedFrame: setup.fixedFrame as string | undefined, unit: setup.unit as string | undefined });
    const sink = new SceneLayerSink(c.scene, undefined, { frames: c.frames, fixedFrame: c.fixedFrame });
    for (const l of (setup.layers ?? []) as Step[]) sink.declareLayer(l.id as string, l.kind as string, (l.meta ?? {}) as LayerMeta);
    if (Array.isArray(setup.transforms)) { sink.declareLayer("tf", "frames"); sink.push("tf", { transforms: (setup.transforms as Step[]).map(transformMsg) }); }
    return { c, width, height, queries: [] };
  },
  /**
   * Steps: setTool, now, push, draw (3D op count plus HUD ops), `query` with state, measure, selection, hover,
   * cursor, tool or a camera query; any other step is an `InputEvent` and pushes the redraw flag.
   */
  step(s, step: Step) {
    const { c, queries } = s;
    switch (step.type) {
      case "setTool": c.setTool(step.tool as Scene3DTool); break;
      case "now": c.now = num(step.now); break;
      case "push": { const l = c.scene.get(step.id as string); if (l) applyLayerPayload(l, payloadFromJson(step.payload)); break; }
      case "draw": {
        const p2 = new RecordingPainter(s.width, s.height), p3 = new RecordingPainter3D(s.width, s.height);
        c.draw3d(p3, s.width, s.height, p2);
        const before = p2.ops.length;
        c.drawHud(p2, s.width, s.height);
        queries.push({ ops3d: p3.ops.length, hud: p2.ops.slice(before) });
        break;
      }
      case "query": {
        const hit = (h: HitResult | null) => (h ? { layerId: h.layerId, world: rV(h.world), index: h.index ?? null } : null);
        if ("state" in step) queries.push(cameraQuery(c.camera, { state: true }));
        else if ("measure" in step) { const d = c.measureDistance(); queries.push({ points: c.measure.map(rV), distance: d === null ? null : r9(d) }); }
        else if ("selection" in step) queries.push(hit(c.selection));
        else if ("hover" in step) queries.push(hit(c.hover));
        else if ("cursor" in step) queries.push(c.cursor ? rV(c.cursor) : null);
        else if ("tool" in step) queries.push(c.tool);
        else queries.push(cameraQuery(c.camera, step));
        break;
      }
      default: queries.push(c.handle(step as unknown as InputEvent));
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

/** Mesh resource parsers: base64 STL/GLB/glTF/Collada in, vertex/triangle counts, bounds, buffer hashes and the material colour out. */
export const meshFormatsDriver: FixtureDriver<{ queries: unknown[] }> = {
  component: "meshFormats",
  create() { return { queries: [] }; },
  /**
   * Query: parse (base64 data, an optional name and optional `files`: sibling files as base64 by path relative to the
   * resource, served to the parser's resolver); errors are pushed as `{ error }`.
   */
  step(s, step: Step) {
    if ("parse" in step) {
      const { data, name, files } = step.parse as { data: string; name?: string; files?: Record<string, string> };
      try {
        const siblings = new Map(Object.entries(files ?? {}).map(([k, v]) => [joinUri(name ?? "", k), fromBase64(v)]));
        const p = parseMeshResource(fromBase64(data), name ?? "", { resolve: (uri) => siblings.get(joinUri(name ?? "", uri)) });
        const mesh = meshFromParsed("fixture", p);
        const b = M.box3FromPositions(p.positions);
        s.queries.push({ vertices: p.positions.length / 3, triangles: (p.indices ? p.indices.length : p.positions.length / 3) / 3, indexed: !!p.indices, bounds: rBox(b), hash: meshHash(mesh), color: p.color ? p.color.map(r9) : null });
      } catch (e) { s.queries.push({ error: (e as Error).message }); }
    } else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

/** URDF: a robot description in, links/joints, markers per visual and joint transforms at given positions out. */
export const urdfDriver: FixtureDriver<{ model: RobotModel; queries: unknown[] }> = {
  component: "urdf",
  create(setup) { return { model: parseUrdf(setup.xml as string), queries: [] }; },
  /** Queries: summary, markers, transforms, point (a link-frame point expressed in the root link). */
  step(s, step: Step) {
    const { model, queries } = s;
    if ("summary" in step) queries.push({ name: model.name, root: urdfRootLink(model), links: model.links.map((l) => ({ name: l.name, visuals: l.visuals.length })), joints: model.joints.map((j) => ({ name: j.name, type: j.type, parent: j.parent, child: j.child, axis: j.axis, lower: j.lower, upper: j.upper })), meshes: urdfMeshUris(model) });
    else if ("markers" in step) queries.push(urdfMarkers(model, step.markers as { prefix?: string; defaultColor?: string }).map((m) => ({ id: m.id, type: m.type, frame: m.frame, position: m.position!.map(r9), orientation: m.orientation!.map(r9), scale: m.scale!.map(r9), color: m.color, opacity: r9(m.opacity ?? 1), meshResource: m.meshResource ?? null })));
    else if ("transforms" in step) { const o = step.transforms as { positions?: Record<string, number>; prefix?: string; time?: number }; queries.push(urdfTransforms(model, o.positions ?? {}, { prefix: o.prefix, time: o.time }).map((t) => ({ child: t.child, parent: t.parent, t: t.t.map(r9), q: t.q.map(r9), time: t.time ?? null }))); }
    else if ("point" in step) {
      // a point in a link's frame expressed in the root, through a FrameTree fed with the transforms
      const o = step.point as { link: string; p: number[]; positions?: Record<string, number> };
      const tree = new FrameTree();
      for (const t of urdfTransforms(model, o.positions ?? {})) tree.set(t.child, t.parent, { t: M.v3(t.t[0], t.t[1], t.t[2]), q: { x: t.q[0], y: t.q[1], z: t.q[2], w: t.q[3] } });
      const m = tree.lookup(urdfRootLink(model) ?? "", o.link);
      queries.push(m ? rV(M.mat4Point(m, o.p[0]!, o.p[1]!, o.p[2]!)) : null);
    } else throw new Error(`unknown query ${JSON.stringify(step)}`);
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
