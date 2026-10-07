// Mori.SkyScope — React hooks for a signal store, a generic source and the WebSocket source.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useMemo, useState } from "react";
import { LiveClock, NullLayerSink, SignalStore, type LayerSink, type SignalSink, type SignalStoreOptions, type Source, type SourceContext } from "@cmori/skyscope-core";
import { WebSocketFrameSource, type WebSocketSourceConfig } from "@cmori/skyscope-sources";

/** A SignalStore that lives as long as the component. */
export function useSignalStore(options?: SignalStoreOptions): SignalStore {
  return useMemo(() => new SignalStore(options), []); // eslint-disable-line react-hooks/exhaustive-deps
}

/** Run any source plugin against a store for the component's lifetime. */
export function useSource<C>(store: SignalSink, factory: () => Source, config: C, deps: unknown[] = [], layers?: LayerSink, enabled = true): { connected: boolean } {
  const [connected, setConnected] = useState(false);
  useEffect(() => {
    if (!enabled) return;
    const src = factory();
    const ctx: SourceContext = { signals: store, layers: layers ?? new NullLayerSink(), clock: new LiveClock(), log: (level, msg) => { if (level !== "info") console.warn(`[skyscope] ${msg}`); else setConnected(true); } };
    void src.start(ctx, config);
    return () => { void src.stop(); setConnected(false); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [store, layers, enabled, ...deps]);
  return { connected };
}

/** Stream SkyScopeFrames from a WebSocket into the store. */
export function useWebSocketSource(store: SignalSink, config: WebSocketSourceConfig, layers?: LayerSink, enabled = true): { connected: boolean } {
  return useSource(store, () => new WebSocketFrameSource(), config, [config.url], layers, enabled);
}
