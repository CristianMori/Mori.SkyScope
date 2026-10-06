// Mori.SkyScope — Fixture driver for the gauges: configuration parsing.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Mori.SkyScope.Core.Gauges;

namespace Mori.SkyScope.Core.Tests.Fixtures;

public sealed partial class GaugeDriver
{
    private static GaugeTheme Theme(JsonElement o)
    {
        var t = GaugeTheme.Light;
        if (Obj(o, "theme") is not { } th) return t;
        return t with
        {
            Background = Str(th, "background") ?? t.Background, Face = Str(th, "face") ?? t.Face, Track = Str(th, "track") ?? t.Track, Tick = Str(th, "tick") ?? t.Tick, MinorTick = Str(th, "minorTick") ?? t.MinorTick,
            Text = Str(th, "text") ?? t.Text, MutedText = Str(th, "mutedText") ?? t.MutedText, Needle = Str(th, "needle") ?? t.Needle, Hub = Str(th, "hub") ?? t.Hub, Value = Str(th, "value") ?? t.Value,
            LedOff = Str(th, "ledOff") ?? t.LedOff, Accent = Str(th, "accent") ?? t.Accent, FontFamily = Str(th, "fontFamily") ?? t.FontFamily, FontSize = Num(th, "fontSize") ?? t.FontSize,
        };
    }

    private static List<Band> Bands(JsonElement o) => o.TryGetProperty("bands", out var b)
        ? b.EnumerateArray().Select(x => new Band(x.GetProperty("from").GetDouble(), x.GetProperty("to").GetDouble(), x.GetProperty("color").GetString()!) { Label = Str(x, "label") }).ToList()
        : [];

    private static RadialGaugeConfig RadialCfg(JsonElement o)
    {
        var c = new RadialGaugeConfig
        {
            Min = Num(o, "min") ?? 0, Max = Num(o, "max") ?? 100, StartAngle = Num(o, "startAngle") ?? -135, Sweep = Num(o, "sweep") ?? 270, Label = Str(o, "label"), Unit = Str(o, "unit"),
            Decimals = (int?)Num(o, "decimals"), MajorTicks = (int)(Num(o, "majorTicks") ?? 10), MinorPerMajor = (int)(Num(o, "minorPerMajor") ?? 5), Damping = Num(o, "damping") ?? 0.15, Theme = Theme(o),
        };
        c.Bands.AddRange(Bands(o));
        if (Obj(o, "style") is { } st)
        {
            var d = RadialGaugeStyle.Default;
            c.Style = new RadialGaugeStyle
            {
                RingWidth = Num(st, "ringWidth") ?? d.RingWidth, TickLength = Num(st, "tickLength") ?? d.TickLength, MinorTickLength = Num(st, "minorTickLength") ?? d.MinorTickLength,
                NeedleWidth = Num(st, "needleWidth") ?? d.NeedleWidth, HubRadius = Num(st, "hubRadius") ?? d.HubRadius, LabelRadius = Num(st, "labelRadius") ?? d.LabelRadius,
                NeedleStyle = Str(st, "needleStyle") == "arc" ? NeedleStyle.Arc : NeedleStyle.Needle,
                ShowTicks = Bool(st, "showTicks") ?? d.ShowTicks, ShowLabels = Bool(st, "showLabels") ?? d.ShowLabels, ShowValue = Bool(st, "showValue") ?? d.ShowValue, ShowBands = Bool(st, "showBands") ?? d.ShowBands,
            };
        }
        return c;
    }

    private static LinearGaugeConfig LinearCfg(JsonElement o)
    {
        var c = new LinearGaugeConfig
        {
            Min = Num(o, "min") ?? 0, Max = Num(o, "max") ?? 100, Orientation = Orient(o), Label = Str(o, "label"), Unit = Str(o, "unit"), Decimals = (int?)Num(o, "decimals"),
            MajorTicks = (int)(Num(o, "majorTicks") ?? 5), MinorPerMajor = (int)(Num(o, "minorPerMajor") ?? 5), Damping = Num(o, "damping") ?? 0.15, Theme = Theme(o),
        };
        c.Bands.AddRange(Bands(o));
        if (Obj(o, "style") is { } st)
        {
            var d = LinearGaugeStyle.Default;
            c.Style = new LinearGaugeStyle
            {
                BarThickness = Num(st, "barThickness") ?? d.BarThickness, TickLength = Num(st, "tickLength") ?? d.TickLength, MinorTickLength = Num(st, "minorTickLength") ?? d.MinorTickLength, PointerSize = Num(st, "pointerSize") ?? d.PointerSize,
                Fill = Bool(st, "fill") ?? d.Fill, ShowTicks = Bool(st, "showTicks") ?? d.ShowTicks, ShowLabels = Bool(st, "showLabels") ?? d.ShowLabels, ShowValue = Bool(st, "showValue") ?? d.ShowValue, ShowBands = Bool(st, "showBands") ?? d.ShowBands,
            };
        }
        return c;
    }
}
