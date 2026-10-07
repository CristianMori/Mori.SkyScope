// Mori.SkyScope — A declarative validation rule.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.RegularExpressions;

namespace Mori.SkyScope.Core.Forms;

/// <summary>
/// A declarative validation rule. Semantics are shared with <c>@cmori/skyscope-core</c> and pinned by
/// <c>spec/fixtures/field.json</c>: change one side, and the other side's tests go red.
/// </summary>
public abstract record ValidationRule
{
    /// <summary>Overrides <see cref="DefaultMessage"/> when set.</summary>
    public string? Message { get; init; }

    /// <summary>Error text used when <see cref="Message"/> is not set.</summary>
    public abstract string DefaultMessage { get; }

    /// <summary>The message reported when the rule fails.</summary>
    public string ErrorMessage => Message ?? DefaultMessage;

    /// <summary>Returns <see langword="true"/> when <paramref name="value"/> passes the rule.</summary>
    public abstract bool IsValid(string value);

    /// <summary>
    /// Whether the rule runs against an empty string. Only <see cref="RequiredRule"/> does;
    /// every other rule treats "empty" as "nothing to validate".
    /// </summary>
    public virtual bool AppliesToEmpty => false;
}

/// <summary>Fails on empty or whitespace-only values; the only rule that runs on empty input.</summary>
public sealed record RequiredRule : ValidationRule
{
    /// <inheritdoc/>
    public override string DefaultMessage => "This field is required";
    /// <inheritdoc/>
    public override bool AppliesToEmpty => true;
    /// <inheritdoc/>
    public override bool IsValid(string value) => !string.IsNullOrWhiteSpace(value);
}

/// <summary>Fails when the value is shorter than <paramref name="Length"/> characters.</summary>
public sealed record MinLengthRule(int Length) : ValidationRule
{
    /// <inheritdoc/>
    public override string DefaultMessage => $"Must be at least {Length} characters";
    /// <inheritdoc/>
    public override bool IsValid(string value) => value.Length >= Length;
}

/// <summary>Fails when the value is longer than <paramref name="Length"/> characters.</summary>
public sealed record MaxLengthRule(int Length) : ValidationRule
{
    /// <inheritdoc/>
    public override string DefaultMessage => $"Must be at most {Length} characters";
    /// <inheritdoc/>
    public override bool IsValid(string value) => value.Length <= Length;
}

/// <summary>Fails unless the value matches the .NET regular expression <paramref name="Pattern"/> (1 s match timeout).</summary>
public sealed record PatternRule(string Pattern) : ValidationRule
{
    /// <inheritdoc/>
    public override string DefaultMessage => "Invalid format";
    /// <inheritdoc/>
    public override bool IsValid(string value) => Regex.IsMatch(value, Pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
}

/// <summary>Requires one '@' with non-blank text on both sides and a dot in the domain part; intentionally lenient.</summary>
public sealed record EmailRule : ValidationRule
{
    // Deliberately simple and identical to the TS side; RFC-grade email validation belongs on the server.
    private static readonly Regex Email = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.None, TimeSpan.FromSeconds(1));
    /// <inheritdoc/>
    public override string DefaultMessage => "Invalid email address";
    /// <inheritdoc/>
    public override bool IsValid(string value) => Email.IsMatch(value);
}

/// <summary>A rule backed by arbitrary code. Not expressible in fixtures; renderer/app use only.</summary>
public sealed record CustomRule(Func<string, bool> Predicate, string ErrorText) : ValidationRule
{
    /// <inheritdoc/>
    public override string DefaultMessage => ErrorText;
    /// <inheritdoc/>
    public override bool IsValid(string value) => Predicate(value);
}

/// <summary>Terse factories: <c>Rules.Required()</c>, <c>Rules.MinLength(3)</c>, …</summary>
public static class Rules
{
    /// <summary>Required rule with an optional custom message.</summary>
    public static RequiredRule Required(string? message = null) => new() { Message = message };
    /// <summary>Minimum length rule with an optional custom message.</summary>
    public static MinLengthRule MinLength(int length, string? message = null) => new(length) { Message = message };
    /// <summary>Maximum length rule with an optional custom message.</summary>
    public static MaxLengthRule MaxLength(int length, string? message = null) => new(length) { Message = message };
    /// <summary>Regular expression rule with an optional custom message.</summary>
    public static PatternRule Pattern(string pattern, string? message = null) => new(pattern) { Message = message };
    /// <summary>Email rule with an optional custom message.</summary>
    public static EmailRule Email(string? message = null) => new() { Message = message };
    /// <summary>Rule backed by <paramref name="predicate"/>, reporting <paramref name="message"/> on failure.</summary>
    public static CustomRule Custom(Func<string, bool> predicate, string message) => new(predicate, message);
}
