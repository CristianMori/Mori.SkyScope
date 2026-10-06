// Mori.SkyScope — Windows Forms host for any gauge or input, on a CPU or OpenGL Skia surface.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Render.Skia;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// Hosts any <see cref="IGaugeDrawable"/>. Repaints on <see cref="Refresh()"/> and, while an <see cref="IAnimatedGauge"/>
/// is settling, on a timer. Mouse, wheel and arrow/space keys drive knob, slider and switch inputs.
/// </summary>
[ToolboxItem(true), Description("Hosts a gauge or an input (radial, linear, LED, numeric, compass, attitude, knob, switch, slider).")]
public class GaugeControl : SkiaHostControl
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private DateTime _last = DateTime.UtcNow;
    private IGaugeDrawable? _gauge;
    private bool _captured;

    /// <summary>Creates the control with the animation timer wired but idle.</summary>
    public GaugeControl() { _timer.Tick += (_, _) => Tick(); }

    /// <summary>The gauge or input to draw; setting it repaints.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IGaugeDrawable? Gauge { get => _gauge; set { _gauge = value; Refresh(); } }

    /// <summary>Request a repaint; starts the animation timer when the gauge is still settling.</summary>
    public new void Refresh()
    {
        Redraw();
        if (_gauge is IAnimatedGauge { Animating: true } && !_timer.Enabled) { _last = DateTime.UtcNow; _timer.Start(); }
    }

    private void Tick()
    {
        var now = DateTime.UtcNow; var dt = Math.Min(0.1, (now - _last).TotalSeconds); _last = now;
        if (_gauge is IAnimatedGauge a) { a.Step(dt); Redraw(); if (!a.Animating) _timer.Stop(); }
        else _timer.Stop();
    }

    protected override void OnPaintSurface(SkiaPaintEventArgs e)
    {
        if (_gauge is null) return;
        using var painter = new SkiaPainter(e.Canvas, e.Width, e.Height, e.Scale);
        _gauge.Draw(painter, e.Width, e.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _captured = true; Capture = true; Pointer("down", e); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (_captured) Pointer("move", e); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _captured = false; Capture = false; Pointer("up", e); }
    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Nudge(Math.Sign(e.Delta)); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Up or Keys.Right) Nudge(1);
        else if (e.KeyCode is Keys.Down or Keys.Left) Nudge(-1);
        else if (e.KeyCode is Keys.Space or Keys.Enter && _gauge is Switch sw) { sw.Toggle(); Refresh(); }
    }

    private void Pointer(string type, MouseEventArgs e)
    {
        double w = LogicalWidth, h = LogicalHeight; var (x, y) = Logical(e);
        switch (_gauge)
        {
            case Knob k: { var l = k.Layout(w, h); if (type == "down") k.PointerDown(l, x, y); else if (type == "move") k.PointerMove(l, x, y); else k.PointerUp(); break; }
            case Slider s: { if (type == "down") s.PointerDown(w, h, x, y); else if (type == "move") s.PointerMove(w, h, x, y); else s.PointerUp(); break; }
            case Switch sw: if (type == "down") sw.Toggle(); break;
            default: return;
        }
        Refresh();
    }

    private void Nudge(int dir)
    {
        switch (_gauge) { case Knob k: k.Nudge(dir); break; case Slider s: s.Nudge(dir); break; default: return; }
        Refresh();
    }

    protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
}
