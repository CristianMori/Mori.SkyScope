// Mori.SkyScope — Pointer/wheel/keyboard gestures as a pure reducer: (state, event) → { state, effects }.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { rectNormalize, type Rect, type Vec2 } from "./geometry.js";

/**
 * Pointer/wheel/keyboard gestures as a pure reducer: `(state, event) → { state, effects }`.
 * Renderers translate platform events into `InputEvent`s and apply `Effect`s to a camera or to
 * chart scales; nothing here touches a DOM or a window. Pinned by `spec/fixtures/interaction.json`.
 */
/** Keyboard modifiers held during a pointer event; an absent flag means not pressed. */
export interface Modifiers { shift?: boolean | undefined; ctrl?: boolean | undefined; alt?: boolean | undefined }

/** Platform-neutral input event in screen pixels; `button` follows the DOM convention (0 left, 1 middle, 2 right). */
export type InputEvent =
  | { type: "pointerdown"; x: number; y: number; button: number; modifiers?: Modifiers | undefined }
  | { type: "pointermove"; x: number; y: number; modifiers?: Modifiers | undefined }
  | { type: "pointerup"; x: number; y: number; button: number; modifiers?: Modifiers | undefined }
  | { type: "pointercancel" }
  | { type: "wheel"; x: number; y: number; deltaY: number; modifiers?: Modifiers | undefined }
  | { type: "dblclick"; x: number; y: number }
  | { type: "keydown"; key: string }
  | { type: "keyup"; key: string };

/** The user-selected tool; modifiers can override it per gesture (shift → box zoom, middle button / space → pan). */
export type Tool = "pan" | "boxZoom" | "cursor" | "select";
/** Gesture lifecycle: no button down, pressed within the click slop, or dragging. */
export type Phase = "idle" | "pressing" | "dragging";

/** Immutable reducer state; create it with `initialInteraction` and advance it with `reduceInteraction`. */
export interface InteractionState {
  /** The selected tool, used for left-button presses without modifiers. */
  tool: Tool;
  /** Current gesture phase. */
  phase: Phase;
  /** Gesture in progress (may differ from `tool`). */
  gesture: Tool | null;
  /** Screen position of the press that started the gesture, or null when idle. */
  start: Vec2 | null;
  /** Screen position at the previous pointer event, from which drag deltas are computed. */
  last: Vec2 | null;
  /** Button that started the gesture. */
  button: number;
  /** True while the space bar is down (a left drag then pans). */
  spaceHeld: boolean;
}

/**
 * What a reducer step asks the host to do: camera moves (`pan`, `zoom`, `boxZoom`), rubber-band updates, hover, click
 * and drag notifications, and `reset` on double-click. Coordinates and deltas are screen pixels.
 */
export type Effect =
  | { type: "pan"; dx: number; dy: number }
  | { type: "zoom"; x: number; y: number; factor: number }
  | { type: "boxZoomPreview"; rect: Rect }
  | { type: "boxZoom"; rect: Rect }
  | { type: "boxZoomCancel" }
  | { type: "hover"; x: number; y: number }
  | { type: "click"; x: number; y: number; button: number; modifiers: Modifiers }
  | { type: "dragStart"; x: number; y: number }
  | { type: "drag"; x: number; y: number; dx: number; dy: number }
  | { type: "dragEnd"; x: number; y: number }
  | { type: "reset" };

/**
 * Tuning for `reduceInteraction`; the defaults are 3 px slop, zoom base 2 per 400 wheel units and an 8 px minimum
 * box.
 */
export interface InteractionOptions {
  /** Pixels of movement before a press becomes a drag. */
  clickSlop?: number;
  /** Wheel: factor = base ^ (−deltaY / divisor). */
  wheelZoomBase?: number;
  /** Wheel delta units per factor of `wheelZoomBase`. */
  wheelZoomDivisor?: number;
  /** Box-zoom rectangles smaller than this (either side) are cancelled. */
  minBoxZoom?: number;
}

const DEFAULTS: Required<InteractionOptions> = { clickSlop: 3, wheelZoomBase: 2, wheelZoomDivisor: 400, minBoxZoom: 8 };

/** Fresh idle state for `tool` (default pan). */
export function initialInteraction(tool: Tool = "pan"): InteractionState {
  return { tool, phase: "idle", gesture: null, start: null, last: null, button: 0, spaceHeld: false };
}

/**
 * Pure step: applies `ev` to `state` and returns the next state plus the effects to apply, in order. Never mutates
 * its input.
 */
export function reduceInteraction(state: InteractionState, ev: InputEvent, options: InteractionOptions = {}): { state: InteractionState; effects: Effect[] } {
  const o = { ...DEFAULTS, ...options };
  const effects: Effect[] = [];
  let s = state;

  switch (ev.type) {
    case "keydown":
      if (ev.key === " ") s = { ...s, spaceHeld: true };
      else if (ev.key === "Escape" && s.phase !== "idle") { if (s.gesture === "boxZoom" && s.phase === "dragging") effects.push({ type: "boxZoomCancel" }); s = idle(s); }
      break;
    case "keyup":
      if (ev.key === " ") s = { ...s, spaceHeld: false };
      break;
    case "pointerdown": {
      if (s.phase !== "idle") break;
      const m = ev.modifiers ?? {};
      const gesture: Tool | null = ev.button === 1 || (ev.button === 0 && s.spaceHeld) ? "pan" : ev.button === 0 ? (m.shift ? "boxZoom" : s.tool) : null;
      s = { ...s, phase: "pressing", gesture, start: { x: ev.x, y: ev.y }, last: { x: ev.x, y: ev.y }, button: ev.button };
      break;
    }
    case "pointermove": {
      if (s.phase === "idle") { effects.push({ type: "hover", x: ev.x, y: ev.y }); break; }
      const start = s.start!, last = s.last!;
      if (s.phase === "pressing") {
        if (Math.abs(ev.x - start.x) <= o.clickSlop && Math.abs(ev.y - start.y) <= o.clickSlop) break;
        s = { ...s, phase: "dragging" };
        if (s.gesture === "select" || s.gesture === "cursor") effects.push({ type: "dragStart", x: start.x, y: start.y });
      }
      const dx = ev.x - last.x, dy = ev.y - last.y;
      switch (s.gesture) {
        case "pan": effects.push({ type: "pan", dx, dy }); break;
        case "boxZoom": effects.push({ type: "boxZoomPreview", rect: rectNormalize(start.x, start.y, ev.x, ev.y) }); break;
        case "cursor": effects.push({ type: "hover", x: ev.x, y: ev.y }); effects.push({ type: "drag", x: ev.x, y: ev.y, dx, dy }); break;
        case "select": effects.push({ type: "drag", x: ev.x, y: ev.y, dx, dy }); break;
      }
      s = { ...s, last: { x: ev.x, y: ev.y } };
      break;
    }
    case "pointerup": {
      if (s.phase === "idle") break;
      const start = s.start!;
      if (s.phase === "pressing") effects.push({ type: "click", x: ev.x, y: ev.y, button: s.button, modifiers: resolveModifiers(ev.modifiers) });
      else if (s.gesture === "boxZoom") {
        const r = rectNormalize(start.x, start.y, ev.x, ev.y);
        effects.push(r.w >= o.minBoxZoom && r.h >= o.minBoxZoom ? { type: "boxZoom", rect: r } : { type: "boxZoomCancel" });
      } else if (s.gesture === "select" || s.gesture === "cursor") effects.push({ type: "dragEnd", x: ev.x, y: ev.y });
      s = idle(s);
      break;
    }
    case "pointercancel":
      if (s.phase === "dragging" && s.gesture === "boxZoom") effects.push({ type: "boxZoomCancel" });
      s = idle(s);
      break;
    case "wheel": {
      const factor = Math.pow(o.wheelZoomBase, -ev.deltaY / o.wheelZoomDivisor);
      effects.push({ type: "zoom", x: ev.x, y: ev.y, factor });
      break;
    }
    case "dblclick":
      effects.push({ type: "reset" });
      break;
  }
  return { state: s, effects };
}

function idle(s: InteractionState): InteractionState { return { ...s, phase: "idle", gesture: null, start: null, last: null }; }

/** Fill in missing modifier flags as false. */
export function resolveModifiers(m?: Modifiers): Required<{ [K in keyof Modifiers]: boolean }> { return { shift: !!m?.shift, ctrl: !!m?.ctrl, alt: !!m?.alt }; }

import type { Camera2D } from "./camera.js";

/** Apply camera-relevant effects to a `Camera2D`. Returns true when handled. */
export function applyCameraEffect(camera: Camera2D, effect: Effect): boolean {
  switch (effect.type) {
    case "pan": camera.pan(effect.dx, effect.dy); return true;
    case "zoom": camera.zoomAt(effect.x, effect.y, effect.factor); return true;
    case "boxZoom": camera.fitScreenRect(effect.rect); return true;
    default: return false;
  }
}
