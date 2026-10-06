// Mori.SkyScope — Fixture drivers for the robotics layers and the scene controller.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { Camera2D, type Camera2DOptions } from "../scene/camera.js";
import { Scene, type HitResult, type Layer } from "../scene/scene.js";
import { GridLayer, PointsLayer, PolylineLayer } from "../scene/layers.js";
import { BitmapLayer, OccupancyGridLayer, PointCloudLayer, PoseLayer, ShapeLayer, TrailLayer, decomposeConformal, laserScanToPoints, type LaserScanLike, type Pose2D, type Shape } from "../scene/robot-layers.js";
import { SceneController, type SceneControllerOptions, type SceneTool } from "../scene/controller.js";
import type { InputEvent } from "../scene/interaction.js";
import { fnv1a } from "../charts/colormaps.js";
import { RecordingPainter } from "../paint/recording-painter.js";
import type { Mat3 } from "../scene/geometry.js";
import { r9, rRect } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

/** Fixture drivers for the robotics layers and the scene controller. Mirrors `RobotDrivers.cs`. */
type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const arr9 = (a: ArrayLike<number>): number[] => Array.from(a, r9);
const mat9 = (m: Mat3): Record<string, number> => ({ a: r9(m.a), b: r9(m.b), c: r9(m.c), d: r9(m.d), e: r9(m.e), f: r9(m.f) });
const hit = (h: HitResult | null): unknown => (h ? { layerId: h.layerId, world: { x: r9(h.world.x), y: r9(h.world.y) }, distance: r9(h.distance), index: h.index ?? null, data: h.data === undefined ? null : typeof h.data === "number" ? r9(h.data) : h.data } : null);

/**
 * Build a 2D layer from a fixture spec: `kind` picks bitmap, occupancy, pointCloud (with an optional scan), pose,
 * shapes, trail, grid, polyline or points; the rest of the spec is the layer's options.
 */
export function makeRobotLayer(spec: Step): Layer {
  const id = spec.id as string;
  switch (spec.kind) {
    case "bitmap": { const img = spec.image as { width: number; height: number }; return new BitmapLayer(id, img, spec.placement as never); }
    case "occupancy": return new OccupancyGridLayer(id, spec as never);
    case "pointCloud": {
      const l = new PointCloudLayer(id, spec as never);
      if (spec.scan) l.setScan(spec.scan as LaserScanLike, (spec.scan as { pose?: Pose2D }).pose);
      return l;
    }
    case "pose": return new PoseLayer(id, spec.pose as Pose2D, spec as never);
    case "shapes": return new ShapeLayer(id, [...(spec.shapes as Shape[])]);
    case "trail": { const t = new TrailLayer(id, spec.maxPoints as number | undefined); const pts = (spec.points as number[] | undefined) ?? []; for (let i = 0; i < pts.length; i += 2) t.append(pts[i]!, pts[i + 1]!); return t; }
    case "grid": return new GridLayer(id, spec as never);
    case "polyline": return new PolylineLayer(id, spec.points as number[], undefined, spec.closed === true);
    case "points": return new PointsLayer(id, spec.points as number[]);
    default: throw new Error(`unknown layer kind ${String(spec.kind)}`);
  }
}

interface LayersState { camera: Camera2D; scene: Scene; queries: unknown[] }

/** Robotics layers in a scene under a `Camera2D`: `setup` gives the camera options and the layer specs. */
export const robotLayersDriver: FixtureDriver<LayersState> = {
  component: "robot-layers",
  create(setup) {
    const cam = setup.camera as Camera2DOptions;
    const scene = new Scene();
    for (const l of (setup.layers as Step[]) ?? []) scene.add(makeRobotLayer(l));
    return { camera: new Camera2D(cam), scene, queries: [] };
  },
  /**
   * Steps: setPose, setData, setCell, setOrigin, append, setPoints, upsert, removeShape; queries: bounds,
   * sceneBounds, hit, pixelToWorld, decompose, laserScan, colorOf, hash, pixel, cellAt, arrow, worldFootprint,
   * pointCount, points, draw.
   */
  step(s, step: Step) {
    const { camera, scene, queries } = s;
    const get = <T>(id: unknown): T => scene.get(id as string) as unknown as T;
    switch (step.type) {
      case "setPose": get<PoseLayer>(step.id).setPose(step.pose as Pose2D); break;
      case "setData": get<OccupancyGridLayer>(step.id).setData(step.data as number[]); break;
      case "setCell": get<OccupancyGridLayer>(step.id).set(num(step.row), num(step.col), num(step.value)); break;
      case "setOrigin": get<OccupancyGridLayer>(step.id).setOrigin(step.pose as Pose2D); break;
      case "append": get<PolylineLayer>(step.id).append(num(step.x), num(step.y)); break;
      case "setPoints": get<PointCloudLayer>(step.id).setPoints(step.points as number[], step.intensities as number[] | undefined); break;
      case "upsert": get<ShapeLayer>(step.id).upsert(step.shape as Shape); break;
      case "removeShape": queries.push(get<ShapeLayer>(step.id).removeShape(step.shapeId as string)); break;
      case "query":
        if ("bounds" in step) queries.push(rRect(get<Layer>(step.bounds).bounds()));
        else if ("sceneBounds" in step) queries.push(rRect(scene.bounds()));
        else if ("hit" in step) { const [x, y] = step.hit as [number, number]; queries.push(hit(scene.hitTest(x, y, camera, camera.width, camera.height, step.tolerance === undefined ? 6 : num(step.tolerance)))); }
        else if ("pixelToWorld" in step) queries.push(mat9(get<BitmapLayer | OccupancyGridLayer>(step.pixelToWorld).pixelToWorld()));
        else if ("decompose" in step) { const d = decomposeConformal(step.decompose as Mat3); queries.push({ tx: r9(d.tx), ty: r9(d.ty), rotation: r9(d.rotation), sx: r9(d.sx), sy: r9(d.sy) }); }
        else if ("laserScan" in step) { const sc = step.laserScan as LaserScanLike & { pose?: Pose2D }; queries.push(arr9(laserScanToPoints(sc, sc.pose))); }
        else if ("colorOf" in step) { const [id, i] = step.colorOf as [string, number]; queries.push(get<PointCloudLayer>(id).colorOf(i)); }
        else if ("hash" in step) { const img = get<OccupancyGridLayer>(step.hash).image(); queries.push({ width: img.width, height: img.height, hash: fnv1a(img.rgba) }); }
        else if ("pixel" in step) { const [id, x, y] = step.pixel as [string, number, number]; const img = get<OccupancyGridLayer>(id).image(), o = (y * img.width + x) * 4; queries.push([img.rgba[o], img.rgba[o + 1], img.rgba[o + 2], img.rgba[o + 3]]); }
        else if ("cellAt" in step) { const [id, x, y] = step.cellAt as [string, number, number]; queries.push(get<OccupancyGridLayer>(id).cellAt(x, y)); }
        else if ("arrow" in step) queries.push(arr9(get<PoseLayer>(step.arrow).arrow({ projection: camera, width: camera.width, height: camera.height, now: 0, opacity: 1 })));
        else if ("worldFootprint" in step) { const f = get<PoseLayer>(step.worldFootprint).worldFootprint(); queries.push(f ? arr9(f) : null); }
        else if ("pointCount" in step) queries.push(get<PointCloudLayer | PolylineLayer>(step.pointCount).pointCount);
        else if ("points" in step) queries.push(arr9(get<PolylineLayer>(step.points).rawPoints()));
        else if ("draw" in step) { const p = new RecordingPainter(camera.width, camera.height); scene.draw(p, camera, 0); queries.push(p.ops); }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};

interface CtrlState { c: SceneController; w: number; h: number; queries: unknown[] }

/** `SceneController` with layers: `setup` gives the size, the controller options and the layer specs. */
export const sceneControllerDriver: FixtureDriver<CtrlState> = {
  component: "scene-controller",
  create(setup) {
    const w = num(setup.width), h = num(setup.height);
    const c = new SceneController((setup.options ?? {}) as SceneControllerOptions);
    c.setViewport(w, h);
    for (const l of (setup.layers as Step[]) ?? []) c.scene.add(makeRobotLayer(l));
    return { c, w, h, queries: [] };
  },
  /**
   * Steps: event (an `InputEvent`; pushes the redraw flag), setTool, rotateBy, fitAll; queries: camera, tool,
   * measure, selection, hover, cursor, boxPreview, scaleBar, draw.
   */
  step(s, step: Step) {
    const { c, w, h, queries } = s;
    switch (step.type) {
      case "event": queries.push(c.handle(step.event as InputEvent)); break;
      case "setTool": c.setTool(step.tool as SceneTool); break;
      case "rotateBy": c.rotateBy(num(step.radians)); break;
      case "fitAll": queries.push(c.fitAll()); break;
      case "query":
        if ("camera" in step) queries.push({ centerX: r9(c.camera.centerX), centerY: r9(c.camera.centerY), zoom: r9(c.camera.zoom), rotation: r9(c.camera.rotation) });
        else if ("tool" in step) queries.push(c.tool);
        else if ("measure" in step) { const d = c.measureDistance(); queries.push({ points: c.measure.map((p) => ({ x: r9(p.x), y: r9(p.y) })), distance: d === null ? null : r9(d) }); }
        else if ("selection" in step) queries.push(hit(c.selection));
        else if ("hover" in step) queries.push(hit(c.hover));
        else if ("cursor" in step) queries.push(c.cursor ? { x: r9(c.cursor.x), y: r9(c.cursor.y) } : null);
        else if ("boxPreview" in step) queries.push(rRect(c.boxPreview));
        else if ("scaleBar" in step) { const sb = c.scaleBar(step.scaleBar === true ? 100 : num(step.scaleBar)); queries.push({ world: r9(sb.world), px: r9(sb.px) }); }
        else if ("draw" in step) { const p = new RecordingPainter(w, h); c.draw(p, w, h); queries.push(p.ops); }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
