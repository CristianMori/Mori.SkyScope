// Mori.SkyScope — Plain TypeScript dashboard, no framework: the DOM views over a store fed by the WebSocket source, a signal
// tree, a hand-made channel list as a second drag source, the chart's drop hook, and adding signals from code.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import "@mori/skyscope-tokens/tokens.css";
import {
  CHANNEL_DRAG_DIGITAL_MIME, CHANNEL_DRAG_MIME, Compass, LinearGauge, LiveClock, NumericDisplay, RadialGauge, SignalStore, encodeChannelDrag,
  type DropTarget, type Tool, type TrendChartOptions,
} from "@mori/skyscope-core";
import { GaugeView, SignalTreePanel, TrendChartView, pickFile, saveFile, type ChannelDropEvent } from "@mori/skyscope-render";
import { WebSocketFrameSource } from "@mori/skyscope-sources";

// ---- page skeleton -------------------------------------------------------------------------------------------------
const WS_URL = new URLSearchParams(location.search).get("ws") ?? `ws://${location.hostname}:5055/ws`;
document.head.insertAdjacentHTML("beforeend", `<style>
  html,body{margin:0;height:100%;font:13px/1.4 var(--skyscope-font-family,system-ui,sans-serif);color:var(--skyscope-color-text,#0f172a);background:var(--skyscope-color-bg,#fff)}
  #app{height:100%;display:flex;flex-direction:column;gap:8px;padding:12px;box-sizing:border-box}
  .bar{display:flex;gap:6px;align-items:center;flex-wrap:wrap}
  .bar button{padding:3px 9px;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff);color:inherit;font:inherit;cursor:pointer}
  .bar button.active{background:var(--skyscope-color-primary,#2563eb);color:#fff;border-color:transparent}
  .main{flex:1;min-height:0;display:grid;grid-template-columns:200px 1fr 200px;gap:8px}
  .panel{display:flex;flex-direction:column;min-height:0;gap:4px}
  .panel h3{margin:0;font-size:12px;color:var(--skyscope-color-text-secondary,#64748b);font-weight:600}
  .mylist{flex:1;min-height:0;overflow:auto;border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:4px;background:var(--skyscope-color-surface,#fff);margin:0;padding:4px;list-style:none}
  .mylist li{padding:3px 6px;border-radius:3px;cursor:grab;display:flex;justify-content:space-between}
  .mylist li:hover{background:color-mix(in srgb,var(--skyscope-color-primary,#2563eb) 8%,transparent)}
  .mylist li span{color:var(--skyscope-color-text-secondary,#64748b);font-size:11px}
  .gauges{display:grid;grid-template-columns:repeat(4,1fr);height:170px;gap:8px}
  .gauges>div{border:1px solid var(--skyscope-color-border,#cbd5e1);border-radius:6px;background:var(--skyscope-color-surface,#fff)}
  .status{font-size:12px;color:var(--skyscope-color-text-secondary,#64748b);white-space:nowrap;overflow:hidden;text-overflow:ellipsis}
</style>`);
const app = document.getElementById("app")!;
app.innerHTML = `
  <div class="bar" id="toolbar"><strong>Mori.SkyScope</strong> <span id="conn">○ connecting…</span><span style="flex:1"></span></div>
  <div class="main">
    <div class="panel"><h3>signal tree (shipped)</h3><div id="tree" style="flex:1;min-height:0"></div></div>
    <div id="chart" style="min-height:0"></div>
    <div class="panel"><h3>my own list (any drag source)</h3><ul class="mylist" id="mylist"></ul><button id="add">add ch7 + ch8 from code</button></div>
  </div>
  <div class="gauges"><div id="g1"></div><div id="g2"></div><div id="g3"></div><div id="g4"></div></div>
  <div class="status" id="status">drag a row from either list into the chart · the drop hook logs here</div>`;
const $ = <T extends HTMLElement>(id: string): T => document.getElementById(id) as T;
const status = (msg: string): void => { $("status").textContent = msg; };

// ---- data: one store, fed by the live socket ----------------------------------------------------------------------
const store = new SignalStore({ retentionSeconds: 600 });
const clock = new LiveClock();
const source = new WebSocketFrameSource();
source.start({ signals: store, layers: { declareLayer() {}, push() {}, reset() {} }, clock, log: (level, msg) => { if (level !== "info") status(`${level}: ${msg}`); } }, { url: WS_URL });
setInterval(() => { const c = $("conn"); c.textContent = source.connected ? `● live ${WS_URL} · ${store.channels.size} channels` : `○ connecting ${WS_URL}…`; c.style.color = source.connected ? "#16a34a" : "#dc2626"; }, 500);

// ---- the trend chart --------------------------------------------------------------------------------------------
const config: TrendChartOptions = {
  timeSpan: 30, timeFormat: "utc", legend: "top-left", navigator: true,
  lanes: [{ id: "analog", weight: 2 }, { id: "fast" }, { id: "io", weight: 0.6 }],
  axes: [{ id: "axis:analog", label: "sine / tri" }, { id: "axis:fast", label: "saw" }],
  series: [
    { id: "s1", channelId: 1, laneId: "analog" }, { id: "s2", channelId: 2, laneId: "analog" }, { id: "s3", channelId: 3, laneId: "fast" },
    { id: "pump", channelId: 200, laneId: "io", kind: "digital" }, { id: "valve", channelId: 201, laneId: "io", kind: "digital" },
  ],
  thresholds: [{ id: "hi", axisId: "axis:analog", from: 1.5, to: 3, color: "#dc2626", label: "high" }],
};
const view = new TrendChartView($("chart"), { store, config, tool: "pan", clock });
view.start();
// The drop hook: see every drop before it lands. Here it only reports; set e.cancel, replace e.target or set e.handled to intervene.
const describe = (t: DropTarget): string => t.kind === "join" ? `shared axis ${t.axisId} in lane ${t.laneId}` : t.kind === "ownAxis" ? `own axis in lane ${t.laneId}` : t.kind === "stack" ? `logic stack of lane ${t.laneId}` : t.kind === "newLane" ? `new lane at ${t.index}` : "nowhere";
view.onChannelDrop = (e: ChannelDropEvent) => { status(`drop: channels ${e.channelIds.join(", ")} → ${describe(e.target)}${e.group ? " (group)" : ""}`); };
view.onConfigChanged = () => { tree.refresh(); };

// ---- the shipped signal tree (optional; the chart does not need it) ---------------------------------------------
const tree = new SignalTreePanel($("tree"), store, { chart: view });

// ---- a hand-made channel list: any element that puts the payload on the data transfer is a drag source -------------
const list = $<HTMLUListElement>("mylist");
let listed = "";
setInterval(() => {
  const infos = [...store.channels.values()].map((c) => c.info);
  const sig = infos.map((i) => i.id).join(",");
  if (sig === listed) return; listed = sig;
  list.replaceChildren(...infos.map((info) => {
    const li = document.createElement("li");
    li.draggable = true; li.innerHTML = `${info.name}<span>${info.kind === "digital" ? "digital" : info.unit ?? ""}</span>`;
    li.addEventListener("dragstart", (e) => {
      const dt = e.dataTransfer!;
      dt.effectAllowed = "copy";
      dt.setData(CHANNEL_DRAG_MIME, encodeChannelDrag({ channels: [{ id: info.id, name: info.name, unit: info.unit, kind: info.kind === "digital" ? "digital" : "analog" }], group: false }));
      dt.setData("text/plain", String(info.id));                       // a plain id list is enough for the chart too
      if (info.kind === "digital") dt.setData(CHANNEL_DRAG_DIGITAL_MIME, "");  // lets the chart preview a logic-stack drop
    });
    return li;
  }));
}, 500);
// Adding from code: the first lane by default, digital channels in its logic stack; a target applies the drop rules.
$("add").addEventListener("click", () => { const ids = view.addChannels([7, 8]); status(ids.length ? `added ${ids.join(", ")} to the first lane` : "channels 7 and 8 are not in the store yet"); });

// ---- toolbar ----------------------------------------------------------------------------------------------------
const bar = $("toolbar");
const button = (label: string, onClick: () => void, group?: string): HTMLButtonElement => { const b = document.createElement("button"); b.textContent = label; if (group) b.dataset.group = group; b.addEventListener("click", onClick); bar.appendChild(b); return b; };
const activate = (group: string, b: HTMLButtonElement): void => { bar.querySelectorAll<HTMLButtonElement>(`button[data-group="${group}"]`).forEach((x) => x.classList.toggle("active", x === b)); };
for (const t of ["pan", "boxZoom", "cursor"] as Tool[]) { const b = button(t, () => { view.setTool(t); activate("tool", b); }, "tool"); if (t === "pan") b.classList.add("active"); }
for (const s of [5, 10, 30, 60, 300]) { const b = button(`${s} s`, () => { view.model.setTimeSpan(s); activate("span", b); }, "span"); if (s === 30) b.classList.add("active"); }
let paused = false;
const pause = button("pause", () => { paused = !paused; if (paused) view.model.pause(); else view.model.resume(); pause.textContent = paused ? "live" : "pause"; });
button("reset", () => { view.model.reset(); paused = false; pause.textContent = "pause"; });
button("save layout", () => saveFile(new Blob([view.exportLayout()], { type: "application/json" }), "skyscope-layout.json", "application/json"));
button("load layout", () => { void pickFile(".json").then(async (f) => { if (f) view.importLayout(await f.text()); }); });

// ---- gauges: the latest sample of a channel drives each one ----------------------------------------------------
const speed = new RadialGauge({ min: -4, max: 4, unit: "m/s", label: "sine", decimals: 2 });
const temp = new LinearGauge({ min: -4, max: 4, unit: "°C", label: "triangle", orientation: "vertical", decimals: 1 });
const bus = new NumericDisplay({ digits: 6, decimals: 3, unit: "V", label: "sawtooth" });
const heading = new Compass({ label: "HDG" });
const gauges = [new GaugeView($("g1"), speed), new GaugeView($("g2"), temp), new GaugeView($("g3"), bus), new GaugeView($("g4"), heading)];
store.subscribe((ids) => {
  const latest = (ch: number): number | null => { const b = store.get(ch)?.buffer; return b && !b.isEmpty ? b.valueAt(b.headSeq - 1) : null; };
  if (ids.includes(1)) { const v = latest(1); if (v !== null) { speed.setValue(v); heading.setHeading(v * 45 + 180); } }
  if (ids.includes(2)) { const v = latest(2); if (v !== null) temp.setValue(v); }
  if (ids.includes(3)) { const v = latest(3); if (v !== null) bus.setValue(v); }
  for (const g of gauges) g.invalidate();
});
