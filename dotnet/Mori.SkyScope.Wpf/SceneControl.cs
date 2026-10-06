// Mori.SkyScope — WPF host for the 2D scene view: mouse and keyboard become core input events.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Input;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// Hosts a <see cref="SceneController"/>: mouse and keyboard become core input events (pan, wheel zoom, shift-drag box
/// zoom, measure, select, Q/E rotate, R reset rotation, F fit all). Repaints on interaction and on <see cref="Invalidate"/>.
/// </summary>
public class SceneControl : SkiaElement
{
    /// <summary>The controller owning the scene, camera, tools and HUD; this element only forwards input to it and paints it.</summary>
    public SceneController Controller { get; } = new();

    /// <summary>Creates the element, maps mouse and keyboard events to <see cref="InputEvent"/>s and keeps the controller's viewport in sync with the element size.</summary>
    public SceneControl()
    {
        Focusable = true;
        PaintSurface += OnPaint;
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
        SizeChanged += (_, _) => { Controller.SetViewport(ActualWidth, ActualHeight); InvalidateVisual(); };
    }

    /// <summary>The scene whose layers are drawn; shortcut for <c>Controller.Scene</c>.</summary>
    public Scene Scene => Controller.Scene;
    /// <summary>The active tool (pan, box zoom, measure, select); setting it repaints.</summary>
    public SceneTool Tool { get => Controller.ActiveTool; set { Controller.SetTool(value); InvalidateVisual(); } }

    /// <summary>Request a repaint after changing layers.</summary>
    public void Invalidate() => InvalidateVisual();
    /// <summary>Fit the camera to every layer and repaint.</summary>
    public void FitAll() { Controller.FitAll(); InvalidateVisual(); }

    private static int Button(MouseButton b) => b switch { MouseButton.Left => 0, MouseButton.Middle => 1, MouseButton.Right => 2, _ => 0 };
    private static Modifiers Mods() => new(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
    private static string? KeyName(Key k) => k switch { Key.Space => " ", Key.Q => "q", Key.E => "e", Key.R => "r", Key.F => "f", Key.Escape => "Escape", _ => null };

    private void Dispatch(InputEvent ev) { if (Controller.Handle(ev)) InvalidateVisual(); }

    private void OnPaint(object? sender, SkiaPaintEventArgs e)
    {
        e.Canvas.Clear(SKColors.Transparent);
        using var painter = new SkiaPainter(e.Canvas, ActualWidth, ActualHeight, e.Scale);
        Controller.Draw(painter, ActualWidth, ActualHeight);
    }
}
