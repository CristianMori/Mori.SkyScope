// Mori.SkyScope — Fixture driver for the signal tree: channels in, rows / selection / drag payloads out.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Charts;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the signal tree: channels in, rows / selection / drag payloads out. TS mirror: <c>signalTreeDriver</c>.</summary>
public sealed class SignalTreeDriver : IFixtureDriver
{
    private sealed record State(SignalTreeModel Model, List<object?> Queries);
    /// <summary>Handles the <c>signal-tree</c> fixtures.</summary>
    public string Component => "signal-tree";

    /// <summary>Declares the setup channels in a store and builds the tree model with the configured separators.</summary>
    public object Create(JsonElement setup)
    {
        var store = new SignalStore(10, 4096);
        if (setup.TryGetProperty("channels", out var chs)) foreach (var ch in chs.EnumerateArray()) store.DeclareChannel(ChannelCatalog.Parse(ch));
        var separators = setup.TryGetProperty("separators", out var sep) && sep.ValueKind == JsonValueKind.String ? sep.GetString()! : "/.:";
        return new State(new SignalTreeModel(store, separators), []);
    }

    private static bool Flag(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    private static string? KindName(ChannelKind? k) => k switch { ChannelKind.Digital => "digital", ChannelKind.State => "state", ChannelKind.Analog => "analog", _ => null };

    /// <summary>Applies query text, group toggles, clicks with modifiers, selection clearing and new channel declarations, and answers rows, selection, dragIds and split queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state; var m = s.Model;
        switch (step.GetProperty("type").GetString())
        {
            case "setQuery": m.SetQuery(step.GetProperty("query").GetString()!); break;
            case "toggleGroup": m.ToggleGroup(step.GetProperty("prefix").GetString()!); break;
            case "click": m.Click(step.GetProperty("rowId").GetString()!, Flag(step, "ctrl"), Flag(step, "shift")); break;
            case "clearSelection": m.ClearSelection(); break;
            case "declare": m.Store.DeclareChannel(ChannelCatalog.Parse(step.GetProperty("channel"))); break;
            case "query":
                if (step.TryGetProperty("rows", out _)) s.Queries.Add(m.Rows().Select(r => new { kind = r.Kind == SignalTreeRowKind.Group ? "group" : "channel", id = r.Id, name = r.Name, depth = r.Depth, expanded = r.Expanded, count = r.Count, channelId = r.ChannelId, unit = r.Unit, channelKind = KindName(r.ChannelKind), selected = r.Selected }).ToList());
                else if (step.TryGetProperty("selection", out _)) s.Queries.Add(m.Selection.OrderBy(x => x).ToList());
                else if (step.TryGetProperty("dragIds", out var d)) s.Queries.Add(m.DragIds(d.GetInt32()));
                else if (step.TryGetProperty("split", out var sp)) { var (g, l) = m.Split(sp.GetString()!); s.Queries.Add(new { group = g, leaf = l }); }
                else throw new InvalidOperationException($"unknown query {step}");
                break;
            case var t: throw new InvalidOperationException($"unknown step {t}");
        }
        return s;
    }

    /// <summary>The recorded query answers.</summary>
    public JsonNode Snapshot(object state) => JsonSerializer.SerializeToNode(new { queries = ((State)state).Queries }, Fixtures.Json)!;
}
