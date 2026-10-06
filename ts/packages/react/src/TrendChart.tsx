// Mori.SkyScope — React component hosting the trend chart view over a signal store.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useRef, type CSSProperties } from "react";
import type { SignalStore, TimeSource, Tool, TrendChartOptions } from "@mori/skyscope-core";
import { TrendChartView, type ChannelDropEvent } from "@mori/skyscope-render";
import { cx } from "./cx.js";

/** Props of `TrendChart`. `store`, `clock`, `webgl` and `maxFps` remount the view when they change; `config` and `tool` are applied in place. */
export interface TrendChartProps {
  /** The store whose channels the chart reads. */
  store: SignalStore;
  /** Chart configuration; applied through `setConfig` (theme and style merge field-wise, other keys replace). */
  config?: TrendChartOptions | undefined;
  /** Active pointer tool (pan, boxZoom, cursor). */
  tool?: Tool | undefined;
  /** Time source for the live window; pass `playback.source.clock` to follow a recording. Default: wall clock. */
  clock?: TimeSource | undefined;
  /** Draw series with WebGL when available (default true); false forces Canvas2D. */
  webgl?: boolean | undefined;
  /** Frames per second cap for the render loop (default 60). */
  maxFps?: number | undefined;
  /** Class of the host div. */
  className?: string | undefined;
  /** Inline style of the host div; it fills its parent. */
  style?: CSSProperties | undefined;
  /** Access the underlying view (model, pause/resume, …). */
  onReady?: ((view: TrendChartView) => void) | undefined;
  /** Channels dropped on the chart (native drag and drop), before they are added: cancel, redirect or handle the drop. */
  onChannelDrop?: ((e: ChannelDropEvent) => void) | undefined;
  /** The arrangement changed through a gesture, a drop or a layout import. */
  onConfigChanged?: (() => void) | undefined;
}

/** A live strip chart bound to a SignalStore. Fills its parent; give the parent a height. */
export function TrendChart({ store, config, tool, clock, webgl, maxFps, className, style, onReady, onChannelDrop, onConfigChanged }: TrendChartProps) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<TrendChartView | null>(null);
  const dropRef = useRef(onChannelDrop); dropRef.current = onChannelDrop;
  const changedRef = useRef(onConfigChanged); changedRef.current = onConfigChanged;
  useEffect(() => {
    const v = new TrendChartView(host.current!, { store, config, tool, clock, webgl, maxFps });
    v.onChannelDrop = (e) => dropRef.current?.(e);
    v.onConfigChanged = () => changedRef.current?.();
    view.current = v;
    v.start();
    onReady?.(v);
    return () => { v.dispose(); view.current = null; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [store, clock, webgl, maxFps]);
  useEffect(() => { if (config) view.current?.setConfig(config); }, [config]);
  useEffect(() => { if (tool) view.current?.setTool(tool); }, [tool]);
  return <div ref={host} className={cx("skyscope-trend-host", className)} style={{ width: "100%", height: "100%", minHeight: 120, ...style }} />;
}
