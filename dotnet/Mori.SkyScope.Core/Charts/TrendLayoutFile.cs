// Mori.SkyScope — Saved chart arrangements: the configuration without theme and style as a versioned JSON file, identical to the TypeScript format.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Mori.SkyScope.Core.Charts;

/// <summary>Enum names as kebab-case strings ("TopLeft" → "top-left"), the spelling the TypeScript core uses.</summary>
public sealed class KebabCaseNamingPolicy : JsonNamingPolicy
{
    /// <summary>The shared instance.</summary>
    public static readonly KebabCaseNamingPolicy Instance = new();
    /// <inheritdoc/>
    public override string ConvertName(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var ch = name[i];
            if (char.IsUpper(ch) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Reads and writes layout files: everything a gesture can change (time span, lanes, axes, series, thresholds, markers,
/// legend and panels) without theme and style. Version 1; the same JSON the TypeScript core writes.
/// </summary>
public static class TrendLayoutFile
{
    /// <summary>Format version written into every file.</summary>
    public const int Version = 1;

    /// <summary>Serializer settings: camelCase names, kebab-case enums, nulls omitted, read-only lists populated on read.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter(KebabCaseNamingPolicy.Instance) },
        WriteIndented = true,
    };

    private static readonly string[] Skipped = ["Theme", "Style"];

    /// <summary>The arrangement of <paramref name="config"/> as a layout file.</summary>
    public static string ToJson(TrendChartConfig config)
    {
        var node = JsonSerializer.SerializeToNode(config, Options)!.AsObject();
        foreach (var key in Skipped) node.Remove(JsonNamingPolicy.CamelCase.ConvertName(key));
        var file = new JsonObject { ["version"] = Version };
        foreach (var kv in node.ToList()) { node.Remove(kv.Key); file[kv.Key] = kv.Value; }
        return file.ToJsonString(Options);
    }

    /// <summary>Parses a layout file into a fresh configuration (default theme and style); throws on an unknown version.</summary>
    public static TrendChartConfig Parse(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("not a layout file");
        var version = node["version"]?.GetValue<int>() ?? 0;
        if (version != Version) throw new FormatException($"unsupported layout version {version}");
        node.Remove("version");
        foreach (var key in Skipped) node.Remove(JsonNamingPolicy.CamelCase.ConvertName(key));
        return node.Deserialize<TrendChartConfig>(Options) ?? new TrendChartConfig();
    }

    /// <summary>Copies the arrangement of <paramref name="from"/> into <paramref name="into"/>, keeping the target's theme and style.</summary>
    public static void Apply(TrendChartConfig into, TrendChartConfig from)
    {
        foreach (var p in typeof(TrendChartConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (Array.IndexOf(Skipped, p.Name) >= 0) continue;
            if (p.CanWrite) { p.SetValue(into, p.GetValue(from)); continue; }
            if (p.GetValue(into) is System.Collections.IList target && p.GetValue(from) is System.Collections.IList source)
            {
                target.Clear();
                foreach (var item in source) target.Add(item);
            }
        }
    }
}
