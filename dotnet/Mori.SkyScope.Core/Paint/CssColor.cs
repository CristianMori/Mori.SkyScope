// Mori.SkyScope — An sRGB colour with straight alpha, parsed from the CSS forms the tokens use.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;

namespace Mori.SkyScope.Core.Paint;

/// <summary>An sRGB colour with straight alpha, parsed from the CSS forms the tokens use.</summary>
/// <param name="R">Red, 0–255.</param>
/// <param name="G">Green, 0–255.</param>
/// <param name="B">Blue, 0–255.</param>
/// <param name="A">Straight (non-premultiplied) alpha, 0–1.</param>
public readonly record struct CssColor(byte R, byte G, byte B, double A = 1)
{
    /// <summary>Fully transparent black.</summary>
    public static readonly CssColor Transparent = new(0, 0, 0, 0);
    /// <summary>Opaque black.</summary>
    public static readonly CssColor Black = new(0, 0, 0);
    /// <summary>Opaque white.</summary>
    public static readonly CssColor White = new(255, 255, 255);

    /// <summary>Copy with alpha <paramref name="a"/>, clamped to [0, 1].</summary>
    public CssColor WithAlpha(double a) => this with { A = Math.Clamp(a, 0, 1) };

    private static readonly Dictionary<string, CssColor> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = Transparent, ["black"] = Black, ["white"] = White,
        ["red"] = new(255, 0, 0), ["green"] = new(0, 128, 0), ["blue"] = new(0, 0, 255), ["yellow"] = new(255, 255, 0),
        ["cyan"] = new(0, 255, 255), ["magenta"] = new(255, 0, 255), ["gray"] = new(128, 128, 128), ["grey"] = new(128, 128, 128),
        ["orange"] = new(255, 165, 0), ["lime"] = new(0, 255, 0), ["currentcolor"] = Black,
    };

    /// <summary>Parses #rgb, #rgba, #rrggbb, #rrggbbaa, rgb()/rgba() (ints or %, alpha 0–1 or %), and common names.</summary>
    public static bool TryParse(string? text, out CssColor color)
    {
        color = Transparent;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s[0] == '#') return TryParseHex(s.AsSpan(1), out color);
        if (Named.TryGetValue(s, out color)) return true;
        var open = s.IndexOf('(');
        if (open > 0 && s.EndsWith(')') && s[..open].Trim().ToLowerInvariant() is "rgb" or "rgba")
        {
            var parts = s[(open + 1)..^1].Replace('/', ',').Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length is 3 or 4 && Channel(parts[0], out var r) && Channel(parts[1], out var g) && Channel(parts[2], out var b))
            {
                double a = 1;
                if (parts.Length == 4 && !Alpha(parts[3], out a)) return false;
                color = new CssColor(r, g, b, a);
                return true;
            }
        }
        return false;
    }

    /// <summary>Parses like <see cref="TryParse"/> and throws <see cref="FormatException"/> when the text is not a colour.</summary>
    public static CssColor Parse(string? text) => TryParse(text, out var c) ? c : throw new FormatException($"Unrecognised colour '{text}'");

    private static bool TryParseHex(ReadOnlySpan<char> h, out CssColor color)
    {
        color = Transparent;
        static bool Hex(ReadOnlySpan<char> s, out byte v) => byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v);
        static bool Hex1(char c, out byte v) { var ok = Hex([c, c], out v); return ok; }
        switch (h.Length)
        {
            case 3 or 4:
                if (!Hex1(h[0], out var r) || !Hex1(h[1], out var g) || !Hex1(h[2], out var b)) return false;
                byte a3 = 255; if (h.Length == 4 && !Hex1(h[3], out a3)) return false;
                color = new CssColor(r, g, b, a3 / 255.0); return true;
            case 6 or 8:
                if (!Hex(h[..2], out var r6) || !Hex(h[2..4], out var g6) || !Hex(h[4..6], out var b6)) return false;
                byte a8 = 255; if (h.Length == 8 && !Hex(h[6..8], out a8)) return false;
                color = new CssColor(r6, g6, b6, a8 / 255.0); return true;
            default: return false;
        }
    }

    private static bool Channel(string p, out byte v)
    {
        v = 0;
        if (p.EndsWith('%')) { if (!double.TryParse(p[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return false; v = (byte)Math.Clamp(Math.Round(pct * 2.55), 0, 255); return true; }
        if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return false;
        v = (byte)Math.Clamp(Math.Round(d), 0, 255); return true;
    }

    private static bool Alpha(string p, out double a)
    {
        a = 1;
        if (p.EndsWith('%')) { if (!double.TryParse(p[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct)) return false; a = Math.Clamp(pct / 100, 0, 1); return true; }
        if (!double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out a)) return false;
        a = Math.Clamp(a, 0, 1); return true;
    }
}
