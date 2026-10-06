// Mori.SkyScope — The signal sink every chart reads from: one ring buffer per channel, sized from rate × retention.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Signals;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Sources;

/// <summary>A declared channel and its ring buffer. The buffer is replaced when timing or capacity change, and grows (keeping its samples) when batches reveal a higher rate than the channel was sized for.</summary>
public sealed class StoreChannel(ChannelInfo info, SignalBuffer buffer)
{
    /// <summary>The merged declaration.</summary>
    public ChannelInfo Info { get; internal set; } = info;
    /// <summary>The samples; hold <see cref="SignalStore.SyncRoot"/> while reading if a source pushes from another thread. The instance changes when the buffer grows: re-read it after each notification.</summary>
    public SignalBuffer Buffer { get; internal set; } = buffer;
    /// <summary>The highest sample rate seen in this channel's batches when no rate was declared; null until the first batch.</summary>
    public double? ObservedRate { get; internal set; }
}

/// <summary>Called after a frame was applied (on the pushing thread) with the ids whose buffers changed, or after <see cref="SignalStore.Reset"/> with every id and an empty frame.</summary>
public delegate void StoreListener(IReadOnlyList<int> changedIds, SkyScopeFrame frame);

/// <summary>
/// The signal sink every chart reads from: one ring buffer per channel, sized from rate × retention.
/// Never throws on bad data — a batch that cannot be applied is counted in <see cref="Dropped"/> and logged.
/// Mirrors <c>sources/signal-store.ts</c>; pinned by <c>spec/fixtures/signal-store.json</c>.
/// </summary>
public sealed class SignalStore(double retentionSeconds = 60, int defaultCapacity = 65536, Log? log = null, int maxCapacity = 1048576) : ISignalSink
{
    private readonly Dictionary<int, StoreChannel> _channels = [];
    private readonly List<StoreListener> _listeners = [];
    private readonly Log _log = log ?? ((_, _) => { });

    /// <summary>
    /// Sources may push from background threads. <see cref="PushFrame"/> and <see cref="DeclareChannel"/> take this
    /// lock; readers (chart render loops) should hold it while iterating buffers, or marshal frames to their own thread
    /// via <c>WebSocketSourceConfig.Dispatch</c>.
    /// </summary>
    public object SyncRoot { get; } = new();

    /// <summary>History kept per channel, used with the declared rate to size buffers.</summary>
    public double RetentionSeconds { get; } = retentionSeconds;
    /// <summary>Starting capacity for channels without a declared rate, before their batches reveal one.</summary>
    public int DefaultCapacity { get; } = defaultCapacity;
    /// <summary>Largest capacity a buffer is given, declared or observed.</summary>
    public int MaxCapacity { get; } = Math.Max(16, maxCapacity);
    /// <summary>Channels by id. A live view: hold <see cref="SyncRoot"/> while enumerating if a source pushes concurrently.</summary>
    public IReadOnlyDictionary<int, StoreChannel> Channels => _channels;
    /// <summary>Batches rejected since the last reset.</summary>
    public int Dropped { get; private set; }
    /// <summary>Why the last batch was rejected, for example <c>timing-mismatch</c> or the buffer's out-of-order marker.</summary>
    public string? LastDropReason { get; private set; }

    /// <summary>Buffer size for a declaration: <c>rate × retention</c> (at least 16, at most <see cref="MaxCapacity"/>) when the rate is known, otherwise <see cref="DefaultCapacity"/>.</summary>
    public int CapacityFor(ChannelInfo info) => info.Rate is > 0 ? CapacityForRate(info.Rate.Value) : DefaultCapacity;
    private int CapacityForRate(double rate) => Math.Min(MaxCapacity, Math.Max(16, (int)Math.Ceiling(rate * RetentionSeconds)));

    /// <summary>Re-declaring keeps the data unless timing or capacity changed.</summary>
    public void DeclareChannel(ChannelInfo info)
    {
        lock (SyncRoot) DeclareChannelCore(info);
    }

    private void DeclareChannelCore(ChannelInfo info)
    {
        _channels.TryGetValue(info.Id, out var existing);
        var merged = Merge(existing?.Info, info);
        var timing = merged.Timing ?? ChannelTiming.Regular;
        var capacity = CapacityFor(merged);
        var kind = timing == ChannelTiming.Timestamped ? TimeKind.Timestamped : TimeKind.Regular;
        if (existing is not null && existing.Buffer.Kind == kind && existing.Buffer.Capacity == capacity) { existing.Info = merged; return; }
        _channels[info.Id] = new StoreChannel(merged, new SignalBuffer(capacity, kind));
    }

    private static ChannelInfo Merge(ChannelInfo? old, ChannelInfo info) => old is null
        ? info with { Timing = info.Timing ?? ChannelTiming.Regular }
        : new ChannelInfo(info.Id, info.Name)
        {
            Unit = info.Unit ?? old.Unit, Kind = info.Kind ?? old.Kind, Timing = info.Timing ?? old.Timing ?? ChannelTiming.Regular,
            Rate = info.Rate ?? old.Rate, Color = info.Color ?? old.Color,
        };

    /// <summary>The channel with this id, or null.</summary>
    public StoreChannel? Get(int id) => _channels.GetValueOrDefault(id);

    /// <summary>Drop all samples; channel declarations survive.</summary>
    public void Reset()
    {
        lock (SyncRoot)
        {
            foreach (var (id, entry) in _channels.ToArray()) _channels[id] = new StoreChannel(entry.Info, new SignalBuffer(entry.Buffer.Capacity, entry.Buffer.Kind));
            Dropped = 0;
        }
        var ids = _channels.Keys.ToList();
        if (ids.Count > 0) foreach (var l in _listeners.ToArray()) l(ids, new SkyScopeFrame(0, 0, []));
    }

    /// <summary>Append every channel batch of the frame under <see cref="SyncRoot"/>, auto-declaring unknown channels; bad batches are dropped and logged, never thrown. Listeners run after the lock is released.</summary>
    public void PushFrame(SkyScopeFrame frame)
    {
        lock (SyncRoot) PushFrameCore(frame);
    }

    private void PushFrameCore(SkyScopeFrame frame)
    {
        var changed = new List<int>();
        foreach (var ch in frame.Channels)
        {
            var entry = _channels.GetValueOrDefault(ch.Id) ?? AutoDeclare(ch);
            try
            {
                GrowForObservedRate(entry, ch);
                Append(entry.Buffer, ch);
                changed.Add(ch.Id);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                Dropped++;
                LastDropReason = e.Message.StartsWith(SignalBuffer.OutOfOrder) ? SignalBuffer.OutOfOrder : e.Message;
                _log(LogLevel.Warn, $"channel {ch.Id}: dropped {ch.Encoding} batch ({LastDropReason})");
            }
        }
        if (changed.Count > 0) foreach (var l in _listeners.ToArray()) l(changed, frame);
    }

    /// <summary>Register a change listener; dispose the result to unsubscribe. Not synchronized with <see cref="PushFrame"/>: subscribe before sources start.</summary>
    public IDisposable Subscribe(StoreListener listener)
    {
        _listeners.Add(listener);
        return new Unsubscriber(() => _listeners.Remove(listener));
    }

    private sealed class Unsubscriber(Action dispose) : IDisposable { public void Dispose() => dispose(); }

    /// <summary>
    /// A channel declared without a rate starts at <see cref="DefaultCapacity"/>; every batch reveals a rate (1/dt for regular and
    /// quantized batches, samples per second of span for timestamped ones), and when the highest rate seen asks for a larger
    /// buffer than the channel has, the buffer grows to <c>rate × retention</c> (capped at <see cref="MaxCapacity"/>), keeping its samples.
    /// </summary>
    private void GrowForObservedRate(StoreChannel entry, FrameChannel ch)
    {
        if (entry.Info.Rate is > 0) return;
        var rate = ObservedRate(ch);
        if (!(rate > 0) || rate <= (entry.ObservedRate ?? 0)) return;
        entry.ObservedRate = rate;
        var wanted = CapacityForRate(rate);
        if (wanted > entry.Buffer.Capacity) entry.Buffer = entry.Buffer.Resized(wanted);
    }

    /// <summary>Sample rate a batch implies: 1/dt for regular and quantized batches, (n − 1) / span for timestamped ones with at least two samples; 0 when unknown.</summary>
    private static double ObservedRate(FrameChannel ch)
    {
        if (ch.Encoding != FrameEncoding.Timestamped) return ch.Dt > 0 ? 1 / ch.Dt : 0;
        var n = ch.Times.Length;
        if (n < 2) return 0;
        var span = ch.Times[n - 1] - ch.Times[0];
        return span > 0 ? (n - 1) / span : 0;
    }

    private StoreChannel AutoDeclare(FrameChannel ch)
    {
        DeclareChannelCore(new ChannelInfo(ch.Id, $"ch{ch.Id}") { Timing = ch.Encoding == FrameEncoding.Timestamped ? ChannelTiming.Timestamped : ChannelTiming.Regular });
        return _channels[ch.Id];
    }

    /// <summary>Regular batches can be expanded into a timestamped buffer; the reverse has no dt and is refused.</summary>
    private static void Append(SignalBuffer buffer, FrameChannel ch)
    {
        if (ch.Encoding == FrameEncoding.Timestamped)
        {
            if (buffer.Kind != TimeKind.Timestamped) throw new InvalidOperationException("timing-mismatch");
            buffer.AppendTimestamped(ch.Times, ch.Values);
            return;
        }
        var values = ch.GetValues();
        if (buffer.Kind == TimeKind.Regular) { buffer.AppendRegular(ch.TStart, ch.Dt, values); return; }
        var times = new double[values.Length];
        for (var i = 0; i < times.Length; i++) times[i] = ch.TStart + i * ch.Dt;
        buffer.AppendTimestamped(times, values);
    }
}
