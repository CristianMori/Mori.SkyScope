// Mori.SkyScope — Append-only ring buffer for one channel.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Signals;

/// <summary>Regular: a fixed sample rate per run, no stored timestamps. Timestamped: one time per sample.</summary>
public enum TimeKind { Regular, Timestamped }

/// <summary>A stretch of a regular buffer with one sample period: the half-open seq range and its <paramref name="Dt"/>.</summary>
/// <param name="FromSeq">First sequence number of the stretch.</param>
/// <param name="ToSeq">Sequence number after the last one.</param>
/// <param name="Dt">Sample period in seconds.</param>
public readonly record struct RunSegment(long FromSeq, long ToSeq, double Dt);

/// <summary>
/// Append-only ring buffer for one channel. Mirrors <c>SignalBuffer</c> in <c>@cmori/skyscope-core</c>;
/// behaviour is pinned by <c>spec/fixtures/signal-buffer.json</c>.
/// Samples are addressed by a monotonically increasing sequence number. Regular-rate channels store
/// no per-sample timestamp: time is derived from "runs" (t0 + i·dt).
/// </summary>
public sealed class SignalBuffer
{
    /// <summary>Error message used by both cores when time goes backwards.</summary>
    public const string OutOfOrder = "out-of-order-time";

    private readonly record struct Run(long StartSeq, double T0, double Dt);

    private readonly float[] _values;
    private readonly double[]? _times;
    private readonly List<Run> _runs = [];
    private long _head;
    private int _runHint;

    /// <summary>Allocates room for <paramref name="capacity"/> samples; timestamped buffers also store a time per sample.</summary>
    public SignalBuffer(int capacity, TimeKind kind = TimeKind.Regular)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be >= 1");
        Capacity = capacity;
        Kind = kind;
        _values = new float[capacity];
        _times = kind == TimeKind.Timestamped ? new double[capacity] : null;
    }

    /// <summary>Samples retained at most; older ones are overwritten.</summary>
    public int Capacity { get; }
    /// <summary>Whether samples carry their own timestamps.</summary>
    public TimeKind Kind { get; }
    /// <summary>Next sequence number to be written (= total samples ever appended).</summary>
    public long HeadSeq => _head;
    /// <summary>Samples currently retained (at most <see cref="Capacity"/>).</summary>
    public int Length => (int)Math.Min(_head, Capacity);
    /// <summary>Oldest retained sequence number.</summary>
    public long FirstSeq => _head - Length;
    /// <summary>True before the first append.</summary>
    public bool IsEmpty => _head == 0;
    /// <summary>Retained regular-rate runs (diagnostics).</summary>
    public int RunCount => _runs.Count;

    /// <summary>Value at a sequence number; throws when outside [<see cref="FirstSeq"/>, <see cref="HeadSeq"/>).</summary>
    public float ValueAt(long seq) { Check(seq); return _values[Slot(seq)]; }

    /// <summary>Time in seconds of a sequence number; throws when outside the retained range.</summary>
    public double TimeAt(long seq)
    {
        Check(seq);
        if (_times is not null) return _times[Slot(seq)];
        var run = _runs[RunIndexFor(seq)];
        return run.T0 + (seq - run.StartSeq) * run.Dt;
    }

    /// <summary>Time of the oldest retained sample; NaN when empty.</summary>
    public double EarliestTime() => _head == 0 ? double.NaN : TimeAt(FirstSeq);
    /// <summary>Time of the newest sample; NaN when empty.</summary>
    public double LatestTime() => _head == 0 ? double.NaN : TimeAt(_head - 1);

    /// <summary>Appends samples at <paramref name="t0"/> + i·<paramref name="dt"/>; a new run starts unless it continues the last one. Throws on a backwards <paramref name="t0"/> or on a timestamped buffer.</summary>
    public void AppendRegular(double t0, double dt, ReadOnlySpan<float> values)
    {
        if (_times is not null) throw new InvalidOperationException("AppendRegular on a timestamped buffer");
        if (!(dt > 0)) throw new ArgumentOutOfRangeException(nameof(dt), "dt must be > 0");
        if (values.Length == 0) return;
        if (_runs.Count > 0)
        {
            var last = _runs[^1];
            var lastT = last.T0 + (_head - 1 - last.StartSeq) * last.Dt;
            if (t0 < lastT) throw new ArgumentException(OutOfOrder, nameof(t0));
            var continues = last.Dt == dt && Math.Abs(t0 - (lastT + dt)) <= dt * 1e-3;
            if (!continues) _runs.Add(new Run(_head, t0, dt));
        }
        else _runs.Add(new Run(_head, t0, dt));
        Write(values);
        PruneRuns();
    }

    /// <summary>Appends samples with explicit times, which must not go backwards. Throws on a regular buffer or mismatched lengths.</summary>
    public void AppendTimestamped(ReadOnlySpan<double> times, ReadOnlySpan<float> values)
    {
        if (_times is null) throw new InvalidOperationException("AppendTimestamped on a regular buffer");
        if (times.Length != values.Length) throw new ArgumentException("times/values length mismatch");
        if (values.Length == 0) return;
        var prev = _head == 0 ? double.NegativeInfinity : LatestTime();
        foreach (var t in times) { if (t < prev) throw new ArgumentException(OutOfOrder, nameof(times)); prev = t; }
        for (var i = 0; i < times.Length; i++) _times[Slot(_head + i)] = times[i];
        Write(values);
    }

    /// <summary>First seq whose time >= t; <see cref="HeadSeq"/> if none.</summary>
    public long IndexOfTime(double t)
    {
        long lo = FirstSeq, hi = _head;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (TimeAt(mid) < t) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>First seq whose time > t; <see cref="HeadSeq"/> if none.</summary>
    public long IndexAfterTime(double t)
    {
        long lo = FirstSeq, hi = _head;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (TimeAt(mid) <= t) lo = mid + 1; else hi = mid; }
        return lo;
    }

    /// <summary>Samples with tFrom &lt;= t &lt;= tTo as a half-open seq range.</summary>
    public (long FromSeq, long ToSeq) Window(double tFrom, double tTo) => (IndexOfTime(tFrom), IndexAfterTime(tTo));

    /// <summary>Callback of <see cref="ForEach"/>: sequence number, time in seconds and value.</summary>
    public delegate void SampleAction(long seq, double t, float v);

    /// <summary>Sequential read of [fromSeq, toSeq) — the hot path for decimation.</summary>
    public void ForEach(long fromSeq, long toSeq, SampleAction fn)
    {
        fromSeq = Math.Max(fromSeq, FirstSeq);
        toSeq = Math.Min(toSeq, _head);
        if (_times is not null)
        {
            for (var seq = fromSeq; seq < toSeq; seq++) { var s = Slot(seq); fn(seq, _times[s], _values[s]); }
            return;
        }
        var cur = fromSeq;
        while (cur < toSeq)
        {
            var ri = RunIndexFor(cur);
            var run = _runs[ri];
            var runEnd = ri + 1 < _runs.Count ? _runs[ri + 1].StartSeq : _head;
            var end = Math.Min(runEnd, toSeq);
            for (; cur < end; cur++) fn(cur, run.T0 + (cur - run.StartSeq) * run.Dt, _values[Slot(cur)]);
        }
    }

    /// <summary>Regular buffers: the runs overlapping [fromSeq, toSeq) as seq ranges clipped to it, in order, each with its dt; empty for timestamped buffers or an empty range.</summary>
    public List<RunSegment> RunsIn(long fromSeq, long toSeq)
    {
        var output = new List<RunSegment>();
        if (_times is not null) return output;
        var seq = Math.Max(fromSeq, FirstSeq);
        var end = Math.Min(toSeq, _head);
        while (seq < end)
        {
            var ri = RunIndexFor(seq);
            var runEnd = ri + 1 < _runs.Count ? _runs[ri + 1].StartSeq : _head;
            var to = Math.Min(runEnd, end);
            output.Add(new RunSegment(seq, to, _runs[ri].Dt));
            seq = to;
        }
        return output;
    }

    private int Slot(long seq) => (int)(seq % Capacity);

    /// <summary>
    /// A new buffer of <paramref name="capacity"/> samples holding the newest retained samples of this one (all of them when
    /// they fit), with the same time kind and the same sequence numbers; runs are carried over so regular channels keep
    /// their 4 bytes per sample. Used by the store when a channel's observed rate asks for more history than it was given.
    /// </summary>
    public SignalBuffer Resized(int capacity)
    {
        var o = new SignalBuffer(capacity, Kind);
        var keep = Math.Min(Length, o.Capacity);
        var from = _head - keep;
        o._head = from;
        if (_times is not null)
        {
            var t = new double[keep]; var v = new float[keep];
            for (var i = 0; i < keep; i++) { var s = Slot(from + i); t[i] = _times[s]; v[i] = _values[s]; }
            if (keep > 0) o.AppendTimestamped(t, v);
            return o;
        }
        var seq = from;
        while (seq < _head)
        {
            var ri = RunIndexFor(seq);
            var run = _runs[ri];
            var end = ri + 1 < _runs.Count ? _runs[ri + 1].StartSeq : _head;
            var v = new float[end - seq];
            for (var i = 0; i < v.Length; i++) v[i] = _values[Slot(seq + i)];
            o._runs.Add(new Run(seq, run.T0 + (seq - run.StartSeq) * run.Dt, run.Dt));
            o.Write(v);
            seq = end;
        }
        return o;
    }

    private void Write(ReadOnlySpan<float> values)
    {
        for (var i = 0; i < values.Length; i++) _values[Slot(_head + i)] = values[i];
        _head += values.Length;
    }

    private void PruneRuns()
    {
        var first = FirstSeq;
        while (_runs.Count > 1 && _runs[1].StartSeq <= first) { _runs.RemoveAt(0); _runHint = 0; }
    }

    private int RunIndexFor(long seq)
    {
        var h = _runHint;
        if (h < _runs.Count && _runs[h].StartSeq <= seq && (h + 1 >= _runs.Count || _runs[h + 1].StartSeq > seq)) return h;
        int lo = 0, hi = _runs.Count - 1;
        while (lo < hi) { var mid = (lo + hi + 1) >> 1; if (_runs[mid].StartSeq <= seq) lo = mid; else hi = mid - 1; }
        _runHint = lo;
        return lo;
    }

    private void Check(long seq)
    {
        if (seq < FirstSeq || seq >= _head) throw new ArgumentOutOfRangeException(nameof(seq), $"seq {seq} out of range [{FirstSeq}, {_head})");
    }
}
