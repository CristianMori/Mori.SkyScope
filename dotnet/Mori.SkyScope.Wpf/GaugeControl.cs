// Mori.SkyScope — WPF host for any gauge or input on the Skia element.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// Hosts any <see cref="IGaugeDrawable"/> on an <see cref="SKElement"/>. Redraws on <see cref="Invalidate"/> and, while an
/// <see cref="IAnimatedGauge"/> is settling, on a dispatcher timer. Pointer/wheel/keys drive knob, slider and switch inputs.
/// </summary>
public class GaugeControl : SkiaElement
{
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _last = DateTime.UtcNow;
    private IGaugeDrawable? _gauge;

    /// <summary>Creates the control: the mouse, wheel and arrow/space keys drive knob, slider and switch inputs, and the animation timer is wired but idle.</summary>
    public GaugeControl()
    {
        Focusable = true;
        _timer.Tick += (_, _) => Tick();
        PaintSurface += OnPaint;
        MouseDown += (s, e) => { Focus(); CaptureMouse(); Pointer("down", e.GetPosition(this)); };
        MouseMove += (s, e) => { if (IsMouseCaptured) Pointer("move", e.GetPosition(this)); };
        MouseUp += (s, e) => { ReleaseMouseCapture(); Pointer("up", e.GetPosition(this)); };
        MouseWheel += (s, e) => { Nudge(Math.Sign(e.Delta)); };
        KeyDown += (s, e) => { if (e.Key is Key.Up or Key.Right) Nudge(1); else if (e.Key is Key.Down or Key.Left) Nudge(-1); else if (e.Key is Key.Space or Key.Enter && _gauge is Switch sw) { sw.Toggle(); Invalidate(); } };
    }

    /// <summary>The gauge or input to draw; setting it repaints.</summary>
    public IGaugeDrawable? Gauge { get => _gauge; set { _gauge = value; Invalidate(); } }

    /// <summary>Request a repaint; starts the animation timer when the gauge is still settling.</summary>
    public void Invalidate()
    {
        InvalidateVisual();
        if (_gauge is IAnimatedGauge { Animating: true } && !_timer.IsEnabled) { _last = DateTime.UtcNow; _timer.Start(); }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow; var dt = Math.Min(0.1, (now - _last).TotalSeconds); _last = now;
        if (_gauge is IAnimatedGauge a) { a.Step(dt); InvalidateVisual(); if (!a.Animating) _timer.Stop(); }
        else _timer.Stop();
    }

    private void OnPaint(object? sender, SkiaPaintEventArgs e)
    {
        var canvas = e.Canvas;
        canvas.Clear(SKColors.Transparent);
        if (_gauge is null) return;
        var scale = e.Scale;
        using var painter = new SkiaPainter(canvas, ActualWidth, ActualHeight, scale);
        _gauge.Draw(painter, ActualWidth, ActualHeight);
    }

    private void Pointer(string type, Point p)
    {
        double w = ActualWidth, h = ActualHeight;
        switch (_gauge)
        {
            case Knob k: { var l = k.Layout(w, h); if (type == "down") k.PointerDown(l, p.X, p.Y); else if (type == "move") k.PointerMove(l, p.X, p.Y); else k.PointerUp(); break; }
            case Slider s: { if (type == "down") s.PointerDown(w, h, p.X, p.Y); else if (type == "move") s.PointerMove(w, h, p.X, p.Y); else s.PointerUp(); break; }
            case Switch sw: if (type == "down") sw.Toggle(); break;
            default: return;
        }
        Invalidate();
    }

    private void Nudge(int dir)
    {
        switch (_gauge) { case Knob k: k.Nudge(dir); break; case Slider s: s.Nudge(dir); break; default: return; }
        Invalidate();
    }
}
