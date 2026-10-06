// Mori.SkyScope — Draws many polylines (decimated series) with WebGL: one GPU buffer per series in data space, one uniform transform per draw, so pan/zoom …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { ClipTransform, Rect } from "@mori/skyscope-core";

/**
 * Draws many polylines (decimated series) with WebGL: one GPU buffer per series in data space,
 * one uniform transform per draw, so pan/zoom never re-uploads. Line width is 1 device pixel
 * (the WebGL guarantee); thicker strokes come from the Canvas2D overlay when needed.
 */
/** Per-draw line appearance: `color` is straight (non-premultiplied) RGBA in 0..1, as `cssToRgba` returns. */
export interface LineStyle { color: [number, number, number, number] }

interface SeriesBuffer { buffer: WebGLBuffer; count: number; capacity: number; xOrigin: number }

const VS = `attribute vec2 a_pos; uniform vec2 u_scale; uniform vec2 u_offset;
void main() { gl_Position = vec4(a_pos * u_scale + u_offset, 0.0, 1.0); }`;
const FS = `precision mediump float; uniform vec4 u_color; void main() { gl_FragColor = u_color; }`;

/**
 * The series line renderer: keeps one vertex buffer per series key in data space (x relative to an origin) and draws
 * each with a single scale/offset uniform pair. WebGL 1 is enough; blending is premultiplied alpha.
 */
export class WebGLLineRenderer {
  /** The underlying WebGL 1 context, exposed for hosts that need to query it; do not change its global state. */
  readonly gl: WebGLRenderingContext;
  private readonly program: WebGLProgram;
  private readonly aPos: number;
  private readonly uScale: WebGLUniformLocation;
  private readonly uOffset: WebGLUniformLocation;
  private readonly uColor: WebGLUniformLocation;
  private readonly series = new Map<string, SeriesBuffer>();
  private scratch = new Float32Array(0);

  /** Acquires a WebGL 1 context on `canvas` (transparent, antialiased, premultiplied) and compiles the line program. Throws when WebGL is unavailable. */
  constructor(readonly canvas: HTMLCanvasElement | OffscreenCanvas, options: WebGLContextAttributes = {}) {
    const gl = canvas.getContext("webgl", { antialias: true, premultipliedAlpha: true, alpha: true, preserveDrawingBuffer: false, ...options }) as WebGLRenderingContext | null;
    if (!gl) throw new Error("WebGL unavailable");
    this.gl = gl;
    this.program = this.link(VS, FS);
    this.aPos = gl.getAttribLocation(this.program, "a_pos");
    this.uScale = gl.getUniformLocation(this.program, "u_scale")!;
    this.uOffset = gl.getUniformLocation(this.program, "u_offset")!;
    this.uColor = gl.getUniformLocation(this.program, "u_color")!;
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
  }

  private link(vs: string, fs: string): WebGLProgram {
    const { gl } = this;
    const compile = (type: number, src: string): WebGLShader => {
      const sh = gl.createShader(type)!;
      gl.shaderSource(sh, src); gl.compileShader(sh);
      if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(`shader: ${gl.getShaderInfoLog(sh) ?? "?"}`);
      return sh;
    };
    const p = gl.createProgram()!;
    gl.attachShader(p, compile(gl.VERTEX_SHADER, vs)); gl.attachShader(p, compile(gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(`program: ${gl.getProgramInfoLog(p) ?? "?"}`);
    return p;
  }

  /** Size the drawing buffer for `pixelRatio` and set the viewport. */
  resize(width: number, height: number, pixelRatio = 1): void {
    const w = Math.max(1, Math.round(width * pixelRatio)), h = Math.max(1, Math.round(height * pixelRatio));
    if (this.canvas.width !== w) this.canvas.width = w;
    if (this.canvas.height !== h) this.canvas.height = h;
    this.pixelRatio = pixelRatio; this.cssHeight = height;
    this.gl.viewport(0, 0, w, h);
  }

  /** Clears the whole drawing buffer to transparent (scissor disabled); call once per frame before the draws. */
  clear(): void { const { gl } = this; gl.disable(gl.SCISSOR_TEST); gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT); }

  /**
   * Upload interleaved [x, y, …] data-space points. `xOrigin` is subtracted from x before the f32
   * conversion (use the window start) so epoch timestamps keep their precision.
   */
  setSeries(key: string, points: ArrayLike<number>, count = points.length / 2, xOrigin = 0): void {
    const { gl } = this;
    let s = this.series.get(key);
    if (!s) { s = { buffer: gl.createBuffer()!, count: 0, capacity: 0, xOrigin }; this.series.set(key, s); }
    const n = count * 2;
    if (this.scratch.length < n) this.scratch = new Float32Array(Math.max(n, this.scratch.length * 2));
    for (let i = 0; i < count; i++) { this.scratch[2 * i] = points[2 * i]! - xOrigin; this.scratch[2 * i + 1] = points[2 * i + 1]!; }
    gl.bindBuffer(gl.ARRAY_BUFFER, s.buffer);
    if (n > s.capacity) { gl.bufferData(gl.ARRAY_BUFFER, this.scratch.subarray(0, n), gl.DYNAMIC_DRAW); s.capacity = n; }
    else gl.bufferSubData(gl.ARRAY_BUFFER, 0, this.scratch.subarray(0, n));
    s.count = count; s.xOrigin = xOrigin;
  }

  /** Frees the GPU buffer of a series that is no longer drawn; unknown keys are ignored. */
  removeSeries(key: string): void {
    const s = this.series.get(key);
    if (s) { this.gl.deleteBuffer(s.buffer); this.series.delete(key); }
  }

  private pixelRatio = 1;
  private cssHeight = 0;

  /**
   * Draw one uploaded series with a clip transform computed for the same `xOrigin` as the upload.
   * `scissor` (CSS pixels, y-down) clips the strip to its lane.
   */
  draw(key: string, t: ClipTransform, style: LineStyle, scissor?: Rect): void {
    const s = this.series.get(key);
    if (!s || s.count < 2) return;
    const { gl } = this;
    if (scissor) {
      const r = this.pixelRatio;
      gl.enable(gl.SCISSOR_TEST);
      gl.scissor(Math.round(scissor.x * r), Math.round((this.cssHeight - scissor.y - scissor.h) * r), Math.round(scissor.w * r), Math.round(scissor.h * r));
    } else gl.disable(gl.SCISSOR_TEST);
    gl.useProgram(this.program);
    gl.bindBuffer(gl.ARRAY_BUFFER, s.buffer);
    gl.enableVertexAttribArray(this.aPos);
    gl.vertexAttribPointer(this.aPos, 2, gl.FLOAT, false, 0, 0);
    gl.uniform2f(this.uScale, t.sx, t.sy);
    gl.uniform2f(this.uOffset, t.ox, t.oy);
    const [r, g, b, a] = style.color;
    gl.uniform4f(this.uColor, r * a, g * a, b * a, a);
    gl.drawArrays(gl.LINE_STRIP, 0, s.count);
  }

  /** Frees every series buffer and the program; the renderer must not be used afterwards. The context itself is left to the browser. */
  dispose(): void {
    for (const s of this.series.values()) this.gl.deleteBuffer(s.buffer);
    this.series.clear();
    this.gl.deleteProgram(this.program);
  }
}

/** "#rrggbb" / "#rrggbbaa" / "rgba(r,g,b,a)" → [0..1] RGBA for WebGL uniforms. */
export function cssToRgba(css: string, opacity = 1): [number, number, number, number] {
  const s = css.trim();
  if (s.startsWith("#")) {
    const h = s.length === 4 || s.length === 5 ? [...s.slice(1)].map((c) => c + c).join("") : s.slice(1);
    const n = parseInt(h.padEnd(8, "f"), 16);
    return [((n >>> 24) & 255) / 255, ((n >>> 16) & 255) / 255, ((n >>> 8) & 255) / 255, ((n & 255) / 255) * opacity];
  }
  const m = /rgba?\(([^)]+)\)/.exec(s);
  if (m) { const p = m[1]!.split(/[\s,\/]+/).filter(Boolean).map(Number); return [p[0]! / 255, p[1]! / 255, p[2]! / 255, (p[3] ?? 1) * opacity]; }
  return [0, 0, 0, opacity];
}
