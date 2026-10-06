// Mori.SkyScope — Hand a file to the browser's download machinery (a recording, a snapshot).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

/** Hand a file to the browser's download machinery (a recording, a snapshot). */
export function saveFile(bytes: Uint8Array | Blob, name: string, type = "application/octet-stream"): void {
  const blob = bytes instanceof Blob ? bytes : new Blob([bytes as BlobPart], { type });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = name; a.style.display = "none";
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** Ask the user for a file through a hidden input; resolves null when the picker is dismissed. */
export function pickFile(accept = ".mcap,.csv"): Promise<File | null> {
  return new Promise((resolve) => {
    const input = document.createElement("input");
    input.type = "file"; input.accept = accept; input.style.display = "none";
    input.onchange = () => { resolve(input.files?.[0] ?? null); input.remove(); };
    input.oncancel = () => { resolve(null); input.remove(); };
    document.body.appendChild(input); input.click();
  });
}

/** Builds a default recording name from the current UTC time, e.g. `skyscope-2026-10-06T12-34-56.mcap`. */
export const recordingFileName = (prefix = "skyscope"): string => `${prefix}-${new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19)}.mcap`;

/**
 * Destination of a recording's bytes. `sink` receives each chunk (it is copied, so the caller may reuse the buffer);
 * `close` flushes and finishes the file; `streaming` is true when chunks go straight to disk rather than to memory.
 */
export interface FileSink { sink: (chunk: Uint8Array) => void; close: () => Promise<void>; streaming: boolean }

/**
 * A byte sink for a recording. Where the File System Access API exists (Chromium) the user picks the file up front and
 * records stream straight to disk — no memory limit; elsewhere bytes accumulate and download on close. Call from a user
 * gesture (the picker needs one).
 */
export async function createFileSink(suggestedName: string): Promise<FileSink> {
  const picker = (window as unknown as { showSaveFilePicker?: (o: unknown) => Promise<{ createWritable(): Promise<{ write(c: Uint8Array): Promise<void>; close(): Promise<void> }> }> }).showSaveFilePicker;
  if (picker) {
    try {
      const handle = await picker({ suggestedName, types: [{ description: "MCAP recording", accept: { "application/octet-stream": [".mcap"] } }] });
      const writable = await handle.createWritable();
      let queue: Promise<void> = Promise.resolve();
      return { streaming: true, sink: (chunk) => { const copy = chunk.slice(); queue = queue.then(() => writable.write(copy)); }, close: async () => { await queue; await writable.close(); } };
    } catch (e) { if ((e as { name?: string }).name === "AbortError") throw e; }
  }
  const chunks: Uint8Array[] = [];
  return { streaming: false, sink: (chunk) => { chunks.push(chunk.slice()); }, close: async () => { saveFile(new Blob(chunks as BlobPart[]), suggestedName); } };
}
