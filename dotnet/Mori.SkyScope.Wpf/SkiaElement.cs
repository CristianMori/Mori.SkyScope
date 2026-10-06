// Mori.SkyScope — A WPF element painted by SkiaSharp into a writeable bitmap at the monitor's device pixel ratio.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace Mori.SkyScope.Wpf;

/// <summary>Arguments of <see cref="SkiaElement.PaintSurface"/>: the canvas to draw on, its pixel geometry and the DPI scale.</summary>
public sealed class SkiaPaintEventArgs(SKCanvas canvas, SKImageInfo info, double scale) : EventArgs
{
    /// <summary>The canvas backed by the element's bitmap, already cleared to transparent.</summary>
    public SKCanvas Canvas { get; } = canvas;
    /// <summary>Pixel size and format of the backing surface.</summary>
    public SKImageInfo Info { get; } = info;
    /// <summary>Device pixels per WPF unit (the monitor DPI scale).</summary>
    public double Scale { get; } = scale;
}

/// <summary>
/// A WPF element that paints with SkiaSharp into a <see cref="WriteableBitmap"/> — CPU raster, DPI-aware, no
/// extra view packages. (A GPU-backed host can come later behind the same <see cref="PaintSurface"/> event.)
/// </summary>
public class SkiaElement : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    /// <summary>Raised on every render pass with a canvas sized to the element at device resolution; draw the content here.</summary>
    public event EventHandler<SkiaPaintEventArgs>? PaintSurface;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 1 || h < 1) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        int pw = Math.Max(1, (int)Math.Ceiling(w * dpi.DpiScaleX)), ph = Math.Max(1, (int)Math.Ceiling(h * dpi.DpiScaleY));
        if (_bitmap is null || _bitmap.PixelWidth != pw || _bitmap.PixelHeight != ph)
            _bitmap = new WriteableBitmap(pw, ph, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
        var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
        _bitmap.Lock();
        try
        {
            using var surface = SKSurface.Create(info, _bitmap.BackBuffer, _bitmap.BackBufferStride);
            surface.Canvas.Clear(SKColors.Transparent);
            PaintSurface?.Invoke(this, new SkiaPaintEventArgs(surface.Canvas, info, dpi.DpiScaleX));
            surface.Canvas.Flush();
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, pw, ph));
        }
        finally { _bitmap.Unlock(); }
        dc.DrawImage(_bitmap, new Rect(0, 0, w, h));
    }
}
