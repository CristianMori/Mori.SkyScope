// Mori.SkyScope — Immutable state of a single text-like form field.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { validate, type ValidationRule } from "./validation.js";

/**
 * Immutable state of a single text-like form field. Renderers own the storage (a `useReducer`,
 * a signal, whatever) and dispatch `FieldAction`s through `reduceField`; nothing here knows about UI.
 */
export interface FieldState {
  /** Current text as typed or set; never null. */
  readonly value: string;
  /** Baseline that `isDirty` compares against and that a reset restores. */
  readonly initialValue: string;
  /** True once focus has left the control or a submit forced it; gates error display. */
  readonly touched: boolean;
  /** Messages of the rules currently failing, in rule order; empty when valid. Recomputed on every change, not only after a blur. */
  readonly errors: readonly string[];
}

/** Transitions a renderer dispatches through `reduceField`. */
export type FieldAction =
  /** The value changed (typing, paste, programmatic set). */
  | { type: "change"; value: string }
  /** Focus left the control; marks the field touched. */
  | { type: "blur" }
  /** Force the field touched without a blur — e.g. a submit attempt. */
  | { type: "touch" }
  /** Return to the initial value (or adopt `value` as the new initial value) and clear touched. */
  | { type: "reset"; value?: string | undefined };

/** Pristine, untouched state whose errors are already evaluated against `rules`. */
export function createField(value = "", rules?: readonly ValidationRule[]): FieldState {
  return { value, initialValue: value, touched: false, errors: validate(value, rules) };
}

/** True when the value differs from the initial value. */
export const isDirty = (s: FieldState): boolean => s.value !== s.initialValue;
/** True when no rule is failing. */
export const isValid = (s: FieldState): boolean => s.errors.length === 0;
/** Errors are computed eagerly but only surfaced once the user has interacted. */
export const shouldShowErrors = (s: FieldState): boolean => s.touched && !isValid(s);

/** Pure reducer: never mutates `state` and returns the same object when nothing changed. Pass the same `rules` on every call. */
export function reduceField(state: FieldState, action: FieldAction, rules?: readonly ValidationRule[]): FieldState {
  switch (action.type) {
    case "change":
      return { ...state, value: action.value, errors: validate(action.value, rules) };
    case "blur":
    case "touch":
      return state.touched ? state : { ...state, touched: true };
    case "reset":
      return createField(action.value ?? state.initialValue, rules);
  }
}

/** Fully-derived view of a field; handy for renderers and what fixtures assert on. */
export function fieldSnapshot(s: FieldState) {
  return {
    value: s.value,
    initialValue: s.initialValue,
    touched: s.touched,
    dirty: isDirty(s),
    valid: isValid(s),
    showErrors: shouldShowErrors(s),
    errors: [...s.errors],
  };
}
