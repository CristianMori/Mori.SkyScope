// Mori.SkyScope — The drag-and-drop payload for channels: what a signal tree (or any other control) puts on the data object, and how a chart reads it back. Mirrors channel-drag.ts.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Core.Charts;

/// <summary>One dragged channel: the id is what the chart needs; name, unit and kind let a drop work before the chart's store knows the channel.</summary>
public sealed record ChannelDragItem(int Id, string? Name = null, string? Unit = null, ChannelKind? Kind = null);

/// <summary>What a channel drag carries: the channels in drag order, and whether they stay together on a drop (one shared axis, one lane) as a Ctrl/Shift group does.</summary>
public sealed record ChannelDragPayload(IReadOnlyList<ChannelDragItem> Channels, bool Group = false)
{
    /// <summary>A payload of ids only.</summary>
    public static ChannelDragPayload Of(IEnumerable<int> channelIds, bool group = false) => new(channelIds.Select(id => new ChannelDragItem(id)).ToList(), group);
    /// <summary>The channel ids in drag order.</summary>
    public IReadOnlyList<int> Ids => Channels.Select(c => c.Id).ToList();
}

/// <summary>
/// Encoding of the channel drag payload. The desktop hosts put the JSON text on a data object under <see cref="Format"/>
/// (and as plain text); the browser uses the MIME type <see cref="Mime"/>. <see cref="TryParse"/> also reads a JSON array
/// of ids and plain text with ids separated by commas or whitespace, so a drag from a grid or another application works.
/// </summary>
public static partial class ChannelDragData
{
    /// <summary>Data-object format name on WPF and Windows Forms.</summary>
    public const string Format = "Mori.SkyScope.Channels";
    /// <summary>MIME type in the browser.</summary>
    public const string Mime = "application/x-skyscope-channels";

    [GeneratedRegex(@"^-?\d+$")] private static partial Regex Integer();
    [GeneratedRegex(@"[\s,;]+")] private static partial Regex Separators();

    /// <summary>Serialises a payload to the JSON text put on the data object.</summary>
    public static string Encode(ChannelDragPayload payload)
    {
        var channels = new JsonArray();
        foreach (var c in payload.Channels)
        {
            var o = new JsonObject { ["id"] = c.Id };
            if (c.Name is not null) o["name"] = c.Name;
            if (c.Unit is not null) o["unit"] = c.Unit;
            if (c.Kind is { } k) o["kind"] = k == ChannelKind.Digital ? "digital" : "analog";
            channels.Add(o);
        }
        return new JsonObject { ["channels"] = channels, ["group"] = payload.Group }.ToJsonString();
    }

    /// <summary>Reads a payload back from text; false when the text carries no channel.</summary>
    public static bool TryParse(string? text, out ChannelDragPayload payload)
    {
        payload = new ChannelDragPayload([]);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith('{') || t.StartsWith('['))
        {
            JsonNode? node;
            try { node = JsonNode.Parse(t); } catch (JsonException) { return false; }
            if (node is JsonArray arr)
            {
                var ids = arr.Where(IsInteger).Select(x => x!.GetValue<int>()).ToList();
                if (ids.Count == 0) return false;
                payload = ChannelDragPayload.Of(ids); return true;
            }
            if (node is JsonObject obj && obj["channels"] is JsonArray chans)
            {
                var items = new List<ChannelDragItem>();
                foreach (var raw in chans)
                {
                    if (IsInteger(raw)) { items.Add(new ChannelDragItem(raw!.GetValue<int>())); continue; }
                    if (raw is not JsonObject r || !IsInteger(r["id"])) continue;
                    var kind = r["kind"] is JsonValue kv && kv.TryGetValue<string>(out var ks) ? ks switch { "digital" => ChannelKind.Digital, "analog" => ChannelKind.Analog, _ => (ChannelKind?)null } : null;
                    items.Add(new ChannelDragItem(r["id"]!.GetValue<int>(), AsString(r["name"]), AsString(r["unit"]), kind));
                }
                if (items.Count == 0) return false;
                payload = new ChannelDragPayload(items, obj["group"] is JsonValue gv && gv.TryGetValue<bool>(out var g) && g); return true;
            }
            return false;
        }
        var parts = Separators().Split(t).Where(p => p.Length > 0).ToList();
        if (parts.Count == 0 || !parts.All(p => Integer().IsMatch(p))) return false;
        payload = ChannelDragPayload.Of(parts.Select(p => int.Parse(p, System.Globalization.CultureInfo.InvariantCulture))); return true;
    }

    /// <summary>True when every channel of the payload is digital, by its own kind or, failing that, by the store's channel info.</summary>
    public static bool IsDigital(ChannelDragPayload payload, SignalStore? store = null)
        => payload.Channels.Count > 0 && payload.Channels.All(c => (c.Kind ?? store?.Get(c.Id)?.Info.Kind) == ChannelKind.Digital);

    private static bool IsInteger(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out _) && !(v.TryGetValue<double>(out var d) && d != Math.Floor(d));
    private static string? AsString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>
/// Arguments of a host's <c>ChannelDrop</c> event, raised when channels are dropped on a chart (native drag and drop with
/// <see cref="ChannelDragData.Format"/>, or plain text ids) before anything is applied. Set <see cref="Cancel"/> to refuse the
/// drop, replace <see cref="Target"/> to redirect it, or set <see cref="Handled"/> after adding the signals yourself; otherwise
/// the chart calls <c>AddChannels(ChannelIds, Target, Group)</c>.
/// </summary>
public sealed class ChannelDropEventArgs(ChannelDragPayload payload, DropTarget target, double x, double y) : EventArgs
{
    /// <summary>The dragged payload as read from the data object.</summary>
    public ChannelDragPayload Payload { get; } = payload;
    /// <summary>The channel ids, in drag order.</summary>
    public IReadOnlyList<int> ChannelIds => Payload.Ids;
    /// <summary>Where the drop lands by the chart's rules; replace it to redirect the drop.</summary>
    public DropTarget Target { get; set; } = target;
    /// <summary>Drop point in chart pixels (device-independent).</summary>
    public double X { get; } = x;
    /// <summary>Drop point in chart pixels (device-independent).</summary>
    public double Y { get; } = y;
    /// <summary>Keep the channels together (one axis, one lane) as a Ctrl/Shift group; from the payload, may be changed.</summary>
    public bool Group { get; set; } = payload.Group;
    /// <summary>Set to refuse the drop.</summary>
    public bool Cancel { get; set; }
    /// <summary>Set after adding the signals yourself (the chart then only repaints and raises <c>ConfigChanged</c>).</summary>
    public bool Handled { get; set; }
}
