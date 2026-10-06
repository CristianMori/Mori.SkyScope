// Mori.SkyScope — Fixture drivers for the 2D scene: interaction reducer and scene layers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { initialInteraction, reduceInteraction, type Effect, type InputEvent, type InteractionState, type Tool } from "../scene/interaction.js";
import { Camera2D, type Camera2DOptions } from "../scene/camera.js";
import { Scene, type Layer } from "../scene/scene.js";
import { PointsLayer, PolylineLayer } from "../scene/layers.js";
import { RecordingPainter, type PaintOp } from "../paint/recording-painter.js";
import { r9, rRect } from "./geometry-drivers.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;
const nums = (v: unknown): number[] => v as number[];

/** Interaction reducer: `setup.tool` picks the tool; every step is an `InputEvent` and the effects accumulate. */
export const interactionDriver: FixtureDriver<{ state: InteractionState; effects: Effect[] }> = {
  component: "interaction",
  create(setup) { return { state: initialInteraction((setup.tool as Tool) ?? "pan"), effects: [] }; },
  step(s, step: Step) {
    const r = reduceInteraction(s.state, step as unknown as InputEvent);
    return { state: r.state, effects: [...s.effects, ...r.effects] };
  },
  /** Phase, gesture, space state and every effect produced. */
  snapshot({ state, effects }) { return { phase: state.phase, gesture: state.gesture, spaceHeld: state.spaceHeld, effects }; },
};

interface SceneState { camera: Camera2D; scene: Scene; painter: RecordingPainter; seen: number; queries: unknown[] }

function makeLayer(spec: Step): Layer {
  const id = spec.id as string, pts = nums(spec.points);
  if (spec.kind === "polyline") return new PolylineLayer(id, pts, undefined, spec.closed === true);
  if (spec.kind === "points") return new PointsLayer(id, pts, spec.radius === undefined ? 4 : num(spec.radius));
  throw new Error(`unknown layer kind ${String(spec.kind)}`);
}

const opName = (o: PaintOp): string => (o.op === "layerDraw" ? `layerDraw(${(o.ops as PaintOp[]).map((x) => x.op).join(",")})` : o.op);

/**
 * 2D scene with polyline and points layers under a `Camera2D`: `setup` gives the camera options and the layer specs.
 */
export const sceneDriver: FixtureDriver<SceneState> = {
  component: "scene",
  create(setup) {
    const cam = setup.camera as Camera2DOptions;
    const scene = new Scene();
    for (const l of setup.layers as Step[]) scene.add(makeLayer(l));
    return { camera: new Camera2D(cam), scene, painter: new RecordingPainter(cam.width ?? 100, cam.height ?? 100), seen: 0, queries: [] };
  },
  /**
   * Steps: setVisible, move, remove, markDirty, pan; queries: hit, bounds, order, draw (op names since the previous
   * draw, so cache reuse is visible).
   */
  step(s, step: Step) {
    const { camera, scene, painter, queries } = s;
    switch (step.type) {
      case "setVisible": scene.get(step.id as string)!.visible = step.visible as boolean; break;
      case "move": scene.move(step.id as string, num(step.index)); break;
      case "remove": scene.remove(step.id as string); break;
      case "markDirty": scene.get(step.id as string)!.dirty = true; break;
      case "pan": camera.pan(num(step.dx), num(step.dy)); break;
      case "query":
        if ("hit" in step) {
          const [x, y] = nums(step.hit);
          const h = scene.hitTest(x!, y!, camera, camera.width, camera.height, step.tolerance === undefined ? 6 : num(step.tolerance));
          queries.push(h ? { layerId: h.layerId, world: { x: r9(h.world.x), y: r9(h.world.y), z: r9(h.world.z) }, screen: { x: r9(h.screen.x), y: r9(h.screen.y) }, distance: r9(h.distance), index: h.index } : null);
        } else if ("bounds" in step) queries.push(rRect(scene.bounds()));
        else if ("order" in step) queries.push(scene.order());
        else if ("draw" in step) { scene.draw(painter, camera, 0); queries.push(painter.ops.slice(s.seen).map(opName)); s.seen = painter.ops.length; }
        else throw new Error(`unknown query ${JSON.stringify(step)}`);
        break;
      default: throw new Error(`unknown step ${String(step.type)}`);
    }
    return s;
  },
  snapshot({ queries }) { return { queries }; },
};
