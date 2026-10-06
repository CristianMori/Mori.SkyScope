// Mori.SkyScope — One subscribed topic and how it maps onto signals and layers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Net;
using System.Text.Json;
using Mori.Ros2Sharp;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Sources.Ros2;

/// <summary>One subscribed topic and how it maps onto signals and layers.</summary>
public sealed record Ros2TopicConfig(string Topic, string Type)
{
    /// <summary>Scene layer id for LaserScan / Odometry / Pose / OccupancyGrid topics (default: the topic name).</summary>
    public string? LayerId { get; init; }
    /// <summary>First channel id for the signals this topic produces (default: allocated sequentially).</summary>
    public int? FirstChannelId { get; init; }
    /// <summary>Channel name prefix (default: the topic without the leading slash).</summary>
    public string? Name { get; init; }
    /// <summary>Subscribe with reliable QoS instead of best effort.</summary>
    public bool Reliable { get; init; }
    /// <summary>Request transient-local durability: latched history (what <c>/tf_static</c> and map publishers keep) is delivered on joining. Matches transient-local publishers only, as the DDS durability rule requires.</summary>
    public bool TransientLocal { get; init; }
}

/// <summary>Node identity, discovery settings and topic list for <see cref="Ros2Source"/>.</summary>
/// <param name="NodeName">Name the node announces on the ROS 2 graph.</param>
public sealed record Ros2SourceConfig(string NodeName = "skyscope")
{
    /// <summary>ROS 2 namespace of the node; empty for the root namespace.</summary>
    public string Namespace { get; init; } = "";
    /// <summary>ROS domain id to discover on.</summary>
    public int DomainId { get; init; }
    /// <summary>Topics to subscribe to and how each maps onto signals and layers.</summary>
    public List<Ros2TopicConfig> Topics { get; init; } = [];
    /// <summary>Unicast peers to add (e.g. a robot on another subnet).</summary>
    public IPAddress[] Peers { get; init; } = [];
    /// <summary>Marshals sink calls onto a UI thread; null = call on the receive thread.</summary>
    public Action<Action>? Dispatch { get; init; }
    /// <summary>Use the message header stamp as chart time when present.</summary>
    public bool UseHeaderTime { get; init; } = true;
    /// <summary>Where sequential channel ids start.</summary>
    public int NextChannelId { get; init; } = 1;
    /// <summary>Emit 3D layer kinds for LaserScan, Odometry/Pose and OccupancyGrid (laserScan3d, pose3d, occupancyGrid3d) instead of the 2D ones. PointCloud2, TF, markers and paths are always 3D.</summary>
    public bool Scene3D { get; init; }
    /// <summary>A URDF robot description to show (visuals as markers, joints from JointState topics). Frame names match the URDF links, as tf publishes them.</summary>
    public string? Urdf { get; init; }
    /// <summary>Folders that resolve <c>package://name/…</c> mesh URIs of the URDF (package name → directory); meshes found are pushed to browsers.</summary>
    public Dictionary<string, string> MeshRoots { get; init; } = [];
    /// <summary>
    /// RTPS fragment size in bytes for samples that do not fit one datagram (the participant's <c>FragmentSize</c>).
    /// Null keeps Mori.Ros2Sharp's default of 64,000, which matches Fast DDS and is accepted by Cyclone DDS; about 1,400
    /// keeps every datagram under the Ethernet MTU on links where IP fragmentation is a problem. Incoming fragments are
    /// reassembled whatever the publisher's size, so this only shapes what this node sends.
    /// </summary>
    public int? FragmentSize { get; init; }
}

/// <summary>
/// ROS 2 source plugin: subscribes through <see cref="Ros2Node"/> (Mori.Ros2Sharp, pure managed RTPS — no ROS install)
/// and decodes the standard messages by hand from CDR. Scalars, arrays, Twist, Imu, BatteryState and JointState become
/// signals; LaserScan, Odometry, Pose(Stamped) and OccupancyGrid become scene layers (Odometry also yields signals).
/// Large payloads (maps, point clouds) arrive through RTPS fragmentation, reassembled by Mori.Ros2Sharp.
/// </summary>
public sealed class Ros2Source : ISource, IDisposable
{
    /// <summary>Source type key (<c>ros2</c>).</summary>
    public string Type => "ros2";
    private Ros2Node? _node;
    private SourceContext? _ctx;
    private Ros2SourceConfig _cfg = new();
    private int _nextChannel;
    private uint _seq;
    private readonly HashSet<string> _declaredLayers = [];
    private readonly Dictionary<string, object> _staticTransforms = [];
    private Action<IReadOnlyDictionary<string, double>, double?>? _robot;
    private readonly Dictionary<string, double> _jointPositions = [];
    private readonly Dictionary<string, int[]> _jointChannels = [];
    /// <summary>Count of messages received over all subscriptions.</summary>
    public long MessagesReceived;
    /// <summary>Count of messages whose CDR payload failed to decode; each one is logged and skipped.</summary>
    public long DecodeErrors;

    /// <summary>Creates the node, adds the unicast peers, allocates channels and a subscription for every configured topic, publishes the URDF robot (markers and meshes) when one is configured and starts discovery. A missing config uses the defaults.</summary>
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        _ctx = ctx; _cfg = config as Ros2SourceConfig ?? new Ros2SourceConfig(); _nextChannel = _cfg.NextChannelId;
        _node = new Ros2Node(_cfg.NodeName, _cfg.Namespace, _cfg.DomainId);
        if (_cfg.FragmentSize is { } fragment) _node.Participant.FragmentSize = fragment;
        foreach (var peer in _cfg.Peers) _node.AddPeer(peer);
        foreach (var t in _cfg.Topics)
        {
            var topic = t; var ids = AllocateChannels(topic);
            var sub = _node.CreateSubscription(topic.Topic, topic.Type, topic.Reliable, topic.TransientLocal);
            sub.DataReceived += (_, payload, _) => OnMessage(topic, ids, payload);
        }
        if (_cfg.Urdf is { } urdf)
        {
            var model = Core.Scene3D.Urdf.Parse(urdf);
            var meshes = Core.Scene3D.Urdf.PublishMeshes(ctx.Layers, model, uri => ResolveMesh(uri, ctx));
            _robot = Core.Scene3D.Urdf.Publish(ctx.Layers, model);
            ctx.Log(LogLevel.Info, $"ros2: robot model '{model.Name}' with {model.Links.Count} links, {model.Joints.Count} joints, {meshes} mesh resources");
        }
        _node.Start();
        ctx.Log(LogLevel.Info, $"ros2: node {_cfg.NodeName} started, {_cfg.Topics.Count} subscriptions");
        return Task.CompletedTask;
    }

    /// <summary>Disposes the node, which ends every subscription.</summary>
    public Task StopAsync() { _node?.Dispose(); _node = null; return Task.CompletedTask; }
    /// <summary>Same as <see cref="StopAsync"/>.</summary>
    public void Dispose() => _node?.Dispose();

    private byte[]? ResolveMesh(string uri, SourceContext ctx)
    {
        // package://pkg/path → MeshRoots[pkg]/path; file:// and plain paths as given
        string? path = null;
        if (uri.StartsWith("package://", StringComparison.Ordinal)) { var rest = uri["package://".Length..]; var slash = rest.IndexOf('/'); if (slash > 0 && _cfg.MeshRoots.TryGetValue(rest[..slash], out var root)) path = System.IO.Path.Combine(root, rest[(slash + 1)..]); }
        else if (uri.StartsWith("file://", StringComparison.Ordinal)) path = uri["file://".Length..];
        else path = uri;
        if (path is null || !File.Exists(path)) { ctx.Log(LogLevel.Warn, $"ros2: mesh not found: {uri}"); return null; }
        return File.ReadAllBytes(path);
    }

    private static string Prefix(Ros2TopicConfig t) => t.Name ?? t.Topic.TrimStart('/').Replace('/', '.');

    /// <summary>Declares the channels a topic type is known to produce and returns their ids.</summary>
    private int[] AllocateChannels(Ros2TopicConfig t)
    {
        string[] names = t.Type switch
        {
            "std_msgs/msg/Float64" or "std_msgs/msg/Float32" or "std_msgs/msg/Int32" or "std_msgs/msg/Int16" or "std_msgs/msg/UInt8" or "std_msgs/msg/Bool" or "std_msgs/msg/Int64" => [""],
            "geometry_msgs/msg/Twist" or "geometry_msgs/msg/TwistStamped" => ["linear.x", "linear.y", "linear.z", "angular.x", "angular.y", "angular.z"],
            "nav_msgs/msg/Odometry" => ["x", "y", "yaw", "vx", "vy", "wz"],
            "geometry_msgs/msg/PoseStamped" or "geometry_msgs/msg/Pose" => ["x", "y", "yaw"],
            "sensor_msgs/msg/Imu" => ["accel.x", "accel.y", "accel.z", "gyro.x", "gyro.y", "gyro.z", "roll", "pitch", "yaw"],
            "sensor_msgs/msg/BatteryState" => ["voltage", "current", "percentage"],
            _ => [],
        };
        var ids = new int[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            ids[i] = (t.FirstChannelId ?? _nextChannel) + i;
            _ctx!.Signals.DeclareChannel(new ChannelInfo(ids[i], names[i].Length == 0 ? Prefix(t) : $"{Prefix(t)}.{names[i]}") { Timing = ChannelTiming.Timestamped });
        }
        if (t.FirstChannelId is null) _nextChannel += names.Length;
        else _nextChannel = Math.Max(_nextChannel, t.FirstChannelId.Value + names.Length);
        return ids;
    }

    private void OnMessage(Ros2TopicConfig t, int[] ids, byte[] payload)
    {
        Interlocked.Increment(ref MessagesReceived);
        try
        {
            var r = new CdrReader(payload);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
            Action apply = t.Type switch
            {
                "std_msgs/msg/Float64" => Scalar(ids, now, r.ReadFloat64()),
                "std_msgs/msg/Float32" => Scalar(ids, now, r.ReadFloat32()),
                "std_msgs/msg/Int32" => Scalar(ids, now, r.ReadInt32()),
                "std_msgs/msg/Int64" => Scalar(ids, now, r.ReadInt64()),
                "std_msgs/msg/Int16" => Scalar(ids, now, r.ReadInt16()),
                "std_msgs/msg/UInt8" => Scalar(ids, now, r.ReadUInt8()),
                "std_msgs/msg/Bool" => Scalar(ids, now, r.ReadBool() ? 1 : 0),
                "std_msgs/msg/Float64MultiArray" => MultiArray(t, now, Cdr.Float64MultiArray(r)),
                "std_msgs/msg/Float32MultiArray" => MultiArray(t, now, Cdr.Float32MultiArray(r)),
                "geometry_msgs/msg/Twist" => Values(ids, now, Cdr.Twist(r)),
                "geometry_msgs/msg/TwistStamped" => Values(ids, Stamp(Cdr.Header(r), now), Cdr.Twist(r)),
                "geometry_msgs/msg/Pose" => PoseMsg(t, ids, now, Cdr.Pose(r)),
                "geometry_msgs/msg/PoseStamped" => PoseMsg(t, ids, Stamp(Cdr.Header(r), now), Cdr.Pose(r)),
                "nav_msgs/msg/Odometry" => Odometry(t, ids, r, now),
                "sensor_msgs/msg/Imu" => Imu(ids, r, now),
                "sensor_msgs/msg/BatteryState" => Battery(ids, r, now),
                "sensor_msgs/msg/JointState" => JointState(t, r, now),
                "sensor_msgs/msg/LaserScan" => LaserScan(t, r),
                "nav_msgs/msg/OccupancyGrid" => OccupancyGrid(t, r),
                "sensor_msgs/msg/PointCloud2" => PointCloud2(t, r, now),
                "tf2_msgs/msg/TFMessage" => TF(t, r),
                "visualization_msgs/msg/Marker" => Markers(t, [Cdr3D.Marker(r)], now),
                "visualization_msgs/msg/MarkerArray" => Markers(t, Cdr3D.MarkerArray(r), now),
                "nav_msgs/msg/Path" => Path(t, r, now),
                _ => () => _ctx!.Log(LogLevel.Warn, $"ros2: no decoder for {t.Type} ({t.Topic})"),
            };
            if (_cfg.Dispatch is { } d) d(apply); else apply();
        }
        catch (Exception e)
        {
            if (Interlocked.Increment(ref DecodeErrors) <= 3) _ctx!.Log(LogLevel.Warn, $"ros2: {t.Topic}: {e.Message}");
        }
    }

    private double Stamp(Cdr.HeaderMsg h, double fallback) => _cfg.UseHeaderTime && (h.Sec != 0 || h.Nanosec != 0) ? h.Sec + h.Nanosec * 1e-9 : fallback;

    private void Push(int[] ids, double t, ReadOnlySpan<double> values)
    {
        var n = Math.Min(ids.Length, values.Length); var channels = new List<FrameChannel>(n);
        for (var i = 0; i < n; i++) channels.Add(FrameChannel.Timestamped((ushort)ids[i], [t], [(float)values[i]]));
        _ctx!.Signals.PushFrame(new SkyScopeFrame(_seq++, t, channels));
    }
    private Action Scalar(int[] ids, double t, double v) => () => Push(ids, t, [v]);
    private Action Values(int[] ids, double t, double[] v) => () => Push(ids, t, v);

    private Action MultiArray(Ros2TopicConfig t, double now, double[] data) => () =>
    {
        // Channels are declared on first sight because the array length is only known from the data.
        if (!_jointChannels.TryGetValue(t.Topic, out var ids) || ids.Length < data.Length)
        {
            var start = ids is { Length: > 0 } ? ids[0] : t.FirstChannelId ?? _nextChannel;
            ids = Enumerable.Range(start, data.Length).ToArray();
            for (var i = 0; i < data.Length; i++) _ctx!.Signals.DeclareChannel(new ChannelInfo(ids[i], $"{Prefix(t)}[{i}]") { Timing = ChannelTiming.Timestamped });
            _nextChannel = Math.Max(_nextChannel, start + data.Length);
            _jointChannels[t.Topic] = ids;
        }
        Push(ids, now, data);
    };

    private Action JointState(Ros2TopicConfig t, CdrReader r, double now)
    {
        var stamp = Stamp(Cdr.Header(r), now);
        var n = r.ReadLength(); var names = new string[n]; for (var i = 0; i < n; i++) names[i] = r.ReadString();
        var pos = Cdr.Float64Seq(r);
        return () =>
        {
            if (!_jointChannels.TryGetValue(t.Topic, out var ids) || ids.Length < names.Length)
            {
                var start = t.FirstChannelId ?? _nextChannel;
                ids = Enumerable.Range(start, names.Length).ToArray();
                for (var i = 0; i < names.Length; i++) _ctx!.Signals.DeclareChannel(new ChannelInfo(ids[i], $"{Prefix(t)}.{names[i]}") { Unit = "rad", Timing = ChannelTiming.Timestamped });
                _nextChannel = Math.Max(_nextChannel, start + names.Length);
                _jointChannels[t.Topic] = ids;
            }
            Push(ids, stamp, pos);
            if (_robot is not null)
            {
                for (var i = 0; i < names.Length && i < pos.Length; i++) _jointPositions[names[i]] = pos[i];
                _robot(_jointPositions, stamp);
            }
        };
    }

    private Action PoseMsg(Ros2TopicConfig t, int[] ids, double stamp, Cdr.PoseMsg p) => () =>
    {
        var yaw = Cdr.Yaw(p.Qx, p.Qy, p.Qz, p.Qw);
        if (_cfg.Scene3D) Layer(t, "pose3d", new { label = Prefix(t) }, new { t = new[] { p.X, p.Y, p.Z }, q = new[] { p.Qx, p.Qy, p.Qz, p.Qw }, stamp });
        else Layer(t, "pose", new { label = Prefix(t) }, new { x = p.X, y = p.Y, yaw });
        Push(ids, stamp, [p.X, p.Y, yaw]);
    };

    private Action Odometry(Ros2TopicConfig t, int[] ids, CdrReader r, double now)
    {
        var h = Cdr.Header(r); var stamp = Stamp(h, now); var frameId = h.FrameId; r.ReadString();
        var p = Cdr.Pose(r); Cdr.Skip(r, 36); var tw = Cdr.Twist(r);
        return () =>
        {
            var yaw = Cdr.Yaw(p.Qx, p.Qy, p.Qz, p.Qw);
            if (_cfg.Scene3D) Layer(t, "pose3d", new { label = Prefix(t), frame = frameId }, new { t = new[] { p.X, p.Y, p.Z }, q = new[] { p.Qx, p.Qy, p.Qz, p.Qw }, stamp });
            else Layer(t, "pose", new { label = Prefix(t) }, new { x = p.X, y = p.Y, yaw });
            Push(ids, stamp, [p.X, p.Y, yaw, tw[0], tw[1], tw[5]]);
        };
    }

    private Action Imu(int[] ids, CdrReader r, double now)
    {
        var stamp = Stamp(Cdr.Header(r), now);
        double qx = r.ReadFloat64(), qy = r.ReadFloat64(), qz = r.ReadFloat64(), qw = r.ReadFloat64(); Cdr.Skip(r, 9);
        double gx = r.ReadFloat64(), gy = r.ReadFloat64(), gz = r.ReadFloat64(); Cdr.Skip(r, 9);
        double ax = r.ReadFloat64(), ay = r.ReadFloat64(), az = r.ReadFloat64();
        var (roll, pitch, yaw) = Cdr.Euler(qx, qy, qz, qw);
        return () => Push(ids, stamp, [ax, ay, az, gx, gy, gz, roll, pitch, yaw]);
    }

    private Action Battery(int[] ids, CdrReader r, double now)
    {
        var stamp = Stamp(Cdr.Header(r), now);
        double v = r.ReadFloat32(), temp = r.ReadFloat32(), cur = r.ReadFloat32(); _ = temp;
        r.ReadFloat32(); r.ReadFloat32(); r.ReadFloat32(); var pct = r.ReadFloat32();
        return () => Push(ids, stamp, [v, cur, pct]);
    }

    private Action LaserScan(Ros2TopicConfig t, CdrReader r)
    {
        var h = Cdr.Header(r);
        float angleMin = r.ReadFloat32(), angleMax = r.ReadFloat32(), angleInc = r.ReadFloat32(), timeInc = r.ReadFloat32(), scanTime = r.ReadFloat32(), rangeMin = r.ReadFloat32(), rangeMax = r.ReadFloat32();
        _ = angleMax; _ = timeInc; _ = scanTime;
        var ranges = Cdr.Float32Seq(r); var intensities = r.Remaining >= 4 ? Cdr.Float32Seq(r) : [];
        // Infinite / NaN readings cannot travel as JSON; anything beyond range_max is dropped by the consumer anyway.
        for (var i = 0; i < ranges.Length; i++) if (!float.IsFinite(ranges[i])) ranges[i] = rangeMax + 1;
        if (_cfg.Scene3D) return () => Layer(t, "laserScan3d", new { pointSize = 3, frame = h.FrameId }, new { scan = new { angleMin, angleIncrement = angleInc, ranges, rangeMin, rangeMax }, stamp = Stamp(h, 0) });
        return () => Layer(t, "pointCloud", new { pointSize = 2, color = "#dc2626" }, new { scan = new { angleMin, angleIncrement = angleInc, ranges, rangeMin, rangeMax }, intensities });
    }

    private Action OccupancyGrid(Ros2TopicConfig t, CdrReader r)
    {
        var hdr = Cdr.Header(r); r.ReadInt32(); r.ReadUInt32();
        float res = r.ReadFloat32(); uint w = r.ReadUInt32(), h = r.ReadUInt32(); var origin = Cdr.Pose(r);
        var n = r.ReadLength(); var data = new double[n]; for (var i = 0; i < n; i++) data[i] = r.ReadInt8();
        return () =>
        {
            var id = t.LayerId ?? t.Topic;
            _declaredLayers.Remove(id);
            var meta = new { width = (int)w, height = (int)h, resolution = res, origin = new { x = origin.X, y = origin.Y, yaw = Cdr.Yaw(origin.Qx, origin.Qy, origin.Qz, origin.Qw) }, frame = hdr.FrameId, z = origin.Z };
            Layer(t, _cfg.Scene3D ? "occupancyGrid3d" : "occupancyGrid", meta, new { data });
        };
    }

    private Action PointCloud2(Ros2TopicConfig t, CdrReader r, double now)
    {
        var c = Cdr3D.PointCloud2(r); var stamp = Stamp(c.Header, now);
        var payload = new Dictionary<string, object?> { ["positions"] = c.Positions, ["stamp"] = stamp };
        if (c.Colors is not null) payload["colors"] = c.Colors; else if (c.Intensities is not null) payload["intensities"] = c.Intensities;
        return () => Layer(t, "pointCloud3d", new { frame = c.Header.FrameId, pointSize = 2 }, payload);
    }
    private Action TF(Ros2TopicConfig t, CdrReader r)
    {
        var list = Cdr3D.TFMessage(r);
        var isStatic = t.Topic.EndsWith("_static", StringComparison.Ordinal);
        var transforms = list.Select(x => new { child = x.Child, parent = x.Parent, t = x.T, q = x.Q, time = isStatic ? (double?)null : x.Sec + x.Nanosec * 1e-9 }).ToArray();
        var id = t.LayerId ?? "tf";
        return () =>
        {
            if (isStatic)
            {
                // static transforms live in the declaration, which relays and recorders replay to late joiners
                foreach (var x in transforms) _staticTransforms[x.child] = x;
                _declaredLayers.Add(id);
                _ctx!.Layers.DeclareLayer(id, "frames", Core.Scene.LayerJson.ToMeta(new { transforms = _staticTransforms.Values.ToArray() }));
                return;
            }
            if (_declaredLayers.Add(id)) _ctx!.Layers.DeclareLayer(id, "frames", Core.Scene.LayerJson.ToMeta(new { transforms = _staticTransforms.Values.ToArray() }));
            _ctx!.Layers.Push(id, new { transforms });
        };
    }
    private Action Markers(Ros2TopicConfig t, List<Cdr3D.MarkerMsg> list, double now)
    {
        var ops = new List<object>();
        foreach (var m in list)
        {
            var id = $"{m.Ns}/{m.Id}";
            if (m.Action == 3) { ops.Add(new { clear = true }); continue; }
            if (m.Action == 2) { ops.Add(new { remove = id }); continue; }
            var kind = Cdr3D.MarkerKind(m.Type);
            if (kind is null) continue;
            ops.Add(new
            {
                upsert = new { id, type = kind, frame = m.Header.FrameId, position = new[] { m.Pose.X, m.Pose.Y, m.Pose.Z }, orientation = new[] { m.Pose.Qx, m.Pose.Qy, m.Pose.Qz, m.Pose.Qw }, scale = m.Scale, color = m.Color, opacity = m.Opacity, points = m.Points, colors = m.Colors.Length > 0 ? m.Colors.Select(b => (int)b).ToArray() : null, text = m.Text.Length > 0 ? m.Text : null, lifetime = m.Lifetime, stamp = Stamp(m.Header, now) },
                now,
            });
        }
        return () => { foreach (var op in ops) Layer(t, "markers", new { }, op); };
    }
    private Action Path(Ros2TopicConfig t, CdrReader r, double now)
    {
        var (h, pos) = Cdr3D.Path(r);
        var payload = new Dictionary<string, object?> { ["positions"] = pos, ["stamp"] = Stamp(h, now) };
        return () => Layer(t, "path3d", new { frame = h.FrameId, color = "#22d3ee" }, payload);
    }

    private void Layer(Ros2TopicConfig t, string kind, object meta, object payload)
    {
        var id = t.LayerId ?? t.Topic;
        if (_declaredLayers.Add(id)) _ctx!.Layers.DeclareLayer(id, kind, Core.Scene.LayerJson.ToMeta(meta));
        _ctx!.Layers.Push(id, payload);
    }
}

/// <summary>Hand-written CDR decoders for the standard ROS 2 messages this plugin understands.</summary>
public static class Cdr
{
    /// <summary>Decoded <c>std_msgs/Header</c>: stamp seconds and nanoseconds, and the frame id.</summary>
    public readonly record struct HeaderMsg(int Sec, uint Nanosec, string FrameId);
    /// <summary>Decoded <c>geometry_msgs/Pose</c>: position and orientation quaternion.</summary>
    public readonly record struct PoseMsg(double X, double Y, double Z, double Qx, double Qy, double Qz, double Qw);

    /// <summary>Reads a <c>std_msgs/Header</c>.</summary>
    public static HeaderMsg Header(CdrReader r) => new(r.ReadInt32(), r.ReadUInt32(), r.ReadString());
    /// <summary>Reads a <c>geometry_msgs/Pose</c>: three float64 for the position, four for the quaternion.</summary>
    public static PoseMsg Pose(CdrReader r) => new(r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64());
    /// <summary>Twist as [lx, ly, lz, ax, ay, az].</summary>
    public static double[] Twist(CdrReader r) => [r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64(), r.ReadFloat64()];
    /// <summary>Skips <paramref name="float64s"/> consecutive float64 fields, for example a covariance block.</summary>
    public static void Skip(CdrReader r, int float64s) { for (var i = 0; i < float64s; i++) r.ReadFloat64(); }
    /// <summary>Reads a length-prefixed float64 sequence.</summary>
    public static double[] Float64Seq(CdrReader r) { var n = r.ReadLength(); var a = new double[n]; for (var i = 0; i < n; i++) a[i] = r.ReadFloat64(); return a; }
    /// <summary>Reads a length-prefixed float32 sequence.</summary>
    public static float[] Float32Seq(CdrReader r) { var n = r.ReadLength(); var a = new float[n]; for (var i = 0; i < n; i++) a[i] = r.ReadFloat32(); return a; }
    private static void Layout(CdrReader r) { var dims = r.ReadLength(); for (var i = 0; i < dims; i++) { r.ReadString(); r.ReadUInt32(); r.ReadUInt32(); } r.ReadUInt32(); }
    /// <summary>Reads a <c>std_msgs/Float64MultiArray</c>, discarding its layout.</summary>
    public static double[] Float64MultiArray(CdrReader r) { Layout(r); return Float64Seq(r); }
    /// <summary>Reads a <c>std_msgs/Float32MultiArray</c> as doubles, discarding its layout.</summary>
    public static double[] Float32MultiArray(CdrReader r) { Layout(r); return Float32Seq(r).Select(v => (double)v).ToArray(); }

    /// <summary>Yaw (rotation about Z) of a quaternion, in radians.</summary>
    public static double Yaw(double qx, double qy, double qz, double qw) => Math.Atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz));
    /// <summary>Roll, pitch and yaw of a quaternion in radians; pitch is clamped to plus or minus 90 degrees at the gimbal-lock singularity.</summary>
    public static (double Roll, double Pitch, double Yaw) Euler(double qx, double qy, double qz, double qw)
    {
        var roll = Math.Atan2(2 * (qw * qx + qy * qz), 1 - 2 * (qx * qx + qy * qy));
        var sp = 2 * (qw * qy - qz * qx);
        var pitch = Math.Abs(sp) >= 1 ? Math.CopySign(Math.PI / 2, sp) : Math.Asin(sp);
        return (roll, pitch, Yaw(qx, qy, qz, qw));
    }
}
