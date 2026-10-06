// Mori.SkyScope — Joins class names, skipping falsy entries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/** Joins class names with a space, skipping false, null, undefined and empty entries. */
export function cx(...parts: Array<string | false | null | undefined>): string { return parts.filter(Boolean).join(" "); }
