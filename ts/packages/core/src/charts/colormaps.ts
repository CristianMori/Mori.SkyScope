// Mori.SkyScope — Colour maps for heatmaps/spectrograms: piecewise-linear RGB interpolation between hex stops.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/**
 * Colour maps for heatmaps/spectrograms: piecewise-linear RGB interpolation between hex stops.
 * Mirrors `Mori.SkyScope.Core.Charts.Colormaps`; pinned by `spec/fixtures/heatmap.json`.
 */
export type ColormapName = "viridis" | "inferno" | "plasma" | "turbo" | "jet" | "grayscale" | "coolwarm";

/** Built-in colour maps as ordered hex stops from the low end (t = 0) to the high end (t = 1). */
export const COLORMAPS: Record<ColormapName, string[]> = {
  viridis: ["#440154", "#482878", "#3e4989", "#31688e", "#26828e", "#1f9e89", "#35b779", "#6ece58", "#b5de2b", "#fde725"],
  inferno: ["#000004", "#1b0c41", "#4a0c6b", "#781c6d", "#a52c60", "#cf4446", "#ed6925", "#fb9b06", "#f7d13d", "#fcffa4"],
  plasma: ["#0d0887", "#41049d", "#6a00a8", "#8f0da4", "#b12a90", "#cc4778", "#e16462", "#f2844b", "#fca636", "#fcce25", "#f0f921"],
  turbo: ["#30123b", "#4662d7", "#36aaf9", "#1ae4b6", "#72fe5e", "#c8ef34", "#faba39", "#f66b19", "#ca2a04", "#7a0403"],
  jet: ["#00007f", "#0000ff", "#007fff", "#00ffff", "#7fff7f", "#ffff00", "#ff7f00", "#ff0000", "#7f0000"],
  grayscale: ["#000000", "#ffffff"],
  coolwarm: ["#3b4cc0", "#8db0fe", "#dddddd", "#f49a7b", "#b40426"],
};

/** Resolves a built-in map name or an explicit stop list to the stop list; a custom array is returned as given, not copied. */
export function colormapStops(map: ColormapName | string[]): string[] { return Array.isArray(map) ? map : COLORMAPS[map]; }

/** Parses a "#rgb" or "#rrggbb" colour (the hash is optional) into 0–255 red, green and blue components. */
export function parseHex(c: string): [number, number, number] {
  const h = c.startsWith("#") ? c.slice(1) : c;
  const s = h.length === 3 ? h.split("").map((ch) => ch + ch).join("") : h;
  return [parseInt(s.slice(0, 2), 16), parseInt(s.slice(2, 4), 16), parseInt(s.slice(4, 6), 16)];
}
const hex2 = (v: number): string => (v < 16 ? "0" : "") + v.toString(16);
/** Formats 0–255 red, green and blue components as a lowercase "#rrggbb" string. */
export function toHex(r: number, g: number, b: number): string { return "#" + hex2(r) + hex2(g) + hex2(b); }

const roundHalfUp = (x: number): number => Math.floor(x + 0.5);

/** RGB for `t` in [0, 1] (clamped); NaN maps to the first stop. */
export function colormapRgb(stops: string[], t: number): [number, number, number] {
  const n = stops.length;
  if (n === 1) return parseHex(stops[0]!);
  const u = Number.isNaN(t) ? 0 : Math.min(1, Math.max(0, t));
  const seg = u * (n - 1);
  let i = Math.floor(seg);
  if (i > n - 2) i = n - 2;
  const f = seg - i;
  const a = parseHex(stops[i]!), b = parseHex(stops[i + 1]!);
  return [roundHalfUp(a[0] + (b[0] - a[0]) * f), roundHalfUp(a[1] + (b[1] - a[1]) * f), roundHalfUp(a[2] + (b[2] - a[2]) * f)];
}
/** Hex colour for `t` in [0, 1] (clamped): `colormapRgb` formatted with `toHex`. */
export function colormapHex(stops: string[], t: number): string { const [r, g, b] = colormapRgb(stops, t); return toHex(r, g, b); }

/** `size` RGB triplets (flat) for fast per-pixel lookup. */
export function colormapLut(stops: string[], size = 256): Uint8Array {
  const lut = new Uint8Array(size * 3);
  for (let i = 0; i < size; i++) { const [r, g, b] = colormapRgb(stops, size === 1 ? 0 : i / (size - 1)); lut[i * 3] = r; lut[i * 3 + 1] = g; lut[i * 3 + 2] = b; }
  return lut;
}

/** FNV-1a over bytes — the raster checksum both cores record for drawing parity. */
export function fnv1a(bytes: ArrayLike<number>): number {
  let h = 0x811c9dc5;
  for (let i = 0; i < bytes.length; i++) { h ^= bytes[i]! & 0xff; h = Math.imul(h, 0x01000193) >>> 0; }
  return h >>> 0;
}
