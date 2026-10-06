// Mori.SkyScope — WPF host for the 3D scene view: an OpenGL surface for the scene and a Skia element for the HUD.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;
using Mori.SkyScope.Render.OpenTK;
using Mori.SkyScope.Render.Skia;
using OpenTK.Windowing.GraphicsLibraryFramework;
using OpenTK.Wpf;
using SkiaSharp;
using MouseButton = System.Windows.Input.MouseButton;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// Hosts a <see cref="Scene3DController"/>: an OpenGL surface (OpenTK <see cref="GLWpfControl"/>) for the scene and a
/// transparent SkiaSharp element on top for the HUD. Mouse and keyboard become core input events (left drag orbits,
/// middle/shift drag pans, right drag dollies, wheel dollies, F fit, R reset, T top-down, O orthographic).
/// Repaints on interaction, on <see cref="Invalidate"/>, or continuously with <see cref="Animate"/>.
/// </summary>
public class SceneControl3D : Grid
{
    private readonly GLWpfControl _gl = new();
    private readonly SkiaElement _hud = new() { IsHitTestVisible = false };
    private GlPainter3D? _painter;
    private SkiaPainter? _scratch;
    private SKBitmap? _scratchBitmap;
    private bool _dirty = true;
    private readonly DateTime _t0 = DateTime.UtcNow;

    /// <summary>The controller owning the 3D scene, frame tree, camera, tools and HUD; this element forwards input to it and renders it.</summary>
    public Scene3DController Controller { get; } = new();
    /// <summary>Chart time for the layers; default: seconds since the control was created.</summary>
    public Func<double>? Clock { get; set; }
    /// <summary>Redraw every frame (live scenes whose layers depend on time); default redraws on demand.</summary>
    public bool Animate { get; set; }
    /// <summary>False when no OpenGL context could be created (no monitor / remote session); the control then shows a notice instead of the scene.</summary>
    public bool GlAvailable { get; private set; }

    /// <summary>Creates the element: starts the OpenGL surface when a monitor is present (a notice is shown otherwise), lays the HUD element on top and maps mouse and keyboard events to <see cref="InputEvent"/>s.</summary>
    public SceneControl3D()
    {
        Focusable = true; Background = Brushes.Transparent; ClipToBounds = true;
        Children.Add(_gl); Children.Add(_hud);
        GlAvailable = HasMonitor();
        if (GlAvailable)
        {
            _gl.Start(new GLWpfControlSettings { MajorVersion = 3, MinorVersion = 3, RenderContinuously = true, Samples = 4 });
            _gl.Render += OnRender;
        }
        else Children.Add(new TextBlock { Text = "3D view: OpenGL is unavailable in this session (no display).", Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
        _hud.PaintSurface += OnHud;
        MouseDown += (_, e) =>
        {
            Focus(); CaptureMouse();
            var p = e.GetPosition(this);
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left) { Dispatch(new InputEvent.DoubleClick(p.X, p.Y)); return; }
            Dispatch(new InputEvent.PointerDown(p.X, p.Y, Button(e.ChangedButton), Mods()));
        };
        MouseMove += (_, e) => { var p = e.GetPosition(this); Dispatch(new InputEvent.PointerMove(p.X, p.Y, Mods())); };
        MouseUp += (_, e) => { ReleaseMouseCapture(); var p = e.GetPosition(this); Dispatch(new InputEvent.PointerUp(p.X, p.Y, Button(e.ChangedButton), Mods())); };
        MouseWheel += (_, e) => { var p = e.GetPosition(this); Dispatch(new InputEvent.Wheel(p.X, p.Y, -e.Delta, Mods())); e.Handled = true; };
        KeyDown += (_, e) => { if (KeyName(e.Key) is { } k) { Dispatch(new InputEvent.KeyDown(k)); e.Handled = true; } };
        KeyUp += (_, e) => { if (KeyName(e.Key) is { } k) Dispatch(new InputEvent.KeyUp(k)); };
        SizeChanged += (_, _) => { Controller.SetViewport(ActualWidth, ActualHeight); Invalidate(); };
        Unloaded += (_, _) => { _painter?.Dispose(); _painter = null; _scratch?.Dispose(); _scratch = null; _scratchBitmap?.Dispose(); _scratchBitmap = null; };
    }

    /// <summary>GLFW dereferences the primary monitor while creating the context; without one (headless/remote sessions) it would crash the process.</summary>
    private static unsafe bool HasMonitor()
    {
        try { if (!GLFW.Init()) return false; return GLFW.GetPrimaryMonitor() != null; }
        catch (Exception) { return false; }
    }

    /// <summary>The scene whose 3D layers are drawn; shortcut for <c>Controller.Scene</c>.</summary>
    public Scene Scene => Controller.Scene;
    /// <summary>The transform tree the layers resolve their frames against; shortcut for <c>Controller.Frames</c>.</summary>
    public FrameTree Frames => Controller.Frames;
    /// <summary>The active tool (orbit, measure, select); setting it repaints.</summary>
    public Scene3DTool Tool { get => Controller.ActiveTool; set { Controller.SetTool(value); Invalidate(); } }

    /// <summary>Request a repaint after changing layers.</summary>
    public void Invalidate() { _dirty = true; _hud.InvalidateVisual(); }
    /// <summary>Fit the camera to every layer and repaint.</summary>
    public void FitAll() { Controller.FitAll(); Invalidate(); }

    private static int Button(MouseButton b) => b switch { MouseButton.Left => 0, MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 };
    private static Modifiers Mods() => new(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    private static string? KeyName(Key k) => k switch { Key.Space => " ", Key.F => "f", Key.R => "r", Key.T => "t", Key.O => "o", Key.Escape => "Escape", _ => null };
    private void Dispatch(InputEvent ev) { if (Controller.Handle(ev)) Invalidate(); }

    private void OnRender(TimeSpan delta)
    {
        if (!_dirty && !Animate) return;
        _dirty = false;
        _painter ??= new GlPainter3D();
        var dpi = VisualTreeHelper.GetDpi(this);
        Controller.Now = Clock?.Invoke() ?? (DateTime.UtcNow - _t0).TotalSeconds;
        _painter.Resize(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight), dpi.DpiScaleX);
        // layer text is drawn in the HUD pass; the GL pass gets a throwaway 2D painter
        if (_scratch is null || _scratch.Width != ActualWidth || _scratch.Height != ActualHeight) { _scratch?.Dispose(); _scratchBitmap ??= new SKBitmap(1, 1); _scratch = new SkiaPainter(new SKCanvas(_scratchBitmap), ActualWidth, ActualHeight, 1); }
        Controller.Draw3D(_painter, ActualWidth, ActualHeight, _scratch);
        if (Animate) _hud.InvalidateVisual();
    }

    private void OnHud(object? sender, SkiaPaintEventArgs e)
    {
        e.Canvas.Clear(SKColors.Transparent);
        using var painter = new SkiaPainter(e.Canvas, ActualWidth, ActualHeight, e.Scale);
        // labels and text of 3D layers (poses, markers) land here; geometry goes through the GL pass
        Controller.Scene.Draw(painter, Controller.Camera, Controller.Now, new NullPainter3D(ActualWidth, ActualHeight, e.Scale));
        Controller.DrawHud(painter, ActualWidth, ActualHeight);
    }
}
