// Mori.SkyScope — Data → GPU clip-space mapping for a pair of axis scales: clip = data · scale + offset.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Scales;

/// <summary>
/// Data → GPU clip-space mapping for a pair of axis scales: clip = data · scale + offset. <c>XOrigin</c> is
/// subtracted from x before upload so f32 vertex buffers keep precision on unix-epoch timestamps.
/// Mirrors <c>scales/clip.ts</c>; pinned by <c>spec/fixtures/clip-transform.json</c>.
/// </summary>
/// <param name="Sx">Clip units per data unit along x.</param>
/// <param name="Ox">Clip x of data x = XOrigin.</param>
/// <param name="Sy">Clip units per data unit along y (negative, since clip y points up).</param>
/// <param name="Oy">Clip y of data y = 0.</param>
public readonly record struct ClipTransform(double Sx, double Ox, double Sy, double Oy)
{
    /// <summary>Builds the transform from two pixel scales and the viewport size in pixels; the x slope and offset are evaluated at <paramref name="xOrigin"/> so epoch-sized times keep precision.</summary>
    public static ClipTransform From(Scale xScale, Scale yScale, double viewportWidth, double viewportHeight, double xOrigin = 0)
    {
        // Slope and offset are evaluated *at the origin* so epoch-sized x never meets catastrophic cancellation.
        double kx = xScale.Apply(xOrigin + 1) - xScale.Apply(xOrigin), bx = xScale.Apply(xOrigin);
        double ky = yScale.Apply(1) - yScale.Apply(0), by = yScale.Apply(0);
        var sx = 2 * kx / viewportWidth;
        var ox = 2 * bx / viewportWidth - 1;
        var sy = -2 * ky / viewportHeight;
        var oy = 1 - 2 * by / viewportHeight;
        return new ClipTransform(sx, ox, sy, oy);
    }

    /// <summary>Clip-space position of a data point; <paramref name="xOrigin"/> must match the one given to <see cref="From"/>.</summary>
    public (double X, double Y) Apply(double x, double y, double xOrigin = 0) => ((x - xOrigin) * Sx + Ox, y * Sy + Oy);
}
