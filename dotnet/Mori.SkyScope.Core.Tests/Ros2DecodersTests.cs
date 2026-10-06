// Mori.SkyScope — The hand-written CDR decoders for the 3D ROS 2 messages, fed with payloads built by Ros2Sharp's CdrWriter.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.Ros2Sharp;
using Mori.SkyScope.Sources.Ros2;

namespace Mori.SkyScope.Core.Tests;

/// <summary>The hand-written CDR decoders for the 3D ROS 2 messages, fed with payloads built by Ros2Sharp's CdrWriter.</summary>
public sealed class Ros2DecodersTests
{
    private static void Header(CdrWriter w, int sec, uint nsec, string frame) { w.Write(sec); w.Write(nsec); w.Write(frame); }
    private static void Pose(CdrWriter w, double x, double y, double z, double qx = 0, double qy = 0, double qz = 0, double qw = 1) { w.Write(x); w.Write(y); w.Write(z); w.Write(qx); w.Write(qy); w.Write(qz); w.Write(qw); }
    /// <summary>The parameterless writer is headerless; the reader expects the 4-byte encapsulation header (CDR_LE) in front, as on the wire.</summary>
    private static CdrReader Reader(CdrWriter w) { var a = w.ToArray(); return new CdrReader(new CdrWriter().ToArray().Length == 0 ? [0, 1, 0, 0, .. a] : a); }

    /// <summary>A cloud with an intensity field keeps only the points whose xyz are finite, with the intensities compacted alongside.</summary>
    [Fact]
    public void PointCloud2WithIntensityDropsNonFinitePoints()
    {
        var w = new CdrWriter();
        Header(w, 10, 500_000_000, "lidar");
        w.Write(1u); w.Write(3u);                       // height, width
        w.WriteLength(4);
        foreach (var (name, off) in new[] { ("x", 0u), ("y", 4u), ("z", 8u), ("intensity", 12u) }) { w.Write(name); w.Write(off); w.Write((byte)7); w.Write(1u); }
        w.Write(false); w.Write(16u); w.Write(48u);     // is_bigendian, point_step, row_step
        var data = new byte[48];
        float[] pts = [1, 2, 3, 0.5f, float.NaN, 0, 0, 0, -1, -2, -3, 0.9f];
        Buffer.BlockCopy(pts, 0, data, 0, 48);
        w.WriteLength(48); w.WriteBytes(data);
        w.Write(false);                                 // is_dense
        var c = Cdr3D.PointCloud2(Reader(w));
        Assert.Equal("lidar", c.Header.FrameId);
        Assert.Equal([1, 2, 3, -1, -2, -3], c.Positions);
        Assert.Equal([0.5f, 0.9f], c.Intensities!);
        Assert.Null(c.Colors);
    }

    /// <summary>A packed <c>rgb</c> float field is unpacked into RGBA bytes in r, g, b order with full alpha.</summary>
    [Fact]
    public void PointCloud2WithPackedRgbYieldsColours()
    {
        var w = new CdrWriter();
        Header(w, 0, 0, "cam");
        w.Write(1u); w.Write(1u);
        w.WriteLength(4);
        foreach (var (name, off) in new[] { ("x", 0u), ("y", 4u), ("z", 8u), ("rgb", 16u) }) { w.Write(name); w.Write(off); w.Write((byte)7); w.Write(1u); }
        w.Write(false); w.Write(32u); w.Write(32u);
        var data = new byte[32];
        Buffer.BlockCopy(new float[] { 1, 1, 1 }, 0, data, 0, 12);
        data[16] = 0x33; data[17] = 0x22; data[18] = 0x11;   // b, g, r little-endian packed float
        w.WriteLength(32); w.WriteBytes(data); w.Write(true);
        var c = Cdr3D.PointCloud2(Reader(w));
        Assert.Equal([0x11, 0x22, 0x33, 255], c.Colors);
        Assert.Null(c.Intensities);
    }

    /// <summary>TFMessage, MarkerArray and Path decode field by field, including the marker kind mapping and the unsupported mesh-resource type.</summary>
    [Fact]
    public void TfMessageMarkerArrayAndPathDecode()
    {
        var tf = new CdrWriter();
        tf.WriteLength(2);
        Header(tf, 1, 0, "map"); tf.Write("odom"); Pose(tf, 1, 2, 0);
        Header(tf, 1, 500_000_000, "odom"); tf.Write("base_link"); Pose(tf, 0.5, 0, 0, 0, 0, 0.7071068, 0.7071068);
        var transforms = Cdr3D.TFMessage(Reader(tf));
        Assert.Equal(2, transforms.Count);
        Assert.Equal(("map", "odom"), (transforms[0].Parent, transforms[0].Child));
        Assert.Equal([0.5, 0, 0], transforms[1].T); Assert.Equal(1.5, transforms[1].Sec + transforms[1].Nanosec * 1e-9, 9);

        var ma = new CdrWriter();
        ma.WriteLength(2);
        // a red cube (type 1, action 0) with a 2 s lifetime
        Header(ma, 3, 0, "map"); ma.Write("boxes"); ma.Write(7); ma.Write(1); ma.Write(0); Pose(ma, 1, 2, 3); ma.Write(0.5); ma.Write(0.5); ma.Write(0.5);
        ma.Write(1f); ma.Write(0f); ma.Write(0f); ma.Write(1f); ma.Write(2); ma.Write(0u); ma.Write(false); ma.WriteLength(0); ma.WriteLength(0); ma.Write("");
        // a text marker (type 9) deleted (action 2)
        Header(ma, 3, 0, "map"); ma.Write("labels"); ma.Write(1); ma.Write(9); ma.Write(2); Pose(ma, 0, 0, 0); ma.Write(1.0); ma.Write(1.0); ma.Write(0.3);
        ma.Write(1f); ma.Write(1f); ma.Write(1f); ma.Write(1f); ma.Write(0); ma.Write(0u); ma.Write(false); ma.WriteLength(0); ma.WriteLength(0); ma.Write("hello");
        var markers = Cdr3D.MarkerArray(Reader(ma));
        Assert.Equal(2, markers.Count);
        Assert.Equal(("boxes", 7, "cube", "#ff0000", 2.0), (markers[0].Ns, markers[0].Id, Cdr3D.MarkerKind(markers[0].Type), markers[0].Color, markers[0].Lifetime));
        Assert.Equal((2, "hello"), (markers[1].Action, markers[1].Text));
        Assert.Null(Cdr3D.MarkerKind(10));   // mesh resources are not supported

        var path = new CdrWriter();
        Header(path, 4, 0, "map"); path.WriteLength(3);
        for (var i = 0; i < 3; i++) { Header(path, 4, 0, "map"); Pose(path, i, i * 2, 0.1); }
        var (h, pos) = Cdr3D.Path(Reader(path));
        Assert.Equal("map", h.FrameId);
        Assert.Equal([0, 0, 0.1f, 1, 2, 0.1f, 2, 4, 0.1f], pos);
    }
}
