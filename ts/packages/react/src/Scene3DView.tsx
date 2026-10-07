// Mori.SkyScope — React component hosting the 3D scene view.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useRef, type CSSProperties } from "react";
import type { Scene3DController, Scene3DTool } from "@cmori/skyscope-core";
import { Scene3DView as Scene3DViewHost, type Scene3DViewOptions } from "@cmori/skyscope-render";
import { cx } from "./cx.js";

/** Props of `Scene3DView`. */
export interface Scene3DViewProps {
  /** Host and controller options, read once at mount; later changes are ignored (remount with a `key` to apply them). */
  options?: Scene3DViewOptions | undefined;
  /** Active pointer tool; applied whenever it changes. */
  tool?: Scene3DTool | undefined;
  /** Receives the controller (add layers to `controller.scene`, transforms to `controller.frames`) and the host (call `invalidate()` after updates). */
  onReady?: ((controller: Scene3DController, view: Scene3DViewHost) => void) | undefined;
  /** Class and inline style of the host div; it fills its parent, so give the parent a height. */
  className?: string | undefined; style?: CSSProperties | undefined;
}

/** RViz-style 3D view: left-drag orbit, middle/shift-drag pan, right-drag or wheel dolly, F fit, R reset, T top-down, O orthographic. */
export function Scene3DView({ options, tool, onReady, className, style }: Scene3DViewProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<Scene3DViewHost | null>(null);
  const ready = useRef(onReady); ready.current = onReady;
  useEffect(() => {
    const v = new Scene3DViewHost(host.current!, options);
    view.current = v;
    ready.current?.(v.controller, v);
    return () => { v.dispose(); view.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);
  useEffect(() => { if (tool && view.current) { view.current.controller.setTool(tool); view.current.invalidate(); } }, [tool]);
  return <div ref={host} className={cx("skyscope-scene3d-host", className)} style={{ width: "100%", height: "100%", minHeight: 160, ...style }} />;
}
