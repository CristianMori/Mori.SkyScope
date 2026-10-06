// Mori.SkyScope — Source-plugin contracts.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { SkyScopeFrame } from "../streaming/frame.js";

/**
 * Source-plugin contracts. Everything that produces data is a `Source`; the core never knows a
 * protocol. Mirrors `Mori.SkyScope.Core.Sources`. A source pushes into two sinks: signals (samples →
 * ring buffers) and layers (messages → scene layers), and reads chart time from `clock`.
 */
/**
 * What a channel's values represent: continuous analog, boolean digital, or an enumerated state; drives how charts
 * render it.
 */
export type ChannelKind = "analog" | "digital" | "state";
/** Whether samples carry their own timestamps or follow a regular rate. */
export type ChannelTiming = "regular" | "timestamped";

/** Channel declaration a source sends before pushing samples; re-declaring updates the fields. */
export interface ChannelInfo {
  /** Stable numeric channel id used in frames. */
  id: number;
  /** Display name, e.g. "robot/battery/voltage"; the signal tree splits it into groups. */
  name: string;
  /** Unit label, e.g. "V". */
  unit?: string | undefined;
  /** Default analog. */
  kind?: ChannelKind | undefined;
  /** Regular channels store no per-sample time (t0 + i·dt); timestamped ones do. Default regular. */
  timing?: ChannelTiming | undefined;
  /** Samples per second, when known. Sizes the ring buffer from the retention window. */
  rate?: number | undefined;
  /** Preferred series colour (CSS), when the source has one. */
  color?: string | undefined;
}

/** Where a source pushes samples. Implemented by `SignalStore` and the MCAP recorder. */
export interface SignalSink {
  /** Announce or update a channel; should precede frames for it (stores auto-declare otherwise). */
  declareChannel(info: ChannelInfo): void;
  /** Append one batch of samples for one or more channels. */
  pushFrame(frame: SkyScopeFrame): void;
  /** Drop all samples (channels stay declared) — playback sources call this when seeking backwards. */
  reset?(): void;
}

/**
 * Where a source pushes scene-layer messages. Implemented by `SceneLayerSink`, `FanoutLayerSink` and `NullLayerSink`.
 */
export interface LayerSink {
  /** Create or replace the layer `id` of `kind` with declaration metadata (see `createLayer`). */
  declareLayer(id: string, kind: string, meta?: Record<string, unknown>): void;
  /** Update a declared layer with a kind-specific payload (see `applyLayerPayload`). */
  push(id: string, payload: unknown): void;
  /** Forget every layer received so far — playback calls this when seeking backwards before replaying. */
  reset?(): void;
}

/** Chart time in seconds. Live sources use the wall clock; playback sources drive their own. */
export interface TimeSource { now(): number }

/** Wall-clock time in seconds since the Unix epoch. */
export class LiveClock implements TimeSource {
  /** `Date.now()` in seconds. */
  now(): number { return Date.now() / 1000; }
}

/** A clock advanced explicitly; used by playback, recording and the fixtures. */
export class ManualClock implements TimeSource {
  /** Starts at `t` seconds (default 0). */
  constructor(private t = 0) {}
  /** The current manual time in seconds. */
  now(): number { return this.t; }
  /** Jump to an absolute time in seconds. */
  set(t: number): void { this.t = t; }
  /** Move forward by `dt` seconds. */
  advance(dt: number): void { this.t += dt; }
}

/** Severity of a source log message. */
export type LogLevel = "info" | "warn" | "error";
/** Logging callback handed to sources; the core never writes to the console itself. */
export type Log = (level: LogLevel, message: string) => void;

/** Everything a source needs from its host, passed to `start`. */
export interface SourceContext {
  /** Sink for samples. */
  signals: SignalSink;
  /** Sink for layer messages. */
  layers: LayerSink;
  /** Chart time, for stamping samples that carry none. */
  clock: TimeSource;
  /** Host logger. */
  log: Log;
}

/** What a source can do; UIs use it to offer the right controls (e.g. a scrubber for playback). */
export type SourceCapability = "signals" | "layers" | "playback";

/** A running data producer with a start/stop lifecycle. */
export interface Source {
  /** Source type name, matching its factory. */
  readonly type: string;
  /** Begin producing into `ctx`; `config` is the source-specific configuration (see `SourceFactory.configSchema`). */
  start(ctx: SourceContext, config: unknown): void | Promise<void>;
  /** Stop producing and release resources; the instance is not reused afterwards. */
  stop(): void | Promise<void>;
}

/** Registered description of a source type; `create` makes a fresh instance. */
export interface SourceFactory {
  /** Unique type name, e.g. "playback". */
  readonly type: string;
  /** Human-readable name for pickers. */
  readonly displayName: string;
  /** What sources of this type can do. */
  readonly capabilities: readonly SourceCapability[];
  /** JSON Schema for the config object, for editors/UI. */
  readonly configSchema?: unknown;
  /** A new, not yet started source. */
  create(): Source;
}

/** Source factories by type name; hosts register plugins here and create sources by name. */
export class SourceRegistry {
  private readonly factories = new Map<string, SourceFactory>();

  /** Add a factory; throws when the type is already registered. Returns the registry for chaining. */
  register(factory: SourceFactory): this {
    if (this.factories.has(factory.type)) throw new Error(`source type "${factory.type}" already registered`);
    this.factories.set(factory.type, factory);
    return this;
  }
  /** Every registered factory, in registration order. */
  list(): SourceFactory[] { return [...this.factories.values()]; }
  /** The factory for a type, or undefined. */
  get(type: string): SourceFactory | undefined { return this.factories.get(type); }
  /** Instantiate a source of the given type; throws when the type is unknown. */
  create(type: string): Source {
    const f = this.factories.get(type);
    if (!f) throw new Error(`unknown source type "${type}"`);
    return f.create();
  }
}

/** A LayerSink that discards everything — for tests and for sources with no scene attached. */
export class NullLayerSink implements LayerSink {
  /** Ignored. */
  declareLayer(): void {}
  /** Ignored. */
  push(): void {}
}
