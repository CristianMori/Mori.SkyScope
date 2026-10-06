// Mori.SkyScope — The plain legend shared by the analytic charts, the drawable contract and shared chart drawing helpers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Anything that paints itself into a rectangle — charts, gauges, inputs. Hosts (WPF, snapshots) treat them uniformly.</summary>
public interface IDrawable
{
    /// <summary>Paints into a <paramref name="width"/> × <paramref name="height"/> pixel area at the painter's origin.</summary>
    void Draw(IPainter p, double width, double height);
}

/// <summary>Swatch drawn beside a legend entry: a line, a filled box or a dot.</summary>
public enum LegendShape { Line, Box, Circle }
/// <summary>One legend row: the id reported on click, the display name, the CSS colour, whether it is dimmed as hidden, and the swatch shape.</summary>
public sealed record LegendItem(string Id, string Name, string Color, bool Hidden, LegendShape Shape);

/// <summary>What the shared legend needs from a chart configuration.</summary>
public interface ILegendConfig
{
    /// <summary>Where the legend sits.</summary>
    LegendPosition Legend { get; }
    /// <summary>Width in pixels of a side or overlay legend.</summary>
    double LegendWidth { get; }
    /// <summary>Gap in pixels between the legend and the plot.</summary>
    double Margin { get; }
    /// <summary>Colours and type.</summary>
    ChartTheme Theme { get; }
    /// <summary>Paddings, row height, opacity, radius and swatch length.</summary>
    ChartStyle Style { get; }
}

/// <summary>
/// The plain legend shared by the analytic charts (cartesian, pie, polar): one row per item, click to toggle.
/// Mirrors <c>charts/legend.ts</c>; pinned through the chart fixtures.
/// </summary>
public static class SimpleLegend
{
    /// <summary>Height of one legend row: the configured value, else font size + 8.</summary>
    public static double RowHeight(ILegendConfig c) => c.Style.LegendRowHeight ?? c.Theme.FontSize + 8;
    /// <summary>Height of a vertical legend with <paramref name="count"/> rows, padding included.</summary>
    public static double Height(ILegendConfig c, int count) => 2 * c.Style.LegendPadding + count * RowHeight(c);
    /// <summary>Height of a single-row Top legend.</summary>
    public static double TopHeight(ILegendConfig c) => RowHeight(c) + 2 * c.Style.LegendPadding - 4;

    /// <summary>Reserves space for Right/Top legends; corner overlays are placed later with <see cref="OverlayRect"/>.</summary>
    public static (Rect Inner, Rect? Legend) Reserve(ILegendConfig c, Rect outer, int count)
    {
        if (count == 0 || c.Legend == LegendPosition.None || TrendLayoutEngine.IsOverlay(c.Legend)) return (outer, null);
        if (c.Legend == LegendPosition.Right)
        {
            var w = Math.Min(c.LegendWidth, outer.W);
            return (new Rect(outer.X, outer.Y, outer.W - w - c.Margin, outer.H), new Rect(outer.X + outer.W - w, outer.Y, w, outer.H));
        }
        var h = TopHeight(c);
        return (new Rect(outer.X, outer.Y + h + c.Margin, outer.W, outer.H - h - c.Margin), new Rect(outer.X, outer.Y, outer.W, h));
    }

    /// <summary>Rectangle of a corner legend inside the plot; null when the legend is not an overlay or has no items.</summary>
    public static Rect? OverlayRect(ILegendConfig c, Rect plot, int count)
    {
        if (count == 0 || !TrendLayoutEngine.IsOverlay(c.Legend)) return null;
        var m = c.Margin;
        double w = Math.Min(c.LegendWidth, plot.W), h = Math.Min(Height(c, count), plot.H);
        bool right = c.Legend is LegendPosition.TopRight or LegendPosition.BottomRight, bottom = c.Legend is LegendPosition.BottomLeft or LegendPosition.BottomRight;
        return new Rect(right ? plot.X + plot.W - w - m : plot.X + m, bottom ? plot.Y + plot.H - h - m : plot.Y + m, w, h);
    }

    private static void Swatch(IPainter p, LegendItem item, double x, double cy, double len, double opacity)
    {
        if (item.Shape == LegendShape.Line) p.Line(x, cy, x + len, cy, new Stroke(item.Color) { Width = 2, Opacity = opacity });
        else if (item.Shape == LegendShape.Circle) p.Circle(x + len / 2, cy, Math.Min(5, len / 2), new Fill(item.Color) { Opacity = opacity });
        else p.Rect(x, cy - 5, len, 10, new Fill(item.Color) { Opacity = opacity }, null, 2);
    }

    /// <summary>Paints the legend box and its rows; hidden items are dimmed.</summary>
    public static void Draw(IPainter p, ILegendConfig c, Rect r, IReadOnlyList<LegendItem> items, string? hoverId = null)
    {
        var th = c.Theme; var st = c.Style; double pad = st.LegendPadding, sw = st.LegendSwatchLength, rowH = RowHeight(c);
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize, Baseline = TextBaseline.Middle };
        var muted = text with { Color = th.MutedText };
        p.Rect(r.X, r.Y, r.W, r.H, new Fill(th.LegendBackground) { Opacity = st.LegendOpacity }, new Stroke(th.LegendBorder), st.LegendRadius);
        if (c.Legend == LegendPosition.Top)
        {
            var x = r.X + pad; var cy = r.Y + r.H / 2;
            foreach (var it in items)
            {
                var w = p.MeasureText(it.Name, text).Width;
                if (x + sw + 6 + w > r.X + r.W - pad + 1) break;
                Swatch(p, it, x, cy, sw, it.Hidden ? 0.3 : 1);
                p.Text(it.Name, x + sw + 6, cy, it.Hidden ? muted : text);
                x += sw + 6 + w + 16;
            }
            return;
        }
        var y = r.Y + pad;
        foreach (var it in items)
        {
            if (y + rowH > r.Y + r.H + 1) return;
            var cy = y + rowH / 2;
            if (hoverId == it.Id) p.Rect(r.X + 2, y, r.W - 4, rowH, new Fill(th.Hover) { Opacity = 0.15 }, null, 2);
            Swatch(p, it, r.X + pad, cy, sw, it.Hidden ? 0.3 : 1);
            p.Text(it.Name, r.X + pad + sw + 6, cy, it.Hidden ? muted : text);
            y += rowH;
        }
    }

    /// <summary>Item under (x, y) or null. Text widths use the painter-independent estimate <c>0.6 × size × length</c>.</summary>
    public static string? ItemAt(ILegendConfig c, Rect? rect, IReadOnlyList<LegendItem> items, double x, double y)
    {
        if (rect is not { } r || x < r.X || x > r.X + r.W || y < r.Y || y > r.Y + r.H) return null;
        double pad = c.Style.LegendPadding, sw = c.Style.LegendSwatchLength, rowH = RowHeight(c);
        if (c.Legend == LegendPosition.Top)
        {
            var cx = r.X + pad;
            foreach (var it in items) { var w = sw + 6 + it.Name.Length * c.Theme.FontSize * 0.6; if (x >= cx && x <= cx + w) return it.Id; cx += w + 16; }
            return null;
        }
        var i = (int)Math.Floor((y - r.Y - pad) / rowH);
        return i >= 0 && i < items.Count ? items[i].Id : null;
    }
}

/// <summary>Point marker shape; <see cref="None"/> draws nothing.</summary>
public enum MarkerShape { None, Circle, Square, Diamond }
/// <summary>A hit on a data point: the series id and the point index.</summary>
public sealed record ChartHit(string SeriesId, int Index);
/// <summary>One row of a tooltip: name, formatted value and swatch colour.</summary>
public sealed record TooltipLine(string Name, string Value, string Color);
/// <summary>Tooltip content anchored at pixel (X, Y): a title row and one line per series.</summary>
public sealed record Tooltip(double X, double Y, string Title, IReadOnlyList<TooltipLine> Lines);

/// <summary>Drawing helpers shared by the analytic charts. Mirrors <c>drawMarker</c>/<c>drawTooltip</c> in <c>cartesian.ts</c>.</summary>
public static class ChartDrawing
{
    /// <summary>Draws one marker centred at (x, y); <paramref name="size"/> is the radius (or half side) in pixels.</summary>
    public static void Marker(IPainter p, MarkerShape shape, double x, double y, double size, string color)
    {
        if (shape == MarkerShape.Circle) p.Circle(x, y, size, new Fill(color));
        else if (shape == MarkerShape.Square) p.Rect(x - size, y - size, 2 * size, 2 * size, new Fill(color));
        else if (shape == MarkerShape.Diamond) p.Polygon([x, y - size * 1.3, x + size * 1.3, y, x, y + size * 1.3, x - size * 1.3, y], new Fill(color));
    }

    /// <summary>Tooltip box beside the anchor, flipped to stay inside <paramref name="bounds"/>.</summary>
    public static void TooltipBox(IPainter p, ChartTheme th, ChartStyle st, Rect bounds, Tooltip tt)
    {
        const double pad = 6; var rowH = th.FontSize + 6;
        var text = new TextStyle(th.Text) { Family = th.FontFamily, Size = th.FontSize, Baseline = TextBaseline.Middle };
        var w = p.MeasureText(tt.Title, text).Width;
        foreach (var ln in tt.Lines) w = Math.Max(w, 16 + p.MeasureText($"{ln.Name}  {ln.Value}", text).Width);
        w += 2 * pad;
        var h = 2 * pad + rowH * (1 + tt.Lines.Count);
        double x = tt.X + 12, y = tt.Y + 12;
        if (x + w > bounds.X + bounds.W) x = tt.X - 12 - w;
        if (y + h > bounds.Y + bounds.H) y = tt.Y - 12 - h;
        p.Rect(x, y, w, h, new Fill(th.LegendBackground) { Opacity = 0.95 }, new Stroke(th.LegendBorder), st.LegendRadius);
        p.Text(tt.Title, x + pad, y + pad + rowH / 2, text with { Color = th.MutedText });
        for (var i = 0; i < tt.Lines.Count; i++)
        {
            var ln = tt.Lines[i]; var cy = y + pad + rowH * (i + 1) + rowH / 2;
            p.Rect(x + pad, cy - 4, 8, 8, new Fill(ln.Color), null, 2);
            p.Text($"{ln.Name}  {ln.Value}", x + pad + 16, cy, text);
        }
    }

    /// <summary>JS <c>Math.round</c> (half up) — .NET's default rounds half to even.</summary>
    public static int RoundHalfUp(double x) => (int)Math.Floor(x + 0.5);
}
