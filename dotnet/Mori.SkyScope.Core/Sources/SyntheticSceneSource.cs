// Mori.SkyScope — Synthetic 2D robot scene for demos: map, lidar, driving robot, trail and zones, pose also streamed as channels.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Sources;

/// <summary>Configuration object for <see cref="SyntheticSceneSource.StartAsync"/>; the defaults give a 16 × 10 m map at 10 Hz.</summary>
public sealed record SyntheticSceneConfig
{
    /// <summary>Pose / scan updates per second.</summary>
    public double RateHz { get; init; } = 10;
    /// <summary>Map columns.</summary>
    public int MapWidth { get; init; } = 64;
    /// <summary>Map rows.</summary>
    public int MapHeight { get; init; } = 40;
    /// <summary>Metres per cell.</summary>
    public double Resolution { get; init; } = 0.25;
    /// <summary>Beams per lidar scan, spread over a full turn.</summary>
    public int ScanBeams { get; init; } = 240;
    /// <summary>Maximum lidar range in metres.</summary>
    public double ScanRange { get; init; } = 8;
    /// <summary>Seed for the random obstacles of <see cref="SyntheticSceneSource.BuildMap"/>.</summary>
    public int Seed { get; init; } = 1;
    /// <summary>First channel id for the pose signals (x, y, yaw, speed).</summary>
    public int FirstChannelId { get; init; } = 100;
    /// <summary>Marshals sink calls onto a UI thread; null = timer thread.</summary>
    public Action<Action>? Dispatch { get; init; }
    /// <summary>Run the internal timer; false when a host steps the simulation itself.</summary>
    public bool UseTimer { get; init; } = true;
}

/// <summary>
/// A synthetic robot for demos and tests: an occupancy-grid map with walls and obstacles, a robot driving a loop, a
/// lidar scan ray-cast against the map, a trail, and annotation shapes — pushed through the layer sink, with the
/// pose also published as signals. Feeds the demo server (relayed to browsers), the WPF sample and snapshots.
/// </summary>
public sealed class SyntheticSceneSource : ISource, IDisposable
{
    /// <summary><c>"synthetic-scene"</c>.</summary>
    public string Type => "synthetic-scene";
    private SourceContext? _ctx;
    private SyntheticSceneConfig _cfg = new();
    private sbyte[] _map = [];
    private Timer? _timer;
    private double _t;
    private uint _seq;
    private DateTime _last;
    /// <summary>The robot pose after the last <see cref="Step"/>, metres and radians.</summary>
    public Pose2D Pose { get; private set; }

    /// <summary>Build the map, declare the layers (map, zones, scan, trail, pose) and the four pose channels, then, unless <see cref="SyntheticSceneConfig.UseTimer"/> is off, start a timer that calls <see cref="Step"/> at the configured rate.</summary>
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        _ctx = ctx; _cfg = config as SyntheticSceneConfig ?? new SyntheticSceneConfig();
        _map = BuildMap(_cfg);
        var c = _cfg;
        Emit(() =>
        {
            ctx.Layers.DeclareLayer("map", "occupancyGrid", LayerJson.ToMeta(new { width = c.MapWidth, height = c.MapHeight, resolution = c.Resolution, origin = new { x = 0, y = 0, yaw = 0 }, data = _map.Select(v => (int)v).ToArray(), unknownOpacity = 0.6 }));
            ctx.Layers.DeclareLayer("zones", "shapes", LayerJson.ToMeta(new
            {
                shapes = new object[]
                {
                    new { id = "dock", kind = "rect", x = 1.0, y = 1.0, w = 2.0, h = 1.5, fill = new { color = "#16a34a", opacity = 0.2 }, stroke = new { color = "#16a34a", width = 1.5 }, label = "dock" },
                    new { id = "nogo", kind = "polygon", points = new[] { 11.0, 7.0, 14.0, 7.0, 14.0, 9.0, 12.0, 9.5 }, fill = new { color = "#dc2626", opacity = 0.15 }, stroke = new { color = "#dc2626", width = 1.5, dash = new[] { 4.0, 3.0 } }, label = "no-go" },
                    new { id = "goal", kind = "circle", x = 13.0, y = 2.5, r = 0.5, stroke = new { color = "#d97706", width = 2 }, label = "goal" },
                }
            }));
            ctx.Layers.DeclareLayer("scan", "pointCloud", LayerJson.ToMeta(new { pointSize = 2.5, color = "#dc2626" }));
            ctx.Layers.DeclareLayer("trail", "trail", LayerJson.ToMeta(new { maxPoints = 400, stroke = new { color = "#16a34a", width = 2, opacity = 0.8 } }));
            ctx.Layers.DeclareLayer("bot", "pose", LayerJson.ToMeta(new { pose = new { x = 3, y = 3, yaw = 0 }, footprint = new[] { -0.35, -0.25, 0.35, -0.25, 0.45, 0, 0.35, 0.25, -0.35, 0.25 }, label = "amr-1", color = "#2563eb", arrowSize = 12 }));
            string[] names = ["x", "y", "yaw", "speed"]; string[] units = ["m", "m", "rad", "m/s"];
            for (var i = 0; i < 4; i++) ctx.Signals.DeclareChannel(new ChannelInfo(c.FirstChannelId + i, "amr-1." + names[i]) { Unit = units[i], Timing = ChannelTiming.Timestamped });
        });
        _last = DateTime.UtcNow;
        if (_cfg.UseTimer) _timer = new Timer(_ => { var now = DateTime.UtcNow; var dt = Math.Min(0.5, (now - _last).TotalSeconds); _last = now; Step(dt); }, null, 0, (int)(1000 / _cfg.RateHz));
        return Task.CompletedTask;
    }

    /// <summary>Stop the timer and detach from the context; declared layers stay in the sink.</summary>
    public Task StopAsync() { _timer?.Dispose(); _timer = null; _ctx = null; return Task.CompletedTask; }
    /// <summary>Stops the timer.</summary>
    public void Dispose() => _timer?.Dispose();

    private void Emit(Action a) { if (_cfg.Dispatch is { } d) d(a); else a(); }

    /// <summary>Advance the simulation by <paramref name="dt"/> seconds and push one update (called by the timer or by a host).</summary>
    public void Step(double dt)
    {
        if (_ctx is null) return;
        _t += dt;
        var c = _cfg;
        double w = c.MapWidth * c.Resolution, h = c.MapHeight * c.Resolution;
        // A rounded-rectangle loop inside the walls, traversed at ~0.6 m/s.
        double cx = w / 2, cy = h / 2, rx = w * 0.32, ry = h * 0.3, omega = 0.6 / Math.Max(rx, ry);
        var a = _t * omega;
        double x = cx + rx * Math.Cos(a), y = cy + ry * Math.Sin(a);
        double vx = -rx * omega * Math.Sin(a), vy = ry * omega * Math.Cos(a);
        var yaw = Math.Atan2(vy, vx);
        Pose = new Pose2D(x, y, yaw);
        var ranges = new double[c.ScanBeams]; var angleInc = 2 * Math.PI / c.ScanBeams;
        for (var i = 0; i < c.ScanBeams; i++) ranges[i] = CastRay(x, y, yaw - Math.PI + i * angleInc, c.ScanRange);
        var speed = Math.Sqrt(vx * vx + vy * vy);
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        Emit(() =>
        {
            var layers = _ctx.Layers;
            layers.Push("bot", new { x, y, yaw });
            layers.Push("trail", new { append = new[] { x, y } });
            layers.Push("scan", new { scan = new { angleMin = -Math.PI, angleIncrement = angleInc, ranges, rangeMin = 0.1, rangeMax = c.ScanRange }, pose = new { x, y, yaw } });
            var ids = c.FirstChannelId;
            _ctx.Signals.PushFrame(new SkyScopeFrame(_seq++, stamp, [
                FrameChannel.Timestamped((ushort)ids, [stamp], [(float)x]), FrameChannel.Timestamped((ushort)(ids + 1), [stamp], [(float)y]),
                FrameChannel.Timestamped((ushort)(ids + 2), [stamp], [(float)yaw]), FrameChannel.Timestamped((ushort)(ids + 3), [stamp], [(float)speed])]));
        });
    }

    /// <summary>Distance along a ray until an occupied cell or the map edge (DDA in 5 cm steps); misses report <c>max + 1</c> (beyond range_max, dropped by consumers, JSON-safe).</summary>
    private double CastRay(double x, double y, double angle, double max)
    {
        var c = _cfg; double dx = Math.Cos(angle), dy = Math.Sin(angle);
        const double step = 0.05;
        for (var d = 0.0; d < max; d += step)
        {
            double px = x + dx * d, py = y + dy * d;
            int col = (int)Math.Floor(px / c.Resolution), row = (int)Math.Floor(py / c.Resolution);
            if (col < 0 || row < 0 || col >= c.MapWidth || row >= c.MapHeight) return d;
            if (_map[row * c.MapWidth + col] >= 50) return d;
        }
        return max + 1;
    }

    /// <summary>Walls around the edge, a few obstacles, an unknown patch; row 0 is the bottom.</summary>
    public static sbyte[] BuildMap(SyntheticSceneConfig c)
    {
        var w = c.MapWidth; var h = c.MapHeight; var m = new sbyte[w * h];
        var rng = new Random(c.Seed);
        for (var r = 0; r < h; r++) for (var col = 0; col < w; col++) m[r * w + col] = (sbyte)(r == 0 || col == 0 || r == h - 1 || col == w - 1 ? 100 : 0);
        void Block(double x0, double y0, double x1, double y1, int v)
        {
            for (var r = (int)(y0 / c.Resolution); r < (int)(y1 / c.Resolution) && r < h; r++)
                for (var col = (int)(x0 / c.Resolution); col < (int)(x1 / c.Resolution) && col < w; col++) if (r >= 0 && col >= 0) m[r * w + col] = (sbyte)v;
        }
        double W = w * c.Resolution, H = h * c.Resolution;
        Block(W * 0.42, H * 0.42, W * 0.58, H * 0.58, 100);           // a pillar in the middle of the loop
        Block(W * 0.05, H * 0.55, W * 0.15, H * 0.95, 100);           // a rack near the left wall
        Block(W * 0.85, H * 0.05, W * 0.95, H * 0.35, 100);           // a rack near the right wall
        Block(W * 0.7, H * 0.75, W * 0.98, H * 0.98, -1);             // unexplored corner
        for (var i = 0; i < 6; i++) { var x = W * (0.2 + 0.6 * rng.NextDouble()); var y = H * (0.2 + 0.6 * rng.NextDouble()); Block(x, y, x + 0.5, y + 0.5, 80); }
        return m;
    }
}

/// <summary>Factory for <see cref="SyntheticSceneSource"/>.</summary>
[SkyScopeSource]
public sealed class SyntheticSceneSourceFactory : ISourceFactory
{
    /// <summary><c>"synthetic-scene"</c>.</summary>
    public string Type => "synthetic-scene";
    /// <summary>Name for pickers.</summary>
    public string DisplayName => "Synthetic robot scene";
    /// <summary>Layers plus the pose signals.</summary>
    public SourceCapabilities Capabilities => SourceCapabilities.Signals | SourceCapabilities.Layers;
    /// <summary>None: the configuration is a <see cref="SyntheticSceneConfig"/> object.</summary>
    public string? ConfigSchema => null;
    /// <summary>A new, not yet started source.</summary>
    public ISource Create() => new SyntheticSceneSource();
}
