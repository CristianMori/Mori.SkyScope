// Mori.SkyScope — JSON form of a drop target, as the TypeScript engine reads and writes it ({ kind: "join" | "ownAxis" | "stack" | "newLane" | "none", … }).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using Mori.SkyScope.Core.Charts;

namespace Mori.SkyScope.Blazor;

/// <summary>Converts <see cref="DropTarget"/> to and from the JSON shape of the TypeScript engine.</summary>
internal static class DropTargetJson
{
    /// <summary>An anonymous object that serialises to the engine's shape.</summary>
    public static object ToJson(DropTarget t) => t switch
    {
        DropTarget.Join j => new { kind = "join", laneId = j.LaneId, axisId = j.AxisId },
        DropTarget.OwnAxis o => new { kind = "ownAxis", laneId = o.LaneId },
        DropTarget.Stack s => new { kind = "stack", laneId = s.LaneId },
        DropTarget.NewLane n => new { kind = "newLane", index = n.Index, afterLaneId = n.AfterLaneId },
        _ => new { kind = "none" },
    };

    /// <summary>Reads a target; unknown kinds become <see cref="DropTarget.None"/>.</summary>
    public static DropTarget Parse(JsonElement e) => e.ValueKind != JsonValueKind.Object ? new DropTarget.None() : e.GetProperty("kind").GetString() switch
    {
        "join" => new DropTarget.Join(e.GetProperty("laneId").GetString()!, e.GetProperty("axisId").GetString()!),
        "ownAxis" => new DropTarget.OwnAxis(e.GetProperty("laneId").GetString()!),
        "stack" => new DropTarget.Stack(e.GetProperty("laneId").GetString()!),
        "newLane" => new DropTarget.NewLane(e.GetProperty("index").GetInt32(), e.TryGetProperty("afterLaneId", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null),
        _ => new DropTarget.None(),
    };
}
