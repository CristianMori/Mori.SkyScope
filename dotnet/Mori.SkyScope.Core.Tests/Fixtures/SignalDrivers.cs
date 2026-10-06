// Mori.SkyScope — Fixture drivers for the signal ring buffer and the M4 decimation.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Signals;

namespace Mori.SkyScope.Core.Tests.Fixtures;

/// <summary>Shared parsing, append and sampling helpers for the signal-buffer family of drivers.</summary>
internal static class SignalSteps
{
    /// <summary>Time kind from <c>kind</c>: <c>timestamped</c>, otherwise regular.</summary>
    public static TimeKind Kind(JsonElement setup) =>
        setup.TryGetProperty("kind", out var k) && k.GetString() == "timestamped" ? TimeKind.Timestamped : TimeKind.Regular;

    /// <summary>Parses a JSON number array as floats.</summary>
    public static float[] Floats(JsonElement e) => e.EnumerateArray().Select(x => x.GetSingle()).ToArray();
    /// <summary>Parses a JSON number array as doubles.</summary>
    public static double[] Doubles(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();

    /// <summary>Stable error codes shared with the TS drivers.</summary>
    public static string ErrorCode(Exception e) => e switch
    {
        ArgumentException a when a.Message.StartsWith(SignalBuffer.OutOfOrder) => SignalBuffer.OutOfOrder,
        InvalidOperationException => "wrong-kind",
        _ => e.Message,
    };

    /// <summary>Handles <c>appendRegular</c> and <c>appendTimestamped</c> steps, recording failures as error codes; returns false for any other step type.</summary>
    public static bool ApplyAppend(SignalBuffer buf, JsonElement step, List<string> errors)
    {
        var type = step.GetProperty("type").GetString();
        try
        {
            switch (type)
            {
                case "appendRegular": buf.AppendRegular(step.GetProperty("t0").GetDouble(), step.GetProperty("dt").GetDouble(), Floats(step.GetProperty("values"))); return true;
                case "appendTimestamped": buf.AppendTimestamped(Doubles(step.GetProperty("times")), Floats(step.GetProperty("values"))); return true;
            }
        }
        catch (Exception e) { errors.Add(ErrorCode(e)); return true; }
        return false;
    }

    /// <summary>Every retained sample as a <c>[time, value]</c> pair.</summary>
    public static List<double[]> Samples(SignalBuffer buf)
    {
        var list = new List<double[]>();
        buf.ForEach(buf.FirstSeq, buf.HeadSeq, (_, t, v) => list.Add([t, v]));
        return list;
    }

    /// <summary>NaN as null, so JSON comparison works.</summary>
    public static double? NanToNull(double v) => double.IsNaN(v) ? null : v;
}

/// <summary>Fixture driver for the signal ring buffer: appends, time and index lookups and window queries.</summary>
public sealed class SignalBufferDriver : IFixtureDriver
{
    /// <summary>Handles the <c>signal-buffer</c> fixtures.</summary>
    public string Component => "signal-buffer";
    private sealed record State(SignalBuffer Buf, List<object?> Queries, List<string> Errors);

    /// <summary>Creates a buffer of the given <c>capacity</c> and time kind.</summary>
    public object Create(JsonElement setup) => new State(new SignalBuffer(setup.GetProperty("capacity").GetInt32(), SignalSteps.Kind(setup)), [], []);

    /// <summary>Appends samples, or answers indexOfTime, indexAfterTime, timeAt, valueAt and window queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        var buf = s.Buf;
        if (SignalSteps.ApplyAppend(buf, step, s.Errors)) return s;
        if (step.GetProperty("type").GetString() != "query") throw new InvalidOperationException($"unknown step {step}");
        if (step.TryGetProperty("indexOfTime", out var a)) s.Queries.Add(buf.IndexOfTime(a.GetDouble()));
        else if (step.TryGetProperty("indexAfterTime", out var b)) s.Queries.Add(buf.IndexAfterTime(b.GetDouble()));
        else if (step.TryGetProperty("timeAt", out var c)) s.Queries.Add(buf.TimeAt(c.GetInt64()));
        else if (step.TryGetProperty("valueAt", out var d)) s.Queries.Add(buf.ValueAt(d.GetInt64()));
        else if (step.TryGetProperty("window", out var w)) { var r = SignalSteps.Doubles(w); var (from, to) = buf.Window(r[0], r[1]); s.Queries.Add(new { fromSeq = from, toSeq = to }); }
        else throw new InvalidOperationException($"unknown query {step}");
        return s;
    }

    /// <summary>Kind, capacity, sequence numbers, time span, run count (regular buffers only), every sample, the query answers and the error codes.</summary>
    public JsonNode Snapshot(object state)
    {
        var (buf, queries, errors) = (State)state;
        return JsonSerializer.SerializeToNode(new
        {
            kind = buf.Kind == TimeKind.Regular ? "regular" : "timestamped", capacity = buf.Capacity,
            head = buf.HeadSeq, length = buf.Length, firstSeq = buf.FirstSeq,
            earliestTime = SignalSteps.NanToNull(buf.EarliestTime()), latestTime = SignalSteps.NanToNull(buf.LatestTime()),
            runs = buf.Kind == TimeKind.Regular ? buf.RunCount : (int?)null,
            samples = SignalSteps.Samples(buf), queries, errors,
        }, Fixtures.Json)!;
    }
}

/// <summary>Fixture driver for the M4 bucket decimation built over a signal buffer.</summary>
public sealed class DecimationDriver : IFixtureDriver
{
    /// <summary>Handles the <c>decimation</c> fixtures.</summary>
    public string Component => "decimation";
    private sealed record State(SignalBuffer Buf, BucketSeries Series, List<object?> Queries, List<string> Errors);

    /// <summary>Creates the buffer and a bucket series with the given <c>quantum</c> and bucket limit.</summary>
    public object Create(JsonElement setup)
    {
        var buf = new SignalBuffer(setup.GetProperty("capacity").GetInt32(), SignalSteps.Kind(setup));
        var max = setup.TryGetProperty("maxBuckets", out var m) ? m.GetInt32() : 4096;
        return new State(buf, new BucketSeries(buf, setup.GetProperty("quantum").GetDouble(), max), [], []);
    }

    /// <summary>Appends samples, updates or rebuilds the series, changes the quantum, and answers bucket range, single bucket and polyline queries.</summary>
    public object Step(object state, JsonElement step)
    {
        var s = (State)state;
        if (SignalSteps.ApplyAppend(s.Buf, step, s.Errors)) return s;
        switch (step.GetProperty("type").GetString())
        {
            case "update": s.Queries.Add(s.Series.Update()); return s;
            case "rebuild": s.Series.Rebuild(); return s;
            case "setQuantum": s.Series.SetQuantum(step.GetProperty("quantum").GetDouble()); return s;
            case "query":
                if (step.TryGetProperty("buckets", out var r)) { var q = SignalSteps.Doubles(r); s.Queries.Add(s.Series.BucketsInRange(q[0], q[1])); }
                else if (step.TryGetProperty("bucket", out var i)) s.Queries.Add(s.Series.GetBucket(i.GetInt64()));
                else if (step.TryGetProperty("polyline", out var p)) { var q = SignalSteps.Doubles(p); s.Queries.Add(s.Series.Polyline(q[0], q[1])); }
                else throw new InvalidOperationException($"unknown query {step}");
                return s;
        }
        throw new InvalidOperationException($"unknown step {step}");
    }

    /// <summary>Quantum and top index of the series plus the query answers and the error codes.</summary>
    public JsonNode Snapshot(object state)
    {
        var s = (State)state;
        return JsonSerializer.SerializeToNode(new { quantum = s.Series.Quantum, top = s.Series.TopIndex, queries = s.Queries, errors = s.Errors }, Fixtures.Json)!;
    }
}
