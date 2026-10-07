// Mori.SkyScope — Blazor bridge for the 3D scene view: layers, transforms and binary payloads from the shared socket.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SceneLayerSink, applyLayerPayload, createLayer, type Scene3DTool } from "@cmori/skyscope-core";
import { Scene3DView, type Scene3DViewOptions } from "@cmori/skyscope-render";
import { acquire } from "./shared.js";

/**
 * The JS side of the Blazor SceneView3D. Same JSON layer contract as the 2D scene (`createLayer` / `applyLayerPayload`),
 * plus the 3D kinds (grid3d, axes, pointCloud3d, path3d, pose3d, markers, laserScan3d, occupancyGrid3d, frames).
 */
/** What `mountScene3D` returns to .NET; every method is invoked through JS interop. */
export interface Scene3DHandle {
  /** Creates (or replaces) a layer of `kind` with the JSON options in `metaJson`, bound to the view's frame tree; false when the kind is unknown. */
  declareLayer(id: string, kind: string, metaJson: string | null): boolean;
  /** Applies a JSON payload to an existing layer; false when the id is unknown or the payload is rejected. Binary payloads arrive over the socket instead. */
  push(id: string, payloadJson: string): boolean;
  /** Removes a layer; false when the id is unknown. */
  removeLayer(id: string): boolean;
  /** Upserts transforms from a JSON array of `{child, parent, t: [x,y,z], q: [x,y,z,w], time?}` (metres, unit quaternion, seconds). */
  setTransforms(transformsJson: string): void;
  /** Switches the pointer tool (orbit, measure, select). */
  setTool(tool: Scene3DTool): void;
  /** Names the frame the camera and every layer are expressed in. */
  setFixedFrame(frame: string): void;
  /** Frames every layer's bounds in the viewport. */
  fitAll(): void;
  /** Restores the initial camera pose. */
  resetView(): void;
  /** Looks straight down the z axis. */
  topDown(): void;
  /** Switches between orthographic (true) and perspective projection. */
  setOrtho(ortho: boolean): void;
  /** Detaches from the shared socket and tears down the view and its GPU resources. */
  dispose(): void;
}

/** `wsUrl`: also subscribe to layer messages (JSON and binary) relayed over the shared SkyScope socket. */
export function mountScene3D(element: HTMLElement, optionsJson: string | null, wsUrl: string | null = null): Scene3DHandle {
  const view = new Scene3DView(element, optionsJson ? (JSON.parse(optionsJson) as Scene3DViewOptions) : {});
  const c = view.controller;
  const env = () => ({ frames: c.frames, fixedFrame: c.fixedFrame });
  let release = (): void => {};
  if (wsUrl) {
    const shared = acquire(wsUrl, 60);
    let fitted = false;
    const detach = shared.layers.add(new SceneLayerSink(c.scene, () => { if (!fitted && c.scene.layers.length > 0) { fitted = true; c.fitAll(); } view.invalidate(); }, env()));
    release = () => { detach(); shared.release(); };
  }
  return {
    declareLayer(id, kind, metaJson) {
      c.scene.remove(id);
      const layer = createLayer(id, kind, metaJson ? (JSON.parse(metaJson) as never) : {}, env());
      if (!layer) return false;
      c.scene.add(layer);
      view.invalidate();
      return true;
    },
    push(id, payloadJson) {
      const layer = c.scene.get(id);
      if (!layer) return false;
      const ok = applyLayerPayload(layer, JSON.parse(payloadJson));
      if (ok) view.invalidate();
      return ok;
    },
    removeLayer(id) { const ok = c.scene.remove(id); if (ok) view.invalidate(); return ok; },
    setTransforms(json) {
      for (const m of JSON.parse(json) as { child: string; parent: string; t: [number, number, number]; q: [number, number, number, number]; time?: number }[]) c.frames.set(m.child, m.parent, { t: { x: m.t[0], y: m.t[1], z: m.t[2] }, q: { x: m.q[0], y: m.q[1], z: m.q[2], w: m.q[3] } }, m.time);
      view.invalidate();
    },
    setTool(tool) { c.setTool(tool); view.invalidate(); },
    setFixedFrame(frame) { c.fixedFrame = frame; view.invalidate(); },
    fitAll() { c.fitAll(); view.invalidate(); },
    resetView() { c.resetView(); view.invalidate(); },
    topDown() { c.topDown(); view.invalidate(); },
    setOrtho(o) { c.camera.setOrtho(o); view.invalidate(); },
    dispose() { release(); view.dispose(); },
  };
}
