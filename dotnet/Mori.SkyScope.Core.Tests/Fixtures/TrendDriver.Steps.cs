// Mori.SkyScope — Fixture driver for the trend chart: commands, layout, hit-test, drop, navigator and drawing queries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Tests.Fixtures;

public sealed partial class TrendDriver
{
    private static DropTarget ParseTarget(JsonElement t) => t.GetProperty("kind").GetString() switch
    {
        "join" => new DropTarget.Join(t.GetProperty("laneId").GetString()!, t.GetProperty("axisId").GetString()!),
        "ownAxis" => new DropTarget.OwnAxis(t.GetProperty("laneId").GetString()!),
        "stack" => new DropTarget.Stack(t.GetProperty("laneId").GetString()!),
        "newLane" => new DropTarget.NewLane(t.GetProperty("index").GetInt32(), t.TryGetProperty("afterLaneId", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null),
        _ => new DropTarget.None(),
    };
    private static object TargetJson(DropTarget t) => t switch
    {
        DropTarget.Join j => new { kind = "join", laneId = j.LaneId, axisId = j.AxisId },
        DropTarget.OwnAxis o => new { kind = "ownAxis", laneId = o.LaneId },
        DropTarget.Stack k => new { kind = "stack", laneId = k.LaneId },
        DropTarget.NewLane n => new { kind = "newLane", index = n.Index, afterLaneId = n.AfterLaneId },
        _ => new { kind = "none" },
    };

    private static object? Opt(double? v) => v is { } d ? Round.R9(d) : null;
    private static string Zone(AxisZone z) => z switch { AxisZone.Top => "top", AxisZone.Bottom => "bottom", _ => "middle" };
    private static AxisZone ParseZone(string? z) => z switch { "top" => AxisZone.Top, "bottom" => AxisZone.Bottom, _ => AxisZone.Middle };
    private static string NavZone(NavigatorZone z) => z switch { NavigatorZone.LeftEdge => "leftEdge", NavigatorZone.RightEdge => "rightEdge", NavigatorZone.Inside => "inside", _ => "outside" };
    private static object HitJson(HitRegion h) => h switch
    {
        HitRegion.Label l => new { kind = "label", seriesId = l.SeriesId, laneId = l.LaneId },
        HitRegion.LegendRow r => new { kind = "legendRow", seriesId = r.SeriesId },
        HitRegion.Header hd => new { kind = "header", laneId = hd.LaneId, part = hd.Part switch { HeaderPart.Collapse => "collapse", HeaderPart.Remove => "remove", _ => "grip" } },
        HitRegion.Axis a => new { kind = "axis", axisId = a.AxisId, laneId = a.LaneId, zone = Zone(a.Zone) },
        HitRegion.Navigator n => new { kind = "navigator", zone = NavZone(n.Zone) },
        HitRegion.Plot pl => new { kind = "plot", laneId = pl.LaneId },
        HitRegion.Stack sk => new { kind = "stack", laneId = sk.LaneId },
        HitRegion.TimeAxis => new { kind = "timeAxis" },
        _ => new { kind = "none" },
    };
    private static List<string> Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToList();
    private static object Ro(Readout r) => new { time = Round.R9(r.Time), values = r.Values.Select(v => new { seriesId = v.SeriesId, time = Round.R9(v.Time), value = Round.R9(v.Value) }).ToList() };

    /// <summary>Applies the model commands (time window, cursors, series and lane moves, drags, axis and navigator interaction, effects) and answers layout, hit-test, drag state, config, window, domain, polyline, readout, legend, drop-target and draw queries; structural changes invalidate the cached layout.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var m = s.Model; var q = s.Queries;
        switch (step.GetProperty("type").GetString())
        {
            case "update": s.Layout = m.Layout(s.Width, s.Height); m.Update(s.Layout); break;
            case "setNow": m.Now = step.GetProperty("now").GetDouble(); break;
            case "pause": m.Pause(); break;
            case "resume": m.Resume(); break;
            case "scrollBy": m.ScrollBy(step.GetProperty("seconds").GetDouble()); break;
            case "setTimeSpan": m.SetTimeSpan(step.GetProperty("seconds").GetDouble()); break;
            case "setCursor": m.SetCursor(step.GetProperty("which").GetString()![0], Num(step, "time")); break;
            case "moveSeries": m.MoveSeries(step.GetProperty("seriesId").GetString()!, step.GetProperty("laneId").GetString()!, Str(step, "axisId")); break;
            case "reset": m.Reset(); break;
            case "setLegend": m.Config.Legend = step.GetProperty("position").GetString() switch { "top-left" => LegendPosition.TopLeft, "top-right" => LegendPosition.TopRight, "bottom-left" => LegendPosition.BottomLeft, "bottom-right" => LegendPosition.BottomRight, "right" => LegendPosition.Right, "top" => LegendPosition.Top, _ => LegendPosition.None }; s.Layout = null; break;
            case "applyDrop": m.ApplyDrop(step.GetProperty("seriesId").GetString()!, ParseTarget(step.GetProperty("target"))); s.Layout = null; break;
            case "beginDrag":
                {
                    var ids = step.TryGetProperty("seriesIds", out var sids) ? Strings(sids) : [step.GetProperty("seriesId").GetString()!];
                    var chs = step.TryGetProperty("channelIds", out var cids) ? cids.EnumerateArray().Select(x => x.GetInt32()).ToList() : [];
                    m.BeginDrag(ids, step.GetProperty("x").GetDouble(), step.GetProperty("y").GetDouble(), Bool(step, "group") ?? false, chs);
                    break;
                }
            case "applyGroupDrop": m.ApplyGroupDrop(Strings(step.GetProperty("seriesIds")), ParseTarget(step.GetProperty("target")), Bool(step, "group") ?? false); s.Layout = null; break;
            case "setSeriesVisible": m.SetSeriesVisible(step.GetProperty("seriesId").GetString()!, Bool(step, "visible") ?? true); s.Layout = null; break;
            case "moveLane": m.MoveLane(step.GetProperty("laneId").GetString()!, step.GetProperty("index").GetInt32()); s.Layout = null; break;
            case "setLaneCollapsed": m.SetLaneCollapsed(step.GetProperty("laneId").GetString()!, Bool(step, "collapsed") ?? true); s.Layout = null; break;
            case "removeLane": m.RemoveLane(step.GetProperty("laneId").GetString()!); s.Layout = null; break;
            case "beginLaneDrag": m.BeginLaneDrag(step.GetProperty("laneId").GetString()!, step.GetProperty("x").GetDouble(), step.GetProperty("y").GetDouble()); break;
            case "updateLaneDrag": m.UpdateLaneDrag(s.Ensure(), step.GetProperty("x").GetDouble(), step.GetProperty("y").GetDouble()); break;
            case "endLaneDrag": m.EndLaneDrag(s.Ensure(), step.GetProperty("x").GetDouble(), step.GetProperty("y").GetDouble()); s.Layout = null; break;
            case "beginAxisDrag": m.BeginAxisDrag(s.Ensure(), step.GetProperty("axisId").GetString()!, step.GetProperty("laneId").GetString()!, ParseZone(Str(step, "zone")), step.GetProperty("y").GetDouble()); break;
            case "updateAxisDrag": m.UpdateAxisDrag(s.Ensure(), step.GetProperty("y").GetDouble()); break;
            case "endAxisDrag": m.EndAxisDrag(); break;
            case "axisZoomAt": m.AxisZoomAt(s.Ensure(), step.GetProperty("axisId").GetString()!, step.GetProperty("laneId").GetString()!, step.GetProperty("y").GetDouble(), step.GetProperty("factor").GetDouble()); break;
            case "axisAutoscale": m.AxisAutoscale(step.GetProperty("axisId").GetString()!); break;
            case "setNavigatorRange": { var r = step.GetProperty("range"); m.NavigatorRange = r.ValueKind == JsonValueKind.Null ? null : (r[0].GetDouble(), r[1].GetDouble()); break; }
            case "beginNavigatorDrag": m.BeginNavigatorDrag(s.Ensure(), step.GetProperty("x").GetDouble()); break;
            case "updateNavigatorDrag": m.UpdateNavigatorDrag(s.Ensure(), step.GetProperty("x").GetDouble()); break;
            case "endNavigatorDrag": m.EndNavigatorDrag(); break;
            case "navigatorCenterAt": m.NavigatorCenterAt(s.Ensure(), step.GetProperty("x").GetDouble()); break;
            case "navigatorKey": m.NavigatorKey(step.GetProperty("key").GetString()!); break;
            case "setReviewEnd": m.SetReviewEnd(step.GetProperty("t").GetDouble()); break;
            case "updateDrag": m.UpdateDrag(s.Ensure(), step.GetProperty("x").GetDouble(), step.GetProperty("y").GetDouble()); break;
            case "endDrag": m.EndDrag(s.Ensure(), step.GetProperty("x").GetDouble(), step.GetProperty("y").GetDouble()); s.Layout = null; break;
            case "cancelDrag": m.CancelDrag(); break;
            case "effect": m.ApplyEffect(InteractionDriver.ParseEffect(step.GetProperty("effect")), s.Ensure()); break;
            case "query":
                if (step.TryGetProperty("layout", out _))
                {
                    var l = s.Ensure();
                    q.Add(new
                    {
                        plot = Round.Rect(l.Plot), timeAxis = Round.Rect(l.TimeAxis), legend = Round.Rect(l.Legend), navigator = Round.Rect(l.Navigator),
                        lanes = l.Lanes.Select(ln => new { laneId = ln.LaneId, rect = Round.Rect(ln.Rect), analog = Round.Rect(ln.Analog), stack = Round.Rect(ln.Stack), collapsed = ln.Collapsed, header = Round.Rect(ln.Header), axes = ln.Axes.Select(a => new { axisId = a.AxisId, side = a.Side == AxisSide.Left ? "left" : "right", rect = Round.Rect(a.Rect) }).ToList(), labels = ln.Labels.Select(lb => new { seriesId = lb.SeriesId, rect = Round.Rect(lb.Rect) }).ToList() }).ToList(),
                    });
                }
                else if (step.TryGetProperty("hitTest", out var ht)) { var a = SignalSteps.Doubles(ht); q.Add(HitJson(m.HitTest(s.Ensure(), a[0], a[1]))); }
                else if (step.TryGetProperty("laneDrag", out _)) q.Add(m.LaneDrag is { } ld ? new { laneId = ld.LaneId, index = ld.Index } : null);
                else if (step.TryGetProperty("axisDrag", out _)) q.Add(m.AxisDrag is { } ad ? new { axisId = ad.AxisId, zone = Zone(ad.Zone), min0 = Round.R9(ad.Min0), max0 = Round.R9(ad.Max0) } : null);
                else if (step.TryGetProperty("laneConfig", out _)) q.Add(m.Lanes().Select(l => new { id = l.Id, collapsed = l.Collapsed }).ToList());
                else if (step.TryGetProperty("seriesIds", out _)) q.Add(m.Config.Series.Select(x => new { id = x.Id, channelId = x.ChannelId, lane = m.LaneIdOf(x), axis = m.AxisIdOf(x), visible = x.Visible }).ToList());
                else if (step.TryGetProperty("navigatorRange", out _)) { var (t0, t1) = m.NavigatorFullRange(); q.Add(new { t0 = Round.R9(t0), t1 = Round.R9(t1) }); }
                else if (step.TryGetProperty("navigatorFrame", out _)) q.Add(Round.Rect(m.NavigatorFrame(s.Ensure())));
                else if (step.TryGetProperty("navigatorZone", out var nz)) q.Add(NavZone(m.NavigatorZoneAt(s.Ensure(), nz.GetDouble())));
                else if (step.TryGetProperty("navigatorSilhouettes", out _)) q.Add(m.NavigatorSilhouettes(s.Ensure()).Select(x => new { seriesId = x.SeriesId, color = x.Color, count = x.Points.Length / 3, first = x.Points.Take(3).Select(Round.R9).ToArray(), last = x.Points.Skip(Math.Max(0, x.Points.Length - 3)).Select(Round.R9).ToArray() }).ToList());
                else if (step.TryGetProperty("window", out _)) { var (t0, t1) = m.Window(); q.Add(new { t0 = Round.R9(t0), t1 = Round.R9(t1) }); }
                else if (step.TryGetProperty("yDomain", out var yd)) { var (lo, hi) = m.YDomain(yd.GetString()!); q.Add(new[] { Round.R9(lo), Round.R9(hi) }); }
                else if (step.TryGetProperty("polyline", out var pl)) q.Add(m.SeriesPolyline(pl.GetString()!).Select(Round.R9).ToList());
                else if (step.TryGetProperty("readout", out var ro)) q.Add(Ro(m.ReadoutAt(ro.GetDouble())));
                else if (step.TryGetProperty("cursorReadouts", out _))
                {
                    var c = m.CursorReadouts();
                    q.Add(new
                    {
                        a = c.A is null ? null : Ro(c.A), b = c.B is null ? null : Ro(c.B),
                        delta = c.Delta is null ? null : new { dt = Round.R9(c.Delta.Dt), values = c.Delta.Values.Select(d => new { seriesId = d.SeriesId, delta = Round.R9(d.Delta) }).ToList() },
                    });
                }
                else if (step.TryGetProperty("legendValue", out var lv)) q.Add(Opt(m.LegendValue(m.Config.Series.First(x => x.Id == lv.GetString()))));
                else if (step.TryGetProperty("state", out _)) q.Add(new { paused = m.Paused, reviewEnd = Opt(m.ReviewEnd), timeSpan = Round.R9(m.TimeSpan), cursorA = Opt(m.CursorA), cursorB = Opt(m.CursorB), hoverTime = Opt(m.HoverTime) });
                else if (step.TryGetProperty("axis", out var ax)) { var a = m.Config.Axes.FirstOrDefault(x => x.Id == ax.GetString()); q.Add(new { min = Opt(a?.Min), max = Opt(a?.Max) }); }
                else if (step.TryGetProperty("laneAxes", out var la)) q.Add(m.AxesIn(la.GetString()!).Select(a => a.Id).ToList());
                else if (step.TryGetProperty("tracks", out var tr)) q.Add(m.DigitalTracks(tr.GetString()!).Select(x => x.Id).ToList());
                else if (step.TryGetProperty("seriesRange", out var sr)) { var sc = m.Config.Series.First(x => x.Id == sr.GetString()); var lane = s.Ensure().Lanes.First(l => l.LaneId == m.LaneIdOf(sc)); var sc2 = m.SeriesScale(sc, lane); q.Add(new[] { Round.R9(sc2.R0), Round.R9(sc2.R1) }); }
                else if (step.TryGetProperty("legendRect", out _)) q.Add(Round.Rect(s.Ensure().Legend));
                else if (step.TryGetProperty("dropTarget", out var dt)) { var a = SignalSteps.Doubles(dt); q.Add(TargetJson(m.DropTargetAt(s.Ensure(), a[0], a[1]))); }
                else if (step.TryGetProperty("dropTargetDigital", out var dtd)) { var a = SignalSteps.Doubles(dtd); q.Add(TargetJson(m.DropTargetAt(s.Ensure(), a[0], a[1], true))); }
                else if (step.TryGetProperty("legendRowAt", out var lr)) { var a = SignalSteps.Doubles(lr); q.Add(m.LegendRowAt(s.Ensure(), a[0], a[1])); }
                else if (step.TryGetProperty("lanes", out _)) q.Add(m.Lanes().Select(l => l.Id).ToList());
                else if (step.TryGetProperty("seriesLane", out var sl)) q.Add(m.LaneIdOf(m.Config.Series.First(x => x.Id == sl.GetString())));
                else if (step.TryGetProperty("seriesAxis", out var sa)) q.Add(m.Config.Series.First(x => x.Id == sa.GetString()).AxisId);
                else if (step.TryGetProperty("drag", out _)) q.Add(m.Drag is { } d ? new { seriesId = d.SeriesId, x = Round.R9(d.X), y = Round.R9(d.Y), target = TargetJson(d.Target) } : null);
                else if (step.TryGetProperty("draw", out _)) { var p = new RecordingPainter(s.Width, s.Height); TrendChartRenderer.Draw(m, p, s.Ensure()); q.Add(JsonNode.Parse(p.Ops.ToJsonString())); }
                else throw new InvalidOperationException($"unknown query {step}");
                break;
            case var t: throw new InvalidOperationException($"unknown step {t}");
        }
        return s;
    }
}
