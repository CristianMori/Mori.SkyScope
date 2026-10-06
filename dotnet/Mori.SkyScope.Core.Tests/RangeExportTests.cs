// Mori.SkyScope — Range export: the shared fixed-point formatter on exact halves, and a CSV export that parses back with its empty cells.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Mcap;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests;

/// <summary>The formatter both cores share and the CSV/MCAP exports of a small store.</summary>
public sealed class RangeExportTests
{
    /// <summary>The same table is pinned in <c>export.test.ts</c>: value, decimals, text.</summary>
    public static TheoryData<double, int, string> Table => new()
    {
        { 0.5, 0, "1" }, { 1.5, 0, "2" }, { 2.5, 0, "3" }, { -0.5, 0, "-1" }, { -2.5, 0, "-3" },
        { 0.125, 2, "0.13" }, { -0.125, 2, "-0.13" }, { 0.375, 2, "0.38" }, { 0.995, 2, "1.00" }, { 5e-7, 6, "0.000001" },
        { -1e-7, 6, "0.000000" }, { -0.0, 3, "0.000" }, { 1234.5678, 2, "1234.57" }, { 2.3000000000000003, 6, "2.300000" }, { 1700000000.123456789, 6, "1700000000.123457" },
        { -123.456, 0, "-123" }, { 0.1, 15, "0.100000000000000" }, { double.NaN, 2, "NaN" }, { double.PositiveInfinity, 2, "Infinity" }, { double.NegativeInfinity, 1, "-Infinity" }, { 3.14159, 20, "3.141590000000000" }, { 7, -1, "7" },
    };

    /// <summary>Rounds exact halves away from zero, never prints -0 and clamps the decimals.</summary>
    [Theory, MemberData(nameof(Table))]
    public void FormatFixedMatchesTheTypeScriptTable(double v, int decimals, string text) => Assert.Equal(text, RangeExport.FormatFixed(v, decimals));

    private static SignalStore Store()
    {
        var store = new SignalStore(retentionSeconds: 10);
        store.DeclareChannel(new ChannelInfo(1, "a") { Rate = 10 });
        store.DeclareChannel(new ChannelInfo(2, "b") { Timing = ChannelTiming.Timestamped });
        store.PushFrame(new SkyScopeFrame(0, 0, [FrameChannel.Regular(1, 0, 0.5, [1, 2, 3, 4, 5])]));
        store.PushFrame(new SkyScopeFrame(1, 0.25, [FrameChannel.Timestamped(2, [0.25, 1, 1.75], [10, 20, 30])]));
        return store;
    }

    /// <summary>One row per distinct time with empty cells; <see cref="CsvRecording.Parse"/> reads the empty cells as missing samples.</summary>
    [Fact]
    public void CsvRoundTripsWithEmptyCells()
    {
        var csv = RangeExport.ExportRangeCsv(Store(), [1, 2], 0, 2, new CsvExportOptions { Decimals = 2, ValueDecimals = 1 });
        Assert.Equal("t,a,b\n0.00,1.0,\n0.25,,10.0\n0.50,2.0,\n1.00,3.0,20.0\n1.50,4.0,\n1.75,,30.0\n2.00,5.0,\n", csv);
        var rec = CsvRecording.Parse(csv);
        Assert.Equal(["a", "b"], rec.Channels.Select(c => c.Name));
        var f = rec.Frames[0];
        Assert.Equal([0, 0.5, 1, 1.5, 2], f.Channels[0].Times);
        Assert.Equal([10, 20, 30], f.Channels[1].Values);
    }

    /// <summary>The MCAP export reads back as a recording of the span, channels in ascending id, each batch keeping its encoding.</summary>
    [Fact]
    public void McapReadsBackAsARecordingOfTheSpan()
    {
        var rec = SkyScopeMcap.ReadRecording(RangeExport.ExportRangeMcap(Store(), [2, 1], 0.5, 1.75));
        Assert.Equal([1, 2], rec.Channels.Select(c => c.Id));
        Assert.Equal([(0.5, 1, FrameEncoding.Regular), (1.0, 2, FrameEncoding.Timestamped)], rec.Frames.Select(f => (f.T0, (int)f.Channels[0].Id, f.Channels[0].Encoding)));
        Assert.Equal(0.5, rec.Start); Assert.Equal(1.75, rec.End);
    }
}
