// Mori.SkyScope — Trend chart configuration, theme, style, layout records and the interaction state types (hit regions, drop targets, drags).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { Rect } from "../scene/geometry.js";
import type { TimeFormat } from "../scales/format.js";

/**
 * TrendChart configuration. Mirrors `Mori.SkyScope.Core.Charts.TrendChartConfig`.
 * A chart is a set of lanes (stacked plot strips sharing the time axis); each lane hosts series that
 * reference Y axes (one or many per lane, left or right). "Overlay" is simply a single lane.
 */
export type SeriesKind = "analog" | "digital";

/** A value axis; series reference it by id. A lane's default axis (`axis:<laneId>`) needs no entry here. */
export interface AxisConfig {
  /** Unique key. */
  id: string;
  /** Caption at the top of the axis strip. */
  label?: string | undefined;
  /** Shown after legend values; also the caption when there is no label. */
  unit?: string | undefined;
  /** Fixed range; omit for autoscale. */
  min?: number | undefined;
  /** Upper end of the fixed range; omit for autoscale. */
  max?: number | undefined;
  /** Which side of the lane the axis column sits on (default left). */
  side?: "left" | "right" | undefined;
  /** Axis colour; carried in the config but not yet used by the painter. */
  color?: string | undefined;
}

/** A stacked plot strip; every lane shares the time axis. */
export interface LaneConfig {
  /** Unique key. */
  id: string;
  /** Lane caption; carried in the config but not yet drawn. */
  label?: string | undefined;
  /** Relative height. Default 1. */
  weight?: number | undefined;
  /** Folded to a thin bar (series stay configured, nothing is drawn). */
  collapsed?: boolean | undefined;
}

/** A series: one channel of the store drawn in a lane against an axis. */
export interface SeriesConfig {
  /** Unique key. */
  id: string;
  /** Store channel the samples come from. */
  channelId: number;
  /** Legend text; defaults to the channel name, then the id. */
  name?: string | undefined;
  /** Stroke colour; palette by series index when omitted. */
  color?: string | undefined;
  /** Defaults to the lane's default axis (`axis:<laneId>`). */
  axisId?: string | undefined;
  /** Defaults to the first lane. */
  laneId?: string | undefined;
  /** Stroke width in pixels; defaults to `style.seriesWidth`. */
  width?: number | undefined;
  /** false hides the series from the plot and the legend without removing it. */
  visible?: boolean | undefined;
  /** Digital series draw as steps on a fixed 0..1 axis. */
  kind?: SeriesKind | undefined;
}

/** A band [from, to] or a line (from only) on an axis. */
export interface ThresholdConfig { /** Unique key. */ id: string; /** Axis whose scale positions it; drawn in every lane that shows the axis. */ axisId: string; /** Line value, or band start, in axis units. */ from: number; /** Band end; omit for a line. */ to?: number | undefined; /** Fill or stroke colour. */ color: string; /** Caption; carried in the config but not yet drawn. */ label?: string | undefined }

/** A vertical event line. */
export interface MarkerConfig { /** Unique key. */ id: string; /** Chart time in seconds. */ time: number; /** Text beside the line. */ label?: string | undefined; /** Line and label colour; defaults to `theme.marker`. */ color?: string | undefined }

/** Colours and type. Everything the chart paints takes its colour from here. */
export interface ChartTheme {
  /** Canvas clear colour. */ background: string; /** Lane and plot fill. */ plotBackground: string; /** Grid lines. */ grid: string; /** Axis lines and ticks. */ axis: string; /** Default text. */ text: string; /** Secondary text (captions, deltas). */ mutedText: string;
  /** Cursor A line. */ cursorA: string; /** Cursor B line. */ cursorB: string; /** Hover line and legend row highlight. */ hover: string; /** Default marker colour. */ marker: string; /** Legend and tooltip fill. */ legendBackground: string; /** Legend and tooltip outline. */ legendBorder: string;
  /** Lane outline (when `style.laneBorder`). */ laneBorder: string; /** Drag cues and drop highlights. */ dropIndicator: string; /** CSS font family for all text. */ fontFamily: string; /** Base font size in pixels. */ fontSize: number;
  /** Lane header bars and the navigator frame. */
  /** Header bar fill. */ laneHeader: string; /** Header glyphs. */ laneHeaderText: string; /** Navigator window frame. */ navigatorFrame: string; /** Navigator strip fill. */ navigatorBackground: string;
}

/** Widths, dashes, opacities and paddings. Everything the chart paints takes its geometry from here or the config. */
export interface ChartStyle {
  /** Grid stroke width in pixels. */ gridWidth: number; /** Grid dash pattern in pixels; null = solid. */ gridDash: number[] | null; /** Horizontal lines at value ticks. */ showValueGrid: boolean; /** Vertical lines at time ticks. */ showTimeGrid: boolean; /** Lines between digital tracks. */ trackSeparators: boolean;
  /** Outline each lane. */ laneBorder: boolean; /** Lane outline width in pixels. */ laneBorderWidth: number;
  /** Tick mark length in pixels. */ axisTickLength: number; /** Gap in pixels between tick and label. */ axisLabelGap: number;
  /** Default series stroke width in pixels. */ seriesWidth: number;
  /** Fill opacity of threshold bands. */ thresholdBandOpacity: number; /** Dash pattern of threshold lines. */ thresholdLineDash: number[];
  /** Marker line width in pixels. */ markerWidth: number; /** Marker dash pattern. */ markerDash: number[];
  /** Cursor and hover line width in pixels. */ cursorWidth: number; /** Hover line dash pattern. */ hoverDash: number[];
  /** Inner padding of the legend box in pixels. */ legendPadding: number; /** Fixed row height in pixels; null = fontSize + 8. */ legendRowHeight: number | null; /** Legend box fill opacity. */ legendOpacity: number; /** Legend box corner radius in pixels. */ legendRadius: number; /** Swatch length in pixels. */ legendSwatchLength: number;
  /** Fraction of a digital track's height kept free above the high level and below the low level. */
  digitalTrackPadding: number;
  /** Opacity of the solid fill under a high logic level. */
  digitalFillOpacity: number;
}

/** Style defaults; partial styles merge over it. */
export const DEFAULT_STYLE: ChartStyle = {
  gridWidth: 1, gridDash: null, showValueGrid: true, showTimeGrid: true, trackSeparators: true,
  laneBorder: false, laneBorderWidth: 1,
  axisTickLength: 4, axisLabelGap: 6,
  seriesWidth: 1.5,
  thresholdBandOpacity: 0.15, thresholdLineDash: [4, 3],
  markerWidth: 1, markerDash: [3, 3],
  cursorWidth: 1, hoverDash: [2, 2],
  legendPadding: 8, legendRowHeight: null, legendOpacity: 0.88, legendRadius: 3, legendSwatchLength: 14,
  digitalTrackPadding: 0.15, digitalFillOpacity: 0.3,
};

/** Legend row height in pixels: the style override, else fontSize + 8. */
export const legendRowHeight = (c: { theme: ChartTheme; style: ChartStyle }): number => c.style.legendRowHeight ?? c.theme.fontSize + 8;

/** Default theme on a white background. */
export const LIGHT_THEME: ChartTheme = {
  background: "#ffffff", plotBackground: "#f8fafc", grid: "#e2e8f0", axis: "#94a3b8", text: "#0f172a", mutedText: "#64748b",
  cursorA: "#2563eb", cursorB: "#dc2626", hover: "#94a3b8", marker: "#d97706", legendBackground: "#ffffff", legendBorder: "#e2e8f0",
  laneBorder: "#e2e8f0", dropIndicator: "#2563eb", laneHeader: "#e2e8f0", laneHeaderText: "#475569", navigatorFrame: "#dc2626", navigatorBackground: "#f1f5f9",
  fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", fontSize: 11,
};

/** Theme on a slate background; shares the font with the light one. */
export const DARK_THEME: ChartTheme = {
  ...LIGHT_THEME,
  background: "#0f172a", plotBackground: "#020617", grid: "#1e293b", axis: "#475569", text: "#f8fafc", mutedText: "#94a3b8",
  cursorA: "#60a5fa", cursorB: "#f87171", hover: "#64748b", marker: "#fbbf24", legendBackground: "#0f172a", legendBorder: "#1e293b",
  laneBorder: "#1e293b", dropIndicator: "#60a5fa", laneHeader: "#1e293b", laneHeaderText: "#94a3b8", navigatorFrame: "#f87171", navigatorBackground: "#0b1220",
};

/** Default series colours, assigned by series index modulo the length. */
export const SERIES_PALETTE = ["#2563eb", "#dc2626", "#16a34a", "#d97706", "#7c3aed", "#0891b2", "#db2777", "#65a30d", "#ea580c", "#4f46e5"];

/** Corner positions float over the plot; "right" and "top" reserve space beside it; "none" hides the legend. */
export type LegendPosition = "top-left" | "top-right" | "bottom-left" | "bottom-right" | "right" | "top" | "none";
/** The positions that float inside the plot. */
export const OVERLAY_LEGENDS: readonly LegendPosition[] = ["top-left", "top-right", "bottom-left", "bottom-right"];

/** Full trend chart configuration; `defaultTrendConfig` fills the defaults. */
export interface TrendChartConfig {
  /** Seconds visible. */
  timeSpan: number;
  /** Time axis labelling: seconds relative to the right edge, or UTC clock time. */
  timeFormat: TimeFormat;
  /** Lanes top to bottom; when empty, a lane "main" is created on demand. */
  lanes: LaneConfig[];
  /** Explicit axes; axis ids not listed here get defaults. */
  axes: AxisConfig[];
  /** Series in draw and legend order. */
  series: SeriesConfig[];
  /** Bands and lines on axes. */
  thresholds: ThresholdConfig[];
  /** Vertical event lines. */
  markers: MarkerConfig[];
  /** Corner overlays float inside the plot; "right"/"top" reserve space beside it. */
  legend: LegendPosition;
  /** Master switch; `style.showValueGrid` / `style.showTimeGrid` refine it. */
  showGrid: boolean;
  /** Colours and font. */
  theme: ChartTheme;
  /** Widths, dashes, opacities and paddings. */
  style: ChartStyle;
  /** Pixels between adjacent lanes. */
  laneGap: number;
  /** Outer margin in pixels. */
  margin: number;
  /** Width of one Y-axis column in pixels. */
  yAxisWidth: number;
  /** Height of the time axis row in pixels. */
  timeAxisHeight: number;
  /** Width in pixels of "right" and overlay legends. */
  legendWidth: number;
  /** Target pixels per time tick / y tick. */
  tickSpacing: number;
  /** Series names inside each lane (top-left) — the drag handles for moving signals. */
  plotLabels: boolean;
  /** A slim bar left of the axes per lane: drag to reorder, fold arrow, remove cross. */
  laneHeaders: boolean;
  /** Width of the header bar in pixels. */
  laneHeaderWidth: number;
  /** Height in pixels of a collapsed lane. */
  collapsedLaneHeight: number;
  /** Overview strip under the time axis showing the whole retained history with the visible window framed. */
  navigator: boolean;
  /** Height of the navigator strip in pixels. */
  navigatorHeight: number;
  /** Show the measurement table (value at A and B, delta, min, max, mean per signal) while both cursors are set. */
  measurePanel: boolean;
  /** Navigator frame keeps its width: dragging its edges is disabled, only moves apply. */
  navigatorFixedRange: boolean;
  /** Height of one logic-analyzer track when a lane mixes analog and digital signals (digital-only lanes share the whole lane). */
  digitalTrackHeight: number;
  /** Largest share of a mixed lane the digital stack may take. */
  digitalStackShare: number;
}

/** Partial theme/style objects are merged over the defaults, so callers only name what they change. */
export type TrendChartOptions = Omit<Partial<TrendChartConfig>, "theme" | "style"> & { theme?: Partial<ChartTheme> | undefined; style?: Partial<ChartStyle> | undefined };

/** Fills in every default (30 s relative window, overlay legend top-left, lane headers and plot labels on, no navigator) under the given options. */
export function defaultTrendConfig(partial: TrendChartOptions = {}): TrendChartConfig {
  const { theme, style, ...rest } = partial;
  return {
    timeSpan: 30, timeFormat: "relative", lanes: [], axes: [], series: [], thresholds: [], markers: [],
    legend: "top-left", showGrid: true, laneGap: 6, margin: 8, yAxisWidth: 48, timeAxisHeight: 22,
    legendWidth: 170, tickSpacing: 80, plotLabels: true, laneHeaders: true, laneHeaderWidth: 14, collapsedLaneHeight: 16,
    navigator: false, navigatorHeight: 48, navigatorFixedRange: false, digitalTrackHeight: 18, digitalStackShare: 0.5, measurePanel: true, ...rest,
    theme: { ...LIGHT_THEME, ...theme }, style: { ...DEFAULT_STYLE, ...style },
  };
}

/** In-place merge used by views: theme and style merge field-wise, everything else replaces. */
/** Version of the layout file format written by `trendLayoutToJson`. */
export const TREND_LAYOUT_VERSION = 1;
/** A saved arrangement: everything a gesture can change (lanes, axes, series, thresholds, markers, legend, panels) without theme and style. */
export interface TrendLayoutFile extends Omit<TrendChartOptions, "theme" | "style"> { version: number }
/** Serialises the arrangement of a chart to JSON (theme and style stay with the application). */
export function trendLayoutToJson(config: TrendChartConfig): string {
  const { theme: _theme, style: _style, ...rest } = config;
  const file: TrendLayoutFile = { version: TREND_LAYOUT_VERSION, ...rest };
  return JSON.stringify(file, null, 2);
}
/** Parses a layout file back into options; throws on an unknown version. */
export function trendLayoutFromJson(json: string): TrendChartOptions {
  const file = JSON.parse(json) as Partial<TrendLayoutFile>;
  if (file.version !== TREND_LAYOUT_VERSION) throw new Error(`unsupported layout version ${String(file.version)}`);
  const { version: _v, ...rest } = file;
  return rest as TrendChartOptions;
}

export function applyTrendOptions(config: TrendChartConfig, partial: TrendChartOptions): void {
  const { theme, style, ...rest } = partial;
  Object.assign(config, rest);
  if (theme) config.theme = { ...config.theme, ...theme };
  if (style) config.style = { ...config.style, ...style };
}

/** Where everything goes for a given size. */
export interface AxisLayout { /** Axis id. */ axisId: string; /** Axis strip in pixels, as tall as the lane's analog area. */ rect: Rect; /** Column side. */ side: "left" | "right" }
/** An in-plot series label (drag handle). Widths use the painter-free estimate 0.6 × fontSize per character, the same in both cores. */
export interface LabelLayout { /** Series id. */ seriesId: string; /** Label box in pixels. */ rect: Rect }
/** `analog` is the part numeric series draw in; `stack` (bottom of the lane) holds the logic-analyzer tracks, null when the lane has none. */
export interface LaneLayout { /** Lane id. */ laneId: string; /** Whole lane in pixels. */ rect: Rect; /** Analog plot area (equals `rect` without a stack). */ analog: Rect; /** Logic-track stack at the bottom, or null. */ stack: Rect | null; /** Axis strips, left ones first. */ axes: AxisLayout[]; /** Header bar left of the axes; null when headers are off. */ header: Rect | null; /** In-plot label boxes. */ labels: LabelLayout[]; /** Folded lane: only `rect` and `header` are meaningful. */ collapsed: boolean }
/** Where everything goes for a given canvas size; produced by `layoutTrendChart`. */
export interface TrendLayout { /** Canvas width. */ width: number; /** Canvas height. */ height: number; /** Area spanned by the lanes above the time axis. */ plot: Rect; /** Lanes top to bottom. */ lanes: LaneLayout[]; /** Time axis row under the lanes. */ timeAxis: Rect; /** Legend rect; null when hidden or empty. */ legend: Rect | null; /** Navigator strip; null when disabled. */ navigator: Rect | null; /** Measurement table over the cursor span; null when hidden. */ measure: Rect | null }

/**
 * Where a dragged series would land (ibaAnalyzer rules): onto an axis strip or a series label → the same axis as that
 * axis/series (`join`); into a lane's free area → the same lane with its own axis (`ownAxis`); onto the time axis,
 * between lanes or outside → a new lane (`newLane`).
 */
export type DropTarget =
  | { kind: "join"; laneId: string; axisId: string }
  | { kind: "ownAxis"; laneId: string }
  | { kind: "stack"; laneId: string }
  | { kind: "newLane"; index: number; afterLaneId: string | null }
  | { kind: "none" };

/** What is under a point of the chart, for hosts to route gestures. */
export type HitRegion =
  | { kind: "label"; seriesId: string; laneId: string }
  | { kind: "legendRow"; seriesId: string }
  | { kind: "header"; laneId: string; part: "grip" | "collapse" | "remove" }
  | { kind: "axis"; axisId: string; laneId: string; zone: "top" | "middle" | "bottom" }
  | { kind: "navigator"; zone: "leftEdge" | "rightEdge" | "inside" | "outside" }
  | { kind: "plot"; laneId: string }
  | { kind: "stack"; laneId: string }
  /** The gap between two open lanes: drag to move height from one to the other. */
  | { kind: "laneGap"; aboveLaneId: string; belowLaneId: string }
  /** Within a few pixels of cursor A or B: drag to move it. */
  | { kind: "cursor"; which: "a" | "b" }
  /** The measurement table over the cursor span. */
  | { kind: "measure" }
  | { kind: "timeAxis" }
  | { kind: "none" };

/** A lane header being dragged to reorder lanes. */
export interface LaneDragState { /** Lane being moved. */ laneId: string; /** Pointer x. */ x: number; /** Pointer y. */ y: number; /** Insertion slot under the pointer (0 = top). */ index: number }
/** A lane gap being dragged: the two lanes' heights and weights when the drag began. */
export interface LaneResizeState { aboveLaneId: string; belowLaneId: string; y0: number; hA0: number; hB0: number; wA0: number; wB0: number }
/** A time cursor being dragged. */
export interface CursorDragState { which: "a" | "b" }/** An axis being shifted (middle) or stretched (top/bottom) by the pointer. */
export interface AxisDragState { /** Axis being changed. */ axisId: string; /** Lane the axis was grabbed in. */ laneId: string; /** Part of the strip that was grabbed. */ zone: "top" | "middle" | "bottom"; /** Pointer y at pick-up. */ y0: number; /** Axis minimum at pick-up. */ min0: number; /** Axis maximum at pick-up. */ max0: number }
/** The navigator frame being moved or resized. */
export interface NavigatorDragState { /** Edge being stretched, or the frame being moved. */ zone: "leftEdge" | "rightEdge" | "inside"; /** Pointer x at pick-up. */ x0: number; /** Window start at pick-up (seconds). */ t0: number; /** Window end at pick-up (seconds). */ t1: number }

/** A signal drag in progress; `target` is recomputed on every pointer move. */
export interface DragState {
  /** First of `seriesIds` (kept for callers that move one signal). */
  seriesId: string;
  /** Every series in the drag (multi-select); a group dropped into free space shares one axis. */
  seriesIds: string[];
  /** Channels not yet in the chart (dragged from a signal tree); series are created on drop. */
  channelIds: number[];
  /** Pointer x. */ x: number; /** Pointer y. */ y: number; /** Where the drop would land right now. */ target: DropTarget;
  /** Ctrl/Shift held: a group dropped on a new-lane target lands in one lane instead of one lane each. */
  group: boolean;
  /** Set when the payload is known to be all digital (or not) before its channels are: a native drag-over can preview a logic-stack drop. */
  digital?: boolean | undefined;
}
