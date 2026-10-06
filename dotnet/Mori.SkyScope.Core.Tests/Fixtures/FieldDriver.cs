// Mori.SkyScope — Fixture driver for the form field state and validation.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Forms;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Fixture driver for the form field reducer: change, blur, touch and reset actions against a set of validation rules.</summary>
public sealed class FieldDriver : IFixtureDriver
{
    /// <summary>Handles the <c>field</c> fixtures.</summary>
    public string Component => "field";

    private sealed record State(FieldState Field, IReadOnlyList<ValidationRule> Rules);

    /// <summary>Creates the field from the initial <c>value</c> and parses the <c>rules</c> list.</summary>
    public object Create(JsonElement setup)
    {
        var value = setup.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
        var rules = setup.TryGetProperty("rules", out var r) ? r.EnumerateArray().Select(ParseRule).ToList() : [];
        return new State(Field.Create(value, rules), rules);
    }

    /// <summary>Reduces one action (<c>change</c>, <c>blur</c>, <c>touch</c>, <c>reset</c>) into the next field state.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        FieldAction action = step.GetProperty("type").GetString() switch
        {
            "change" => new FieldAction.Change(step.GetProperty("value").GetString()!),
            "blur" => new FieldAction.Blur(),
            "touch" => new FieldAction.Touch(),
            "reset" => new FieldAction.Reset(step.TryGetProperty("value", out var v) ? v.GetString() : null),
            var t => throw new InvalidOperationException($"Unknown field step '{t}'"),
        };
        return s with { Field = Field.Reduce(s.Field, action, s.Rules) };
    }

    /// <summary>Value, initial value, touched/dirty/valid flags, error visibility and error messages of the field.</summary>
    public JsonNode Snapshot(object state)
    {
        var f = ((State)state).Field;
        return JsonSerializer.SerializeToNode(new
        {
            value = f.Value, initialValue = f.InitialValue, touched = f.Touched,
            dirty = f.Dirty, valid = f.Valid, showErrors = f.ShowErrors, errors = f.Errors,
        }, Fixtures.Json)!;
    }

    private static ValidationRule ParseRule(JsonElement e)
    {
        var message = e.TryGetProperty("message", out var m) ? m.GetString() : null;
        return e.GetProperty("type").GetString() switch
        {
            "required" => Rules.Required(message),
            "minLength" => Rules.MinLength(e.GetProperty("length").GetInt32(), message),
            "maxLength" => Rules.MaxLength(e.GetProperty("length").GetInt32(), message),
            "pattern" => Rules.Pattern(e.GetProperty("pattern").GetString()!, message),
            "email" => Rules.Email(message),
            var t => throw new InvalidOperationException($"Unknown rule type '{t}'"),
        };
    }
}
