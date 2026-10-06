// Mori.SkyScope — Uploads core rasters (heatmaps) to Skia images once per raster version.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Runtime.CompilerServices;
using Mori.SkyScope.Core.Paint;
using SkiaSharp;

namespace Mori.SkyScope.Render.Skia;

/// <summary>Uploads core <see cref="RasterImage"/>s (heatmaps) to <see cref="SKImage"/>s, once per raster version.</summary>
public static class RasterCache
{
    private sealed class Entry { public SKImage? Image; public long Version = -1; }
    private static readonly ConditionalWeakTable<RasterImage, Entry> Cache = [];

    /// <summary>Returns the cached image for the raster, re-uploading its RGBA pixels only when the raster <c>Version</c> has changed since the last call.</summary>
    public static SKImage Get(RasterImage ri)
    {
        var e = Cache.GetValue(ri, _ => new Entry());
        if (e.Image is not null && e.Version == ri.Version) return e.Image;
        e.Image?.Dispose();
        var info = new SKImageInfo(ri.PixelWidth, ri.PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        e.Image = SKImage.FromPixelCopy(info, ri.Rgba, ri.PixelWidth * 4);
        e.Version = ri.Version;
        return e.Image;
    }
}
