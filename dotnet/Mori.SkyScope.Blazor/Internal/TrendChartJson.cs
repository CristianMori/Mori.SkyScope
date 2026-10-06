// Mori.SkyScope — Serialises the C# chart configuration into exactly the JSON the TS engine reads (camelCase, string enums).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using Mori.SkyScope.Core.Charts;

namespace Mori.SkyScope.Blazor;

/// <summary>Serialises the C# chart configuration into exactly the JSON the TS engine reads (camelCase, string enums).</summary>
public static class TrendChartJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>The trend chart configuration as the JSON the TS <c>TrendChart</c> accepts.</summary>
    public static string Serialize(TrendChartConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>Any object (anonymous objects included) with the same camelCase / string-enum rules.</summary>
    public static string SerializeLoose(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
}
