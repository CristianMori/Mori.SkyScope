// Mori.SkyScope — Fixture driver for the gauges: steps, layout and drawing-parity queries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Gauges;
using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Tests.Fixtures;

public sealed partial class GaugeDriver
{
    private static double[] R9(IEnumerable<double> xs) => xs.Select(Round.R9).ToArray();

    /// <summary>Applies <c>set</c>, <c>step</c>, pointer, nudge and toggle commands to whichever gauge kind is under test, and answers layout, tick, needle, formatting, settling, value and <c>draw</c> (recorded paint ops) queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var g = s.G; var q = s.Queries; double w = s.W, h = s.H;
        double D(string n) => step.GetProperty(n).GetDouble();
        switch (step.GetProperty("type").GetString())
        {
            case "set":
                switch (g)
                {
                    case RadialGauge rg: rg.SetValue(D("value")); break;
                    case LinearGauge lg: lg.SetValue(D("value")); break;
                    case LedArray la: if (step.TryGetProperty("bits", out var bits)) la.SetBits(bits.GetUInt32()); else la.SetValue(D("value")); break;
                    case NumericDisplay nd: nd.SetValue(step.GetProperty("value").ValueKind == JsonValueKind.Null ? null : D("value")); break;
                    case Compass cp: cp.SetHeading(D("heading")); break;
                    case AttitudeIndicator ai: ai.Set(D("pitch"), D("roll")); break;
                    case Knob kn: kn.SetValue(D("value")); break;
                    case Slider sl: sl.SetValue(D("value")); break;
                    case Switch sw: sw.Set(step.GetProperty("on").GetBoolean()); break;
                    case Led led: led.On = step.GetProperty("on").GetBoolean(); break;
                }
                break;
            case "step":
                switch (g) { case RadialGauge rg: rg.Step(D("dt")); break; case LinearGauge lg: lg.Step(D("dt")); break; case Compass cp: cp.Step(D("dt")); break; case AttitudeIndicator ai: ai.Step(D("dt")); break; }
                break;
            case "setMode": ((LedArray)g).Config.Mode = Str(step, "mode") == "bits" ? LedArrayMode.Bits : LedArrayMode.Level; break;
            case "pointerDown": if (g is Knob k1) k1.PointerDown(k1.Layout(w, h), D("x"), D("y")); else ((Slider)g).PointerDown(w, h, D("x"), D("y")); break;
            case "pointerMove": if (g is Knob k2) k2.PointerMove(k2.Layout(w, h), D("x"), D("y")); else ((Slider)g).PointerMove(w, h, D("x"), D("y")); break;
            case "pointerUp": if (g is Knob k3) k3.PointerUp(); else ((Slider)g).PointerUp(); break;
            case "nudge": if (g is Knob k4) k4.Nudge(D("dir")); else ((Slider)g).Nudge(D("dir")); break;
            case "toggle": ((Switch)g).Toggle(); break;
            case "query":
                if (step.TryGetProperty("angleFor", out var af)) q.Add(Round.R9(g is Knob kk ? kk.AngleFor(af.GetDouble()) : ((RadialGauge)g).AngleFor(af.GetDouble())));
                else if (step.TryGetProperty("ticks", out _)) q.Add((g is RadialGauge r1 ? r1.Ticks() : ((LinearGauge)g).Ticks()).Select(t => new { value = Round.R9(t.Value), major = t.Major, label = t.Label }).ToList());
                else if (step.TryGetProperty("needle", out _)) { var rg = (RadialGauge)g; q.Add(R9(rg.Needle(rg.Layout(w, h)))); }
                else if (step.TryGetProperty("format", out var f)) q.Add(g is RadialGauge r2 ? r2.FormatValue(f.GetDouble()) : ((LinearGauge)g).FormatValue(f.GetDouble()));
                else if (step.TryGetProperty("layout", out _))
                {
                    if (g is RadialGauge r3) { var l = r3.Layout(w, h); q.Add(new { cx = Round.R9(l.Cx), cy = Round.R9(l.Cy), r = Round.R9(l.R) }); }
                    else { var l = ((LinearGauge)g).Layout(w, h); q.Add(new { track = Round.Rect(l.Track), horizontal = l.Horizontal }); }
                }
                else if (step.TryGetProperty("displayed", out _)) q.Add(Round.R9(g is Compass c1 ? c1.Heading.Displayed : g is RadialGauge r4 ? r4.Value.Displayed : ((LinearGauge)g).Value.Displayed));
                else if (step.TryGetProperty("settled", out _)) q.Add(g is Compass c2 ? c2.Heading.Settled : g is RadialGauge r5 ? r5.Value.Settled : ((LinearGauge)g).Value.Settled);
                else if (step.TryGetProperty("posFor", out var pf)) { var lg = (LinearGauge)g; q.Add(Round.R9(lg.PosFor(lg.Layout(w, h), pf.GetDouble()))); }
                else if (step.TryGetProperty("mask", out var mk)) q.Add(NumericDisplay.SevenSegmentMask(mk.GetString()![0]));
                else if (step.TryGetProperty("text", out _)) q.Add(((NumericDisplay)g).Text());
                else if (step.TryGetProperty("lit", out _)) q.Add(((LedArray)g).Lit());
                else if (step.TryGetProperty("valueFromPoint", out var vp)) { var a = SignalSteps.Doubles(vp); q.Add(Round.R9(g is Knob k5 ? k5.ValueFromPoint(k5.Layout(w, h), a[0], a[1]) : ((Slider)g).ValueFromPoint(w, h, a[0], a[1]))); }
                else if (step.TryGetProperty("value", out _)) q.Add(Round.R9(g is Knob k6 ? k6.Value : ((Slider)g).Value));
                else if (step.TryGetProperty("groundPolygon", out _)) { var ai = (AttitudeIndicator)g; q.Add(R9(ai.GroundPolygon(ai.Layout(w, h)))); }
                else if (step.TryGetProperty("on", out _)) q.Add(g is Switch sw ? sw.On : ((Led)g).On);
                else if (step.TryGetProperty("draw", out _))
                {
                    var p = new RecordingPainter(w, h);
                    switch (g)
                    {
                        case RadialGauge x: x.Draw(p, w, h); break; case LinearGauge x: x.Draw(p, w, h); break; case Led x: x.Draw(p, w, h); break; case LedArray x: x.Draw(p, w, h); break;
                        case NumericDisplay x: x.Draw(p, w, h); break; case Compass x: x.Draw(p, w, h); break; case AttitudeIndicator x: x.Draw(p, w, h); break;
                        case Knob x: x.Draw(p, w, h); break; case Switch x: x.Draw(p, w, h); break; case Slider x: x.Draw(p, w, h); break;
                    }
                    q.Add(JsonNode.Parse(p.Ops.ToJsonString()));
                }
                else throw new InvalidOperationException($"unknown query {step}");
                break;
            case var t: throw new InvalidOperationException($"unknown step {t}");
        }
        return s;
    }
}
