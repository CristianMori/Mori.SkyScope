// Mori.SkyScope — Blazor bridge for the 2D scene view: layers declared and pushed as JSON, relayed layers from the shared socket.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SceneLayerSink, applyLayerPayload, createLayer, type SceneTool } from "@mori/skyscope-core";
import { SceneView, type SceneViewOptions } from "@mori/skyscope-render";
import { acquire } from "./shared.js";

/**
 * The JS side of the Blazor SceneView. Layers are declared and updated with the same JSON shapes a
 * `LayerSink` receives (see `createLayer` / `applyLayerPayload`), so a Blazor page and a source plugin speak
 * one language.
 */
/** What `mountScene` returns to .NET; every method is invoked through JS interop. */
export interface SceneHandle {
  /** Creates (or replaces) a layer of `kind` with the JSON options in `metaJson`; false when the kind is unknown. */
  declareLayer(id: string, kind: string, metaJson: string | null): boolean;
  /** Applies a JSON payload to an existing layer (the same shape a `LayerSink.push` takes); false when the id is unknown or the payload is rejected. */
  push(id: string, payloadJson: string): boolean;
  /** Removes a layer; false when the id is unknown. */
  removeLayer(id: string): boolean;
  /** Switches the pointer tool (pan, boxZoom, measure, select). */
  setTool(tool: SceneTool): void;
  /** Frames every layer's bounds in the viewport. */
  fitAll(): void;
  /** Sets the map rotation about the view centre, in radians (0 = north up; R resets it). */
  setRotation(radians: number): void;
  /** Detaches from the shared socket and tears down the view. */
  dispose(): void;
}

/** `wsUrl`: also subscribe to layer messages relayed over the shared SkyScope socket (fit-all on the first declaration). */
export function mountScene(element: HTMLElement, optionsJson: string | null, wsUrl: string | null = null): SceneHandle {
  const view = new SceneView(element, optionsJson ? (JSON.parse(optionsJson) as SceneViewOptions) : {});
  const c = view.controller;
  let release = (): void => {};
  if (wsUrl) {
    const shared = acquire(wsUrl, 60);
    let fitted = false;
    const detach = shared.layers.add(new SceneLayerSink(c.scene, () => { if (!fitted && c.scene.layers.length > 0) { fitted = true; c.fitAll(); } view.invalidate(); }));
    release = () => { detach(); shared.release(); };
  }
  return {
    declareLayer(id, kind, metaJson) {
      c.scene.remove(id);
      const layer = createLayer(id, kind, metaJson ? (JSON.parse(metaJson) as never) : {});
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
    setTool(tool) { c.setTool(tool); view.invalidate(); },
    fitAll() { c.fitAll(); view.invalidate(); },
    setRotation(r) { c.setRotation(r); view.invalidate(); },
    dispose() { release(); view.dispose(); },
  };
}
