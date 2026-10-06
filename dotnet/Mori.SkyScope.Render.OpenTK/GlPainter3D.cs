// Mori.SkyScope — OpenGL 3.3 implementation of the 3D painter contract, sharing its shader bodies with the WebGL2 painter.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;
using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene3D;
using OpenTK.Graphics.OpenGL4;

namespace Mori.SkyScope.Render.OpenTK;

/// <summary>
/// OpenGL 3.3 core implementation of <see cref="IPainter3D"/>: one VAO per mesh key re-uploaded when the mesh
/// version changes, the same shader bodies as the WebGL2 painter (<c>render/src/webgl-painter3d.ts</c>) behind a
/// <c>#version 330 core</c> preamble. Create it on the thread that owns the GL context; call <see cref="Resize"/>
/// whenever the framebuffer size changes.
/// </summary>
public sealed class GlPainter3D : IPainter3D, IDisposable
{
    private const string GeomVs = """
        #version 330 core
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
        }
        """;
    private const string GeomFs = """
        #version 330 core
        in vec4 v_col; out vec4 o_col;
        void main() { o_col = vec4(v_col.rgb * v_col.a, v_col.a); }
        """;
    private const string TexVs = """
        #version 330 core
        layout(location = 0) in vec3 a_pos;
        layout(location = 1) in vec2 a_uv;
        uniform mat4 u_mvp;
        out vec2 v_uv;
        void main() { gl_Position = u_mvp * vec4(a_pos, 1.0); v_uv = a_uv; }
        """;
    private const string TexFs = """
        #version 330 core
        in vec2 v_uv; out vec4 o_col;
        uniform sampler2D u_tex; uniform vec4 u_color;
        void main() { vec4 t = texture(u_tex, v_uv) * u_color; o_col = vec4(t.rgb * t.a, t.a); }
        """;

    /// <summary>Screen-space thick lines: one instanced quad per segment; endpoints read the mesh buffers with an instance divisor.</summary>
    private const string LineVs = """
        #version 330 core
        layout(location = 0) in vec3 a_p0;
        layout(location = 1) in vec4 a_c0;
        layout(location = 2) in vec3 a_p1;
        layout(location = 3) in vec4 a_c1;
        uniform mat4 u_mvp; uniform vec4 u_color; uniform vec2 u_viewport; uniform float u_width; uniform int u_hasColor;
        out vec4 v_col;
        void main() {
          vec4 c0 = u_mvp * vec4(a_p0, 1.0), c1 = u_mvp * vec4(a_p1, 1.0);
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
        }
        """;

    private sealed class LineVao { public int Vao; public int Version = -1; public bool HasColor; }
    private readonly Dictionary<string, LineVao> _lineVaos = [];
    private readonly int _line;

    private sealed class GpuMesh { public int Vao, Pos, Col, Nor, Idx; public int Version = -1; public bool HasColor, HasNormal, Indexed; public long LastUsed; }
    private sealed class GpuTexture { public int Tex; public long Version = -1; public long LastUsed; }

    private readonly int _geom, _tex;
    private readonly Dictionary<string, int> _u = [];
    private readonly Dictionary<string, GpuMesh> _meshes = [];
    private readonly Dictionary<IImageHandle, GpuTexture> _textures = [];
    private double[] _viewProj = Mat4.Identity;
    private float[] _light = [0, 0, 1];
    private long _frame;
    private int _quadVao, _quadBuf;

    /// <summary>Logical viewport width in device-independent units, as set by <see cref="Resize"/>.</summary>
    public double Width { get; private set; } = 1;
    /// <summary>Logical viewport height in device-independent units, as set by <see cref="Resize"/>.</summary>
    public double Height { get; private set; } = 1;
    /// <summary>Device pixels per logical unit, as set by <see cref="Resize"/>.</summary>
    public double PixelRatio { get; private set; } = 1;

    /// <summary>Compiles and links the three shader programs and enables premultiplied-alpha blending, depth testing and program point size. Requires a current OpenGL 3.3 context.</summary>
    public GlPainter3D()
    {
        _geom = Link(GeomVs, GeomFs); _tex = Link(TexVs, TexFs); _line = Link(LineVs, GeomFs);
        foreach (var n in new[] { "u_mvp", "u_model", "u_color", "u_pointSize", "u_hasColor", "u_lit", "u_light" }) _u[n] = GL.GetUniformLocation(_geom, n);
        foreach (var n in new[] { "u_mvp", "u_color", "u_tex" }) _u["t." + n] = GL.GetUniformLocation(_tex, n);
        foreach (var n in new[] { "u_mvp", "u_color", "u_viewport", "u_width", "u_hasColor" }) _u["l." + n] = GL.GetUniformLocation(_line, n);
        GL.Enable(EnableCap.Blend); GL.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Lequal);
        GL.Enable(EnableCap.ProgramPointSize);
    }

    private static int Link(string vs, string fs)
    {
        static int Compile(ShaderType type, string src)
        {
            var sh = GL.CreateShader(type); GL.ShaderSource(sh, src); GL.CompileShader(sh);
            GL.GetShader(sh, ShaderParameter.CompileStatus, out var ok);
            if (ok == 0) throw new InvalidOperationException("shader: " + GL.GetShaderInfoLog(sh));
            return sh;
        }
        var p = GL.CreateProgram();
        var v = Compile(ShaderType.VertexShader, vs); var f = Compile(ShaderType.FragmentShader, fs);
        GL.AttachShader(p, v); GL.AttachShader(p, f); GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out var linked);
        if (linked == 0) throw new InvalidOperationException("program: " + GL.GetProgramInfoLog(p));
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }

    /// <summary>Logical size in device-independent units and the device pixel ratio; sets the GL viewport.</summary>
    public void Resize(double width, double height, double pixelRatio = 1)
    {
        Width = width; Height = height; PixelRatio = pixelRatio;
        GL.Viewport(0, 0, Math.Max(1, (int)Math.Round(width * pixelRatio)), Math.Max(1, (int)Math.Round(height * pixelRatio)));
    }

    private static float[] F(double[] m) { var o = new float[16]; for (var i = 0; i < 16; i++) o[i] = (float)m[i]; return o; }

    /// <summary>Starts a frame: stores the view-projection matrix, takes the light direction from the Z axis of the view and clears colour and depth to <paramref name="clear"/> (a CSS colour) or to transparent.</summary>
    public void Begin(double[] view, double[] proj, string? clear = null)
    {
        _frame++;
        _viewProj = Mat4.Mul(proj, view);
        _light = [(float)view[2], (float)view[6], (float)view[10]];
        if (clear is not null && CssColor.TryParse(clear, out var c)) { float a = (float)c.A; GL.ClearColor(c.R / 255f * a, c.G / 255f * a, c.B / 255f * a, a); }
        else GL.ClearColor(0, 0, 0, 0);
        GL.DepthMask(true);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }
    /// <summary>Ends a frame. Every 600 frames it frees the meshes and textures that have not been drawn for 36000 frames.</summary>
    public void End()
    {
        if (_frame % 600 != 0) return;
        foreach (var (k, m) in _meshes.ToList()) if (_frame - m.LastUsed > 36000) { Free(m); _meshes.Remove(k); foreach (var suffix in new[] { ":strip", ":list" }) if (_lineVaos.Remove(k + suffix, out var lv)) GL.DeleteVertexArray(lv.Vao); }
        foreach (var (k, t) in _textures.ToList()) if (_frame - t.LastUsed > 36000) { GL.DeleteTexture(t.Tex); _textures.Remove(k); }
    }

    private GpuMesh Upload(Mesh3D mesh)
    {
        if (_meshes.TryGetValue(mesh.Key, out var m) && m.Version == mesh.Version) { m.LastUsed = _frame; return m; }
        if (m is null) { m = new GpuMesh { Vao = GL.GenVertexArray(), Pos = GL.GenBuffer() }; _meshes[mesh.Key] = m; }
        GL.BindVertexArray(m.Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, m.Pos); GL.BufferData(BufferTarget.ArrayBuffer, mesh.Positions.Length * 4, mesh.Positions, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);
        if (mesh.Colors is { } colors)
        {
            if (m.Col == 0) m.Col = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.Col); GL.BufferData(BufferTarget.ArrayBuffer, colors.Length, colors, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, 0, 0);
        }
        else GL.DisableVertexAttribArray(1);
        if (mesh.Normals is { } normals)
        {
            if (m.Nor == 0) m.Nor = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.Nor); GL.BufferData(BufferTarget.ArrayBuffer, normals.Length * 4, normals, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, 0, 0);
        }
        else GL.DisableVertexAttribArray(2);
        if (mesh.Indices is { } idx)
        {
            if (m.Idx == 0) m.Idx = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, m.Idx); GL.BufferData(BufferTarget.ElementArrayBuffer, idx.Length * 4, idx, BufferUsageHint.DynamicDraw);
        }
        GL.BindVertexArray(0);
        m.Version = mesh.Version; m.HasColor = mesh.Colors is not null; m.HasNormal = mesh.Normals is not null; m.Indexed = mesh.Indices is not null; m.LastUsed = _frame;
        return m;
    }
    private static void Free(GpuMesh m)
    {
        GL.DeleteVertexArray(m.Vao); GL.DeleteBuffer(m.Pos);
        if (m.Col != 0) GL.DeleteBuffer(m.Col); if (m.Nor != 0) GL.DeleteBuffer(m.Nor); if (m.Idx != 0) GL.DeleteBuffer(m.Idx);
    }

    private static (float R, float G, float B, float A) Rgba(string css, double opacity)
    {
        if (!CssColor.TryParse(css, out var c)) return (0, 0, 0, (float)opacity);
        return (c.R / 255f, c.G / 255f, c.B / 255f, (float)(c.A * opacity));
    }

    private void DrawMesh(Mesh3D mesh, Material3D? material, PrimitiveType mode)
    {
        var r = material ?? Material3D.Default; var m = Upload(mesh); var count = mesh.VertexCount;
        if (count == 0) return;
        GL.UseProgram(_geom);
        GL.BindVertexArray(m.Vao);
        GL.UniformMatrix4(_u["u_mvp"], 1, false, F(r.Model is null ? _viewProj : Mat4.Mul(_viewProj, r.Model)));
        GL.UniformMatrix4(_u["u_model"], 1, false, F(r.Model ?? Mat4.Identity));
        var (cr, cg, cb, ca) = Rgba(r.Color, r.Opacity);
        GL.Uniform4(_u["u_color"], cr, cg, cb, ca);
        GL.Uniform1(_u["u_pointSize"], (float)(r.PointSize * PixelRatio));
        GL.Uniform1(_u["u_hasColor"], m.HasColor ? 1 : 0);
        GL.Uniform1(_u["u_lit"], r.Lit && m.HasNormal ? 1 : 0);
        GL.Uniform3(_u["u_light"], _light[0], _light[1], _light[2]);
        if (!m.HasColor) GL.VertexAttrib4(1, 1f, 1f, 1f, 1f);
        if (!m.HasNormal) GL.VertexAttrib3(2, 0f, 0f, 1f);
        if (r.DepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(ca >= 1);
        GL.LineWidth((float)Math.Max(1, r.LineWidth * PixelRatio));
        if (m.Indexed) GL.DrawElements(mode, count, DrawElementsType.UnsignedInt, 0); else GL.DrawArrays(mode, 0, count);
        GL.BindVertexArray(0);
    }

    /// <summary>Draws every vertex of the mesh as a point.</summary>
    public void Points(Mesh3D mesh, Material3D? material = null) => DrawMesh(mesh, material, PrimitiveType.Points);
    /// <summary>Draws line segments, or a strip; non-indexed meshes wider than 1.5 device pixels go through the instanced thick-line path because core profiles ignore wide GL lines.</summary>
    public void Lines(Mesh3D mesh, Material3D? material = null, bool strip = false)
    {
        var r = material ?? Material3D.Default; var px = r.LineWidth * PixelRatio;
        if (px <= 1.5 || mesh.Indices is not null) { DrawMesh(mesh, material, strip ? PrimitiveType.LineStrip : PrimitiveType.Lines); return; }
        DrawThickLines(mesh, r, strip, (float)px);
    }

    private void DrawThickLines(Mesh3D mesh, Material3D r, bool strip, float px)
    {
        var m = Upload(mesh); var count = mesh.VertexCount;
        var segments = strip ? count - 1 : count / 2;
        if (segments <= 0) return;
        var key = mesh.Key + (strip ? ":strip" : ":list");
        if (!_lineVaos.TryGetValue(key, out var v) || v.Version != mesh.Version || v.HasColor != m.HasColor)
        {
            if (v is null) { v = new LineVao { Vao = GL.GenVertexArray() }; _lineVaos[key] = v; }
            GL.BindVertexArray(v.Vao);
            int stride = strip ? 12 : 24, cstride = strip ? 4 : 8;
            GL.BindBuffer(BufferTarget.ArrayBuffer, m.Pos);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0); GL.VertexAttribDivisor(0, 1);
            GL.EnableVertexAttribArray(2); GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, stride, 12); GL.VertexAttribDivisor(2, 1);
            if (m.HasColor && m.Col != 0)
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, m.Col);
                GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, cstride, 0); GL.VertexAttribDivisor(1, 1);
                GL.EnableVertexAttribArray(3); GL.VertexAttribPointer(3, 4, VertexAttribPointerType.UnsignedByte, true, cstride, 4); GL.VertexAttribDivisor(3, 1);
            }
            else { GL.DisableVertexAttribArray(1); GL.DisableVertexAttribArray(3); }
            GL.BindVertexArray(0);
            v.Version = mesh.Version; v.HasColor = m.HasColor;
        }
        GL.UseProgram(_line);
        GL.BindVertexArray(v.Vao);
        GL.UniformMatrix4(_u["l.u_mvp"], 1, false, F(r.Model is null ? _viewProj : Mat4.Mul(_viewProj, r.Model)));
        var (cr, cg, cb, ca) = Rgba(r.Color, r.Opacity);
        GL.Uniform4(_u["l.u_color"], cr, cg, cb, ca);
        GL.Uniform2(_u["l.u_viewport"], (float)(Width * PixelRatio), (float)(Height * PixelRatio));
        GL.Uniform1(_u["l.u_width"], px);
        GL.Uniform1(_u["l.u_hasColor"], m.HasColor ? 1 : 0);
        if (r.DepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(ca >= 1);
        GL.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, segments);
        GL.BindVertexArray(0);
    }
    /// <summary>Draws the mesh as a triangle list.</summary>
    public void Triangles(Mesh3D mesh, Material3D? material = null) => DrawMesh(mesh, material, PrimitiveType.Triangles);

    private int Texture(IImageHandle image)
    {
        if (image is not RasterImage raster) throw new NotSupportedException("GlPainter3D draws core RasterImages; platform images are not supported");
        if (_textures.TryGetValue(image, out var t) && t.Version == raster.Version) { t.LastUsed = _frame; return t.Tex; }
        if (t is null) { t = new GpuTexture { Tex = GL.GenTexture() }; _textures[image] = t; }
        GL.BindTexture(TextureTarget.Texture2D, t.Tex);
        // premultiply on upload, as the WebGL path does
        var px = raster.Rgba; var pre = new byte[px.Length];
        for (var i = 0; i < px.Length; i += 4) { var a = px[i + 3]; pre[i] = (byte)(px[i] * a / 255); pre[i + 1] = (byte)(px[i + 1] * a / 255); pre[i + 2] = (byte)(px[i + 2] * a / 255); pre[i + 3] = a; }
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, (int)raster.Width, (int)raster.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pre);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        t.Version = raster.Version; t.LastUsed = _frame;
        return t.Tex;
    }

    /// <summary>Draws a core <c>RasterImage</c> as a textured quad spanning the four <paramref name="corners"/> (twelve values, xyz each). The texture is premultiplied on upload and cached by raster version; the quad reads depth but does not write it.</summary>
    public void Image(IImageHandle image, ReadOnlySpan<double> corners, Material3D? material = null)
    {
        var r = material ?? Material3D.Default;
        if (_quadVao == 0)
        {
            _quadVao = GL.GenVertexArray(); _quadBuf = GL.GenBuffer();
            GL.BindVertexArray(_quadVao); GL.BindBuffer(BufferTarget.ArrayBuffer, _quadBuf);
            GL.BufferData(BufferTarget.ArrayBuffer, 4 * 5 * 4, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(0); GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 20, 0);
            GL.EnableVertexAttribArray(1); GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 20, 12);
            GL.BindVertexArray(0);
        }
        var c = corners;
        float[] data = [(float)c[0], (float)c[1], (float)c[2], 0, 1, (float)c[3], (float)c[4], (float)c[5], 1, 1, (float)c[9], (float)c[10], (float)c[11], 0, 0, (float)c[6], (float)c[7], (float)c[8], 1, 0];
        GL.UseProgram(_tex);
        GL.BindVertexArray(_quadVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadBuf); GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, data.Length * 4, data);
        GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, Texture(image)); GL.Uniform1(_u["t.u_tex"], 0);
        GL.UniformMatrix4(_u["t.u_mvp"], 1, false, F(r.Model is null ? _viewProj : Mat4.Mul(_viewProj, r.Model)));
        var (cr, cg, cb, ca) = Rgba(r.Color, r.Opacity);
        GL.Uniform4(_u["t.u_color"], cr, cg, cb, ca);
        if (r.DepthTest) GL.Enable(EnableCap.DepthTest); else GL.Disable(EnableCap.DepthTest);
        GL.DepthMask(false);
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        GL.DepthMask(true);
        GL.BindVertexArray(0);
    }

    /// <summary>Deletes every GL object this painter created: mesh VAOs and buffers, line VAOs, textures, the image quad and the three programs.</summary>
    public void Dispose()
    {
        foreach (var m in _meshes.Values) Free(m);
        _meshes.Clear();
        foreach (var v in _lineVaos.Values) GL.DeleteVertexArray(v.Vao);
        _lineVaos.Clear();
        GL.DeleteProgram(_line);
        foreach (var t in _textures.Values) GL.DeleteTexture(t.Tex);
        _textures.Clear();
        if (_quadVao != 0) { GL.DeleteVertexArray(_quadVao); GL.DeleteBuffer(_quadBuf); }
        GL.DeleteProgram(_geom); GL.DeleteProgram(_tex);
    }
}
