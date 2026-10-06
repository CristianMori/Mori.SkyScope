// Mori.SkyScope — Declarative validation rules.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * Declarative validation rules. Semantics are shared with `Mori.SkyScope.Core` (C#) and pinned by
 * `spec/fixtures/field.json`: change one side, and the other side's tests go red.
 */
export type ValidationRule =
  /** Non-blank text after trimming. */
  | { type: "required"; message?: string | undefined }
  /** At least `length` UTF-16 code units. */
  | { type: "minLength"; length: number; message?: string | undefined }
  /** At most `length` UTF-16 code units. */
  | { type: "maxLength"; length: number; message?: string | undefined }
  /** Must match `pattern`, an ECMAScript regular expression tested unanchored. */
  | { type: "pattern"; pattern: string; message?: string | undefined }
  /** Loose `local@domain.tld` shape. */
  | { type: "email"; message?: string | undefined }
  /** Backed by arbitrary code. Not expressible in fixtures; app use only. */
  | { type: "custom"; validate: (value: string) => boolean; message: string };

/** Terse factories: `rules.required()`, `rules.minLength(3)`, … */
export const rules = {
  /** Non-blank text. */
  required: (message?: string): ValidationRule => ({ type: "required", message }),
  /** At least `length` characters. */
  minLength: (length: number, message?: string): ValidationRule => ({ type: "minLength", length, message }),
  /** At most `length` characters. */
  maxLength: (length: number, message?: string): ValidationRule => ({ type: "maxLength", length, message }),
  /** Regular-expression match. */
  pattern: (pattern: string, message?: string): ValidationRule => ({ type: "pattern", pattern, message }),
  /** Loose email shape. */
  email: (message?: string): ValidationRule => ({ type: "email", message }),
  /** Predicate-backed rule; `message` is mandatory because there is no default. */
  custom: (validate: (value: string) => boolean, message: string): ValidationRule => ({ type: "custom", validate, message }),
} as const;

// Deliberately simple and identical to the C# side; RFC-grade email validation belongs on the server.
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

function defaultMessage(rule: ValidationRule): string {
  switch (rule.type) {
    case "required": return "This field is required";
    case "minLength": return `Must be at least ${rule.length} characters`;
    case "maxLength": return `Must be at most ${rule.length} characters`;
    case "pattern": return "Invalid format";
    case "email": return "Invalid email address";
    case "custom": return rule.message;
  }
}

/** The rule's own message, or the built-in English default. */
export function errorMessage(rule: ValidationRule): string {
  return rule.message ?? defaultMessage(rule);
}

/** Only `required` runs against an empty string; every other rule treats "empty" as "nothing to validate". */
function appliesToEmpty(rule: ValidationRule): boolean {
  return rule.type === "required";
}

/** Tests one rule in isolation; unlike `validate` it does not skip empty values. */
export function isRuleValid(rule: ValidationRule, value: string): boolean {
  switch (rule.type) {
    case "required": return value.trim().length > 0;
    case "minLength": return value.length >= rule.length;
    case "maxLength": return value.length <= rule.length;
    case "pattern": return new RegExp(rule.pattern).test(value);
    case "email": return EMAIL.test(value);
    case "custom": return rule.validate(value);
  }
}

/**
 * A failing `required` rule short-circuits (it is the only error reported).
 * Otherwise every failing rule is reported, in rule order. Rules other than required skip empty values.
 */
export function validate(value: string, rules: readonly ValidationRule[] = []): string[] {
  if (rules.length === 0) return [];
  for (const rule of rules) {
    if (appliesToEmpty(rule) && !isRuleValid(rule, value)) return [errorMessage(rule)];
  }
  if (value.length === 0) return [];
  const errors: string[] = [];
  for (const rule of rules) {
    if (!appliesToEmpty(rule) && !isRuleValid(rule, value)) errors.push(errorMessage(rule));
  }
  return errors;
}
