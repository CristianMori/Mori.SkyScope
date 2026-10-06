// Mori.SkyScope — Immutable state of a single text-like form field.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Forms;

/// <summary>
/// Immutable state of a single text-like form field. Renderers own the storage and dispatch
/// <see cref="FieldAction"/>s through <see cref="Field.Reduce"/>; nothing here knows about UI.
/// </summary>
/// <param name="Value">Current text.</param>
/// <param name="InitialValue">Text the field started with or was last reset to.</param>
/// <param name="Touched">Whether the user has interacted (blur or submit).</param>
/// <param name="Errors">Messages of the failing rules, in rule order; empty when valid.</param>
public sealed record FieldState(string Value, string InitialValue, bool Touched, IReadOnlyList<string> Errors)
{
    /// <summary>True when the value differs from the initial one.</summary>
    public bool Dirty => Value != InitialValue;
    /// <summary>True when no rule fails.</summary>
    public bool Valid => Errors.Count == 0;
    /// <summary>Errors are computed eagerly but only surfaced once the user has interacted.</summary>
    public bool ShowErrors => Touched && !Valid;
}

/// <summary>What can happen to a field; applied through <see cref="Field.Reduce"/>.</summary>
public abstract record FieldAction
{
    /// <summary>The value changed (typing, paste, programmatic set).</summary>
    public sealed record Change(string Value) : FieldAction;
    /// <summary>Focus left the control; marks the field touched.</summary>
    public sealed record Blur : FieldAction;
    /// <summary>Force the field touched without a blur — e.g. a submit attempt.</summary>
    public sealed record Touch : FieldAction;
    /// <summary>Return to the initial value (or adopt <paramref name="Value"/> as the new initial value) and clear touched.</summary>
    public sealed record Reset(string? Value = null) : FieldAction;
}

/// <summary>Pure functions over <see cref="FieldState"/>: creation, validation and the reducer.</summary>
public static class Field
{
    /// <summary>Untouched initial state for a value, with its errors already computed.</summary>
    public static FieldState Create(string value = "", IReadOnlyList<ValidationRule>? rules = null)
        => new(value, value, Touched: false, Validate(value, rules));

    /// <summary>
    /// A failing <see cref="RequiredRule"/> short-circuits (it is the only error reported).
    /// Otherwise every failing rule is reported, in rule order. Rules other than required skip empty values.
    /// </summary>
    public static IReadOnlyList<string> Validate(string value, IReadOnlyList<ValidationRule>? rules)
    {
        if (rules is null || rules.Count == 0) return [];

        foreach (var rule in rules)
            if (rule.AppliesToEmpty && !rule.IsValid(value))
                return [rule.ErrorMessage];

        if (value.Length == 0) return [];

        List<string>? errors = null;
        foreach (var rule in rules)
            if (!rule.AppliesToEmpty && !rule.IsValid(value))
                (errors ??= []).Add(rule.ErrorMessage);

        return errors ?? [];
    }

    /// <summary>Next state for an action; the input is never mutated. Unknown action types throw.</summary>
    public static FieldState Reduce(FieldState state, FieldAction action, IReadOnlyList<ValidationRule>? rules) => action switch
    {
        FieldAction.Change c => state with { Value = c.Value, Errors = Validate(c.Value, rules) },
        FieldAction.Blur or FieldAction.Touch => state.Touched ? state : state with { Touched = true },
        FieldAction.Reset r => Create(r.Value ?? state.InitialValue, rules),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown field action"),
    };
}
