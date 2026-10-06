// Mori.SkyScope — Blazor bridge entry point: mounts the trend chart and re-exports the other mounts; bundled into the Razor package.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Tool, TrendChartOptions } from "@mori/skyscope-core";
import { acquire } from "./shared.js";
import { TrendChartView, saveFile } from "@mori/skyscope-render";
import type { DropTarget } from "@mori/skyscope-core";

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
  /** The arrangement as a JSON layout file. */
  exportLayout(): string;
  /** Replace the arrangement with a layout file; theme and style are kept. */
  importLayout(json: string): void;
  /** Hand the layout file to the browser's download machinery. */
  downloadLayout(fileName: string): void;
  /** Download the visible signals between cursors A and B as CSV or MCAP; false, nothing saved, unless both cursors are set. */
  exportCursors(format: "csv" | "mcap"): boolean;
  /** Add channels from code; `targetJson` is an optional DropTarget. Returns the series ids. */
  addChannels(channelIds: number[], targetJson: string | null, group: boolean): string[];
  /**
   * Route drops to .NET: the object's `OnChannelDrop(json)` receives the channel ids, the resolved target and the point,
   * and returns "apply" (default behaviour), "cancel" or "handled". The call is asynchronous, so the chart applies the
   * drop after the answer arrives.
   */
  setDropHandler(dotnet: { invokeMethodAsync(name: string, ...args: unknown[]): Promise<string> } | null): void;
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
    exportLayout: () => view.exportLayout(),
    importLayout: (json) => view.importLayout(json),
    downloadLayout: (fileName) => saveFile(new Blob([view.exportLayout()], { type: "application/json" }), fileName, "application/json"),
    exportCursors: (format) => view.exportCursors(format),
    addChannels: (ids, targetJson, group) => view.addChannels(ids, targetJson ? (JSON.parse(targetJson) as DropTarget) : undefined, group),
    setDropHandler: (dotnet) => {
      view.onChannelDrop = dotnet ? (e) => {
        // .NET answers asynchronously: hold the drop here and apply (or not) when the verdict arrives
        e.handled = true;
        const json = JSON.stringify({ channelIds: e.channelIds, channels: e.payload.channels, target: e.target, x: e.x, y: e.y, group: e.group });
        void dotnet.invokeMethodAsync("OnChannelDrop", json).then((verdict) => { if (verdict !== "cancel" && verdict !== "handled") view.addChannels(e.channelIds, e.target, e.group); });
      } : null;
    },
    dispose: () => { view.dispose(); release(); },
  };
}
export * from "./signal-tree.js";
export * from "./chart-editor.js";
export * from "./gauges.js";
export * from "./charts.js";
export * from "./scene.js";
export * from "./scene3d.js";
export * from "./playback.js";
export * from "./recorder.js";
