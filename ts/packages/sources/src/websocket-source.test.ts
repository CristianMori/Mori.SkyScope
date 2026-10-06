// Mori.SkyScope — Tests the WebSocket frame source against an in-process socket server.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, it, expect } from "vitest";
import { WebSocketServer } from "ws";
import { encodeFrame, ManualClock, NullLayerSink, SignalStore, type SourceContext } from "@mori/skyscope-core";
import { WebSocketFrameSource } from "./websocket-source.js";

const wait = (ms: number) => new Promise((r) => setTimeout(r, ms));
async function until(pred: () => boolean, ms = 3000): Promise<void> {
  const t0 = Date.now();
  while (!pred()) { if (Date.now() - t0 > ms) throw new Error("timeout"); await wait(10); }
}

describe("WebSocketFrameSource", () => {
  it("declares channels from the catalog and pushes decoded frames into the store", async () => {
    const wss = new WebSocketServer({ port: 0 });
    const port = (wss.address() as { port: number }).port;
    wss.on("connection", (ws) => {
      ws.send(JSON.stringify({ type: "channels", channels: [{ id: 1, name: "speed", unit: "m/s", rate: 4 }] }));
      ws.send(encodeFrame({ seq: 1, t0: 0, channels: [{ id: 1, encoding: "regular", tStart: 0, dt: 0.25, values: Float32Array.from([1, 2, 3]) }] }));
      ws.send(encodeFrame({ seq: 2, t0: 0, channels: [{ id: 7, encoding: "timestamped", times: Float64Array.from([5]), values: Float32Array.from([9]) }] }));
      ws.send(new Uint8Array([1, 2, 3]));
    });
    const store = new SignalStore({ retentionSeconds: 10 });
    const logs: string[] = [];
    const ctx: SourceContext = { signals: store, layers: new NullLayerSink(), clock: new ManualClock(), log: (l, m) => logs.push(`${l}:${m}`) };
    const src = new WebSocketFrameSource();
    src.start(ctx, { url: `ws://127.0.0.1:${port}`, worker: false, reconnectMs: 0 });
    try {
      await until(() => store.channels.size === 2 && src.framesReceived === 2 && logs.some((l) => l.includes("bad frame")));
      expect(store.get(1)?.info).toMatchObject({ name: "speed", unit: "m/s", rate: 4 });
      expect(store.get(1)?.buffer.length).toBe(3);
      expect(store.get(7)?.buffer.kind).toBe("timestamped");
      expect(src.connected).toBe(true);
    } finally {
      src.stop();
      wss.close();
    }
    expect(src.connected).toBe(false);
  });
});

describe("WebSocketFrameSource binary layers", () => {
  it("routes SkyScopeLayer messages to the layer sink with typed arrays intact", async () => {
    const { encodeLayerMessage, Scene, SceneLayerSink, PointCloud3DLayer } = await import("@mori/skyscope-core");
    const wss = new WebSocketServer({ port: 0 });
    const port = (wss.address() as { port: number }).port;
    wss.on("connection", (ws) => {
      ws.send(JSON.stringify({ type: "layer", id: "cloud", kind: "pointCloud3d", meta: { frame: "map" } }));
      ws.send(encodeLayerMessage("cloud", { positions: Float32Array.from([0, 0, 0, 1, 2, 3]), intensities: Float32Array.from([0, 1]), stamp: 2.5 }));
    });
    const scene = new Scene(), sink = new SceneLayerSink(scene);
    const store = new SignalStore({ retentionSeconds: 10 });
    const ctx: SourceContext = { signals: store, layers: sink, clock: new ManualClock(), log: () => {} };
    const src = new WebSocketFrameSource();
    src.start(ctx, { url: `ws://127.0.0.1:${port}`, worker: false, reconnectMs: 0 });
    try {
      await until(() => src.layerMessagesReceived === 1);
      const layer = scene.get("cloud") as InstanceType<typeof PointCloud3DLayer>;
      expect(layer).toBeInstanceOf(PointCloud3DLayer);
      expect(layer.pointCount).toBe(2);
      expect(layer.mesh.colors?.length).toBe(8);
      expect(layer.stamp).toBe(2.5);
      expect(layer.bounds3()).toEqual({ min: { x: 0, y: 0, z: 0 }, max: { x: 1, y: 2, z: 3 } });
    } finally {
      src.stop();
      wss.close();
    }
  });
});
