// Mori.SkyScope — Incremental M4 decimation over a signal buffer at a pixel quantum, rebuilt on zoom.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Signals;

/// <summary>One decimation bucket: the first, minimum, maximum and last sample it holds, with their times.</summary>
/// <param name="Index">floor(t / quantum).</param>
/// <param name="T">Bucket start time in seconds (Index × quantum).</param>
/// <param name="Count">Samples folded into the bucket.</param>
/// <param name="First">Value of the first sample.</param>
/// <param name="Min">Smallest value.</param>
/// <param name="Max">Largest value.</param>
/// <param name="Last">Value of the last sample.</param>
/// <param name="TFirst">Time of the first sample.</param>
/// <param name="TMin">Time of the smallest value.</param>
/// <param name="TMax">Time of the largest value.</param>
/// <param name="TLast">Time of the last sample.</param>
public readonly record struct Bucket(
    long Index, double T, int Count,
    float First, float Min, float Max, float Last,
    double TFirst, double TMin, double TMax, double TLast);

/// <summary>
/// Incremental M4 decimation over a <see cref="SignalBuffer"/>. Mirrors <c>BucketSeries</c> in
/// <c>@cmori/skyscope-core</c>; pinned by <c>spec/fixtures/decimation.json</c>.
/// Bucket index = floor(t / quantum). Each bucket keeps first/min/max/last with their times, so a
/// window draws at most 4 points per bucket regardless of sample count.
/// </summary>
public sealed class BucketSeries
{
    private readonly int[] _count;
    private readonly float[] _vFirst, _vMin, _vMax, _vLast;
    private readonly double[] _tFirst, _tMin, _tMax, _tLast;
    private long? _top;      // highest bucket index seen
    private long _cursor;    // next seq to process

    /// <summary>Decimates <paramref name="buffer"/> at <paramref name="quantum"/> seconds per bucket, retaining the newest <paramref name="maxBuckets"/> buckets. Nothing is folded until <see cref="Update"/>.</summary>
    public BucketSeries(SignalBuffer buffer, double quantum, int maxBuckets = 4096)
    {
        if (!(quantum > 0)) throw new ArgumentOutOfRangeException(nameof(quantum), "quantum must be > 0");
        if (maxBuckets < 1) throw new ArgumentOutOfRangeException(nameof(maxBuckets), "maxBuckets must be >= 1");
        Buffer = buffer; Quantum = quantum; MaxBuckets = maxBuckets;
        _count = new int[maxBuckets];
        _vFirst = new float[maxBuckets]; _vMin = new float[maxBuckets]; _vMax = new float[maxBuckets]; _vLast = new float[maxBuckets];
        _tFirst = new double[maxBuckets]; _tMin = new double[maxBuckets]; _tMax = new double[maxBuckets]; _tLast = new double[maxBuckets];
    }

    /// <summary>Buffer being decimated.</summary>
    public SignalBuffer Buffer { get; }
    /// <summary>Ring capacity in buckets; older buckets are dropped.</summary>
    public int MaxBuckets { get; }
    /// <summary>Bucket width in seconds.</summary>
    public double Quantum { get; private set; }
    /// <summary>Highest bucket index seen, or null before any sample.</summary>
    public long? TopIndex => _top;

    /// <summary>Changes the bucket width and rebuilds when it differs.</summary>
    public void SetQuantum(double quantum)
    {
        if (!(quantum > 0)) throw new ArgumentOutOfRangeException(nameof(quantum), "quantum must be > 0");
        if (quantum == Quantum) return;
        Quantum = quantum;
        Rebuild();
    }

    /// <summary>Discard all buckets and re-derive them from whatever the buffer still holds.</summary>
    public void Rebuild()
    {
        Array.Clear(_count);
        _top = null;
        _cursor = Buffer.FirstSeq;
        Update();
    }

    /// <summary>Fold samples appended since the last call. Returns how many were processed.</summary>
    public long Update()
    {
        var from = Math.Max(_cursor, Buffer.FirstSeq);
        var to = Buffer.HeadSeq;
        if (to <= from) { _cursor = to; return 0; }
        Buffer.ForEach(from, to, (_, t, v) => Add(t, v));
        _cursor = to;
        return to - from;
    }

    /// <summary>Bucket by index, or null when it is empty or outside the retained range.</summary>
    public Bucket? GetBucket(long index)
    {
        if (_top is null || index > _top || index <= _top - MaxBuckets) return null;
        var s = Slot(index);
        if (_count[s] == 0) return null;
        return new Bucket(index, index * Quantum, _count[s],
            _vFirst[s], _vMin[s], _vMax[s], _vLast[s],
            _tFirst[s], _tMin[s], _tMax[s], _tLast[s]);
    }

    /// <summary>Non-empty buckets whose span intersects [tFrom, tTo].</summary>
    public List<Bucket> BucketsInRange(double tFrom, double tTo)
    {
        var outList = new List<Bucket>();
        if (_top is null) return outList;
        var lo = Math.Max((long)Math.Floor(tFrom / Quantum), _top.Value - MaxBuckets + 1);
        var hi = Math.Min((long)Math.Floor(tTo / Quantum), _top.Value);
        for (var i = lo; i <= hi; i++) if (GetBucket(i) is { } b) outList.Add(b);
        return outList;
    }

    /// <summary>
    /// M4 polyline for [tFrom, tTo] as interleaved [t, v, t, v, …]: per bucket first, then min/max in
    /// time order (skipping any that coincide with first/last), then last.
    /// </summary>
    public double[] Polyline(double tFrom, double tTo)
    {
        var pts = new List<double>();
        foreach (var b in BucketsInRange(tFrom, tTo))
        {
            pts.Add(b.TFirst); pts.Add(b.First);
            if (b.Count > 1)
            {
                var minFirst = b.TMin <= b.TMax;
                double t1 = minFirst ? b.TMin : b.TMax, v1 = minFirst ? b.Min : b.Max;
                double t2 = minFirst ? b.TMax : b.TMin, v2 = minFirst ? b.Max : b.Min;
                if (t1 != b.TFirst && t1 != b.TLast) { pts.Add(t1); pts.Add(v1); }
                if (t2 != b.TFirst && t2 != b.TLast) { pts.Add(t2); pts.Add(v2); }
                pts.Add(b.TLast); pts.Add(b.Last);
            }
        }
        return [.. pts];
    }

    private int Slot(long index) => (int)(((index % MaxBuckets) + MaxBuckets) % MaxBuckets);

    private void Add(double t, float v)
    {
        var b = (long)Math.Floor(t / Quantum);
        if (_top is null || b > _top)
        {
            var start = _top is null ? b : _top.Value + 1;
            var from = Math.Max(start, b - MaxBuckets + 1);
            for (var i = from; i <= b; i++) _count[Slot(i)] = 0;
            _top = b;
        }
        else if (b <= _top - MaxBuckets) return;

        var s = Slot(b);
        if (_count[s] == 0)
        {
            _count[s] = 1;
            _vFirst[s] = _vMin[s] = _vMax[s] = _vLast[s] = v;
            _tFirst[s] = _tMin[s] = _tMax[s] = _tLast[s] = t;
            return;
        }
        _count[s]++;
        _vLast[s] = v; _tLast[s] = t;
        if (v < _vMin[s]) { _vMin[s] = v; _tMin[s] = t; }
        if (v > _vMax[s]) { _vMax[s] = v; _tMax[s] = t; }
    }
}
