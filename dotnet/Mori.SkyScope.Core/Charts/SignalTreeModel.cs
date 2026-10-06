// Mori.SkyScope — The signal tree beside a chart: channels grouped by name prefix, search, selection and drag payloads.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Row type: a collapsible group header or a channel leaf.</summary>
public enum SignalTreeRowKind { Group, Channel }

/// <summary>One visible row of the signal tree. Ids are <c>g:&lt;prefix&gt;</c> for groups and <c>c:&lt;channelId&gt;</c> for channels.</summary>
public sealed record SignalTreeRow(SignalTreeRowKind Kind, string Id, string Name, int Depth, bool Selected)
{
    /// <summary>Groups only: whether the children are listed.</summary>
    public bool? Expanded { get; init; }
    /// <summary>Groups only: number of channels in the group after the search filter.</summary>
    public int? Count { get; init; }
    /// <summary>Channels only: the store channel id.</summary>
    public int? ChannelId { get; init; }
    /// <summary>Channels only: the channel unit.</summary>
    public string? Unit { get; init; }
    /// <summary>Channels only: analog, digital or state.</summary>
    public ChannelKind? ChannelKind { get; init; }
}

/// <summary>
/// The signal tree beside a chart (ibaAnalyzer's signal tree): every channel of a store, grouped by the prefix of its
/// name (<c>amr-1/pose/x</c> → group <c>amr-1/pose</c>, leaf <c>x</c>), searchable, with a selection that is dragged onto
/// the chart. Ctrl toggles, Shift selects a range over the visible rows; plain click selects one.
/// Pure state, identical to <c>charts/signal-tree.ts</c> and pinned by <c>spec/fixtures/signal-tree.json</c>.
/// </summary>
/// <param name="store">Store whose channels the tree lists.</param>
/// <param name="separators">Characters that split a channel name into group prefix and leaf; the last one in the name wins.</param>
public sealed class SignalTreeModel(SignalStore store, string separators = "/.:")
{
    private readonly HashSet<string> _folded = [];
    private int? _anchor;

    /// <summary>Store whose channels the tree lists.</summary>
    public SignalStore Store { get; } = store;
    /// <summary>Characters that split a channel name into group prefix and leaf.</summary>
    public string Separators { get; } = separators;
    /// <summary>Search text; matched case-insensitively against name and unit. Every group is open while searching.</summary>
    public string Query { get; set; } = "";
    /// <summary>Selected channel ids. Prefer <see cref="Click"/> over direct edits so the shift-range anchor stays consistent.</summary>
    public HashSet<int> Selection { get; } = [];

    /// <summary>Group prefix and leaf name of a channel name (split at the last separator).</summary>
    public (string Group, string Leaf) Split(string name)
    {
        var cut = -1;
        for (var i = name.Length - 1; i >= 0; i--) if (Separators.Contains(name[i])) { cut = i; break; }
        return cut < 0 ? ("", name) : (name[..cut], name[(cut + 1)..]);
    }

    private bool Matches(ChannelInfo c)
    {
        var q = Query.Trim().ToLowerInvariant();
        return q == "" || c.Name.ToLowerInvariant().Contains(q) || (c.Unit?.ToLowerInvariant().Contains(q) ?? false);
    }

    private static int ByName(ChannelInfo a, ChannelInfo b) { var c = string.CompareOrdinal(a.Name, b.Name); return c != 0 ? c : a.Id - b.Id; }

    /// <summary>Visible rows top to bottom: ungrouped channels first, then groups (sorted) with their channels (sorted) when open.</summary>
    public List<SignalTreeRow> Rows()
    {
        var channels = Store.Channels.Values.Select(c => c.Info).Where(Matches).ToList();
        var groups = new Dictionary<string, List<ChannelInfo>>();
        foreach (var c in channels) { var g = Split(c.Name).Group; if (!groups.TryGetValue(g, out var list)) groups[g] = list = []; list.Add(c); }
        var searching = Query.Trim() != "";
        var rows = new List<SignalTreeRow>();
        void Push(ChannelInfo c, int depth) => rows.Add(new SignalTreeRow(SignalTreeRowKind.Channel, $"c:{c.Id}", depth == 0 ? c.Name : Split(c.Name).Leaf, depth, Selection.Contains(c.Id)) { ChannelId = c.Id, Unit = c.Unit, ChannelKind = c.Kind });
        if (groups.TryGetValue("", out var ungrouped)) { ungrouped.Sort(ByName); foreach (var c in ungrouped) Push(c, 0); }
        foreach (var g in groups.Keys.Where(k => k != "").OrderBy(k => k, StringComparer.Ordinal))
        {
            var list = groups[g]; list.Sort(ByName);
            var expanded = searching || !_folded.Contains(g);
            rows.Add(new SignalTreeRow(SignalTreeRowKind.Group, $"g:{g}", g, 0, list.All(c => Selection.Contains(c.Id))) { Expanded = expanded, Count = list.Count });
            if (expanded) foreach (var c in list) Push(c, 1);
        }
        return rows;
    }

    /// <summary>Sets the search text.</summary>
    public void SetQuery(string q) => Query = q;
    /// <summary>Folds or unfolds a group by prefix (folds are ignored while searching).</summary>
    public void ToggleGroup(string prefix) { if (!_folded.Remove(prefix)) _folded.Add(prefix); }
    /// <summary>Whether a group is folded.</summary>
    public bool IsFolded(string prefix) => _folded.Contains(prefix);

    /// <summary>
    /// Click on a row. Channels: plain = only this one, Ctrl = toggle, Shift = range from the last plain/ctrl click over the
    /// visible channel rows. Groups: plain toggles the fold, Ctrl adds/removes all its channels, Shift selects them.
    /// </summary>
    public void Click(string rowId, bool ctrl = false, bool shift = false)
    {
        var rows = Rows();
        var row = rows.FirstOrDefault(r => r.Id == rowId);
        if (row is null) return;
        if (row.Kind == SignalTreeRowKind.Group)
        {
            var prefix = row.Id[2..];
            var all = Store.Channels.Values.Select(c => c.Info).Where(c => Matches(c) && Split(c.Name).Group == prefix).Select(c => c.Id).ToList();
            if (ctrl) { var every = all.All(Selection.Contains); foreach (var id in all) { if (every) Selection.Remove(id); else Selection.Add(id); } }
            else if (shift) { foreach (var id in all) Selection.Add(id); }
            else ToggleGroup(prefix);
            return;
        }
        var cid = row.ChannelId!.Value;
        if (shift && _anchor is { } anchor)
        {
            var visible = rows.Where(r => r.Kind == SignalTreeRowKind.Channel).Select(r => r.ChannelId!.Value).ToList();
            int a = visible.IndexOf(anchor), b = visible.IndexOf(cid);
            if (a >= 0 && b >= 0) { if (!ctrl) Selection.Clear(); for (var i = Math.Min(a, b); i <= Math.Max(a, b); i++) Selection.Add(visible[i]); return; }
        }
        if (ctrl) { if (!Selection.Remove(cid)) Selection.Add(cid); }
        else { Selection.Clear(); Selection.Add(cid); }
        _anchor = cid;
    }

    /// <summary>What a drag starting on <paramref name="channelId"/> carries: the selection in row order when it is part of it, else that channel alone.</summary>
    public List<int> DragIds(int channelId)
    {
        if (!Selection.Contains(channelId)) return [channelId];
        return Rows().Where(r => r.Kind == SignalTreeRowKind.Channel).Select(r => r.ChannelId!.Value).Where(Selection.Contains).ToList();
    }
    /// <summary>Empties the selection and forgets the shift-range anchor.</summary>
    public void ClearSelection() { Selection.Clear(); _anchor = null; }
}
