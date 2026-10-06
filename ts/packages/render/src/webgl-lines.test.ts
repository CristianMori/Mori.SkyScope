// Mori.SkyScope — Tests of the WebGL series line renderer against a recording stand-in for the WebGL 1 context (no GPU in the test runner).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { describe, expect, it } from "vitest";
import { DEFAULT_LINE_WIDTH, WebGLLineRenderer, cssToRgba } from "./webgl-lines.js";

interface Call { name: string; args: unknown[] }

/** A WebGL 1 context stand-in: every method records its call; shader and program queries succeed; the instancing extension is optional. */
function fakeGl(withInstancing: boolean): { gl: WebGLRenderingContext; calls: Call[]; sources: string[] } {
  const calls: Call[] = [], sources: string[] = [];
  const ext = { vertexAttribDivisorANGLE: (i: number, d: number) => calls.push({ name: "divisor", args: [i, d] }), drawArraysInstancedANGLE: (m: number, f: number, c: number, n: number) => calls.push({ name: "drawInstanced", args: [m, f, c, n] }) };
  const consts: Record<string, number> = { VERTEX_SHADER: 1, FRAGMENT_SHADER: 2, COMPILE_STATUS: 3, LINK_STATUS: 4, ARRAY_BUFFER: 5, STATIC_DRAW: 6, DYNAMIC_DRAW: 7, FLOAT: 8, BLEND: 9, ONE: 10, ONE_MINUS_SRC_ALPHA: 11, SCISSOR_TEST: 12, COLOR_BUFFER_BIT: 13, LINE_STRIP: 14, TRIANGLE_STRIP: 15 };
  let attribs = 0;
  const target: Record<string, unknown> = { ...consts };
  const gl = new Proxy(target, {
    get(t, prop: string) {
      if (prop in t) return t[prop];
      return (...args: unknown[]) => {
        calls.push({ name: prop, args });
        switch (prop) {
          case "getExtension": return withInstancing && args[0] === "ANGLE_instanced_arrays" ? ext : null;
          case "createShader": case "createProgram": case "createBuffer": return {};
          case "shaderSource": sources.push(args[1] as string); return undefined;
          case "getShaderParameter": case "getProgramParameter": return true;
          case "getAttribLocation": return attribs++;
          case "getUniformLocation": return { name: args[1] };
          default: return undefined;
        }
      };
    },
  }) as unknown as WebGLRenderingContext;
  return { gl, calls, sources };
}

function canvasFor(gl: WebGLRenderingContext): HTMLCanvasElement {
  return { width: 0, height: 0, getContext: () => gl } as unknown as HTMLCanvasElement;
}

describe("WebGLLineRenderer", () => {
  it("draws each segment as an instanced quad expanded by the width in device pixels", () => {
    const { gl, calls, sources } = fakeGl(true);
    const r = new WebGLLineRenderer(canvasFor(gl));
    expect(r.thick).toBe(true);
    expect(sources.some((s) => s.includes("attribute vec2 a_p0") && s.includes("attribute vec2 a_p1") && s.includes("a_corner"))).toBe(true);
    expect(sources.some((s) => s.includes("GL_FRAGMENT_PRECISION_HIGH") && s.includes("u_halfWidth"))).toBe(true);
    r.resize(400, 300, 2);
    r.setSeries("a", [0, 0, 1, 1, 2, 0, 3, 1], 4, 0);
    calls.length = 0;
    r.draw("a", { sx: 1, sy: 1, ox: 0, oy: 0 }, { color: cssToRgba("#ff0000"), width: 3 }, { x: 10, y: 20, w: 100, h: 50 });
    const draw = calls.find((c) => c.name === "drawInstanced");
    expect(draw?.args).toEqual([15, 0, 4, 3]);
    expect(calls.some((c) => c.name === "drawArrays")).toBe(false);
    const half = calls.find((c) => c.name === "uniform1f" && (c.args[0] as { name: string }).name === "u_halfWidth");
    expect(half?.args[1]).toBe(3);   // 3 logical px × ratio 2 / 2
    const viewport = calls.find((c) => c.name === "uniform2f" && (c.args[0] as { name: string }).name === "u_viewport");
    expect(viewport?.args.slice(1)).toEqual([800, 600]);
    const pointers = calls.filter((c) => c.name === "vertexAttribPointer").map((c) => c.args.slice(4));
    expect(pointers).toContainEqual([8, 0]);
    expect(pointers).toContainEqual([8, 8]);
    const divisors = calls.filter((c) => c.name === "divisor").map((c) => c.args[1]);
    expect(divisors.sort()).toEqual([0, 1, 1]);
    const scissor = calls.find((c) => c.name === "scissor");
    expect(scissor?.args).toEqual([20, 460, 200, 100]);
    const color = calls.find((c) => c.name === "uniform4f");
    expect(color?.args.slice(1)).toEqual([1, 0, 0, 1]);
  });

  it("uses the default width when the style gives none", () => {
    const { gl, calls } = fakeGl(true);
    const r = new WebGLLineRenderer(canvasFor(gl));
    r.resize(100, 100, 1);
    r.setSeries("a", [0, 0, 1, 1], 2);
    r.draw("a", { sx: 1, sy: 1, ox: 0, oy: 0 }, { color: [0, 0, 1, 0.5] });
    const half = calls.find((c) => c.name === "uniform1f" && (c.args[0] as { name: string }).name === "u_halfWidth");
    expect(half?.args[1]).toBe(DEFAULT_LINE_WIDTH / 2);
    const color = calls.find((c) => c.name === "uniform4f");
    expect(color?.args.slice(1)).toEqual([0, 0, 0.5, 0.5]);   // premultiplied
  });

  it("falls back to native line strips without the instancing extension", () => {
    const { gl, calls, sources } = fakeGl(false);
    const r = new WebGLLineRenderer(canvasFor(gl));
    expect(r.thick).toBe(false);
    expect(sources.some((s) => s.includes("attribute vec2 a_pos"))).toBe(true);
    r.resize(100, 100, 1);
    r.setSeries("a", [0, 0, 1, 1, 2, 2], 3);
    calls.length = 0;
    r.draw("a", { sx: 1, sy: 1, ox: 0, oy: 0 }, { color: [0, 0, 0, 1], width: 4 });
    expect(calls.find((c) => c.name === "drawArrays")?.args).toEqual([14, 0, 3]);
    expect(calls.some((c) => c.name === "drawInstanced")).toBe(false);
  });

  it("skips series with fewer than two points and frees buffers on dispose", () => {
    const { gl, calls } = fakeGl(true);
    const r = new WebGLLineRenderer(canvasFor(gl));
    r.setSeries("one", [0, 0], 1);
    calls.length = 0;
    r.draw("one", { sx: 1, sy: 1, ox: 0, oy: 0 }, { color: [0, 0, 0, 1] });
    expect(calls.some((c) => c.name === "drawInstanced")).toBe(false);
    r.dispose();
    expect(calls.filter((c) => c.name === "deleteBuffer").length).toBe(2);   // the series and the corner quad
    expect(calls.some((c) => c.name === "deleteProgram")).toBe(true);
  });
});
