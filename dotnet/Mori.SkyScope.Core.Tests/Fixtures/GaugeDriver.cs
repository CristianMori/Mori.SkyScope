// Mori.SkyScope — Fixture driver for the gauges: creation, stepping and snapshots.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Gauges;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the gauges: builds any gauge kind from JSON options, applies value, animation and pointer steps and answers geometry, text and drawing queries.</summary>
public sealed partial class GaugeDriver : IFixtureDriver
{
    /// <summary>Handles the <c>gauges</c> fixtures.</summary>
    public string Component => "gauges";

    private sealed class State(string kind, object g, double w, double h)
    {
        public string Kind = kind; public object G = g; public double W = w, H = h; public List<object?> Queries = [];
    }

    private static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    private static bool? Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;
    private static JsonElement? Obj(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
    private static Orientation Orient(JsonElement o) => Str(o, "orientation") == "vertical" ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>Instantiates the gauge named by <c>gauge</c> with its parsed options and optional initial value, remembering the widget size for layout queries.</summary>
    public object Create(JsonElement setup)
    {
        var kind = setup.GetProperty("gauge").GetString()!;
        var o = setup.TryGetProperty("options", out var oo) ? oo : JsonDocument.Parse("{}").RootElement;
        var init = Num(setup, "initial");
        object g = kind switch
        {
            "radial" => new RadialGauge(RadialCfg(o), init),
            "linear" => new LinearGauge(LinearCfg(o), init),
            "led" => new Led(new LedConfig { Label = Str(o, "label"), OnColor = Str(o, "onColor") ?? "#16a34a", OffColor = Str(o, "offColor"), Shape = Str(o, "shape") == "square" ? LedShape.Square : LedShape.Round, Theme = Theme(o) }),
            "ledArray" => new LedArray(new LedArrayConfig { Count = (int)(Num(o, "count") ?? 10), Orientation = Orient(o), Mode = Str(o, "mode") == "bits" ? LedArrayMode.Bits : LedArrayMode.Level, Min = Num(o, "min") ?? 0, Max = Num(o, "max") ?? 100, Label = Str(o, "label"), Gap = Num(o, "gap") ?? 3, Theme = Theme(o) }),
            "numeric" => new NumericDisplay(new NumericDisplayConfig { Digits = (int)(Num(o, "digits") ?? 5), Decimals = (int)(Num(o, "decimals") ?? 1), Label = Str(o, "label"), Unit = Str(o, "unit"), SegmentColor = Str(o, "segmentColor") ?? "#16a34a", OffOpacity = Num(o, "offOpacity") ?? 0.08, Slant = Num(o, "slant") ?? 0.08, Theme = Theme(o) }),
            "compass" => new Compass(new CompassConfig { Mode = Str(o, "mode") == "card" ? CompassMode.Card : CompassMode.Needle, Label = Str(o, "label"), Damping = Num(o, "damping") ?? 0.2, ShowValue = Bool(o, "showValue") ?? true, Theme = Theme(o) }, init ?? 0),
            "attitude" => new AttitudeIndicator(new AttitudeConfig { Sky = Str(o, "sky") ?? "#3b82f6", Ground = Str(o, "ground") ?? "#92400e", Horizon = Str(o, "horizon") ?? "#ffffff", Symbol = Str(o, "symbol") ?? "#facc15", PitchLadderDeg = Num(o, "pitchLadderDeg") ?? 10, VisiblePitchDeg = Num(o, "visiblePitchDeg") ?? 45, Damping = Num(o, "damping") ?? 0.12, Theme = Theme(o) }),
            "knob" => new Knob(new KnobConfig { Min = Num(o, "min") ?? 0, Max = Num(o, "max") ?? 100, Step = Num(o, "step") ?? 0, StartAngle = Num(o, "startAngle") ?? -135, Sweep = Num(o, "sweep") ?? 270, Label = Str(o, "label"), Unit = Str(o, "unit"), Decimals = (int?)Num(o, "decimals"), Theme = Theme(o) }, init),
            "switch" => new Switch(new SwitchConfig { Label = Str(o, "label"), OnLabel = Str(o, "onLabel") ?? "ON", OffLabel = Str(o, "offLabel") ?? "OFF", Theme = Theme(o) }),
            "slider" => new Slider(new SliderConfig { Min = Num(o, "min") ?? 0, Max = Num(o, "max") ?? 100, Step = Num(o, "step") ?? 0, Orientation = Orient(o), Label = Str(o, "label"), Unit = Str(o, "unit"), Decimals = (int?)Num(o, "decimals"), Theme = Theme(o) }, init),
            _ => throw new InvalidOperationException($"unknown gauge {kind}"),
        };
        return new State(kind, g, setup.GetProperty("width").GetDouble(), setup.GetProperty("height").GetDouble());
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
