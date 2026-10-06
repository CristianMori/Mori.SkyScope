// Mori.SkyScope — Joins class names, skipping nulls/empties: Css.Classes("a", cond .
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core;

namespace Mori.SkyScope.Blazor;

/// <summary>CSS class-name helpers shared by the components.</summary>
internal static class Css
{
    /// <summary>Joins class names, skipping nulls/empties: <c>Css.Classes("a", cond ? "b" : null)</c>.</summary>
    public static string Classes(params string?[] parts) => string.Join(' ', parts.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>BEM size modifier; md is the default and emits nothing.</summary>
    public static string? Size(string block, Size size) => size == Core.Size.Md ? null : $"{block}--{size.ToString().ToLowerInvariant()}";
}
