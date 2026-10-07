// Mori.SkyScope — Windows Forms painting surfaces for SkiaSharp: a CPU bitmap or an OpenGL context, behind one host control.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenTK;
using OpenTK.Graphics.OpenGL4;
using SkiaSharp;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Mori.SkyScope.WinForms;

/// <summary>How a control rasterises: SkiaSharp into a CPU bitmap, or SkiaSharp on an OpenGL surface (the designer always paints on the CPU).</summary>
public enum RenderingMode
{
    /// <summary>SkiaSharp draws into a bitmap that GDI blits; works everywhere, including remote sessions and the designer.</summary>
    Cpu,
    /// <summary>SkiaSharp draws on an OpenGL 3.3 surface; faster for large surfaces and the only mode that can share a context with the 3D painter.</summary>
    Gpu,
}

/// <summary>Arguments of a paint pass: the canvas, the logical size in device-independent pixels and the device pixel ratio.</summary>
public sealed class SkiaPaintEventArgs(SKCanvas canvas, double width, double height, double scale) : EventArgs
{
    /// <summary>The canvas, already cleared; drawing is in logical pixels once scaled by <see cref="Scale"/>.</summary>
    public SKCanvas Canvas { get; } = canvas;
    /// <summary>Logical width in device-independent pixels.</summary>
    public double Width { get; } = width;
    /// <summary>Logical height in device-independent pixels.</summary>
    public double Height { get; } = height;
    /// <summary>Device pixels per logical pixel (the monitor DPI scale).</summary>
    public double Scale { get; } = scale;
}

/// <summary>A child control that can paint a Skia canvas on request.</summary>
internal interface ISkiaSurface
{
    /// <summary>Raised on every paint pass.</summary>
    event EventHandler<SkiaPaintEventArgs>? PaintSurface;
    /// <summary>Runs a hook between a raw OpenGL pass and the Skia pass; ignored on the CPU surface.</summary>
    Action? GlPass { get; set; }
    /// <summary>Schedules a repaint.</summary>
    void Redraw();
    /// <summary>The control itself.</summary>
    Control AsControl { get; }
    /// <summary>Paints now and returns the result as a bitmap in device pixels (null when the surface cannot paint).</summary>
    Bitmap? Snapshot();
    /// <summary>Paints one frame now and presents it, without waiting for a paint message.</summary>
    void PaintNow();
}

/// <summary>CPU surface: Skia draws into a premultiplied BGRA bitmap sized to the client area in device pixels; GDI blits it.</summary>
internal sealed class CpuSurface : Control, ISkiaSurface
{
    private SKBitmap? _bitmap;
    public event EventHandler<SkiaPaintEventArgs>? PaintSurface;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Action? GlPass { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Control AsControl => this;

    public CpuSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    public void Redraw() => Invalidate();

    private SKBitmap Render()
    {
        int pw = Math.Max(1, ClientSize.Width), ph = Math.Max(1, ClientSize.Height);
        if (_bitmap is null || _bitmap.Width != pw || _bitmap.Height != ph) { _bitmap?.Dispose(); _bitmap = new SKBitmap(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul); }
        var scale = DeviceDpi / 96.0;
        using var surface = SKSurface.Create(_bitmap.Info, _bitmap.GetPixels(), _bitmap.RowBytes);
        surface.Canvas.Clear(SKColors.Transparent);
        PaintSurface?.Invoke(this, new SkiaPaintEventArgs(surface.Canvas, pw / scale, ph / scale, scale));
        surface.Canvas.Flush();
        return _bitmap;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var bmp = Render();
        using var gdi = new Bitmap(bmp.Width, bmp.Height, bmp.RowBytes, GdiPixelFormat.Format32bppPArgb, bmp.GetPixels());
        e.Graphics.DrawImageUnscaled(gdi, 0, 0);
    }

    public Bitmap? Snapshot()
    {
        var bmp = Render();
        var result = new Bitmap(bmp.Width, bmp.Height, GdiPixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(result);
        g.Clear(BackColor);
        using var gdi = new Bitmap(bmp.Width, bmp.Height, bmp.RowBytes, GdiPixelFormat.Format32bppPArgb, bmp.GetPixels());
        g.DrawImageUnscaled(gdi, 0, 0);
        return result;
    }

    public void PaintNow()
    {
        if (!IsHandleCreated) return;
        using var g = CreateGraphics();
        OnPaint(new PaintEventArgs(g, ClientRectangle));
    }

    protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(BackColor); }

    protected override void Dispose(bool disposing) { if (disposing) { _bitmap?.Dispose(); _bitmap = null; } base.Dispose(disposing); }
}

/// <summary>
/// OpenGL surface: a child window with its own device context and a WGL 3.3 core context, on whose default framebuffer
/// Skia draws through a GPU context. An optional <see cref="GlPass"/> runs first with the same context current, which is
/// how the 3D control draws its scene under the Skia HUD. Any failure raises <see cref="Failed"/> once and the host falls back to the CPU.
/// </summary>
internal sealed class GpuSurface : Control, ISkiaSurface
{
    private IntPtr _hdc, _hglrc;
    private GRContext? _gr;
    private GRBackendRenderTarget? _target;
    private SKSurface? _surface;
    private int _w, _h;
    public event EventHandler<SkiaPaintEventArgs>? PaintSurface;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Action? GlPass { get; set; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Control AsControl => this;
    /// <summary>False once context creation or a paint failed; the host then falls back to the CPU surface.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public bool Available { get; private set; } = true;
    /// <summary>Samples per pixel of the window's framebuffer (4 when the driver granted the multisampled pixel format, 0 otherwise or before the context exists).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public int Samples { get; private set; }
    /// <summary>Raised once when OpenGL turned out to be unusable.</summary>
    public event Action? Failed;

    public GpuSurface()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, false);
        TabStop = false;
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= Wgl.CS_OWNDC; cp.Style |= Wgl.WS_CLIPCHILDREN | Wgl.WS_CLIPSIBLINGS; return cp; }
    }

    public void Redraw() => Invalidate();

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (DesignMode) return;
        try { _hglrc = Wgl.CreateContext(Handle, out _hdc, out var samples); Samples = samples; }
        catch (Exception) { Fail(); }
    }

    protected override void OnHandleDestroyed(EventArgs e) { ReleaseGl(); base.OnHandleDestroyed(e); }

    private void Fail() { if (!Available) return; Available = false; ReleaseGl(); Failed?.Invoke(); }

    private void ReleaseGl()
    {
        if (_hglrc != IntPtr.Zero) { try { Wgl.wglMakeCurrent(_hdc, _hglrc); _surface?.Dispose(); _target?.Dispose(); _gr?.Dispose(); } catch (Exception) { } }
        _surface = null; _target = null; _gr = null; _w = _h = 0; Samples = 0;
        if (_hglrc != IntPtr.Zero) { Wgl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); Wgl.wglDeleteContext(_hglrc); _hglrc = IntPtr.Zero; }
        if (_hdc != IntPtr.Zero) { Wgl.ReleaseDC(Handle, _hdc); _hdc = IntPtr.Zero; }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (DesignMode || !Available || _hglrc == IntPtr.Zero) { e.Graphics.Clear(BackColor); return; }
        try { Render(); Wgl.SwapBuffers(_hdc); }
        catch (Exception) { e.Graphics.Clear(BackColor); Fail(); }
    }

    public void PaintNow()
    {
        if (DesignMode || !Available || _hglrc == IntPtr.Zero) return;
        try { Render(); Wgl.SwapBuffers(_hdc); }
        catch (Exception) { Fail(); }
    }

    public Bitmap? Snapshot()
    {
        if (DesignMode || !Available || _hglrc == IntPtr.Zero) return null;
        try
        {
            var (pw, ph) = Render();
            var bmp = new Bitmap(pw, ph, GdiPixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, pw, ph), System.Drawing.Imaging.ImageLockMode.WriteOnly, GdiPixelFormat.Format32bppArgb);
            try { ReadBackBuffer(pw, ph, data.Scan0); }
            finally { bmp.UnlockBits(data); }
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
            Wgl.SwapBuffers(_hdc);
            return bmp;
        }
        catch (Exception) { Fail(); return null; }
    }

    /// <summary>
    /// Reads the back buffer as BGRA rows into <paramref name="dst"/>. A multisampled window framebuffer is normally read
    /// directly (the driver resolves it); when the driver refuses, the frame is resolved by a blit into a single-sample
    /// renderbuffer and read from there.
    /// </summary>
    private static void ReadBackBuffer(int pw, int ph, IntPtr dst)
    {
        while (GL.GetError() != ErrorCode.NoError) { }
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        GL.PixelStore(PixelStoreParameter.PackAlignment, 4);
        GL.ReadPixels(0, 0, pw, ph, PixelFormat.Bgra, PixelType.UnsignedByte, dst);
        if (GL.GetError() == ErrorCode.NoError) return;
        int fbo = GL.GenFramebuffer(), rb = GL.GenRenderbuffer();
        try
        {
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, rb);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, pw, ph);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, fbo);
            GL.FramebufferRenderbuffer(FramebufferTarget.DrawFramebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, rb);
            GL.BlitFramebuffer(0, 0, pw, ph, 0, 0, pw, ph, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
            GL.ReadPixels(0, 0, pw, ph, PixelFormat.Bgra, PixelType.UnsignedByte, dst);
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("glReadPixels failed on the resolved framebuffer");
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            GL.DeleteFramebuffer(fbo); GL.DeleteRenderbuffer(rb);
        }
    }

    /// <summary>One frame: clear, the GL pass (multisampling on), then Skia on the default framebuffer. Leaves the frame in the back buffer.</summary>
    private (int W, int H) Render()
    {
        {
            if (!Wgl.wglMakeCurrent(_hdc, _hglrc)) throw new InvalidOperationException("wglMakeCurrent failed");
            int pw = Math.Max(1, ClientSize.Width), ph = Math.Max(1, ClientSize.Height);
            var scale = DeviceDpi / 96.0;
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, pw, ph);
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            if (Samples > 0) GL.Enable(EnableCap.Multisample);
            GlPass?.Invoke();
            _gr ??= GRContext.CreateGl(GRGlInterface.Create()) ?? throw new InvalidOperationException("Skia could not bind the OpenGL context");
            if (_surface is null || _w != pw || _h != ph)
            {
                _surface?.Dispose(); _target?.Dispose();
                var samples = Math.Min(Samples, _gr.GetMaxSurfaceSampleCount(SKColorType.Rgba8888));
                _target = new GRBackendRenderTarget(pw, ph, samples, 8, new GRGlFramebufferInfo(0, SKColorType.Rgba8888.ToGlSizedFormat()));
                _surface = SKSurface.Create(_gr, _target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888) ?? throw new InvalidOperationException("Skia could not create a surface on the framebuffer");
                _w = pw; _h = ph;
            }
            _gr.ResetContext();
            if (GlPass is null) _surface.Canvas.Clear(SKColors.Transparent);
            PaintSurface?.Invoke(this, new SkiaPaintEventArgs(_surface.Canvas, pw / scale, ph / scale, scale));
            _surface.Canvas.Flush();
            _gr.Flush();
            return (pw, ph);
        }
    }

    protected override void Dispose(bool disposing) { if (disposing) ReleaseGl(); base.Dispose(disposing); }
}

/// <summary>The handful of Win32 and WGL calls needed to put an OpenGL 3.3 core context on a child window, and the OpenTK bindings loader for it.</summary>
internal static class Wgl
{
    public const int CS_OWNDC = 0x20, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
    private const uint PFD_DRAW_TO_WINDOW = 4, PFD_SUPPORT_OPENGL = 0x20, PFD_DOUBLEBUFFER = 1;
    private const int WGL_CONTEXT_MAJOR_VERSION_ARB = 0x2091, WGL_CONTEXT_MINOR_VERSION_ARB = 0x2092, WGL_CONTEXT_PROFILE_MASK_ARB = 0x9126, WGL_CONTEXT_CORE_PROFILE_BIT_ARB = 1;
    private const int WGL_DRAW_TO_WINDOW_ARB = 0x2001, WGL_ACCELERATION_ARB = 0x2003, WGL_SUPPORT_OPENGL_ARB = 0x2010, WGL_DOUBLE_BUFFER_ARB = 0x2011, WGL_PIXEL_TYPE_ARB = 0x2013, WGL_COLOR_BITS_ARB = 0x2014,
        WGL_ALPHA_BITS_ARB = 0x201B, WGL_DEPTH_BITS_ARB = 0x2022, WGL_STENCIL_BITS_ARB = 0x2023, WGL_FULL_ACCELERATION_ARB = 0x2027, WGL_TYPE_RGBA_ARB = 0x202B, WGL_SAMPLE_BUFFERS_ARB = 0x2041, WGL_SAMPLES_ARB = 0x2042;
    /// <summary>Samples per pixel asked of the driver (the context reports what it granted).</summary>
    public const int RequestedSamples = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormatDescriptor
    {
        public ushort nSize, nVersion; public uint dwFlags; public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved; public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] private static extern int DescribePixelFormat(IntPtr hdc, int format, uint size, ref PixelFormatDescriptor pfd);
    [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] public static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport("opengl32.dll")] public static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate IntPtr CreateContextAttribs(IntPtr hdc, IntPtr share, int[] attribs);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool ChoosePixelFormatArb(IntPtr hdc, int[] intAttribs, float[]? floatAttribs, uint maxFormats, int[] formats, out uint count);

    private static bool _bindingsLoaded, _probed;
    private static ChoosePixelFormatArb? _choosePixelFormatArb;

    /// <summary>The OpenGL renderer and version strings of the first context made, for example "NVIDIA GeForce RTX 3060/PCIe/SSE2, 3.3.0 NVIDIA 560.94"; null until one exists.</summary>
    public static string? Renderer { get; private set; }

    private static PixelFormatDescriptor BasicDescriptor() => new() { nSize = (ushort)Marshal.SizeOf<PixelFormatDescriptor>(), nVersion = 1, dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER, cColorBits = 32, cAlphaBits = 8, cDepthBits = 24, cStencilBits = 8 };

    /// <summary>A hidden one-pixel window with its own device context, used once to reach <c>wglChoosePixelFormatARB</c> (a window's pixel format can be set only once, so the real window cannot serve).</summary>
    private sealed class ProbeWindow : NativeWindow, IDisposable
    {
        public ProbeWindow() => CreateHandle(new CreateParams { Caption = "SkyScope OpenGL probe", ClassStyle = CS_OWNDC, Style = unchecked((int)0x80000000) /* WS_POPUP */, Width = 1, Height = 1 });
        public void Dispose() => DestroyHandle();
    }

    /// <summary>Fetches <c>wglChoosePixelFormatARB</c> through a throwaway legacy context on a probe window, once per process; leaves it null when the driver has no such entry point.</summary>
    private static void Probe()
    {
        if (_probed) return;
        _probed = true;
        using var probe = new ProbeWindow();
        var hdc = GetDC(probe.Handle);
        if (hdc == IntPtr.Zero) return;
        var ctx = IntPtr.Zero;
        try
        {
            var pfd = BasicDescriptor();
            var format = ChoosePixelFormat(hdc, ref pfd);
            if (format == 0 || !SetPixelFormat(hdc, format, ref pfd)) return;
            ctx = wglCreateContext(hdc);
            if (ctx == IntPtr.Zero || !wglMakeCurrent(hdc, ctx)) return;
            var p = wglGetProcAddress("wglChoosePixelFormatARB");
            if (p != IntPtr.Zero) _choosePixelFormatArb = Marshal.GetDelegateForFunctionPointer<ChoosePixelFormatArb>(p);
        }
        catch (Exception) { _choosePixelFormatArb = null; }
        finally
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            if (ctx != IntPtr.Zero) wglDeleteContext(ctx);
            ReleaseDC(probe.Handle, hdc);
        }
    }

    /// <summary>A double-buffered 32-bit RGBA format with depth, stencil and <see cref="RequestedSamples"/> samples per pixel through <c>wglChoosePixelFormatARB</c>; 0 when the entry point or such a format is unavailable.</summary>
    private static int ChooseMultisampledFormat(IntPtr hdc)
    {
        Probe();
        if (_choosePixelFormatArb is null) return 0;
        int[] attribs = [WGL_DRAW_TO_WINDOW_ARB, 1, WGL_SUPPORT_OPENGL_ARB, 1, WGL_DOUBLE_BUFFER_ARB, 1, WGL_ACCELERATION_ARB, WGL_FULL_ACCELERATION_ARB, WGL_PIXEL_TYPE_ARB, WGL_TYPE_RGBA_ARB,
            WGL_COLOR_BITS_ARB, 32, WGL_ALPHA_BITS_ARB, 8, WGL_DEPTH_BITS_ARB, 24, WGL_STENCIL_BITS_ARB, 8, WGL_SAMPLE_BUFFERS_ARB, 1, WGL_SAMPLES_ARB, RequestedSamples, 0];
        var formats = new int[1];
        try { return _choosePixelFormatArb(hdc, attribs, null, 1, formats, out var count) && count > 0 ? formats[0] : 0; }
        catch (Exception) { return 0; }
    }

    /// <summary>
    /// Set a double-buffered 32-bit pixel format with depth, stencil and, when the driver offers one, <see cref="RequestedSamples"/> samples per
    /// pixel on the window, create a 3.3 core context (legacy context as a fallback) and load OpenTK's bindings. <paramref name="samples"/>
    /// is what the framebuffer actually has (0 without multisampling).
    /// </summary>
    public static IntPtr CreateContext(IntPtr hwnd, out IntPtr hdc, out int samples)
    {
        hdc = GetDC(hwnd);
        if (hdc == IntPtr.Zero) throw new InvalidOperationException("GetDC failed");
        var pfd = BasicDescriptor();
        var format = ChooseMultisampledFormat(hdc);
        if (format != 0) DescribePixelFormat(hdc, format, pfd.nSize, ref pfd);
        else format = ChoosePixelFormat(hdc, ref pfd);
        if (format == 0 || !SetPixelFormat(hdc, format, ref pfd)) throw new InvalidOperationException("no OpenGL pixel format");
        var legacy = wglCreateContext(hdc);
        if (legacy == IntPtr.Zero) throw new InvalidOperationException("wglCreateContext failed");
        if (!wglMakeCurrent(hdc, legacy)) { wglDeleteContext(legacy); throw new InvalidOperationException("wglMakeCurrent failed"); }
        var ctx = legacy;
        var p = wglGetProcAddress("wglCreateContextAttribsARB");
        if (p != IntPtr.Zero)
        {
            var create = Marshal.GetDelegateForFunctionPointer<CreateContextAttribs>(p);
            var modern = create(hdc, IntPtr.Zero, [WGL_CONTEXT_MAJOR_VERSION_ARB, 3, WGL_CONTEXT_MINOR_VERSION_ARB, 3, WGL_CONTEXT_PROFILE_MASK_ARB, WGL_CONTEXT_CORE_PROFILE_BIT_ARB, 0]);
            if (modern != IntPtr.Zero && wglMakeCurrent(hdc, modern)) { wglDeleteContext(legacy); ctx = modern; }
        }
        if (!_bindingsLoaded) { GL.LoadBindings(new Bindings()); _bindingsLoaded = true; }
        Renderer ??= $"{GL.GetString(StringName.Renderer)}, {GL.GetString(StringName.Version)}";
        GL.GetInteger(GetPName.SampleBuffers, out var sampleBuffers);
        GL.GetInteger(GetPName.Samples, out samples);
        if (sampleBuffers == 0) samples = 0;
        return ctx;
    }

    private sealed class Bindings : IBindingsContext
    {
        private readonly IntPtr _module = GetModuleHandle("opengl32.dll");
        public IntPtr GetProcAddress(string procName)
        {
            var p = wglGetProcAddress(procName);
            return p == IntPtr.Zero || p == new IntPtr(1) || p == new IntPtr(2) || p == new IntPtr(3) || p == new IntPtr(-1) ? Wgl.GetProcAddress(_module, procName) : p;
        }
    }
}

/// <summary>
/// Base of every SkyScope Windows Forms control: owns a CPU or GPU surface that fills it, forwards the surface's mouse
/// events as its own (coordinates in logical pixels), takes the keyboard focus on a click, and repaints through
/// <see cref="Redraw"/>. Derived controls draw in <see cref="OnPaintSurface"/>.
/// </summary>
public abstract class SkiaHostControl : Control
{
    private ISkiaSurface? _surface;
    private RenderingMode _rendering = RenderingMode.Cpu;
    private bool _gpuFailed;

    /// <summary>Creates the host with a CPU surface; the surface is swapped when <see cref="Rendering"/> changes.</summary>
    protected SkiaHostControl()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.UserPaint, false);
        TabStop = true;
        AttachSurface();
    }

    /// <summary>Siting happens after construction: a control that asked for the GPU in its constructor swaps to the CPU surface once it learns it lives in a designer.</summary>
    public override ISite? Site
    {
        get => base.Site;
        set { base.Site = value; if (DesignMode && _surface is GpuSurface) AttachSurface(); }
    }

    /// <summary>CPU bitmap or OpenGL surface. The designer and sessions without OpenGL always paint on the CPU.</summary>
    [Category("Rendering"), DefaultValue(RenderingMode.Cpu), Description("CPU bitmap or OpenGL surface. The designer and sessions without OpenGL always paint on the CPU.")]
    public RenderingMode Rendering
    {
        get => _rendering;
        set { if (_rendering == value) return; _rendering = value; AttachSurface(); }
    }

    /// <summary>The rendering mode in use: <see cref="RenderingMode.Cpu"/> when the GPU was requested but no OpenGL context could be made.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RenderingMode EffectiveRendering => _surface is GpuSurface ? RenderingMode.Gpu : RenderingMode.Cpu;

    /// <summary>Samples per pixel of the OpenGL framebuffer (4 when the driver granted multisampling); 0 on the CPU surface or before the context exists.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Samples => _surface is GpuSurface g ? g.Samples : 0;

    /// <summary>Device pixels per logical pixel.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double PixelRatio => DeviceDpi / 96.0;
    /// <summary>Logical width in device-independent pixels.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double LogicalWidth => ClientSize.Width / PixelRatio;
    /// <summary>Logical height in device-independent pixels.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double LogicalHeight => ClientSize.Height / PixelRatio;

    /// <summary>Number of paint passes the surface has run since the control was created (on the CPU bitmap or the OpenGL framebuffer); the sample's <c>--bench</c> mode reads it to measure frame rates.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public long FramesPainted { get; private set; }

    /// <summary>Schedules a repaint of the surface.</summary>
    public void Redraw() => _surface?.Redraw();

    /// <summary>Paints the control now and returns the pixels (device resolution), from the bitmap on the CPU or read back from the OpenGL framebuffer. Null when nothing could be painted.</summary>
    public Bitmap? Snapshot() => _surface?.Snapshot();

    /// <summary>Paints one frame now and presents it (a GDI blit on the CPU, a buffer swap on the GPU) without waiting for a paint message; counts in <see cref="FramesPainted"/>. For benchmarks and for desktops whose window manager delivers no paint messages.</summary>
    public void PaintNow() => _surface?.PaintNow();

    /// <summary>The OpenGL renderer and version of the first GPU surface created in this process, null while none exists (or when OpenGL is unavailable).</summary>
    public static string? OpenGlRenderer => Wgl.Renderer;

    /// <summary>Draw the content; the canvas is in logical pixels once the painter applies <see cref="SkiaPaintEventArgs.Scale"/>.</summary>
    protected abstract void OnPaintSurface(SkiaPaintEventArgs e);

    /// <summary>A raw OpenGL pass before Skia draws, available on the GPU surface only (used by the 3D control).</summary>
    protected virtual Action? GlPass => null;

    private void AttachSurface()
    {
        if (_surface is not null) { Controls.Remove(_surface.AsControl); _surface.AsControl.Dispose(); _surface = null; }
        var wantGpu = _rendering == RenderingMode.Gpu && !DesignMode && !_gpuFailed;
        ISkiaSurface s = wantGpu ? new GpuSurface() : new CpuSurface();
        s.PaintSurface += (_, e) => { FramesPainted++; OnPaintSurface(e); };
        s.GlPass = GlPass;
        var c = s.AsControl;
        c.Dock = DockStyle.Fill;
        c.MouseDown += (_, e) => { Focus(); OnMouseDown(e); };
        c.MouseMove += (_, e) => OnMouseMove(e);
        c.MouseUp += (_, e) => OnMouseUp(e);
        c.MouseWheel += (_, e) => OnMouseWheel(e);
        c.MouseLeave += (_, e) => OnMouseLeave(e);
        c.MouseDoubleClick += (_, e) => OnMouseDoubleClick(e);
        // drag and drop lands on the surface window; the host decides (AllowDrop on the host, handlers in derived controls)
        c.AllowDrop = true;
        c.DragEnter += (_, e) => { if (AllowDrop) OnDragEnter(e); else e.Effect = DragDropEffects.None; };
        c.DragOver += (_, e) => { if (AllowDrop) OnDragOver(e); else e.Effect = DragDropEffects.None; };
        c.DragLeave += (_, e) => OnDragLeave(e);
        c.DragDrop += (_, e) => { if (AllowDrop) OnDragDrop(e); };
        Controls.Add(c);
        _surface = s;
        if (s is GpuSurface g) g.Failed += () => { _gpuFailed = true; if (IsHandleCreated) BeginInvoke(AttachSurface); else AttachSurface(); };
    }

    /// <summary>Pointer position of a mouse event in logical pixels.</summary>
    protected (double X, double Y) Logical(MouseEventArgs e) => (e.X / PixelRatio, e.Y / PixelRatio);

    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Redraw(); }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space ? true : base.IsInputKey(keyData);
}
