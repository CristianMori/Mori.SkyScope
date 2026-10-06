// Mori.SkyScope — Context menu for a series of the trend chart: show or hide, rename, colour, line width, remove.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { SERIES_PALETTE, type TrendChartModel } from "@mori/skyscope-core";

const CSS = `
.skyscope-menu{position:fixed;z-index:1000;min-width:180px;padding:4px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:6px;background:var(--skyscope-color-surface,#fff);color:var(--skyscope-color-text,#0f172a);box-shadow:0 8px 24px rgba(0,0,0,.18);font:12px/1.4 var(--skyscope-font-family,system-ui,sans-serif);user-select:none}
.skyscope-menu-title{padding:4px 8px 6px;font-weight:600;border-bottom:1px solid var(--skyscope-color-border,#cbd5e1);margin-bottom:4px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
.skyscope-menu-item{display:flex;align-items:center;gap:8px;padding:5px 8px;border-radius:4px;cursor:pointer}
.skyscope-menu-item:hover{background:color-mix(in srgb,var(--skyscope-color-primary,#2563eb) 10%,transparent)}
.skyscope-menu-label{flex:1}
.skyscope-menu-row{display:flex;align-items:center;gap:6px;padding:4px 8px}
.skyscope-menu-swatch{width:16px;height:16px;border-radius:3px;border:1px solid rgba(0,0,0,.2);cursor:pointer}
.skyscope-menu-swatch[data-on=true]{outline:2px solid var(--skyscope-color-primary,#2563eb);outline-offset:1px}
.skyscope-menu input[type=text]{flex:1;padding:3px 6px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff);color:inherit;font:inherit}
.skyscope-menu input[type=color]{width:24px;height:20px;padding:0;border:none;background:none;cursor:pointer}
.skyscope-menu-width{padding:2px 8px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;cursor:pointer}
.skyscope-menu-width[data-on=true]{background:var(--skyscope-color-primary,#2563eb);color:#fff;border-color:transparent}
.skyscope-menu-danger{color:#dc2626}
`;

/**
 * Opens the series menu at a client position. Every action goes through the model; `onChange` runs after each one so the
 * host can invalidate layout and persist the configuration. Closes on a click elsewhere, on Escape or after an action.
 */
export function showSeriesMenu(model: TrendChartModel, seriesId: string, clientX: number, clientY: number, onChange: () => void): void {
  const s = model.config.series.find((x) => x.id === seriesId);
  if (!s) return;
  if (!document.getElementById("skyscope-menu-css")) { const st = document.createElement("style"); st.id = "skyscope-menu-css"; st.textContent = CSS; document.head.appendChild(st); }
  document.querySelectorAll(".skyscope-menu").forEach((e) => e.remove());
  const menu = document.createElement("div");
  menu.className = "skyscope-menu";
  const close = (): void => { menu.remove(); document.removeEventListener("pointerdown", onOutside, true); document.removeEventListener("keydown", onKey, true); };
  const onOutside = (e: Event): void => { if (!menu.contains(e.target as Node)) close(); };
  const onKey = (e: KeyboardEvent): void => { if (e.key === "Escape") close(); };
  const apply = (): void => { onChange(); };
  const item = (label: string, action: () => void, danger = false): void => {
    const el = document.createElement("div"); el.className = "skyscope-menu-item" + (danger ? " skyscope-menu-danger" : "");
    const t = document.createElement("span"); t.className = "skyscope-menu-label"; t.textContent = label; el.appendChild(t);
    el.addEventListener("click", () => { action(); apply(); close(); });
    menu.appendChild(el);
  };
  const title = document.createElement("div"); title.className = "skyscope-menu-title"; title.textContent = model.seriesName(s); menu.appendChild(title);

  item(s.visible === false ? "Show" : "Hide", () => model.toggleSeries(seriesId));

  // rename: an input that commits on Enter or blur
  const renameRow = document.createElement("div"); renameRow.className = "skyscope-menu-row";
  const input = document.createElement("input"); input.type = "text"; input.placeholder = "name"; input.value = s.name ?? ""; input.title = "Display name; empty restores the channel name";
  const commit = (): void => { model.renameSeries(seriesId, input.value); apply(); title.textContent = model.seriesName(s); };
  input.addEventListener("keydown", (e) => { if (e.key === "Enter") { commit(); close(); } e.stopPropagation(); });
  input.addEventListener("change", commit);
  renameRow.append(input); menu.appendChild(renameRow);

  // colour: the palette plus a free picker
  const colorRow = document.createElement("div"); colorRow.className = "skyscope-menu-row";
  const current = model.seriesColor(s);
  for (const c of SERIES_PALETTE) {
    const sw = document.createElement("div"); sw.className = "skyscope-menu-swatch"; sw.style.background = c; sw.dataset.on = String(c.toLowerCase() === current.toLowerCase()); sw.title = c;
    sw.addEventListener("click", () => { model.setSeriesColor(seriesId, c); apply(); close(); });
    colorRow.appendChild(sw);
  }
  const picker = document.createElement("input"); picker.type = "color"; picker.value = current.length === 7 ? current : "#2563eb"; picker.title = "Custom colour";
  picker.addEventListener("input", () => { model.setSeriesColor(seriesId, picker.value); apply(); });
  colorRow.appendChild(picker); menu.appendChild(colorRow);

  // line width
  const widthRow = document.createElement("div"); widthRow.className = "skyscope-menu-row";
  const label = document.createElement("span"); label.textContent = "width"; label.style.color = "var(--skyscope-color-text-secondary,#64748b)"; widthRow.appendChild(label);
  const currentW = s.width ?? model.config.style.seriesWidth;
  for (const w of [1, 1.5, 2, 3]) {
    const b = document.createElement("span"); b.className = "skyscope-menu-width"; b.textContent = String(w); b.dataset.on = String(w === currentW);
    b.addEventListener("click", () => { model.setSeriesWidth(seriesId, w); apply(); close(); });
    widthRow.appendChild(b);
  }
  menu.appendChild(widthRow);

  item("Remove from chart", () => model.removeSeries(seriesId), true);

  document.body.appendChild(menu);
  const r = menu.getBoundingClientRect();
  menu.style.left = `${Math.min(clientX, window.innerWidth - r.width - 4)}px`;
  menu.style.top = `${Math.min(clientY, window.innerHeight - r.height - 4)}px`;
  setTimeout(() => { document.addEventListener("pointerdown", onOutside, true); document.addEventListener("keydown", onKey, true); }, 0);
}
