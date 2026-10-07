// Mori.SkyScope — Records the shared SkyScope stream (frames, catalog, relayed layers) of wsUrl to an MCAP file in the browser.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { saveFile, recordingFileName } from "@cmori/skyscope-render";
import { acquire } from "./shared.js";

/**
 * Records the shared SkyScope stream (frames, catalog, relayed layers) of `wsUrl` to an MCAP file in the browser.
 * The recorder sits between the socket and every chart/scene on the page, so what is recorded is exactly what was shown.
 */
/** What `mountRecorder` returns to .NET; every method is invoked through JS interop. */
export interface RecorderHandle {
  /** Starts recording into memory; a second call while recording is ignored by the recorder. */
  start(): void;
  /** Stops and downloads the file; returns its size in bytes (0 when nothing was recording). */
  stopAndSave(fileName: string | null): number;
  /** Progress counters: whether recording, MCAP messages written, bytes so far and elapsed seconds. */
  stats(): { recording: boolean; messages: number; bytes: number; duration: number };
  /** Releases the shared socket reference; does not stop a recording in progress. */
  dispose(): void;
}

/** Binds to the shared stream of `wsUrl` (acquiring it if needed); `maxBytes` > 0 caps the in-memory recording size. */
export function mountRecorder(wsUrl: string, maxBytes: number): RecorderHandle {
  const shared = acquire(wsUrl, 60);
  const rec = shared.recorder;
  if (maxBytes > 0) rec.maxBytes = maxBytes;
  return {
    start: () => rec.start(),
    stopAndSave: (fileName) => { const bytes = rec.stop(); if (!bytes) return 0; saveFile(bytes, fileName ?? recordingFileName()); return bytes.length; },
    stats: () => { const s = rec.stats(); return { recording: s.recording, messages: s.messages, bytes: s.bytes, duration: s.duration }; },
    dispose: () => shared.release(),
  };
}
