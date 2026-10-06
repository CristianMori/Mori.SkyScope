// Mori.SkyScope — WPF host for an analytic chart: hover tooltips, legend clicks, box zoom, wheel zoom, double-click reset.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Input;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf;

/// <summary>
/// Hosts an analytic chart (<see cref="CartesianChart"/>, <see cref="PieChart"/>, <see cref="PolarChart"/>, <see cref="Heatmap"/>)
/// or any other <see cref="IDrawable"/>: hover tooltips, legend clicks, drag = box zoom, wheel = zoom, double-click = reset.
/// Repaints only on <see cref="Invalidate"/> or interaction.
/// </summary>
public class ChartControl : SkiaElement
{
    private IDrawable? _chart;
    private bool _dragging;

    /// <summary>Creates the control, makes it focusable and wires the mouse handlers that drive hover, legend clicks, box zoom and wheel zoom.</summary>
    public ChartControl()
    {
        Focusable = true;
        PaintSurface += OnPaint;
        MouseMove += (_, e) => { var p = e.GetPosition(this); Move(p.X, p.Y); };
        MouseLeave += (_, _) => { Leave(); };
        MouseDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left) return;
            Focus();
            var p = e.GetPosition(this);
            if (e.ClickCount == 2) { if (_chart is CartesianChart cz) { cz.ResetZoom(); InvalidateVisual(); } return; }
            if (Click(p.X, p.Y)) { InvalidateVisual(); return; }
            if (_chart is CartesianChart cc) { CaptureMouse(); _dragging = true; cc.BeginBox(p.X, p.Y); }
        };
        MouseUp += (_, _) => { if (!_dragging) return; _dragging = false; ReleaseMouseCapture(); if (_chart is CartesianChart cc) cc.EndBox(ActualWidth, ActualHeight); InvalidateVisual(); };
        MouseWheel += (_, e) => { if (_chart is not CartesianChart cc) return; var p = e.GetPosition(this); cc.WheelZoom(p.X, p.Y, -e.Delta, cc.Layout(ActualWidth, ActualHeight)); InvalidateVisual(); e.Handled = true; };
    }

    /// <summary>The chart to host; setting it repaints. Pointer interaction is wired for the four analytic chart types, any other drawable is only painted.</summary>
    public IDrawable? Chart { get => _chart; set { _chart = value; InvalidateVisual(); } }

    /// <summary>Request a repaint after changing the model or its data.</summary>
    public void Invalidate() => InvalidateVisual();

    private void Move(double x, double y)
    {
        double w = ActualWidth, h = ActualHeight;
        switch (_chart)
        {
            case CartesianChart c: c.PointerMove(x, y, w, h); break;
            case PieChart c: c.PointerMove(x, y, w, h); break;
            case PolarChart c: c.PointerMove(x, y, w, h); break;
            case Heatmap c: c.PointerMove(x, y, w, h); break;
            default: return;
        }
        InvalidateVisual();
    }

    private void Leave()
    {
        switch (_chart)
        {
            case CartesianChart c: c.PointerLeave(); break;
            case PieChart c: c.PointerLeave(); break;
            case PolarChart c: c.PointerLeave(); break;
            case Heatmap c: c.PointerLeave(); break;
            default: return;
        }
        InvalidateVisual();
    }

    private bool Click(double x, double y)
    {
        double w = ActualWidth, h = ActualHeight;
        return _chart switch
        {
            CartesianChart c => c.Click(x, y, w, h),
            PieChart c => c.Click(x, y, w, h),
            PolarChart c => c.Click(x, y, w, h),
            _ => false,
        };
    }

    private void OnPaint(object? sender, SkiaPaintEventArgs e)
    {
        e.Canvas.Clear(SKColors.Transparent);
        if (_chart is null) return;
        using var painter = new SkiaPainter(e.Canvas, ActualWidth, ActualHeight, e.Scale);
        _chart.Draw(painter, ActualWidth, ActualHeight);
    }
}
