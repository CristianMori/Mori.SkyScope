// Mori.SkyScope — URDF robot descriptions → markers (one per visual, in the link's frame) and frame-tree transforms (one per joint, driven by joint positions).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { quatFromEuler, quatMul, quatFromAxisAngle, quatRotate, v3add, v3scale, v3, type Quat } from "./math3.js";
import type { Marker } from "./robot-layers3d.js";
import type { FrameTransformMessage } from "./layers3d.js";

/**
 * URDF robot descriptions → markers (one per visual, in the link's frame) and frame-tree transforms (one per joint,
 * driven by joint positions). Mirrors `Mori.SkyScope.Core.Scene3D.Urdf`; pinned by `spec/fixtures/urdf.json`.
 * Geometry: box, cylinder, sphere, mesh (by resource URI, loaded through the scene's `MeshRegistry`).
 */
/** `<origin>`: translation in metres and roll-pitch-yaw in radians. */
export interface UrdfOrigin { xyz: [number, number, number]; rpy: [number, number, number] }
/** `<geometry>` child: box size, cylinder radius and length, sphere radius, or a mesh resource with scale. */
export type UrdfGeometry =
  | { type: "box"; size: [number, number, number] }
  | { type: "cylinder"; radius: number; length: number }
  | { type: "sphere"; radius: number }
  | { type: "mesh"; filename: string; scale: [number, number, number] };
/**
 * One `<visual>` of a link: its pose in the link frame, geometry and resolved material (hex colour, or null when
 * unspecified).
 */
export interface UrdfVisual { name: string | null; origin: UrdfOrigin; geometry: UrdfGeometry; color: string | null; opacity: number }
/** A `<link>` and its visuals. */
export interface UrdfLink { name: string; visuals: UrdfVisual[] }
/** URDF joint types; only revolute, continuous and prismatic respond to joint positions. */
export type UrdfJointType = "fixed" | "revolute" | "continuous" | "prismatic" | "floating" | "planar";
/**
 * A `<joint>`: the child frame's pose in the parent, the motion axis in the joint frame and optional position limits.
 */
export interface UrdfJoint { name: string; type: UrdfJointType; parent: string; child: string; origin: UrdfOrigin; axis: [number, number, number]; lower: number | null; upper: number | null }
/** Parsed `<robot>`: links, joints and named materials. */
export interface RobotModel { name: string; links: UrdfLink[]; joints: UrdfJoint[]; materials: Record<string, { color: string; opacity: number }> }

/** A tiny XML element tree (URDF needs elements and attributes only). */
export interface XmlElement { name: string; attrs: Record<string, string>; children: XmlElement[] }

/** Dependency-free XML element parser: elements, attributes, self-closing tags; comments, PIs, CDATA and text are skipped. */
export function parseXml(text: string): XmlElement {
  const root: XmlElement = { name: "#root", attrs: {}, children: [] };
  const stack: XmlElement[] = [root];
  const re = /<!--[\s\S]*?-->|<!\[CDATA\[[\s\S]*?\]\]>|<\?[\s\S]*?\?>|<!DOCTYPE[^>]*>|<\/([A-Za-z_][\w.:-]*)\s*>|<([A-Za-z_][\w.:-]*)((?:\s+[\w.:-]+\s*=\s*(?:"[^"]*"|'[^']*'))*)\s*(\/?)>/g;
  const attrRe = /([\w.:-]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
  let m: RegExpExecArray | null;
  while ((m = re.exec(text))) {
    if (m[1] !== undefined) { if (stack.length > 1 && stack[stack.length - 1]!.name === m[1]) stack.pop(); continue; }
    if (m[2] === undefined) continue;
    const el: XmlElement = { name: m[2], attrs: {}, children: [] };
    let a: RegExpExecArray | null; attrRe.lastIndex = 0;
    while ((a = attrRe.exec(m[3] ?? ""))) el.attrs[a[1]!] = (a[2] ?? a[3] ?? "").replace(/&quot;/g, '"').replace(/&apos;/g, "'").replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&amp;/g, "&");
    stack[stack.length - 1]!.children.push(el);
    if (!m[4]) stack.push(el);
  }
  return root;
}

const child = (e: XmlElement, name: string): XmlElement | undefined => e.children.find((c) => c.name === name);
const children = (e: XmlElement, name: string): XmlElement[] => e.children.filter((c) => c.name === name);
const nums = (s: string | undefined, n: number, fill: number): number[] => { const out = (s ?? "").trim().split(/\s+/).filter(Boolean).map(Number); while (out.length < n) out.push(fill); return out.slice(0, n); };
const origin = (e: XmlElement | undefined): UrdfOrigin => ({ xyz: nums(e?.attrs.xyz, 3, 0) as [number, number, number], rpy: nums(e?.attrs.rpy, 3, 0) as [number, number, number] });
const hex2 = (v: number): string => Math.round(Math.max(0, Math.min(1, v)) * 255).toString(16).padStart(2, "0");

/**
 * Parse a URDF document. Unknown geometry falls back to a 1 cm box; material colours come from `rgba` or from a named
 * `<material>` declared at robot level.
 */
export function parseUrdf(xml: string): RobotModel {
  const doc = parseXml(xml);
  const robot = child(doc, "robot") ?? doc;
  const materials: RobotModel["materials"] = {};
  const materialOf = (e: XmlElement | undefined): { color: string; opacity: number } | null => {
    if (!e) return null;
    const c = child(e, "color");
    if (c?.attrs.rgba) { const [r, g, b, a] = nums(c.attrs.rgba, 4, 1); return { color: `#${hex2(r!)}${hex2(g!)}${hex2(b!)}`, opacity: a! }; }
    return e.attrs.name ? materials[e.attrs.name] ?? null : null;
  };
  for (const m of children(robot, "material")) if (m.attrs.name) { const c = materialOf(m); if (c) materials[m.attrs.name] = c; }
  const links: UrdfLink[] = children(robot, "link").map((l) => ({
    name: l.attrs.name ?? "", visuals: children(l, "visual").map((v) => {
      const g = child(v, "geometry"), mat = materialOf(child(v, "material"));
      let geometry: UrdfGeometry;
      if (g && child(g, "box")) geometry = { type: "box", size: nums(child(g, "box")!.attrs.size, 3, 1) as [number, number, number] };
      else if (g && child(g, "cylinder")) { const c = child(g, "cylinder")!; geometry = { type: "cylinder", radius: Number(c.attrs.radius ?? 0.5), length: Number(c.attrs.length ?? 1) }; }
      else if (g && child(g, "sphere")) geometry = { type: "sphere", radius: Number(child(g, "sphere")!.attrs.radius ?? 0.5) };
      else if (g && child(g, "mesh")) { const c = child(g, "mesh")!; geometry = { type: "mesh", filename: c.attrs.filename ?? "", scale: nums(c.attrs.scale, 3, 1) as [number, number, number] }; }
      else geometry = { type: "box", size: [0.01, 0.01, 0.01] };
      return { name: v.attrs.name ?? null, origin: origin(child(v, "origin")), geometry, color: mat?.color ?? null, opacity: mat?.opacity ?? 1 };
    }),
  }));
  const joints: UrdfJoint[] = children(robot, "joint").map((j) => {
    const limit = child(j, "limit");
    return {
      name: j.attrs.name ?? "", type: (j.attrs.type ?? "fixed") as UrdfJointType,
      parent: child(j, "parent")?.attrs.link ?? "", child: child(j, "child")?.attrs.link ?? "",
      origin: origin(child(j, "origin")), axis: nums(child(j, "axis")?.attrs.xyz ?? "1 0 0", 3, 0) as [number, number, number],
      lower: limit?.attrs.lower !== undefined ? Number(limit.attrs.lower) : null, upper: limit?.attrs.upper !== undefined ? Number(limit.attrs.upper) : null,
    };
  });
  return { name: robot.attrs.name ?? "", links, joints, materials };
}

/** The root link: the one no joint names as a child. */
export function urdfRootLink(model: RobotModel): string | null {
  const children = new Set(model.joints.map((j) => j.child));
  return model.links.find((l) => !children.has(l.name))?.name ?? null;
}

const rpyQuat = (rpy: [number, number, number]): Quat => quatFromEuler(rpy[0], rpy[1], rpy[2]);

/** One marker per visual, in the link's frame; `prefix` namespaces frame names and marker ids (several robots in one scene). */
export function urdfMarkers(model: RobotModel, options: { prefix?: string | undefined; defaultColor?: string | undefined } = {}): Marker[] {
  const prefix = options.prefix ?? "", def = options.defaultColor ?? "#9ca3af";
  const out: Marker[] = [];
  for (const link of model.links) link.visuals.forEach((v, i) => {
    const q = rpyQuat(v.origin.rpy), g = v.geometry;
    const base: Marker = { id: `${prefix}${link.name}/${v.name ?? i}`, type: "cube", frame: prefix + link.name, position: v.origin.xyz, orientation: [q.x, q.y, q.z, q.w], color: v.color ?? def, opacity: v.opacity };
    switch (g.type) {
      case "box": out.push({ ...base, type: "cube", scale: g.size }); break;
      case "cylinder": out.push({ ...base, type: "cylinder", scale: [g.radius * 2, g.radius * 2, g.length] }); break;
      case "sphere": out.push({ ...base, type: "sphere", scale: [g.radius * 2, g.radius * 2, g.radius * 2] }); break;
      case "mesh": out.push({ ...base, type: "mesh", meshResource: g.filename, scale: g.scale }); break;
    }
  });
  return out;
}

/**
 * Child-in-parent transforms for every joint at the given joint positions (radians or metres; missing = 0).
 * Static (no `time`) when `time` is omitted; revolute/continuous rotate about the axis, prismatic slide along it.
 */
export function urdfTransforms(model: RobotModel, positions: Record<string, number> = {}, options: { prefix?: string | undefined; time?: number | undefined } = {}): FrameTransformMessage[] {
  const prefix = options.prefix ?? "";
  return model.joints.map((j) => {
    const q0 = rpyQuat(j.origin.rpy), pos = positions[j.name] ?? 0;
    const axis = v3(j.axis[0], j.axis[1], j.axis[2]);
    let q = q0, t = v3(j.origin.xyz[0], j.origin.xyz[1], j.origin.xyz[2]);
    if ((j.type === "revolute" || j.type === "continuous") && pos !== 0) q = quatMul(q0, quatFromAxisAngle(axis, pos));
    else if (j.type === "prismatic" && pos !== 0) t = v3add(t, quatRotate(q0, v3scale(axis, pos)));
    return { child: prefix + j.child, parent: prefix + j.parent, t: [t.x, t.y, t.z], q: [q.x, q.y, q.z, q.w], time: options.time };
  });
}

/** Mesh resource URIs a model needs (deduplicated, in link order). */
export function urdfMeshUris(model: RobotModel): string[] {
  const out: string[] = [];
  for (const l of model.links) for (const v of l.visuals) if (v.geometry.type === "mesh" && !out.includes(v.geometry.filename)) out.push(v.geometry.filename);
  return out;
}

/** Layer declarations that put a robot model into a scene through any `LayerSink`: its visuals as a marker set and its joints as static transforms at rest. */
export function urdfDeclarations(model: RobotModel, options: { prefix?: string | undefined; defaultColor?: string | undefined; markersId?: string | undefined; framesId?: string | undefined } = {}): { id: string; kind: string; meta: Record<string, unknown> }[] {
  const prefix = options.prefix ?? "";
  return [
    { id: options.framesId ?? `${prefix}tf`, kind: "frames", meta: { transforms: urdfTransforms(model, {}, { prefix }) } },
    { id: options.markersId ?? `${prefix}robot`, kind: "markers", meta: { markers: urdfMarkers(model, { prefix, defaultColor: options.defaultColor }) } },
  ];
}

/** Declare a model on a sink, then drive its joints with `pushJointPositions`. */
export function publishUrdf(sink: { declareLayer(id: string, kind: string, meta?: Record<string, unknown>): void; push(id: string, payload: unknown): void }, model: RobotModel, options: Parameters<typeof urdfDeclarations>[1] = {}): (positions: Record<string, number>, time?: number) => void {
  for (const d of urdfDeclarations(model, options)) sink.declareLayer(d.id, d.kind, d.meta);
  const framesId = options.framesId ?? `${options.prefix ?? ""}tf`;
  return (positions, time) => sink.push(framesId, { transforms: urdfTransforms(model, positions, { prefix: options.prefix, time }) });
}
