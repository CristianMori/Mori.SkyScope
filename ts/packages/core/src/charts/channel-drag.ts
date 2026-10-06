// Mori.SkyScope — The drag-and-drop payload for channels: what a signal tree (or any other control) puts on the clipboard-style data transfer, and how a chart reads it back.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { SignalStore } from "../sources/signal-store.js";

/** The MIME type a channel drag carries in the browser (`dataTransfer.setData`); the desktop hosts use the format name `Mori.SkyScope.Channels`. */
export const CHANNEL_DRAG_MIME = "application/x-skyscope-channels";
/** A second, empty MIME type present when every dragged channel is digital, so a chart can preview a logic-stack drop before the data is readable. */
export const CHANNEL_DRAG_DIGITAL_MIME = "application/x-skyscope-digital";

/** One dragged channel: the id is what the chart needs; name, unit and kind let a drop work before the chart's store knows the channel. */
export interface ChannelDragItem {
  /** Channel id in the store. */
  id: number;
  /** Display name, optional. */
  name?: string | undefined;
  /** Unit, optional. */
  unit?: string | undefined;
  /** `analog` or `digital`; digital channels land in a lane's logic stack. */
  kind?: "analog" | "digital" | undefined;
}

/** What a channel drag carries. */
export interface ChannelDragPayload {
  /** The channels, in drag order. */
  channels: ChannelDragItem[];
  /** True keeps several channels together on a drop (one shared axis, one lane) as a Ctrl/Shift group does. */
  group: boolean;
}

/** Serialises a payload to the JSON text put on the data transfer. */
export function encodeChannelDrag(payload: ChannelDragPayload): string {
  return JSON.stringify({
    channels: payload.channels.map((c) => {
      const o: Record<string, unknown> = { id: c.id };
      if (c.name !== undefined && c.name !== null) o.name = c.name;
      if (c.unit !== undefined && c.unit !== null) o.unit = c.unit;
      if (c.kind !== undefined && c.kind !== null) o.kind = c.kind;
      return o;
    }),
    group: payload.group === true,
  });
}

/**
 * Reads a payload back from text. Accepts the JSON object `encodeChannelDrag` writes, a JSON array of channel ids, or
 * plain text with ids separated by commas or whitespace (so a drag from a grid or another application works too).
 * Returns null when the text carries no channel.
 */
export function parseChannelDrag(text: string | null | undefined): ChannelDragPayload | null {
  if (!text) return null;
  const t = text.trim();
  if (t.length === 0) return null;
  if (t.startsWith("{") || t.startsWith("[")) {
    let v: unknown;
    try { v = JSON.parse(t); } catch { return null; }
    if (Array.isArray(v)) { const ids = v.filter((x): x is number => typeof x === "number" && Number.isInteger(x)); return ids.length ? { channels: ids.map((id) => ({ id })), group: false } : null; }
    if (v && typeof v === "object" && Array.isArray((v as { channels?: unknown }).channels)) {
      const items: ChannelDragItem[] = [];
      for (const raw of (v as { channels: unknown[] }).channels) {
        if (typeof raw === "number" && Number.isInteger(raw)) { items.push({ id: raw }); continue; }
        if (!raw || typeof raw !== "object") continue;
        const r = raw as { id?: unknown; name?: unknown; unit?: unknown; kind?: unknown };
        if (typeof r.id !== "number" || !Number.isInteger(r.id)) continue;
        const item: ChannelDragItem = { id: r.id };
        if (typeof r.name === "string") item.name = r.name;
        if (typeof r.unit === "string") item.unit = r.unit;
        if (r.kind === "digital" || r.kind === "analog") item.kind = r.kind;
        items.push(item);
      }
      return items.length ? { channels: items, group: (v as { group?: unknown }).group === true } : null;
    }
    return null;
  }
  const parts = t.split(/[\s,;]+/).filter((p) => p.length > 0);
  if (parts.length === 0 || !parts.every((p) => /^-?\d+$/.test(p))) return null;
  return { channels: parts.map((p) => ({ id: Number.parseInt(p, 10) })), group: false };
}

/** True when every channel of the payload is digital, by its own `kind` or, failing that, by the store's channel info. */
export function channelDragIsDigital(payload: ChannelDragPayload, store?: SignalStore | null): boolean {
  if (payload.channels.length === 0) return false;
  return payload.channels.every((c) => (c.kind ?? store?.get(c.id)?.info.kind) === "digital");
}
