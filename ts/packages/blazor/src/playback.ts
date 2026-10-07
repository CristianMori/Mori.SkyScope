// Mori.SkyScope — Browser-side playback for Blazor: a CSV or MCAP recording drives a PlaybackSource against a store and a layer fan-out registered under pl…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { PlaybackSource, parseCsv, readRecording, type CsvOptions, type Recording, type SourceContext } from "@cmori/skyscope-core";
import { pickFile } from "@cmori/skyscope-render";
import { registerPlayback, unregisterPlayback } from "./shared.js";

/**
 * Browser-side playback for Blazor: a CSV or MCAP recording drives a PlaybackSource against a store and a layer fan-out
 * registered under `playback:<key>`; charts and scenes on the page bind to them by passing that key as their `WsUrl`.
 */
/** What `mountPlayback` returns to .NET; every method is invoked through JS interop. */
export interface PlaybackHandle {
  /** Transport: start, stop, or flip between the two. */
  play(): void; pause(): void; toggle(): void;
  /** Seek to a fraction 0..1 of the recording. */
  seekProgress(p: number): void;
  /** Rate multiplier (1 = real time) and whether playback restarts at the end. */
  setSpeed(s: number): void; setLoop(loop: boolean): void;
  /** Let the user pick a .mcap or .csv file and load it. Resolves to the file name or null. */
  openFile(): Promise<string | null>;
  /** Load an MCAP file by URL (fetched by the browser). */
  loadUrl(url: string): Promise<void>;
  /** Transport snapshot: `position`/`duration` in seconds, `progress` 0..1, `loaded` false until a recording is in. Polled by .NET. */
  state(): { playing: boolean; position: number; duration: number; progress: number; speed: number; loop: boolean; finished: boolean; loaded: boolean };
  /** Stops the tick timer and the source and detaches the clock from the `playback:<key>` entry (its store stays for bound charts). */
  dispose(): void;
}

/**
 * Creates the playback for `key`. `csvText` (with optional CSV options JSON) loads a recording immediately; `mcapUrl`
 * fetches one asynchronously; `autoplay` starts as soon as a recording is loaded. A 50 ms timer ticks the source.
 */
export function mountPlayback(key: string, csvText: string | null, csvOptionsJson: string | null, autoplay: boolean, mcapUrl: string | null): PlaybackHandle {
  const source = new PlaybackSource();
  const { store, layers } = registerPlayback(key, source.clock);
  const ctx: SourceContext = { signals: store, layers, clock: source.clock, log: () => {} };
  let loaded = false;
  const load = (recording: Recording): void => { store.reset(); layers.reset(); source.start(ctx, { recording, autoplay }); loaded = true; };
  if (csvText) load(parseCsv(csvText, csvOptionsJson ? (JSON.parse(csvOptionsJson) as CsvOptions) : {}));
  let last = performance.now();
  const timer = setInterval(() => { const now = performance.now(); source.tick((now - last) / 1000); last = now; }, 50);
  const handle: PlaybackHandle = {
    play: () => source.play(), pause: () => source.pause(), toggle: () => { if (source.playing) source.pause(); else source.play(); },
    seekProgress: (p) => { const r = source.recording; if (r) source.seek(r.start + p * source.duration); },
    setSpeed: (s) => { source.speed = s; }, setLoop: (loop) => { source.loop = loop; },
    async openFile() {
      const file = await pickFile(".mcap,.csv");
      if (!file) return null;
      if (file.name.toLowerCase().endsWith(".csv")) load(parseCsv(await file.text())); else load(readRecording(new Uint8Array(await file.arrayBuffer())));
      return file.name;
    },
    async loadUrl(url) { const res = await fetch(url); load(readRecording(new Uint8Array(await res.arrayBuffer()))); },
    state: () => ({ playing: source.playing, position: source.position, duration: source.duration, progress: source.progress, speed: source.speed, loop: source.loop, finished: source.finished, loaded }),
    dispose: () => { clearInterval(timer); source.stop(); unregisterPlayback(key); },
  };
  if (mcapUrl) void handle.loadUrl(mcapUrl);
  return handle;
}
