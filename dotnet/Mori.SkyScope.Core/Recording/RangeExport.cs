// Mori.SkyScope — Export a time range of a store's channels to CSV or to an MCAP recording: the cursor span of a chart, or any explicit range.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Text;
using Mori.SkyScope.Core.Signals;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Mcap;

/// <summary>Options of <see cref="RangeExport.ExportRangeCsv"/>: decimals of the time column and of the value cells.</summary>
public sealed record CsvExportOptions
{
    /// <summary>Decimals of the time column (default 6).</summary>
    public int Decimals { get; init; } = 6;
    /// <summary>Decimals of the value cells (default 6).</summary>
    public int ValueDecimals { get; init; } = 6;
}

/// <summary>
/// Export a time range of a store's channels: <see cref="ExportRangeCsv"/> writes the wide CSV that <see cref="CsvRecording.Parse"/>
/// reads back (one row per distinct timestamp, a cell empty when a channel has no sample there), <see cref="ExportRangeMcap"/> the
/// MCAP file that <see cref="SkyScopeMcap.ReadRecording(byte[])"/> reads back, laid out like an <see cref="McapRecorder"/> recording of
/// that span. Both cores produce identical text and bytes. Mirrors <c>recording/export.ts</c>; pinned by <c>spec/fixtures/trend-chart.json</c>.
/// </summary>
public static class RangeExport
{
    /// <summary>Samples per frame in an MCAP export.</summary>
    public const int BatchSamples = 1000;

    private static readonly double[] Pow10 = [1, 10, 100, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];
    /// <summary>2^53: integer parts up to here print exactly in both cores.</summary>
    private const double ExactLimit = 9007199254740992;

    /// <summary>
    /// Fixed-point formatting shared with the TypeScript core: half away from zero on the double arithmetic both languages share
    /// (<c>floor(fraction × 10^decimals + 0.5)</c>, carried into the integer part), invariant digits and never "-0". "F" formatting
    /// and JavaScript's <c>toFixed</c> disagree on exact halves, so neither is used. NaN and the infinities print as <c>NaN</c>,
    /// <c>Infinity</c> and <c>-Infinity</c>; magnitudes of 2^53 and above fall back to the shortest round-trip form.
    /// <paramref name="decimals"/> is clamped to 0–15.
    /// </summary>
    public static string FormatFixed(double v, int decimals)
    {
        if (!double.IsFinite(v)) return double.IsNaN(v) ? "NaN" : v > 0 ? "Infinity" : "-Infinity";
        var d = Math.Clamp(decimals, 0, 15);
        var abs = Math.Abs(v);
        if (abs >= ExactLimit) return v.ToString("R", CultureInfo.InvariantCulture).Replace("E+", "e+").Replace("E-", "e-");
        var whole = Math.Floor(abs);
        var p = Pow10[d];
        var frac = Math.Floor((abs - whole) * p + 0.5);
        if (frac >= p) { whole += 1; frac = 0; }
        var digits = d == 0 ? ((long)whole).ToString(CultureInfo.InvariantCulture) : ((long)whole).ToString(CultureInfo.InvariantCulture) + "." + ((long)frac).ToString(CultureInfo.InvariantCulture).PadLeft(d, '0');
        return v < 0 && (whole > 0 || frac > 0) ? "-" + digits : digits;
    }

    /// <summary>The store entries of the requested ids, without duplicates and in the given order; ids the store does not know are skipped.</summary>
    private static List<StoreChannel> SelectChannels(SignalStore store, IEnumerable<int> channelIds)
    {
        var seen = new HashSet<int>(); var output = new List<StoreChannel>();
        foreach (var id in channelIds)
        {
            if (!seen.Add(id)) continue;
            if (store.Get(id) is { } c) output.Add(c);
        }
        return output;
    }

    /// <summary>Column headers: the channel name (<c>ch&lt;id&gt;</c> when blank), <c>#&lt;id&gt;</c> appended where two selected channels share a name; commas, quotes and line breaks become spaces.</summary>
    public static List<string> CsvColumnNames(IReadOnlyList<ChannelInfo> channels)
    {
        var clean = channels.Select(c => { var n = c.Name.Replace(',', ' ').Replace('"', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim(); return n.Length > 0 ? n : $"ch{c.Id}"; }).ToList();
        var counts = clean.GroupBy(n => n, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        return clean.Select((n, i) => counts[n] > 1 ? $"{n}#{channels[i].Id}" : n).ToList();
    }

    /// <summary>
    /// Wide CSV of the samples with <c>t0 ≤ t ≤ t1</c> (either order) of the given channels: header <c>t,&lt;name&gt;[,&lt;name&gt;…]</c>,
    /// one row per distinct timestamp across the channels in ascending order, a cell empty when a channel has no sample at that time
    /// (the last of several samples at one time wins), <c>\n</c> line endings and a trailing newline. Times and values are written
    /// with <see cref="FormatFixed"/>. Channels the store does not know are skipped.
    /// </summary>
    public static string ExportRangeCsv(SignalStore store, IEnumerable<int> channelIds, double t0, double t1, CsvExportOptions? options = null)
    {
        options ??= new CsvExportOptions();
        double from = Math.Min(t0, t1), to = Math.Max(t0, t1);
        int td = options.Decimals, vd = options.ValueDecimals;
        List<StoreChannel> channels;
        List<(List<double> Times, List<float> Values)> columns;
        lock (store.SyncRoot)
        {
            channels = SelectChannels(store, channelIds);
            columns = channels.Select(c =>
            {
                var times = new List<double>(); var values = new List<float>();
                var (fromSeq, toSeq) = c.Buffer.Window(from, to);
                c.Buffer.ForEach(fromSeq, toSeq, (_, t, v) => { times.Add(t); values.Add(v); });
                return (times, values);
            }).ToList();
        }
        var all = columns.SelectMany(c => c.Times).ToList();
        all.Sort();
        var sb = new StringBuilder();
        sb.Append('t');
        foreach (var n in CsvColumnNames(channels.Select(c => c.Info).ToList())) sb.Append(',').Append(n);
        sb.Append('\n');
        var at = new int[columns.Count];
        var prev = double.NaN;
        foreach (var t in all)
        {
            if (t == prev) continue;
            prev = t;
            sb.Append(FormatFixed(t, td));
            for (var k = 0; k < columns.Count; k++)
            {
                var (times, values) = columns[k];
                sb.Append(',');
                var j = at[k];
                if (j < times.Count && times[j] == t)
                {
                    while (j + 1 < times.Count && times[j + 1] == t) j++;
                    sb.Append(FormatFixed(values[j], vd));
                    at[k] = j + 1;
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// MCAP file of the samples with <c>t0 ≤ t ≤ t1</c> (either order) of the given channels, in the layout <see cref="McapRecorder"/>
    /// writes: the frame and catalog channels, one catalog message with the selected channels (ascending id) stamped at the range start,
    /// then one <see cref="SkyScopeFrame"/> message per batch of at most <see cref="BatchSamples"/> samples, channel by channel in
    /// ascending id. Regular channels keep their encoding (one regular batch per run, cut at the range and at the batch size),
    /// timestamped channels write timestamped batches; every frame is stamped with its first sample time.
    /// <see cref="SkyScopeMcap.ReadRecording(byte[])"/> reads it back; channels the store does not know are skipped.
    /// </summary>
    public static byte[] ExportRangeMcap(SignalStore store, IEnumerable<int> channelIds, double t0, double t1)
    {
        double from = Math.Min(t0, t1), to = Math.Max(t0, t1);
        var w = new McapWriter();
        var frameSchema = w.AddSchema("skyscope.Frame", "text", "SkyScopeFrame v1: binary batch of channel samples (see Mori.SkyScope streaming/frame)");
        var catalogSchema = w.AddSchema("skyscope.Channels", "jsonschema", "{}");
        var frameChannel = w.AddChannel(SkyScopeMcap.FramesTopic, frameSchema, SkyScopeMcap.FrameMessageEncoding);
        var catalogChannel = w.AddChannel(SkyScopeMcap.ChannelsTopic, catalogSchema, "json");
        lock (store.SyncRoot)
        {
            var channels = SelectChannels(store, channelIds).OrderBy(c => c.Info.Id).ToList();
            if (channels.Count > 0) w.AddMessage(catalogChannel, McapTime.SecondsToNs(from), Encoding.UTF8.GetBytes("{\"channels\":[" + string.Join(",", channels.Select(c => SkyScopeMcap.ChannelInfoJson(c.Info))) + "]}"));
            uint seq = 0;
            void Emit(FrameChannel ch, double tStart) => w.AddMessage(frameChannel, McapTime.SecondsToNs(tStart), FrameCodec.Encode(new SkyScopeFrame(seq++, tStart, [ch])));
            foreach (var c in channels)
            {
                var buf = c.Buffer; var id = (ushort)c.Info.Id;
                var (fromSeq, toSeq) = buf.Window(from, to);
                if (toSeq <= fromSeq) continue;
                if (buf.Kind == TimeKind.Regular)
                {
                    foreach (var run in buf.RunsIn(fromSeq, toSeq))
                    {
                        for (var s = run.FromSeq; s < run.ToSeq; s += BatchSamples)
                        {
                            var n = (int)Math.Min(BatchSamples, run.ToSeq - s);
                            var values = new float[n];
                            for (var i = 0; i < n; i++) values[i] = buf.ValueAt(s + i);
                            var tStart = buf.TimeAt(s);
                            Emit(FrameChannel.Regular(id, tStart, run.Dt, values), tStart);
                        }
                    }
                }
                else
                {
                    for (var s = fromSeq; s < toSeq; s += BatchSamples)
                    {
                        var n = (int)Math.Min(BatchSamples, toSeq - s);
                        var times = new double[n]; var values = new float[n];
                        for (var i = 0; i < n; i++) { times[i] = buf.TimeAt(s + i); values[i] = buf.ValueAt(s + i); }
                        Emit(FrameChannel.Timestamped(id, times, values), times[0]);
                    }
                }
            }
        }
        return w.Finish();
    }
}
