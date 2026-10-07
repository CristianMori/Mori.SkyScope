// Mori.SkyScope — WebGL2 implementation of Painter3D: one VAO per mesh key, re-uploaded when the mesh version changes; three programs (flat/lit geometry, t…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

import { isRasterImage, type ImageHandle, type Mat4, type Material3D, type Mesh3D, type Painter3D, resolveMaterial, meshVertexCount } from "@cmori/skyscope-core";
import { cssToRgba } from "./webgl-lines.js";

/**
 * WebGL2 implementation of `Painter3D`: one VAO per mesh key, re-uploaded when the mesh version changes; three
 * programs (flat/lit geometry, textured quad). GLSL ES 300 — the same shader source drives the OpenTK painter
 * on the desktop. Lines wider than one device pixel are drawn as instanced screen-space quads.
 */
const GEOM_VS = `#version 300 es
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec4 a_col;
layout(location = 2) in vec3 a_nor;
uniform mat4 u_mvp; uniform mat4 u_model; uniform vec4 u_color; uniform float u_pointSize; uniform int u_hasColor; uniform int u_lit; uniform vec3 u_light;
out vec4 v_col;
void main() {
  gl_Position = u_mvp * vec4(a_pos, 1.0);
  gl_PointSize = u_pointSize;
  vec4 c = u_color * (u_hasColor == 1 ? a_col : vec4(1.0));
  if (u_lit == 1) { vec3 n = normalize(mat3(u_model) * a_nor); float d = abs(dot(n, u_light)); c.rgb *= 0.35 + 0.65 * d; }
  v_col = c;
}`;
const GEOM_FS = `#version 300 es
precision mediump float;
in vec4 v_col; out vec4 o_col;
void main() { o_col = vec4(v_col.rgb * v_col.a, v_col.a); }`;
const TEX_VS = `#version 300 es
layout(location = 0) in vec3 a_pos;
layout(location = 1) in vec2 a_uv;
uniform mat4 u_mvp;
out vec2 v_uv;
void main() { gl_Position = u_mvp * vec4(a_pos, 1.0); v_uv = a_uv; }`;
const TEX_FS = `#version 300 es
precision mediump float;
in vec2 v_uv; out vec4 o_col;
uniform sampler2D u_tex; uniform vec4 u_color;
void main() { vec4 t = texture(u_tex, v_uv) * u_color; o_col = vec4(t.rgb * t.a, t.a); }`;

/** Screen-space thick lines: each segment is an instanced quad; the two endpoints come from the same buffers with an instance divisor. */
const LINE_VS = `#version 300 es
layout(location = 0) in vec3 a_p0;
layout(location = 1) in vec4 a_c0;
layout(location = 2) in vec3 a_p1;
layout(location = 3) in vec4 a_c1;
uniform mat4 u_mvp; uniform vec4 u_color; uniform vec2 u_viewport; uniform float u_width; uniform int u_hasColor;
out vec4 v_col;
void main() {
  vec4 c0 = u_mvp * vec4(a_p0, 1.0), c1 = u_mvp * vec4(a_p1, 1.0);
  // clip against the near plane so segments crossing behind the eye do not flip
  if (c0.w < 0.001 && c1.w < 0.001) { gl_Position = vec4(2.0, 2.0, 2.0, 1.0); v_col = vec4(0.0); return; }
  if (c0.w < 0.001) { float t = (0.001 - c0.w) / (c1.w - c0.w); c0 = mix(c0, c1, t); }
  if (c1.w < 0.001) { float t = (0.001 - c1.w) / (c0.w - c1.w); c1 = mix(c1, c0, t); }
  vec2 n0 = c0.xy / c0.w, n1 = c1.xy / c1.w;
  vec2 d = (n1 - n0) * u_viewport; float len = length(d);
  vec2 dir = len > 0.0 ? d / len : vec2(1.0, 0.0); vec2 nrm = vec2(-dir.y, dir.x) * u_width * 0.5;
  bool end = gl_VertexID >= 2; float side = (gl_VertexID == 1 || gl_VertexID == 3) ? 1.0 : -1.0;
  vec4 c = end ? c1 : c0;
  gl_Position = vec4(c.xy + nrm * side / u_viewport * 2.0 * c.w, c.z, c.w);
  v_col = u_color * (u_hasColor == 1 ? (end ? a_c1 : a_c0) : vec4(1.0));
}`;

interface GpuMesh { vao: WebGLVertexArrayObject; pos: WebGLBuffer; col: WebGLBuffer | null; nor: WebGLBuffer | null; idx: WebGLBuffer | null; version: number; hasColor: boolean; hasNormal: boolean; indexed: boolean; lastUsed: number }
interface GpuTexture { tex: WebGLTexture; version: number; lastUsed: number }

function mul(a: Mat4, b: Mat4): Float32Array {
  const o = new Float32Array(16);
  for (let c = 0; c < 4; c++) for (let r = 0; r < 4; r++) o[c * 4 + r] = a[r]! * b[c * 4]! + a[4 + r]! * b[c * 4 + 1]! + a[8 + r]! * b[c * 4 + 2]! + a[12 + r]! * b[c * 4 + 3]!;
  return o;
}

/**
 * The WebGL2 `Painter3D`. GPU meshes and textures are cached by key/handle and re-uploaded only when their version
 * changes; entries unused for about ten minutes are evicted in `end()`. All colours are premultiplied before upload.
 */
export class WebGLPainter3D implements Painter3D {
  /** The underlying WebGL2 context, exposed for hosts that need to query it; do not change its global state. */
  readonly gl: WebGL2RenderingContext;
  private readonly geom: WebGLProgram; private readonly tex: WebGLProgram; private readonly line: WebGLProgram;
  private readonly lineVaos = new Map<string, { vao: WebGLVertexArrayObject; version: number; strip: boolean; hasColor: boolean }>();
  private readonly u: Record<string, WebGLUniformLocation | null> = {};
  private readonly meshes = new Map<string, GpuMesh>();
  private readonly textures = new Map<ImageHandle, GpuTexture>();
  private viewProj: Mat4 = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
  private light: [number, number, number] = [0, 0, 1];
  private frame = 0;
  private quad: { vao: WebGLVertexArrayObject; buf: WebGLBuffer } | null = null;
  /** Current viewport in CSS pixels (`width`, `height`) and the device pixel ratio, as last set by `resize`; thick lines and point sizes scale by `pixelRatio`. */
  width = 1; height = 1; pixelRatio = 1;

  /** Acquires a WebGL2 context on `canvas` (transparent, antialiased, premultiplied), compiles the three programs and enables blending and depth testing. Throws when WebGL2 is unavailable. */
  constructor(readonly canvas: HTMLCanvasElement | OffscreenCanvas, options: WebGLContextAttributes = {}) {
    const gl = canvas.getContext("webgl2", { antialias: true, premultipliedAlpha: true, alpha: true, preserveDrawingBuffer: false, ...options }) as WebGL2RenderingContext | null;
    if (!gl) throw new Error("WebGL2 unavailable");
    this.gl = gl;
    this.geom = this.link(GEOM_VS, GEOM_FS); this.tex = this.link(TEX_VS, TEX_FS); this.line = this.link(LINE_VS, GEOM_FS);
    for (const n of ["u_mvp", "u_model", "u_color", "u_pointSize", "u_hasColor", "u_lit", "u_light"]) this.u[n] = gl.getUniformLocation(this.geom, n);
    for (const n of ["u_mvp", "u_color", "u_tex"]) this.u[`t.${n}`] = gl.getUniformLocation(this.tex, n);
    for (const n of ["u_mvp", "u_color", "u_viewport", "u_width", "u_hasColor"]) this.u[`l.${n}`] = gl.getUniformLocation(this.line, n);
    gl.enable(gl.BLEND); gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LEQUAL);
  }

  private link(vs: string, fs: string): WebGLProgram {
    const { gl } = this;
    const compile = (type: number, src: string): WebGLShader => {
      const sh = gl.createShader(type)!; gl.shaderSource(sh, src); gl.compileShader(sh);
      if (!gl.getShaderParameter(sh, gl.COMPILE_STATUS)) throw new Error(`shader: ${gl.getShaderInfoLog(sh) ?? "?"}`);
      return sh;
    };
    const p = gl.createProgram()!;
    gl.attachShader(p, compile(gl.VERTEX_SHADER, vs)); gl.attachShader(p, compile(gl.FRAGMENT_SHADER, fs)); gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(`program: ${gl.getProgramInfoLog(p) ?? "?"}`);
    return p;
  }

  /** Size the drawing buffer for `pixelRatio` and set the viewport. */
  resize(width: number, height: number, pixelRatio = 1): void {
    const w = Math.max(1, Math.round(width * pixelRatio)), h = Math.max(1, Math.round(height * pixelRatio));
    if (this.canvas.width !== w) this.canvas.width = w;
    if (this.canvas.height !== h) this.canvas.height = h;
    this.width = width; this.height = height; this.pixelRatio = pixelRatio;
    this.gl.viewport(0, 0, w, h);
  }

  /** Starts a frame: stores view × projection (column-major), derives the light direction from the view's z axis and clears colour and depth (to `clear`, a CSS colour, or transparent). */
  begin(view: Mat4, proj: Mat4, clear: string | null = null): void {
    const { gl } = this;
    this.frame++;
    this.viewProj = Array.from(mul(proj, view));
    this.light = [view[2]!, view[6]!, view[10]!];
    if (clear) { const [r, g, b, a] = cssToRgba(clear); gl.clearColor(r * a, g * a, b * a, a); } else gl.clearColor(0, 0, 0, 0);
    gl.depthMask(true);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
  }
  /** Ends a frame; every 600th frame it evicts GPU meshes and textures that no draw has touched for 36000 frames. */
  end(): void {
    // evict GPU meshes/textures unused for ~10 minutes at 60 fps
    if (this.frame % 600 === 0) {
      for (const [k, m] of this.meshes) if (this.frame - m.lastUsed > 36000) { this.free(m); this.meshes.delete(k); for (const suffix of [":strip", ":list"]) { const v = this.lineVaos.get(k + suffix); if (v) { this.gl.deleteVertexArray(v.vao); this.lineVaos.delete(k + suffix); } } }
      for (const [k, t] of this.textures) if (this.frame - t.lastUsed > 36000) { this.gl.deleteTexture(t.tex); this.textures.delete(k); }
    }
  }

  private upload(mesh: Mesh3D): GpuMesh {
    const { gl } = this;
    let m = this.meshes.get(mesh.key);
    if (m && m.version === mesh.version) { m.lastUsed = this.frame; return m; }
    if (!m) {
      m = { vao: gl.createVertexArray()!, pos: gl.createBuffer()!, col: null, nor: null, idx: null, version: -1, hasColor: false, hasNormal: false, indexed: false, lastUsed: this.frame };
      this.meshes.set(mesh.key, m);
    }
    gl.bindVertexArray(m.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, m.pos); gl.bufferData(gl.ARRAY_BUFFER, mesh.positions, gl.DYNAMIC_DRAW);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 0, 0);
    if (mesh.colors) {
      m.col ??= gl.createBuffer()!;
      gl.bindBuffer(gl.ARRAY_BUFFER, m.col); gl.bufferData(gl.ARRAY_BUFFER, mesh.colors, gl.DYNAMIC_DRAW);
      gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 4, gl.UNSIGNED_BYTE, true, 0, 0);
    } else gl.disableVertexAttribArray(1);
    if (mesh.normals) {
      m.nor ??= gl.createBuffer()!;
      gl.bindBuffer(gl.ARRAY_BUFFER, m.nor); gl.bufferData(gl.ARRAY_BUFFER, mesh.normals, gl.DYNAMIC_DRAW);
      gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 3, gl.FLOAT, false, 0, 0);
    } else gl.disableVertexAttribArray(2);
    if (mesh.indices) { m.idx ??= gl.createBuffer()!; gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, m.idx); gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, mesh.indices, gl.DYNAMIC_DRAW); }
    gl.bindVertexArray(null);
    m.version = mesh.version; m.hasColor = !!mesh.colors; m.hasNormal = !!mesh.normals; m.indexed = !!mesh.indices; m.lastUsed = this.frame;
    return m;
  }
  private free(m: GpuMesh): void {
    const { gl } = this;
    gl.deleteVertexArray(m.vao); gl.deleteBuffer(m.pos); if (m.col) gl.deleteBuffer(m.col); if (m.nor) gl.deleteBuffer(m.nor); if (m.idx) gl.deleteBuffer(m.idx);
  }

  private drawMesh(mesh: Mesh3D, material: Material3D | undefined, mode: number): void {
    const { gl } = this, r = resolveMaterial(material), m = this.upload(mesh), count = meshVertexCount(mesh);
    if (count === 0) return;
    gl.useProgram(this.geom);
    gl.bindVertexArray(m.vao);
    const mvp = r.model ? mul(this.viewProj, r.model) : Float32Array.from(this.viewProj);
    gl.uniformMatrix4fv(this.u.u_mvp!, false, mvp);
    gl.uniformMatrix4fv(this.u.u_model!, false, Float32Array.from(r.model ?? [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]));
    const [cr, cg, cb, ca] = cssToRgba(r.color, r.opacity);
    gl.uniform4f(this.u.u_color!, cr, cg, cb, ca);
    gl.uniform1f(this.u.u_pointSize!, r.pointSize * this.pixelRatio);
    gl.uniform1i(this.u.u_hasColor!, m.hasColor ? 1 : 0);
    gl.uniform1i(this.u.u_lit!, r.lit && m.hasNormal ? 1 : 0);
    gl.uniform3f(this.u.u_light!, this.light[0], this.light[1], this.light[2]);
    if (!m.hasColor) gl.vertexAttrib4f(1, 1, 1, 1, 1);
    if (!m.hasNormal) gl.vertexAttrib3f(2, 0, 0, 1);
    if (r.depthTest) gl.enable(gl.DEPTH_TEST); else gl.disable(gl.DEPTH_TEST);
    gl.depthMask(ca >= 1);
    if (m.indexed) gl.drawElements(mode, count, gl.UNSIGNED_INT, 0); else gl.drawArrays(mode, 0, count);
    gl.bindVertexArray(null);
  }

  /** Draws the mesh vertices as points; the size comes from `material.pointSize` in CSS pixels. */
  points(mesh: Mesh3D, material?: Material3D): void { this.drawMesh(mesh, material, this.gl.POINTS); }
  /** Draws the mesh as line segments (pairs) or, with `strip`, as one connected strip. Widths above ~1.5 device pixels use the instanced thick-line path, except for indexed meshes, which stay hairlines. */
  lines(mesh: Mesh3D, material?: Material3D, strip = false): void {
    const r = resolveMaterial(material), px = r.lineWidth * this.pixelRatio;
    if (px <= 1.5 || mesh.indices) { this.drawMesh(mesh, material, strip ? this.gl.LINE_STRIP : this.gl.LINES); return; }
    this.drawThickLines(mesh, r, strip, px);
  }

  /** Instanced quads, one per segment; endpoint attributes read the mesh buffers with divisor 1 and a 12-byte (strip) or 24-byte (list) stride. */
  private drawThickLines(mesh: Mesh3D, r: ReturnType<typeof resolveMaterial>, strip: boolean, px: number): void {
    const { gl } = this, m = this.upload(mesh), count = meshVertexCount(mesh);
    const segments = strip ? count - 1 : Math.floor(count / 2);
    if (segments <= 0) return;
    const key = mesh.key + (strip ? ":strip" : ":list");
    let v = this.lineVaos.get(key);
    if (!v || v.version !== mesh.version || v.hasColor !== m.hasColor) {
      if (!v) { v = { vao: gl.createVertexArray()!, version: -1, strip, hasColor: false }; this.lineVaos.set(key, v); }
      gl.bindVertexArray(v.vao);
      const stride = strip ? 12 : 24, cstride = strip ? 4 : 8;
      gl.bindBuffer(gl.ARRAY_BUFFER, m.pos);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, stride, 0); gl.vertexAttribDivisor(0, 1);
      gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 3, gl.FLOAT, false, stride, 12); gl.vertexAttribDivisor(2, 1);
      if (m.hasColor && m.col) {
        gl.bindBuffer(gl.ARRAY_BUFFER, m.col);
        gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 4, gl.UNSIGNED_BYTE, true, cstride, 0); gl.vertexAttribDivisor(1, 1);
        gl.enableVertexAttribArray(3); gl.vertexAttribPointer(3, 4, gl.UNSIGNED_BYTE, true, cstride, 4); gl.vertexAttribDivisor(3, 1);
      } else { gl.disableVertexAttribArray(1); gl.disableVertexAttribArray(3); }
      gl.bindVertexArray(null);
      v.version = mesh.version; v.hasColor = m.hasColor;
    }
    gl.useProgram(this.line);
    gl.bindVertexArray(v.vao);
    gl.uniformMatrix4fv(this.u["l.u_mvp"]!, false, r.model ? mul(this.viewProj, r.model) : Float32Array.from(this.viewProj));
    const [cr, cg, cb, ca] = cssToRgba(r.color, r.opacity);
    gl.uniform4f(this.u["l.u_color"]!, cr, cg, cb, ca);
    gl.uniform2f(this.u["l.u_viewport"]!, this.width * this.pixelRatio, this.height * this.pixelRatio);
    gl.uniform1f(this.u["l.u_width"]!, px);
    gl.uniform1i(this.u["l.u_hasColor"]!, m.hasColor ? 1 : 0);
    if (r.depthTest) gl.enable(gl.DEPTH_TEST); else gl.disable(gl.DEPTH_TEST);
    gl.depthMask(ca >= 1);
    gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, segments);
    gl.bindVertexArray(null);
  }
  /** Draws the mesh as a triangle list; lit when the material asks for it and the mesh carries normals. Translucent draws do not write depth. */
  triangles(mesh: Mesh3D, material?: Material3D): void { this.drawMesh(mesh, material, this.gl.TRIANGLES); }

  private texture(image: ImageHandle): WebGLTexture {
    const { gl } = this;
    const version = isRasterImage(image) ? image.version : 0;
    let t = this.textures.get(image);
    if (t && t.version === version) { t.lastUsed = this.frame; return t.tex; }
    if (!t) { t = { tex: gl.createTexture()!, version: -1, lastUsed: this.frame }; this.textures.set(image, t); }
    gl.bindTexture(gl.TEXTURE_2D, t.tex);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, true);
    if (isRasterImage(image)) gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, image.width, image.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, image.rgba);
    else gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, image as unknown as TexImageSource);
    gl.pixelStorei(gl.UNPACK_PREMULTIPLY_ALPHA_WEBGL, false);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    t.version = version; t.lastUsed = this.frame;
    return t.tex;
  }

  /** Corners bottom-left, bottom-right, top-right, top-left map to image pixel corners (0,H), (W,H), (W,0), (0,0). */
  image(image: ImageHandle, corners: ArrayLike<number>, material?: Material3D): void {
    const { gl } = this, r = resolveMaterial(material);
    if (!this.quad) {
      const vao = gl.createVertexArray()!, buf = gl.createBuffer()!;
      gl.bindVertexArray(vao); gl.bindBuffer(gl.ARRAY_BUFFER, buf);
      gl.bufferData(gl.ARRAY_BUFFER, 4 * 5 * 4, gl.DYNAMIC_DRAW);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 20, 0);
      gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 20, 12);
      gl.bindVertexArray(null);
      this.quad = { vao, buf };
    }
    const c = corners, data = Float32Array.from([c[0]!, c[1]!, c[2]!, 0, 1, c[3]!, c[4]!, c[5]!, 1, 1, c[9]!, c[10]!, c[11]!, 0, 0, c[6]!, c[7]!, c[8]!, 1, 0]);
    gl.useProgram(this.tex);
    gl.bindVertexArray(this.quad.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.quad.buf); gl.bufferSubData(gl.ARRAY_BUFFER, 0, data);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.texture(image)); gl.uniform1i(this.u["t.u_tex"]!, 0);
    gl.uniformMatrix4fv(this.u["t.u_mvp"]!, false, r.model ? mul(this.viewProj, r.model) : Float32Array.from(this.viewProj));
    const [cr, cg, cb, ca] = cssToRgba(r.color, r.opacity);
    gl.uniform4f(this.u["t.u_color"]!, cr, cg, cb, ca);
    if (r.depthTest) gl.enable(gl.DEPTH_TEST); else gl.disable(gl.DEPTH_TEST);
    gl.depthMask(false);
    gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
    gl.depthMask(true);
    gl.bindVertexArray(null);
  }

  /** Frees every cached mesh, line VAO, texture, the quad and the programs; the painter must not be used afterwards. */
  dispose(): void {
    const { gl } = this;
    for (const m of this.meshes.values()) this.free(m);
    this.meshes.clear();
    for (const v of this.lineVaos.values()) gl.deleteVertexArray(v.vao);
    this.lineVaos.clear();
    gl.deleteProgram(this.line);
    for (const t of this.textures.values()) gl.deleteTexture(t.tex);
    this.textures.clear();
    if (this.quad) { gl.deleteVertexArray(this.quad.vao); gl.deleteBuffer(this.quad.buf); }
    gl.deleteProgram(this.geom); gl.deleteProgram(this.tex);
  }
}
