// Mori.SkyScope — WebSocket frame source: binary frames into a store, catalog and layer messages relayed, optional Web Worker, reconnect.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { decodeFrame, decodeLayerMessage, isLayerMessage, type ChannelInfo, type Source, type SourceContext, type SourceFactory } from "@mori/skyscope-core";

/** Configuration of `WebSocketFrameSource`. */
export interface WebSocketSourceConfig {
  /** Socket URL of a SkyScope stream (`ws://` or `wss://`). */
  url: string;
  /** Subprotocol(s) offered in the handshake, passed through to the WebSocket constructor. */
  protocols?: string | string[] | undefined;
  /** Reconnect delay after a drop; 0 disables. */
  reconnectMs?: number | undefined;
  /**
   * Run the socket in a Web Worker so receive/batching never waits on a busy main thread. Frames are
   * transferred (zero-copy) and decoded on the main thread, where decoding is a few microseconds.
   */
  worker?: boolean | undefined;
}

/** Text control messages the server may send alongside binary frames. */
export type ControlMessage =
  | { type: "channels"; channels: ChannelInfo[] }
  | { type: "hello"; name?: string; version?: number }
  | { type: "layer"; id: string; kind: string; meta?: Record<string, unknown> }
  | { type: "push"; id: string; payload: unknown };

const WORKER_SOURCE = `
let ws = null, url = "", protocols = undefined, reconnectMs = 1000, closed = false, pending = [], flush = null;
function connect() {
  ws = new WebSocket(url, protocols);
  ws.binaryType = "arraybuffer";
  ws.onopen = () => postMessage({ type: "open" });
  ws.onmessage = (e) => {
    if (typeof e.data === "string") { postMessage({ type: "text", data: e.data }); return; }
    pending.push(e.data);
    if (flush === null) flush = setTimeout(() => { flush = null; const b = pending; pending = []; postMessage({ type: "frames", buffers: b }, b); }, 0);
  };
  ws.onerror = () => postMessage({ type: "error" });
  ws.onclose = () => { postMessage({ type: "close" }); ws = null; if (!closed && reconnectMs > 0) setTimeout(connect, reconnectMs); };
}
onmessage = (e) => {
  const m = e.data;
  if (m.type === "start") { url = m.url; protocols = m.protocols; reconnectMs = m.reconnectMs; closed = false; connect(); }
  else if (m.type === "stop") { closed = true; if (ws) ws.close(); }
};`;

/** Binary SkyScopeFrames over a WebSocket, straight into the signal sink. Reconnects. */
export class WebSocketFrameSource implements Source {
  /** Registry type id of this source. */
  readonly type = "websocket";
  private ws: WebSocket | null = null;
  private worker: Worker | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;
  private stopped = false;
  private ctx: SourceContext | null = null;
  /** Count of binary signal frames decoded and pushed since construction (bad frames are not counted). */
  framesReceived = 0;
  /** Count of binary layer messages decoded and pushed since construction. */
  layerMessagesReceived = 0;
  /** Total bytes of binary messages received, including rejected ones. */
  bytesReceived = 0;
  /** True while the socket is open; false after a close until the reconnect succeeds. */
  connected = false;

  /** Opens the socket, in a Web Worker when `config.worker` is true or unset and workers are available, otherwise on the main thread. */
  start(ctx: SourceContext, config: WebSocketSourceConfig): void {
    this.ctx = ctx; this.stopped = false;
    const useWorker = config.worker ?? (typeof Worker !== "undefined" && typeof Blob !== "undefined" && typeof URL !== "undefined" && "createObjectURL" in URL);
    if (useWorker) this.startWorker(config); else this.connect(config);
  }

  private handleText(data: string): void {
    let msg: ControlMessage;
    try { msg = JSON.parse(data) as ControlMessage; } catch { this.ctx?.log("warn", "websocket: unparseable text message"); return; }
    if (msg.type === "channels") for (const c of msg.channels) this.ctx!.signals.declareChannel(c);
    else if (msg.type === "layer") this.ctx!.layers.declareLayer(msg.id, msg.kind, msg.meta);
    else if (msg.type === "push") this.ctx!.layers.push(msg.id, msg.payload);
  }

  private handleFrame(buffer: ArrayBuffer): void {
    this.bytesReceived += buffer.byteLength;
    if (isLayerMessage(new Uint8Array(buffer))) {
      try { const m = decodeLayerMessage(buffer); this.ctx!.layers.push(m.id, m.payload); this.layerMessagesReceived++; }
      catch (e) { this.ctx?.log("warn", `websocket: bad layer message (${e instanceof Error ? e.message : String(e)})`); }
      return;
    }
    try { this.ctx!.signals.pushFrame(decodeFrame(buffer)); this.framesReceived++; }
    catch (e) { this.ctx?.log("warn", `websocket: bad frame (${e instanceof Error ? e.message : String(e)})`); }
  }

  private connect(config: WebSocketSourceConfig): void {
    const ws = new WebSocket(config.url, config.protocols);
    ws.binaryType = "arraybuffer";
    ws.onopen = () => { this.connected = true; this.ctx?.log("info", `websocket: connected ${config.url}`); };
    ws.onmessage = (e: MessageEvent) => { if (typeof e.data === "string") this.handleText(e.data); else this.handleFrame(e.data as ArrayBuffer); };
    ws.onerror = () => this.ctx?.log("warn", "websocket: error");
    ws.onclose = () => {
      this.connected = false; this.ws = null;
      const delay = config.reconnectMs ?? 1000;
      if (!this.stopped && delay > 0) this.timer = setTimeout(() => this.connect(config), delay);
    };
    this.ws = ws;
  }

  private startWorker(config: WebSocketSourceConfig): void {
    const blob = new Blob([WORKER_SOURCE], { type: "text/javascript" });
    const w = new Worker(URL.createObjectURL(blob));
    w.onmessage = (e: MessageEvent) => {
      const m = e.data as { type: string; data?: string; buffers?: ArrayBuffer[] };
      switch (m.type) {
        case "open": this.connected = true; this.ctx?.log("info", `websocket(worker): connected ${config.url}`); break;
        case "close": this.connected = false; break;
        case "error": this.ctx?.log("warn", "websocket(worker): error"); break;
        case "text": this.handleText(m.data!); break;
        case "frames": for (const b of m.buffers!) this.handleFrame(b); break;
      }
    };
    w.postMessage({ type: "start", url: config.url, protocols: config.protocols, reconnectMs: config.reconnectMs ?? 1000 });
    this.worker = w;
  }

  /** Closes the socket (or terminates the worker) and cancels any pending reconnect; no close callback fires afterwards. */
  stop(): void {
    this.stopped = true;
    if (this.timer !== null) { clearTimeout(this.timer); this.timer = null; }
    if (this.ws) { this.ws.onclose = null; this.ws.close(); this.ws = null; }
    if (this.worker) { this.worker.postMessage({ type: "stop" }); this.worker.terminate(); this.worker = null; }
    this.connected = false;
  }
}

/** Registry entry for `WebSocketFrameSource`, with a JSON schema for its configuration. */
export const webSocketSourceFactory: SourceFactory = {
  type: "websocket",
  displayName: "SkyScope stream (WebSocket)",
  capabilities: ["signals"],
  configSchema: { type: "object", required: ["url"], properties: { url: { type: "string", format: "uri" }, reconnectMs: { type: "integer", default: 1000 }, worker: { type: "boolean" } } },
  create: () => new WebSocketFrameSource(),
};
