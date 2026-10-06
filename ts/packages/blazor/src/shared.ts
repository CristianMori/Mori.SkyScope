// Mori.SkyScope — One socket per URL shared by every chart and scene on the page; released when the last user disposes.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { FanoutLayerSink, LiveClock, McapRecorder, SignalStore, type SourceContext, type TimeSource } from "@mori/skyscope-core";
import { WebSocketFrameSource } from "@mori/skyscope-sources";

/** One socket per URL shared by every chart and scene on the page; released when the last user disposes. */
interface Shared { store: SignalStore; source: WebSocketFrameSource; layers: FanoutLayerSink; recorder: McapRecorder; refs: number }
const shared = new Map<string, Shared>();

/** Playback-backed stores, addressed by charts as `playback:<key>`; the clock indirection lets a chart mount before its Playback component. */
interface PlaybackEntry { store: SignalStore; layers: FanoutLayerSink; ref: { clock: TimeSource | null }; clock: TimeSource }
const playbacks = new Map<string, PlaybackEntry>();
function playbackEntry(key: string): PlaybackEntry {
  let e = playbacks.get(key);
  if (!e) { const ref: { clock: TimeSource | null } = { clock: null }; e = { store: new SignalStore({ retentionSeconds: 3600 }), layers: new FanoutLayerSink(), ref, clock: { now: () => ref.clock?.now() ?? 0 } }; playbacks.set(key, e); }
  return e;
}
/** Binds a playback source's clock to the `playback:<key>` store (creating the store on first use) and returns the sinks the source should write to. */
export function registerPlayback(key: string, clock: TimeSource): { store: SignalStore; layers: FanoutLayerSink } { const e = playbackEntry(key); e.ref.clock = clock; return { store: e.store, layers: e.layers }; }
/** Detaches the clock of a playback key; bound charts then read time 0 until another playback registers. The store is kept. */
export function unregisterPlayback(key: string): void { const e = playbacks.get(key); if (e) e.ref.clock = null; }

/**
 * Resolves what a mount binds to. Undefined `wsUrl` gives a private empty store; `playback:<key>` gives the shared
 * playback store and its clock; a socket URL opens (or reuses) one connection per URL with reference counting, and
 * `release` closes it when the last user disposes. `retention` (seconds) applies only when this call creates the store.
 */
export function acquire(wsUrl: string | undefined, retention: number): { store: SignalStore; source: WebSocketFrameSource | null; layers: FanoutLayerSink; recorder: McapRecorder; clock: TimeSource; release: () => void } {
  if (!wsUrl) { const store = new SignalStore({ retentionSeconds: retention }), layers = new FanoutLayerSink(); return { store, source: null, layers, recorder: new McapRecorder({ signals: store, layers }), clock: new LiveClock(), release: () => {} }; }
  if (wsUrl.startsWith("playback:")) { const e = playbackEntry(wsUrl.slice("playback:".length)); return { store: e.store, source: null, layers: e.layers, recorder: new McapRecorder({ signals: e.store, layers: e.layers }, e.clock), clock: e.clock, release: () => {} }; }
  let entry = shared.get(wsUrl);
  if (!entry) {
    const store = new SignalStore({ retentionSeconds: retention });
    const layers = new FanoutLayerSink();
    const source = new WebSocketFrameSource();
    // The recorder sits between the socket and the page's sinks; it costs nothing while not recording.
    const recorder = new McapRecorder({ signals: store, layers });
    const ctx: SourceContext = { signals: recorder, layers: recorder, clock: new LiveClock(), log: (l, m) => { if (l !== "info") console.warn(`[skyscope] ${m}`); } };
    source.start(ctx, { url: wsUrl });
    entry = { store, source, layers, recorder, refs: 0 };
    shared.set(wsUrl, entry);
  }
  entry.refs++;
  const e = entry;
  return { store: e.store, source: e.source, layers: e.layers, recorder: e.recorder, clock: new LiveClock(), release: () => { if (--e.refs <= 0) { e.source.stop(); shared.delete(wsUrl); } } };
}
