// Mori.SkyScope — Source-plugin contracts.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Sources;

/// <summary>
/// Source-plugin contracts. Everything that produces data is an <see cref="ISource"/>; the core never
/// knows a protocol. Mirrors <c>sources/contracts.ts</c>. A source pushes into two sinks — signals
/// (samples → ring buffers) and layers (messages → scene layers) — and reads chart time from the clock.
/// </summary>
public enum ChannelKind { Analog, Digital, State }
/// <summary>How a channel's samples are timed: <c>Regular</c> samples sit at <c>t0 + i · dt</c> and carry no time of their own, <c>Timestamped</c> samples each carry a time in seconds.</summary>
public enum ChannelTiming { Regular, Timestamped }

/// <summary>Declaration of one signal channel. Re-declaring an id merges the non-null fields over the earlier declaration.</summary>
/// <param name="Id">Channel id, unique within a source; frames refer to it.</param>
/// <param name="Name">Display name, shown in legends and the signal tree.</param>
public sealed record ChannelInfo(int Id, string Name)
{
    /// <summary>Unit label for axes and readouts, for example <c>"m/s"</c>.</summary>
    public string? Unit { get; init; }
    /// <summary>Analog, digital or state; charts pick a renderer from it. Analog when null.</summary>
    public ChannelKind? Kind { get; init; }
    /// <summary>Regular channels store no per-sample time (t0 + i·dt); timestamped ones do. Default regular.</summary>
    public ChannelTiming? Timing { get; init; }
    /// <summary>Samples per second, when known. Sizes the ring buffer from the retention window.</summary>
    public double? Rate { get; init; }
    /// <summary>Preferred series colour (hex); charts assign one from the palette when null.</summary>
    public string? Color { get; init; }
}

/// <summary>Where a source delivers samples. Implementations may be called from a source's background thread.</summary>
public interface ISignalSink
{
    /// <summary>Declare or update a channel before (or while) pushing its samples.</summary>
    void DeclareChannel(ChannelInfo info);
    /// <summary>Deliver a batch of samples for one or more channels; undeclared channels are auto-declared by the store.</summary>
    void PushFrame(SkyScopeFrame frame);
    /// <summary>Drop all samples (channels stay declared) — playback sources call this when seeking backwards.</summary>
    void Reset() { }
}

/// <summary>Where a source delivers scene layers: a declaration creates a layer of a JSON-described kind, pushes update it. Implementations may be called from a source's background thread.</summary>
public interface ILayerSink
{
    /// <summary>Create (or replace) the layer <paramref name="id"/> of <paramref name="kind"/>; <paramref name="meta"/> holds its JSON-shaped settings and initial content (values are JSON-serializable objects or <c>JsonElement</c>s).</summary>
    void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null);
    /// <summary>Update a declared layer; <paramref name="payload"/> is a JSON-shaped object, a <c>JsonElement</c>, or a dictionary carrying binary arrays.</summary>
    void Push(string id, object? payload);
    /// <summary>Forget every layer received so far — playback calls this when seeking backwards before replaying.</summary>
    void Reset() { }
}

/// <summary>Chart time in seconds. Live sources use the wall clock; playback sources drive their own.</summary>
/// <remarks><c>Now()</c> returns the current chart time in seconds.</remarks>
public interface ITimeSource { double Now(); }

/// <summary>Chart time from the wall clock, as Unix seconds (UTC).</summary>
public sealed class LiveClock : ITimeSource
{
    /// <summary>Unix time in seconds with millisecond resolution.</summary>
    public double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
}

/// <summary>Chart time driven explicitly, for playback and tests; starts at <paramref name="t"/> seconds. Not thread-safe.</summary>
public sealed class ManualClock(double t = 0) : ITimeSource
{
    private double _t = t;
    /// <summary>The time last set or advanced to, seconds.</summary>
    public double Now() => _t;
    /// <summary>Jump to an absolute time in seconds.</summary>
    public void Set(double t) => _t = t;
    /// <summary>Move forward by <paramref name="dt"/> seconds (negative moves back).</summary>
    public void Advance(double dt) => _t += dt;
}

/// <summary>Severity of a source log message.</summary>
public enum LogLevel { Info, Warn, Error }
/// <summary>Where sources report connection state and dropped data; may be called from any thread.</summary>
public delegate void Log(LogLevel level, string message);

/// <summary>Everything a source needs from its host.</summary>
/// <param name="Signals">Sink for samples.</param>
/// <param name="Layers">Sink for scene layers.</param>
/// <param name="Clock">Chart time, used by live sources to stamp generated data.</param>
/// <param name="Log">Diagnostics callback.</param>
public sealed record SourceContext(ISignalSink Signals, ILayerSink Layers, ITimeSource Clock, Log Log);

/// <summary>What a source type can produce: <c>Signals</c>, <c>Layers</c>, and whether it supports <c>Playback</c> control (seek, speed).</summary>
[Flags]
public enum SourceCapabilities { None = 0, Signals = 1, Layers = 2, Playback = 4 }

/// <summary>A running data producer. <see cref="StartAsync"/> connects and begins pushing into the context's sinks; <see cref="StopAsync"/> stops and releases resources. One instance runs at most once.</summary>
public interface ISource
{
    /// <summary>Source type key, the same as its factory's.</summary>
    string Type { get; }
    /// <summary>Begin producing. <paramref name="config"/> is the source-specific configuration object; an unexpected type throws <see cref="ArgumentException"/>. Returns once started, not when finished.</summary>
    Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default);
    /// <summary>Stop producing and release resources; safe to call when not started.</summary>
    Task StopAsync();
}

/// <summary>Describes a source type and creates instances of it; registered in a <see cref="SourceRegistry"/>.</summary>
public interface ISourceFactory
{
    /// <summary>Unique source type key, for example <c>"websocket"</c>.</summary>
    string Type { get; }
    /// <summary>Human-readable name for pickers.</summary>
    string DisplayName { get; }
    /// <summary>What sources of this type produce.</summary>
    SourceCapabilities Capabilities { get; }
    /// <summary>JSON Schema for the config object, for editors/UI.</summary>
    string? ConfigSchema { get; }
    /// <summary>A new, not yet started source.</summary>
    ISource Create();
}

/// <summary>Marks a factory for assembly scanning: <c>SourceRegistry.Scan(typeof(MySource).Assembly)</c>.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SkyScopeSourceAttribute : Attribute;

/// <summary>Source factories by type key. Not thread-safe; populate at start-up.</summary>
public sealed class SourceRegistry
{
    private readonly Dictionary<string, ISourceFactory> _factories = [];

    /// <summary>Add a factory; throws <see cref="InvalidOperationException"/> when its type is already registered.</summary>
    /// <returns>This registry, for chaining.</returns>
    public SourceRegistry Register(ISourceFactory factory)
    {
        if (!_factories.TryAdd(factory.Type, factory)) throw new InvalidOperationException($"source type \"{factory.Type}\" already registered");
        return this;
    }

    /// <summary>Register every concrete <see cref="ISourceFactory"/> in the assembly marked with <see cref="SkyScopeSourceAttribute"/>, using its parameterless constructor.</summary>
    /// <returns>This registry, for chaining.</returns>
    public SourceRegistry Scan(System.Reflection.Assembly assembly)
    {
        foreach (var t in assembly.GetTypes())
            if (t.GetCustomAttributes(typeof(SkyScopeSourceAttribute), false).Length > 0 && typeof(ISourceFactory).IsAssignableFrom(t) && !t.IsAbstract)
                Register((ISourceFactory)Activator.CreateInstance(t)!);
        return this;
    }

    /// <summary>Snapshot of the registered factories in registration order.</summary>
    public IReadOnlyList<ISourceFactory> List() => [.. _factories.Values];
    /// <summary>The factory for a type key, or null.</summary>
    public ISourceFactory? Get(string type) => _factories.GetValueOrDefault(type);
    /// <summary>Create a source of the given type; throws <see cref="KeyNotFoundException"/> for an unknown type.</summary>
    public ISource Create(string type) => (_factories.GetValueOrDefault(type) ?? throw new KeyNotFoundException($"unknown source type \"{type}\"")).Create();
}

/// <summary>A layer sink that discards everything — for tests and sources with no scene attached.</summary>
public sealed class NullLayerSink : ILayerSink
{
    /// <summary>No-op.</summary>
    public void DeclareLayer(string id, string kind, IReadOnlyDictionary<string, object?>? meta = null) { }
    /// <summary>No-op.</summary>
    public void Push(string id, object? payload) { }
}
