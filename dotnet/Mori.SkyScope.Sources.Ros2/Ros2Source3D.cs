// Mori.SkyScope — Hand-written CDR decoders for the 3D messages (sensor_msgs/PointCloud2, tf2_msgs/TFMessage, visualization_msgs/Marker(Array), nav_msgs/Pa…
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.Ros2Sharp;

namespace Mori.SkyScope.Sources.Ros2;

/// <summary>
/// Hand-written CDR decoders for the 3D messages (<c>sensor_msgs/PointCloud2</c>, <c>tf2_msgs/TFMessage</c>,
/// <c>visualization_msgs/Marker(Array)</c>, <c>nav_msgs/Path</c>) → the payload shapes of the 3D layers
/// (<c>pointCloud3d</c>, <c>frames</c>, <c>markers</c>, <c>path3d</c>).
/// </summary>
public static class Cdr3D
{
    /// <summary>One field of a PointCloud2 layout: name, byte offset within a point, ROS datatype code and element count.</summary>
    public sealed record PointField(string Name, uint Offset, byte Datatype, uint Count);
    /// <summary>A decoded PointCloud2: positions (xyz interleaved), optional intensities or RGBA colours.</summary>
    public sealed record PointCloud(Cdr.HeaderMsg Header, float[] Positions, float[]? Intensities, byte[]? Colors, int Width, int Height);

    private static float ReadFieldFloat(ReadOnlySpan<byte> p, int offset, byte datatype) => datatype switch
    {
        1 => (sbyte)p[offset], 2 => p[offset], 3 => BitConverter.ToInt16(p[offset..]), 4 => BitConverter.ToUInt16(p[offset..]),
        5 => BitConverter.ToInt32(p[offset..]), 6 => BitConverter.ToUInt32(p[offset..]), 7 => BitConverter.ToSingle(p[offset..]), 8 => (float)BitConverter.ToDouble(p[offset..]),
        _ => float.NaN,
    };

    /// <summary>sensor_msgs/msg/PointCloud2. Points with non-finite xyz are dropped; <c>intensity</c> or packed <c>rgb</c>/<c>rgba</c> fields colour the cloud.</summary>
    public static PointCloud PointCloud2(CdrReader r)
    {
        var header = Cdr.Header(r);
        uint height = r.ReadUInt32(), width = r.ReadUInt32();
        var nf = r.ReadLength(); var fields = new PointField[nf];
        for (var i = 0; i < nf; i++) fields[i] = new PointField(r.ReadString(), r.ReadUInt32(), r.ReadUInt8(), r.ReadUInt32());
        var bigEndian = r.ReadBool(); uint pointStep = r.ReadUInt32(), rowStep = r.ReadUInt32();
        var data = r.ReadBytes(r.ReadLength());
        if (bigEndian) throw new NotSupportedException("big-endian PointCloud2");
        PointField? F(string n) => fields.FirstOrDefault(f => f.Name == n);
        var fx = F("x") ?? throw new InvalidDataException("PointCloud2 without x"); var fy = F("y") ?? throw new InvalidDataException("PointCloud2 without y"); var fz = F("z");
        var fi = F("intensity"); var frgb = F("rgb") ?? F("rgba");
        var n = (int)(width * height); var step = (int)pointStep;
        var pos = new float[n * 3]; var inten = fi is null ? null : new float[n]; var col = frgb is null ? null : new byte[n * 4];
        var kept = 0;
        for (var i = 0; i < n; i++)
        {
            var row = i / (int)Math.Max(1, width); var colIdx = i % (int)Math.Max(1, width);
            var o = row * (int)rowStep + colIdx * step;
            if (o + step > data.Length) break;
            var p = data.AsSpan(o, step);
            float x = ReadFieldFloat(p, (int)fx.Offset, fx.Datatype), y = ReadFieldFloat(p, (int)fy.Offset, fy.Datatype), z = fz is null ? 0 : ReadFieldFloat(p, (int)fz.Offset, fz.Datatype);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) continue;
            pos[3 * kept] = x; pos[3 * kept + 1] = y; pos[3 * kept + 2] = z;
            if (inten is not null) inten[kept] = ReadFieldFloat(p, (int)fi!.Offset, fi.Datatype);
            if (col is not null) { var c = (int)frgb!.Offset; col[4 * kept] = p[c + 2]; col[4 * kept + 1] = p[c + 1]; col[4 * kept + 2] = p[c]; col[4 * kept + 3] = 255; }
            kept++;
        }
        if (kept < n) { pos = pos[..(kept * 3)]; inten = inten?[..kept]; col = col?[..(kept * 4)]; }
        return new PointCloud(header, pos, inten, col, (int)width, (int)height);
    }

    /// <summary>One stamped transform of a TFMessage: parent and child frames, stamp, translation <c>[x, y, z]</c> and quaternion <c>[x, y, z, w]</c>.</summary>
    public sealed record Transform(string Parent, string Child, double Sec, uint Nanosec, double[] T, double[] Q);
    /// <summary>tf2_msgs/msg/TFMessage → one transform per stamped entry.</summary>
    public static List<Transform> TFMessage(CdrReader r)
    {
        var n = r.ReadLength(); var list = new List<Transform>(n);
        for (var i = 0; i < n; i++)
        {
            var h = Cdr.Header(r); var child = r.ReadString();
            double tx = r.ReadFloat64(), ty = r.ReadFloat64(), tz = r.ReadFloat64(), qx = r.ReadFloat64(), qy = r.ReadFloat64(), qz = r.ReadFloat64(), qw = r.ReadFloat64();
            list.Add(new Transform(h.FrameId, child, h.Sec, h.Nanosec, [tx, ty, tz], [qx, qy, qz, qw]));
        }
        return list;
    }

    /// <summary>A decoded visualization marker: namespace and id, ROS type and action codes, pose, scale, colour as CSS hex with the alpha as <c>Opacity</c>, lifetime in seconds, per-point xyz and RGBA data, and the label text.</summary>
    public sealed record MarkerMsg(string Ns, int Id, int Type, int Action, Cdr.HeaderMsg Header, Cdr.PoseMsg Pose, double[] Scale, string Color, double Opacity, double Lifetime, double[] Points, byte[] Colors, string Text);
    private static string Hex(float r, float g, float b) => $"#{(int)Math.Clamp(r * 255, 0, 255):x2}{(int)Math.Clamp(g * 255, 0, 255):x2}{(int)Math.Clamp(b * 255, 0, 255):x2}";

    /// <summary>visualization_msgs/msg/Marker.</summary>
    public static MarkerMsg Marker(CdrReader r)
    {
        var h = Cdr.Header(r); var ns = r.ReadString(); int id = r.ReadInt32(), type = r.ReadInt32(), action = r.ReadInt32();
        var pose = Cdr.Pose(r);
        double sx = r.ReadFloat64(), sy = r.ReadFloat64(), sz = r.ReadFloat64();
        float cr = r.ReadFloat32(), cg = r.ReadFloat32(), cb = r.ReadFloat32(), ca = r.ReadFloat32();
        int lsec = r.ReadInt32(); uint lnsec = r.ReadUInt32(); r.ReadBool();
        var np = r.ReadLength(); var pts = new double[np * 3];
        for (var i = 0; i < np; i++) { pts[3 * i] = r.ReadFloat64(); pts[3 * i + 1] = r.ReadFloat64(); pts[3 * i + 2] = r.ReadFloat64(); }
        var nc = r.ReadLength(); var cols = new byte[nc * 4];
        for (var i = 0; i < nc; i++) { cols[4 * i] = (byte)Math.Clamp(r.ReadFloat32() * 255, 0, 255); cols[4 * i + 1] = (byte)Math.Clamp(r.ReadFloat32() * 255, 0, 255); cols[4 * i + 2] = (byte)Math.Clamp(r.ReadFloat32() * 255, 0, 255); cols[4 * i + 3] = (byte)Math.Clamp(r.ReadFloat32() * 255, 0, 255); }
        var text = r.ReadString();
        return new MarkerMsg(ns, id, type, action, h, pose, [sx, sy, sz], Hex(cr, cg, cb), ca, lsec + lnsec * 1e-9, pts, cols, text);
    }
    /// <summary>visualization_msgs/msg/MarkerArray.</summary>
    public static List<MarkerMsg> MarkerArray(CdrReader r) { var n = r.ReadLength(); var l = new List<MarkerMsg>(n); for (var i = 0; i < n; i++) l.Add(Marker(r)); return l; }

    /// <summary>ROS marker type constants → SkyScope marker kinds (null = unsupported: mesh resources, triangle lists, cube/sphere lists).</summary>
    public static string? MarkerKind(int type) => type switch { 0 => "arrow", 1 => "cube", 2 => "sphere", 3 => "cylinder", 4 => "lineStrip", 5 => "lineList", 8 => "points", 9 => "text", _ => null };

    /// <summary>nav_msgs/msg/Path → xyz positions.</summary>
    public static (Cdr.HeaderMsg Header, float[] Positions) Path(CdrReader r)
    {
        var h = Cdr.Header(r); var n = r.ReadLength(); var pos = new float[n * 3];
        for (var i = 0; i < n; i++) { Cdr.Header(r); var p = Cdr.Pose(r); pos[3 * i] = (float)p.X; pos[3 * i + 1] = (float)p.Y; pos[3 * i + 2] = (float)p.Z; }
        return (h, pos);
    }
}
