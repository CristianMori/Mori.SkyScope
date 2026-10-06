// Mori.SkyScope — Drives a PlaybackSource against store for the component's lifetime: a 50 ms timer ticks the manual clock and pushes frames; the returned …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import { McapRecorder, NullLayerSink, PlaybackSource, SignalStore, formatNumber, type LayerSink, type Recording, type RecorderStats, type SourceContext } from "@mori/skyscope-core";
import { createFileSink, recordingFileName, saveFile, type FileSink } from "@mori/skyscope-render";
import { Button } from "./Button.js";
import { cx } from "./cx.js";

/**
 * Snapshot of the transport. `position` is the recording time in seconds (same epoch as the recording), `duration`
 * the recording length in seconds, `progress` the fraction 0..1 played, `speed` the rate multiplier (1 = real time),
 * `loop` whether playback restarts at the end and `finished` whether it reached the end without looping.
 */
export interface PlaybackState { playing: boolean; position: number; duration: number; progress: number; speed: number; loop: boolean; finished: boolean }
/**
 * What `usePlayback` returns: the underlying `source` (its `clock` drives charts), the latest `state`, and the transport
 * commands. `seek` takes an absolute recording time in seconds, `seekProgress` a fraction 0..1; every command re-renders.
 */
export interface Playback { source: PlaybackSource; state: PlaybackState; play(): void; pause(): void; toggle(): void; seek(t: number): void; seekProgress(p: number): void; setSpeed(s: number): void; setLoop(loop: boolean): void }

/**
 * Drives a PlaybackSource against `store` for the component's lifetime: a 50 ms timer ticks the manual clock and pushes
 * frames; the returned state re-renders about 10× per second. Pass `playback.source.clock` to the TrendChart so its live
 * window follows the recording.
 */
export function usePlayback(store: SignalStore, recording: Recording | null, options: { speed?: number; loop?: boolean; autoplay?: boolean } = {}, layers?: LayerSink): Playback {
  const source = useMemo(() => new PlaybackSource(), []);
  const [state, setState] = useState<PlaybackState>({ playing: false, position: 0, duration: 0, progress: 0, speed: options.speed ?? 1, loop: options.loop ?? false, finished: false });
  const snapshot = (): PlaybackState => ({ playing: source.playing, position: source.position, duration: source.duration, progress: source.progress, speed: source.speed, loop: source.loop, finished: source.finished });
  const opts = useRef(options); opts.current = options;
  useEffect(() => {
    if (!recording) return;
    store.reset(); layers?.reset?.();
    const ctx: SourceContext = { signals: store, layers: layers ?? new NullLayerSink(), clock: source.clock, log: () => {} };
    source.start(ctx, { recording, speed: opts.current.speed, loop: opts.current.loop, autoplay: opts.current.autoplay });
    let last = performance.now(), n = 0;
    const timer = setInterval(() => {
      const now = performance.now();
      source.tick((now - last) / 1000); last = now;
      if (++n % 2 === 0 || !source.playing) setState(snapshot());
    }, 50);
    setState(snapshot());
    return () => { clearInterval(timer); source.stop(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [store, recording, source, layers]);
  const refresh = () => setState(snapshot());
  return {
    source, state,
    play: () => { source.play(); refresh(); }, pause: () => { source.pause(); refresh(); }, toggle: () => { if (source.playing) source.pause(); else source.play(); refresh(); },
    seek: (t) => { source.seek(t); refresh(); }, seekProgress: (p) => { const r = source.recording; if (r) { source.seek(r.start + p * source.duration); refresh(); } },
    setSpeed: (s) => { source.speed = s; refresh(); }, setLoop: (loop) => { source.loop = loop; refresh(); },
  };
}

const fmt = (t: number): string => { const m = Math.floor(t / 60), s = t - m * 60; return `${m}:${s < 10 ? "0" : ""}${formatNumber(s, 2)}`; };

/** Props of `PlaybackControls`: the `playback` from `usePlayback`, the `speeds` offered as buttons (default 0.25..10), and `className`/`style` for the bar. */
export interface PlaybackControlsProps { playback: Playback; speeds?: number[] | undefined; className?: string | undefined; style?: CSSProperties | undefined }

/** Transport bar: play/pause, a scrubber, elapsed / total, speed buttons and loop. */
export function PlaybackControls({ playback, speeds = [0.25, 0.5, 1, 2, 5, 10], className, style }: PlaybackControlsProps) {
  const { state } = playback;
  return (
    <div className={cx("skyscope-playback", className)} style={{ display: "flex", alignItems: "center", gap: 8, ...style }}>
      <Button size="sm" variant="primary" onClick={playback.toggle}>{state.playing ? "⏸" : "▶"}</Button>
      <Button size="sm" onClick={() => playback.seekProgress(0)}>⏮</Button>
      <input type="range" min={0} max={1000} value={Math.round(state.progress * 1000)} onChange={(e) => playback.seekProgress(Number(e.target.value) / 1000)} style={{ flex: 1, minWidth: 120 }} aria-label="position" />
      <span style={{ fontVariantNumeric: "tabular-nums", fontSize: 12, minWidth: 100, textAlign: "right" }}>{fmt(state.progress * state.duration)} / {fmt(state.duration)}</span>
      {speeds.map((s) => <Button key={s} size="sm" active={state.speed === s} onClick={() => playback.setSpeed(s)}>{s}×</Button>)}
      <Button size="sm" active={state.loop} onClick={() => playback.setLoop(!state.loop)}>loop</Button>
    </div>
  );
}

/** Polls a recorder's stats a couple of times per second so a button can show elapsed time and size. */
export function useRecorder(recorder: McapRecorder): RecorderStats {
  const [stats, setStats] = useState<RecorderStats>(() => recorder.stats());
  useEffect(() => { const id = setInterval(() => setStats(recorder.stats()), 500); return () => clearInterval(id); }, [recorder]);
  return stats;
}

/** Props of `RecordButton`. */
export interface RecordButtonProps {
  /** The recorder to start and stop; `fileName` names the download (default `recordingFileName()`). */
  recorder: McapRecorder; fileName?: string | undefined;
  /** Pick the file when recording starts and stream to it (no memory limit where the File System Access API exists). */
  streamToFile?: boolean | undefined;
  /** Called after a stop with the MCAP bytes, or null when they were streamed to disk; `className`/`style` go on the wrapper span. */
  onSaved?: ((bytes: Uint8Array | null) => void) | undefined; className?: string | undefined; style?: CSSProperties | undefined;
}

/** Record / stop; stopping downloads the MCAP file (or closes the streamed one). */
export function RecordButton({ recorder, fileName, streamToFile, onSaved, className, style }: RecordButtonProps) {
  const s = useRecorder(recorder);
  const fileSink = useRef<FileSink | null>(null);
  const toggle = async () => {
    if (!s.recording) {
      if (streamToFile) { try { fileSink.current = await createFileSink(fileName ?? recordingFileName()); } catch { return; } recorder.start(fileSink.current.sink); }
      else recorder.start();
      return;
    }
    const bytes = recorder.stop();
    if (fileSink.current) { await fileSink.current.close(); fileSink.current = null; onSaved?.(null); return; }
    if (bytes) { saveFile(bytes, fileName ?? recordingFileName()); onSaved?.(bytes); }
  };
  const label = s.recording ? `REC ${formatNumber(s.duration, 1)} s · ${s.messages} msgs · ${formatNumber(s.bytes / 1e6, 1)} MB` : s.bytes > 0 ? `last: ${s.messages} msgs · ${formatNumber(s.bytes / 1e6, 1)} MB` : "";
  return (
    <span className={cx("skyscope-recorder", className)} style={{ display: "inline-flex", alignItems: "center", gap: 8, ...style }}>
      <Button size="sm" variant={s.recording ? "primary" : "default"} onClick={() => void toggle()}>{s.recording ? "■ Stop" : "● Record"}</Button>
      <span style={{ fontSize: 12, color: "var(--skyscope-color-text-secondary)", minWidth: 150 }}>{label}</span>
    </span>
  );
}
