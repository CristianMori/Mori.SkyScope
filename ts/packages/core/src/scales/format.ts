// Mori.SkyScope — Deterministic tick labels shared with the C# side (UTC, no locale).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { formatNumber } from "./ticks.js";

/** "utc": wall-clock labels from unix seconds; "relative": offset in seconds from an origin, shown as s, m:ss or h:mm:ss. */
export type TimeFormat = "utc" | "relative";

const pad = (n: number, w: number): string => String(n).padStart(w, "0");

/**
 * Deterministic tick labels shared with the C# side (UTC, no locale).
 * `step` sets the precision; `span` (the axis extent) sets the style, so every label on one axis
 * uses the same shape (relative: s → m:ss → h:mm:ss).
 */
export function formatTimeTick(t: number, step: number, mode: TimeFormat, decimals = 0, span = step): string {
  if (mode === "relative") {
    const neg = t < 0; const a = Math.abs(t);
    const secs = (s: number): string => (s < 10 ? "0" : "") + formatNumber(s, decimals);
    if (span >= 3600) { const h = Math.floor(a / 3600), m = Math.floor((a - h * 3600) / 60); return `${neg ? "-" : ""}${h}:${pad(m, 2)}:${secs(a - h * 3600 - m * 60)}`; }
    if (span >= 60) { const m = Math.floor(a / 60); return `${neg ? "-" : ""}${m}:${secs(a - m * 60)}`; }
    return formatNumber(t, decimals);
  }
  const ms = Math.floor(t * 1000 + 0.5);
  const d = new Date(ms);
  const HH = pad(d.getUTCHours(), 2), mm = pad(d.getUTCMinutes(), 2), ss = pad(d.getUTCSeconds(), 2);
  if (step < 1) {
    const frac = pad(ms - Math.floor(ms / 1000) * 1000, 3).slice(0, Math.max(1, decimals));
    return `${HH}:${mm}:${ss}.${frac}`;
  }
  if (step < 60) return `${HH}:${mm}:${ss}`;
  if (step < 86400) return `${HH}:${mm}`;
  return `${d.getUTCFullYear()}-${pad(d.getUTCMonth() + 1, 2)}-${pad(d.getUTCDate(), 2)}`;
}
