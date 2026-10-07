// Mori.SkyScope — React component hosting the 2D scene view.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useRef, type CSSProperties } from "react";
import type { SceneController, SceneTool } from "@cmori/skyscope-core";
import { SceneView as SceneViewHost, type SceneViewOptions } from "@cmori/skyscope-render";
import { cx } from "./cx.js";

/** Props of `SceneView`. */
export interface SceneViewProps {
  /** Host and controller options, read once at mount; later changes are ignored (remount with a `key` to apply them). */
  options?: SceneViewOptions | undefined;
  /** Active pointer tool; applied whenever it changes. */
  tool?: SceneTool | undefined;
  /** Receives the controller (add layers to `controller.scene`) and the host (call `invalidate()` after updates). */
  onReady?: ((controller: SceneController, view: SceneViewHost) => void) | undefined;
  /** Class and inline style of the host div; it fills its parent, so give the parent a height. */
  className?: string | undefined; style?: CSSProperties | undefined;
}

/** Composable map view: pan, wheel zoom, shift-drag box zoom, measure and select tools, Q/E rotate, F fit all. */
export function SceneView({ options, tool, onReady, className, style }: SceneViewProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<SceneViewHost | null>(null);
  const ready = useRef(onReady); ready.current = onReady;
  useEffect(() => {
    const v = new SceneViewHost(host.current!, options);
    view.current = v;
    ready.current?.(v.controller, v);
    return () => { v.dispose(); view.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => { if (tool && view.current) { view.current.controller.setTool(tool); view.current.invalidate(); } }, [tool]);
  return <div ref={host} className={cx("skyscope-scene-host", className)} style={{ width: "100%", height: "100%", minHeight: 160, ...style }} />;
}
