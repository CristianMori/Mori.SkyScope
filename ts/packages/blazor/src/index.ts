// Mori.SkyScope — Blazor bridge entry point: mounts the trend chart and re-exports the other mounts; bundled into the Razor package.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Tool, TrendChartOptions } from "@mori/skyscope-core";
import { acquire } from "./shared.js";
import { TrendChartView } from "@mori/skyscope-render";

/**
 * The JS side of the Blazor components. Bundled by esbuild into
 * `Mori.SkyScope.Blazor/wwwroot/skyscope.js` and imported via IJSRuntime. Control plane only:
 * configuration crosses the interop boundary as JSON; samples stream straight from the WebSocket.
 */
/**
 * Options of `mountTrendChart`. `wsUrl` selects the shared stream (a `ws://` URL, or `playback:<key>` for a browser-side
 * recording; omitted = an empty local store); `configJson` is the serialized TrendChartOptions; `tool` the initial pointer
 * tool; `webgl` whether series use WebGL (default true); `retentionSeconds` the store's history when this mount creates it (default 60).
 */
export interface MountOptions { wsUrl?: string | undefined; configJson?: string | undefined; tool?: Tool | undefined; webgl?: boolean | undefined; retentionSeconds?: number | undefined }

/** What `mountTrendChart` returns to .NET; methods are invoked through JS interop, `view` is used by other JS mounts. */
export interface TrendChartHandle {
  /** Applies a serialized TrendChartOptions in place (theme and style merge field-wise, other keys replace). */
  setConfig(configJson: string): void;
  /** Switches the pointer tool (pan, boxZoom, cursor). */
  setTool(tool: Tool): void;
  /** Transport of the live window: `pause` freezes it, `resume` follows the clock again, `reset` clears zoom and cursors. */
  pause(): void; resume(): void; reset(): void;
  /** Sets the visible window length in seconds. */
  setTimeSpan(seconds: number): void;
  /** Connection and ingest counters: socket state, frames received, channels declared, samples dropped by the store. */
  stats(): { connected: boolean; frames: number; channels: number; dropped: number };
  /** The view, for other mounts on the page (the signal tree attaches to it). */
  view: TrendChartView;
  /** Tears down the view and releases the shared socket (closed when no other mount uses it). */
  dispose(): void;
}

/** Mounts a trend chart in `element` on the shared store of `options.wsUrl` and starts its render loop. */
export function mountTrendChart(element: HTMLElement, options: MountOptions): TrendChartHandle {
  const { store, source, clock, release } = acquire(options.wsUrl, options.retentionSeconds ?? 60);
  const config = options.configJson ? (JSON.parse(options.configJson) as TrendChartOptions) : {};
  const view = new TrendChartView(element, { store, config, tool: options.tool, webgl: options.webgl, clock });
  view.start();
  return {
    view,
    setConfig: (json) => view.setConfig(JSON.parse(json) as TrendChartOptions),
    setTool: (tool) => view.setTool(tool),
    pause: () => view.model.pause(), resume: () => view.model.resume(), reset: () => view.model.reset(),
    setTimeSpan: (s) => view.model.setTimeSpan(s),
    stats: () => ({ connected: source?.connected ?? false, frames: source?.framesReceived ?? 0, channels: store.channels.size, dropped: store.dropped }),
    dispose: () => { view.dispose(); release(); },
  };
}
export * from "./signal-tree.js";
export * from "./gauges.js";
export * from "./charts.js";
export * from "./scene.js";
export * from "./scene3d.js";
export * from "./playback.js";
export * from "./recorder.js";
