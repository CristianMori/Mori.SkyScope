// Mori.SkyScope — Windows Forms host for an analytic chart: hover tooltips, legend clicks, box zoom, wheel zoom, double-click reset.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.ComponentModel;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Render.Skia;

namespace Mori.SkyScope.WinForms;

/// <summary>
/// Hosts an analytic chart (<see cref="CartesianChart"/>, <see cref="PieChart"/>, <see cref="PolarChart"/>, <see cref="Heatmap"/>)
/// or any other <see cref="IDrawable"/>: hover tooltips, legend clicks, drag = box zoom, wheel = zoom, double-click = reset.
/// Repaints only on <see cref="Refresh()"/> or interaction.
/// </summary>
[ToolboxItem(true), Description("Hosts an XY, pie, polar or heatmap chart.")]
public class ChartControl : SkiaHostControl
{
    private IDrawable? _chart;
    private bool _dragging;

    /// <summary>The chart to host; setting it repaints. Pointer interaction is wired for the four analytic chart types, any other drawable is only painted.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IDrawable? Chart { get => _chart; set { _chart = value; Redraw(); } }

    /// <summary>Request a repaint after changing the model or its data.</summary>
    public new void Refresh() => Redraw();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (x, y) = Logical(e); double w = LogicalWidth, h = LogicalHeight;
        switch (_chart)
        {
            case CartesianChart c: c.PointerMove(x, y, w, h); break;
            case PieChart c: c.PointerMove(x, y, w, h); break;
            case PolarChart c: c.PointerMove(x, y, w, h); break;
            case Heatmap c: c.PointerMove(x, y, w, h); break;
            default: return;
        }
        Redraw();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        switch (_chart)
        {
            case CartesianChart c: c.PointerLeave(); break;
            case PieChart c: c.PointerLeave(); break;
            case PolarChart c: c.PointerLeave(); break;
            case Heatmap c: c.PointerLeave(); break;
            default: return;
        }
        Redraw();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var (x, y) = Logical(e); double w = LogicalWidth, h = LogicalHeight;
        if (e.Clicks == 2) { if (_chart is CartesianChart cz) { cz.ResetZoom(); Redraw(); } return; }
        var clicked = _chart switch { CartesianChart c => c.Click(x, y, w, h), PieChart c => c.Click(x, y, w, h), PolarChart c => c.Click(x, y, w, h), _ => false };
        if (clicked) { Redraw(); return; }
        if (_chart is CartesianChart cc) { Capture = true; _dragging = true; cc.BeginBox(x, y); }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        _dragging = false; Capture = false;
        if (_chart is CartesianChart cc) cc.EndBox(LogicalWidth, LogicalHeight);
        Redraw();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_chart is not CartesianChart cc) return;
        var (x, y) = Logical(e);
        cc.WheelZoom(x, y, -e.Delta, cc.Layout(LogicalWidth, LogicalHeight));
        Redraw();
    }

    protected override void OnPaintSurface(SkiaPaintEventArgs e)
    {
        if (_chart is null) return;
        using var painter = new SkiaPainter(e.Canvas, e.Width, e.Height, e.Scale);
        _chart.Draw(painter, e.Width, e.Height);
    }
}
