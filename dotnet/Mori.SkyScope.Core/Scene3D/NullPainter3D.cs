// Mori.SkyScope — A 3D painter that draws nothing, used to traverse layers for their 2D overlays.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>
/// An <see cref="IPainter3D"/> that draws nothing. Hosts whose GL pass and HUD pass run separately (WPF) draw the scene
/// once more with this painter so layer text and labels land on the 2D HUD painter.
/// </summary>
public sealed class NullPainter3D(double width, double height, double pixelRatio = 1) : IPainter3D
{
    /// <summary>Viewport width in logical pixels, as given.</summary>
    public double Width { get; } = width;
    /// <summary>Viewport height in logical pixels, as given.</summary>
    public double Height { get; } = height;
    /// <summary>Device pixels per logical pixel, as given.</summary>
    public double PixelRatio { get; } = pixelRatio;
    /// <summary>No-op.</summary>
    public void Begin(double[] view, double[] proj, string? clear = null) { }
    /// <summary>No-op.</summary>
    public void Points(Mesh3D mesh, Material3D? material = null) { }
    /// <summary>No-op.</summary>
    public void Lines(Mesh3D mesh, Material3D? material = null, bool strip = false) { }
    /// <summary>No-op.</summary>
    public void Triangles(Mesh3D mesh, Material3D? material = null) { }
    /// <summary>No-op.</summary>
    public void Image(IImageHandle image, ReadOnlySpan<double> corners, Material3D? material = null) { }
    /// <summary>No-op.</summary>
    public void End() { }
}
