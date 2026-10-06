// Mori.SkyScope — Colour maps for heatmaps/spectrograms: piecewise-linear RGB interpolation between hex stops.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Colour maps for heatmaps/spectrograms: piecewise-linear RGB interpolation between hex stops. Mirrors <c>charts/colormaps.ts</c>.</summary>
public static class Colormaps
{
    /// <summary>Built-in maps by name (viridis, inferno, plasma, turbo, jet, grayscale, coolwarm) as hex stops from low to high.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Named = new Dictionary<string, string[]>
    {
        ["viridis"] = ["#440154", "#482878", "#3e4989", "#31688e", "#26828e", "#1f9e89", "#35b779", "#6ece58", "#b5de2b", "#fde725"],
        ["inferno"] = ["#000004", "#1b0c41", "#4a0c6b", "#781c6d", "#a52c60", "#cf4446", "#ed6925", "#fb9b06", "#f7d13d", "#fcffa4"],
        ["plasma"] = ["#0d0887", "#41049d", "#6a00a8", "#8f0da4", "#b12a90", "#cc4778", "#e16462", "#f2844b", "#fca636", "#fcce25", "#f0f921"],
        ["turbo"] = ["#30123b", "#4662d7", "#36aaf9", "#1ae4b6", "#72fe5e", "#c8ef34", "#faba39", "#f66b19", "#ca2a04", "#7a0403"],
        ["jet"] = ["#00007f", "#0000ff", "#007fff", "#00ffff", "#7fff7f", "#ffff00", "#ff7f00", "#ff0000", "#7f0000"],
        ["grayscale"] = ["#000000", "#ffffff"],
        ["coolwarm"] = ["#3b4cc0", "#8db0fe", "#dddddd", "#f49a7b", "#b40426"],
    };

    /// <summary>A named map or an explicit stop list.</summary>
    public static string[] Stops(string name) => Named.TryGetValue(name, out var s) ? s : throw new ArgumentException($"unknown colormap '{name}'", nameof(name));

    /// <summary>RGB components of a #rgb or #rrggbb colour; the hash is optional.</summary>
    public static (int R, int G, int B) ParseHex(string c)
    {
        var h = c.StartsWith('#') ? c[1..] : c;
        if (h.Length == 3) h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
        return (int.Parse(h[..2], NumberStyles.HexNumber), int.Parse(h[2..4], NumberStyles.HexNumber), int.Parse(h[4..6], NumberStyles.HexNumber));
    }
    /// <summary>Lower-case #rrggbb for the given components (0–255).</summary>
    public static string ToHex(int r, int g, int b) => $"#{r:x2}{g:x2}{b:x2}";

    /// <summary>RGB for <paramref name="t"/> in [0, 1] (clamped); NaN maps to the first stop.</summary>
    public static (int R, int G, int B) Rgb(string[] stops, double t)
    {
        var n = stops.Length;
        if (n == 1) return ParseHex(stops[0]);
        var u = double.IsNaN(t) ? 0 : Math.Min(1, Math.Max(0, t));
        var seg = u * (n - 1);
        var i = (int)Math.Floor(seg);
        if (i > n - 2) i = n - 2;
        var f = seg - i;
        var a = ParseHex(stops[i]); var b = ParseHex(stops[i + 1]);
        return (ChartDrawing.RoundHalfUp(a.R + (b.R - a.R) * f), ChartDrawing.RoundHalfUp(a.G + (b.G - a.G) * f), ChartDrawing.RoundHalfUp(a.B + (b.B - a.B) * f));
    }
    /// <summary>Hex colour for <paramref name="t"/> in [0, 1]; see <see cref="Rgb"/>.</summary>
    public static string Hex(string[] stops, double t) { var (r, g, b) = Rgb(stops, t); return ToHex(r, g, b); }

    /// <summary><paramref name="size"/> RGB triplets (flat) for fast per-pixel lookup.</summary>
    public static byte[] Lut(string[] stops, int size = 256)
    {
        var lut = new byte[size * 3];
        for (var i = 0; i < size; i++) { var (r, g, b) = Rgb(stops, size == 1 ? 0 : (double)i / (size - 1)); lut[i * 3] = (byte)r; lut[i * 3 + 1] = (byte)g; lut[i * 3 + 2] = (byte)b; }
        return lut;
    }
}
