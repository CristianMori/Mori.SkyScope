// Mori.SkyScope — Windows Forms host for the 3D scene view: the OpenGL painter draws the scene and Skia draws the HUD on the same surface.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;
using Mori.SkyScope.Render.OpenTK;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// Hosts a <see cref="Scene3DController"/> on the OpenGL surface (a WGL 3.3 core context on the control's own window): the scene goes through <see cref="GlPainter3D"/>, then
/// Skia draws labels and the HUD on the same framebuffer. Mouse and keyboard become core input events (left drag orbits,
/// middle/shift drag pans, right drag dollies, wheel dollies, F fit, R reset, T top-down, O orthographic). Repaints on
/// interaction, on <see cref="Refresh()"/>, or continuously with <see cref="Animate"/>. Without OpenGL the control shows a notice.
/// </summary>
[ToolboxItem(true), Description("3D scene view: point clouds, frames, paths, markers, meshes, URDF robots.")]
public class SceneControl3D : SkiaHostControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly DateTime _t0 = DateTime.UtcNow;
    private GlPainter3D? _painter;
    private SkiaPainter? _scratch;
    private SKBitmap? _scratchBitmap;
    private bool _dirty = true;

    /// <summary>Creates the control on the GPU surface and wires the animation timer.</summary>
    public SceneControl3D()
    {
        Rendering = RenderingMode.Gpu;
        _timer.Tick += (_, _) => { if (Animate || _dirty) Redraw(); };
    }

    /// <summary>The controller owning the 3D scene, frame tree, camera, tools and HUD.</summary>
    [Browsable(false)] public Scene3DController Controller { get; } = new();
    /// <summary>The scene whose 3D layers are drawn; shortcut for <c>Controller.Scene</c>.</summary>
    [Browsable(false)] public Scene Scene => Controller.Scene;
    /// <summary>The transform tree the layers resolve their frames against; shortcut for <c>Controller.Frames</c>.</summary>
    [Browsable(false)] public FrameTree Frames => Controller.Frames;
    /// <summary>Chart time for the layers; default: seconds since the control was created.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] public Func<double>? Clock { get; set; }
    /// <summary>Redraw every frame (live scenes whose layers depend on time); default redraws on demand.</summary>
    [Category("Behavior"), DefaultValue(false)]
    public bool Animate { get; set; }
    /// <summary>The active tool (orbit, measure, select); setting it repaints.</summary>
    [Category("Behavior"), DefaultValue(Scene3DTool.Orbit)]
    public Scene3DTool Tool { get => Controller.ActiveTool; set { Controller.SetTool(value); Refresh(); } }
    /// <summary>False when the scene could not get an OpenGL context; the control then shows a notice instead of the scene.</summary>
    [Browsable(false)] public bool GlAvailable => EffectiveRendering == RenderingMode.Gpu;

    /// <summary>Request a repaint after changing layers.</summary>
    public new void Refresh() { _dirty = true; Redraw(); }
    /// <summary>Fit the camera to every layer and repaint.</summary>
    public void FitAll() { Controller.FitAll(); Refresh(); }

    private static int Button(MouseButtons b) => b switch { MouseButtons.Left => 0, MouseButtons.Middle => 1, MouseButtons.Right => 2, _ => 0 };
    private static Modifiers Mods() => new(ModifierKeys.HasFlag(Keys.Shift), ModifierKeys.HasFlag(Keys.Control), ModifierKeys.HasFlag(Keys.Alt));
    private static string? KeyName(Keys k) => k switch { Keys.Space => " ", Keys.F => "f", Keys.R => "r", Keys.T => "t", Keys.O => "o", Keys.Escape => "Escape", _ => null };
    private void Dispatch(InputEvent ev) { if (Controller.Handle(ev)) Refresh(); }

    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); if (!DesignMode) _timer.Start(); }
    protected override void OnHandleDestroyed(EventArgs e) { _timer.Stop(); base.OnHandleDestroyed(e); }
    protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Controller.SetViewport(LogicalWidth, LogicalHeight); _dirty = true; }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); Capture = true;
        var (x, y) = Logical(e);
        if (e.Clicks == 2 && e.Button == MouseButtons.Left) { Dispatch(new InputEvent.DoubleClick(x, y)); return; }
        Dispatch(new InputEvent.PointerDown(x, y, Button(e.Button), Mods()));
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); var (x, y) = Logical(e); Dispatch(new InputEvent.PointerMove(x, y, Mods())); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; var (x, y) = Logical(e); Dispatch(new InputEvent.PointerUp(x, y, Button(e.Button), Mods())); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); var (x, y) = Logical(e); Dispatch(new InputEvent.Wheel(x, y, -e.Delta, Mods())); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (KeyName(e.KeyCode) is { } k) { Dispatch(new InputEvent.KeyDown(k)); e.Handled = true; } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); if (KeyName(e.KeyCode) is { } k) Dispatch(new InputEvent.KeyUp(k)); }

    /// <summary>The OpenGL pass: the 3D layers through the GL painter; layer text is deferred to the Skia pass through a throwaway 2D painter.</summary>
    protected override Action? GlPass => () =>
    {
        _dirty = false;
        _painter ??= new GlPainter3D();
        double w = LogicalWidth, h = LogicalHeight;
        Controller.Now = Clock?.Invoke() ?? (DateTime.UtcNow - _t0).TotalSeconds;
        _painter.Resize(Math.Max(1, w), Math.Max(1, h), PixelRatio);
        if (_scratch is null || _scratch.Width != w || _scratch.Height != h) { _scratch?.Dispose(); _scratchBitmap ??= new SKBitmap(1, 1); _scratch = new SkiaPainter(new SKCanvas(_scratchBitmap), w, h, 1); }
        Controller.Draw3D(_painter, w, h, _scratch);
    };

    protected override void OnPaintSurface(SkiaPaintEventArgs e)
    {
        using var painter = new SkiaPainter(e.Canvas, e.Width, e.Height, e.Scale);
        if (EffectiveRendering != RenderingMode.Gpu)
        {
            var style = new Core.Paint.TextStyle("#64748b") { Align = Core.Paint.TextAlign.Center, Baseline = Core.Paint.TextBaseline.Middle };
            painter.Text("3D view", e.Width / 2, e.Height / 2 - 9, style);
            painter.Text("OpenGL is unavailable in this session", e.Width / 2, e.Height / 2 + 9, style);
            return;
        }
        // labels and text of 3D layers (poses, markers) land here; geometry went through the GL pass
        Controller.Scene.Draw(painter, Controller.Camera, Controller.Now, new NullPainter3D(e.Width, e.Height, e.Scale));
        Controller.DrawHud(painter, e.Width, e.Height);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _timer.Dispose(); _painter?.Dispose(); _painter = null; _scratch?.Dispose(); _scratch = null; _scratchBitmap?.Dispose(); _scratchBitmap = null; }
        base.Dispose(disposing);
    }
}
