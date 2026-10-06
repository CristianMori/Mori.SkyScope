// Mori.SkyScope — Draws many polylines (decimated series) with WebGL as anti-aliased instanced quads: one GPU buffer per series in data space, one uniform transform per draw.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import type { ClipTransform, Rect } from "@mori/skyscope-core";

/**
 * Draws many polylines (decimated series) with WebGL: one GPU buffer per series in data space,
 * one uniform transform per draw, so pan/zoom never re-uploads. Each segment is an instanced screen-space
 * quad expanded by the stroke width in device pixels, with a one-pixel feathered edge and round caps in the
 * fragment shader, so strokes are thick and anti-aliased on every GPU. Without `ANGLE_instanced_arrays`
 * (rare on WebGL 1) the renderer falls back to native one-pixel `LINE_STRIP`s.
 */
/** Per-draw line appearance: `color` is straight (non-premultiplied) RGBA in 0..1, as `cssToRgba` returns; `width` in logical pixels (default 1.5), scaled by the device pixel ratio given to `resize`. */
export interface LineStyle { color: [number, number, number, number]; width?: number | undefined }

/** Stroke width used when a draw does not give one, in logical pixels. */
export const DEFAULT_LINE_WIDTH = 1.5;

interface SeriesBuffer { buffer: WebGLBuffer; count: number; capacity: number; xOrigin: number }

/**
 * Thick lines: one quad per segment. `a_p0`/`a_p1` read consecutive points of the series buffer with an instance
 * divisor (stride 8 bytes, `a_p1` offset by one point); `a_corner` is the quad corner (x: 0 = start, 1 = end; y: side).
 * The quad is expanded by the half width plus half a pixel on every side, and the fragment shader receives the
 * fragment's position in the segment's frame (along, across) in device pixels, plus the segment length.
 */
const THICK_VS = `attribute vec2 a_p0; attribute vec2 a_p1; attribute vec2 a_corner;
uniform vec2 u_scale; uniform vec2 u_offset; uniform vec2 u_viewport; uniform float u_halfWidth;
varying vec2 v_pos; varying float v_len;
void main() {
  vec2 h = u_viewport * 0.5;
  vec2 p0 = (a_p0 * u_scale + u_offset) * h, p1 = (a_p1 * u_scale + u_offset) * h;
  vec2 d = p1 - p0; float len = length(d);
  vec2 dir = len > 0.0 ? d / len : vec2(1.0, 0.0); vec2 nrm = vec2(-dir.y, dir.x);
  float ext = u_halfWidth + 0.5;
  float along = a_corner.x < 0.5 ? -ext : len + ext; float across = a_corner.y * ext;
  vec2 p = p0 + dir * along + nrm * across;
  v_pos = vec2(along, across); v_len = len;
  gl_Position = vec4(p / h, 0.0, 1.0);
}`;
/** Coverage from the distance to the segment (round caps): 1 inside the half width minus half a pixel, 0 beyond plus half a pixel. */
const THICK_FS = `#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
uniform vec4 u_color; uniform float u_halfWidth;
varying vec2 v_pos; varying float v_len;
void main() {
  float beyond = max(0.0, max(-v_pos.x, v_pos.x - v_len));
  float dist = length(vec2(beyond, v_pos.y));
  float a = clamp(u_halfWidth + 0.5 - dist, 0.0, 1.0);
  gl_FragColor = u_color * a;
}`;

/** Hairline fallback: native one-pixel line strips. */
const THIN_VS = `attribute vec2 a_pos; uniform vec2 u_scale; uniform vec2 u_offset;
void main() { gl_Position = vec4(a_pos * u_scale + u_offset, 0.0, 1.0); }`;
const THIN_FS = `precision mediump float; uniform vec4 u_color; void main() { gl_FragColor = u_color; }`;

/** The two draw calls of `ANGLE_instanced_arrays` the thick path needs (typed here so the fallback can be a null). */
interface Instancing { divisor(index: number, divisor: number): void; draw(mode: number, first: number, count: number, instances: number): void }

/**
 * The series line renderer: keeps one vertex buffer per series key in data space (x relative to an origin) and draws
 * each with a single scale/offset uniform pair. WebGL 1 with `ANGLE_instanced_arrays` is enough; blending is premultiplied alpha.
 */
export class WebGLLineRenderer {
  /** The underlying WebGL 1 context, exposed for hosts that need to query it; do not change its global state. */
  readonly gl: WebGLRenderingContext;
  /** True when segments are drawn as anti-aliased instanced quads; false on the native hairline fallback (no instancing extension). */
  readonly thick: boolean;
  private readonly instancing: Instancing | null;
  private readonly program: WebGLProgram;
  private readonly aPos: number; private readonly aP1: number; private readonly aCorner: number;
  private readonly uScale: WebGLUniformLocation;
  private readonly uOffset: WebGLUniformLocation;
  private readonly uColor: WebGLUniformLocation;
  private readonly uViewport: WebGLUniformLocation | null;
  private readonly uHalfWidth: WebGLUniformLocation | null;
  private readonly corners: WebGLBuffer | null;
  private readonly series = new Map<string, SeriesBuffer>();
  private scratch = new Float32Array(0);
  private pixelRatio = 1;
  private cssHeight = 0;
  private bufW = 1; private bufH = 1;

  /** Acquires a WebGL 1 context on `canvas` (transparent, antialiased, premultiplied) and compiles the line program. Throws when WebGL is unavailable. */
  constructor(readonly canvas: HTMLCanvasElement | OffscreenCanvas, options: WebGLContextAttributes = {}) {
    const gl = canvas.getContext("webgl", { antialias: true, premultipliedAlpha: true, alpha: true, preserveDrawingBuffer: false, ...options }) as WebGLRenderingContext | null;
    if (!gl) throw new Error("WebGL unavailable");
    this.gl = gl;
    this.instancing = instancing(gl);
    this.thick = this.instancing !== null;
    if (this.thick) {
      this.program = this.link(THICK_VS, THICK_FS);
      this.aPos = gl.getAttribLocation(this.program, "a_p0");
      this.aP1 = gl.getAttribLocation(this.program, "a_p1");
      this.aCorner = gl.getAttribLocation(this.program, "a_corner");
      this.uViewport = gl.getUniformLocation(this.program, "u_viewport");
      this.uHalfWidth = gl.getUniformLocation(this.program, "u_halfWidth");
      this.corners = gl.createBuffer()!;
      gl.bindBuffer(gl.ARRAY_BUFFER, this.corners);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, -1, 0, 1, 1, -1, 1, 1]), gl.STATIC_DRAW);
    } else {
      this.program = this.link(THIN_VS, THIN_FS);
      this.aPos = gl.getAttribLocation(this.program, "a_pos");
      this.aP1 = -1; this.aCorner = -1; this.uViewport = null; this.uHalfWidth = null; this.corners = null;
    }
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
    this.pixelRatio = pixelRatio; this.cssHeight = height; this.bufW = w; this.bufH = h;
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

  /**
   * Draw one uploaded series with a clip transform computed for the same `xOrigin` as the upload.
   * `style.width` is in logical pixels (default `DEFAULT_LINE_WIDTH`) and is multiplied by the pixel ratio of the last `resize`.
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
    gl.uniform2f(this.uScale, t.sx, t.sy);
    gl.uniform2f(this.uOffset, t.ox, t.oy);
    const [r, g, b, a] = style.color;
    gl.uniform4f(this.uColor, r * a, g * a, b * a, a);
    gl.bindBuffer(gl.ARRAY_BUFFER, s.buffer);
    gl.enableVertexAttribArray(this.aPos);
    const inst = this.instancing;
    if (!inst) {
      gl.vertexAttribPointer(this.aPos, 2, gl.FLOAT, false, 0, 0);
      gl.drawArrays(gl.LINE_STRIP, 0, s.count);
      return;
    }
    const halfWidth = Math.max(0, (style.width ?? DEFAULT_LINE_WIDTH) * this.pixelRatio) * 0.5;
    gl.uniform2f(this.uViewport!, this.bufW, this.bufH);
    gl.uniform1f(this.uHalfWidth!, halfWidth);
    gl.vertexAttribPointer(this.aPos, 2, gl.FLOAT, false, 8, 0); inst.divisor(this.aPos, 1);
    gl.enableVertexAttribArray(this.aP1);
    gl.vertexAttribPointer(this.aP1, 2, gl.FLOAT, false, 8, 8); inst.divisor(this.aP1, 1);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.corners);
    gl.enableVertexAttribArray(this.aCorner);
    gl.vertexAttribPointer(this.aCorner, 2, gl.FLOAT, false, 0, 0); inst.divisor(this.aCorner, 0);
    inst.draw(gl.TRIANGLE_STRIP, 0, 4, s.count - 1);
  }

  /** Frees every series buffer and the program; the renderer must not be used afterwards. The context itself is left to the browser. */
  dispose(): void {
    for (const s of this.series.values()) this.gl.deleteBuffer(s.buffer);
    this.series.clear();
    if (this.corners) this.gl.deleteBuffer(this.corners);
    this.gl.deleteProgram(this.program);
  }
}

/** Instanced drawing on a WebGL 1 context through `ANGLE_instanced_arrays`, or null when the extension is missing. */
function instancing(gl: WebGLRenderingContext): Instancing | null {
  const ext = gl.getExtension("ANGLE_instanced_arrays");
  if (!ext) return null;
  return {
    divisor: (index, divisor) => ext.vertexAttribDivisorANGLE(index, divisor),
    draw: (mode, first, count, instances) => ext.drawArraysInstancedANGLE(mode, first, count, instances),
  };
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
