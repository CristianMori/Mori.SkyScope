// Mori.SkyScope — Replays generic op steps onto any Painter — also used to drive the real renderers in demos.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { RecordingPainter } from "../paint/recording-painter.js";
import type { Fill, Painter, Stroke, TextStyle } from "../paint/painter.js";
import type { FixtureDriver } from "./drivers.js";

type Step = Record<string, unknown>;
const num = (v: unknown): number => v as number;

/** Replays generic op steps onto any Painter — also used to drive the real renderers in demos. */
export function applyPaintStep(p: Painter, s: Step, queries?: unknown[]): void {
  const stroke = s.stroke as Stroke | undefined, fill = s.fill as Fill | undefined;
  switch (s.type) {
    case "save": p.save(); break;
    case "restore": p.restore(); break;
    case "translate": p.translate(num(s.x), num(s.y)); break;
    case "scale": p.scale(num(s.sx), num(s.sy)); break;
    case "rotate": p.rotate(num(s.radians)); break;
    case "clipRect": p.clipRect(num(s.x), num(s.y), num(s.w), num(s.h)); break;
    case "clear": p.clear(s.color as string | undefined); break;
    case "line": p.line(num(s.x1), num(s.y1), num(s.x2), num(s.y2), stroke!); break;
    case "polyline": p.polyline(s.points as number[], stroke!, s.offset === undefined ? 0 : num(s.offset), s.count === undefined ? undefined : num(s.count)); break;
    case "polygon": p.polygon(s.points as number[], fill, stroke); break;
    case "rect": p.rect(num(s.x), num(s.y), num(s.w), num(s.h), fill, stroke, s.radius === undefined ? 0 : num(s.radius)); break;
    case "circle": p.circle(num(s.cx), num(s.cy), num(s.r), fill, stroke); break;
    case "arc": p.arc(num(s.cx), num(s.cy), num(s.r), num(s.start), num(s.end), stroke!); break;
    case "sector": p.sector(num(s.cx), num(s.cy), num(s.inner), num(s.outer), num(s.start), num(s.end), fill, stroke); break;
    case "text": p.text(s.text as string, num(s.x), num(s.y), s.style as TextStyle); break;
    case "measure": queries?.push(p.measureText(s.text as string, s.style as TextStyle)); break;
    case "layer": p.layer(s.key as string, num(s.width), num(s.height), (c) => { for (const o of s.ops as Step[]) applyPaintStep(c, o); }, num(s.x), num(s.y), s.dirty === true); break;
    default: throw new Error(`unknown paint step ${String(s.type)}`);
  }
}

interface State { painter: RecordingPainter; queries: unknown[] }

/**
 * Painter op replay: `setup` gives the surface size; every step is a paint op, and `measure` steps answer into the
 * queries.
 */
export const paintDriver: FixtureDriver<State> = {
  component: "paint",
  create(setup) { return { painter: new RecordingPainter(num(setup.width), num(setup.height)), queries: [] }; },
  step(s, step) { applyPaintStep(s.painter, step, s.queries); return s; },
  /** Recorded ops and the text measurements. */
  snapshot({ painter, queries }) { return { ops: painter.ops, queries }; },
};
