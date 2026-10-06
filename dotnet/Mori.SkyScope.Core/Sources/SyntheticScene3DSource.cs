// Mori.SkyScope — Synthetic 3D scene for demos: frame tree, spinning lidar cloud, trail, pose, markers and a two-joint arm.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;

namespace Mori.SkyScope.Core.Sources;

/// <summary>Configuration object for <see cref="SyntheticScene3DSource.StartAsync"/>; the defaults give a 10 Hz, 2000-point demo.</summary>
public sealed record SyntheticScene3DConfig
{
    /// <summary>Cloud / pose updates per second.</summary>
    public double RateHz { get; init; } = 10;
    /// <summary>Points in each lidar cloud.</summary>
    public int CloudPoints { get; init; } = 2000;
    /// <summary>Marshals sink calls onto a UI thread; null = timer thread.</summary>
    public Action<Action>? Dispatch { get; init; }
    /// <summary>Run the internal timer; false when a host steps the simulation itself with <see cref="SyntheticScene3DSource.Step"/>.</summary>
    public bool UseTimer { get; init; } = true;
}

/// <summary>
/// A synthetic 3D scene for demos and tests, pushed through the layer sink in the 3D layer contract: a frame tree
/// (map → base_link → laser), a spinning lidar cloud as a binary <c>pointCloud3d</c> push, a trail (<c>path3d</c>),
/// a labelled <c>pose3d</c> and a marker set. Feeds the demo server (relayed as binary layer messages), samples and MCAP.
/// </summary>
public sealed class SyntheticScene3DSource : ISource, IDisposable
{
    /// <summary><c>"synthetic-scene-3d"</c>.</summary>
    public string Type => "synthetic-scene-3d";
    private SourceContext? _ctx;
    private SyntheticScene3DConfig _cfg = new();
    private Timer? _timer;
    private DateTime _t0;
    private double _t;
    private float[] _pos = [], _inten = [];
    private Action<IReadOnlyDictionary<string, double>, double?>? _arm;
    /// <summary>A two-joint arm mounted on the robot (the same model as the URDF fixture).</summary>
    public const string ArmUrdf = """
        <robot name="arm">
          <material name="steel"><color rgba="0.75 0.75 0.8 1"/></material>
          <link name="base_link"><visual><origin xyz="0 0 0.1"/><geometry><cylinder radius="0.08" length="0.2"/></geometry><material name="steel"/></visual></link>
          <link name="upper_arm"><visual><origin xyz="0.2 0 0" rpy="0 1.5707963 0"/><geometry><cylinder radius="0.04" length="0.4"/></geometry><material><color rgba="0.96 0.62 0.04 1"/></material></visual></link>
          <link name="forearm"><visual><origin xyz="0.15 0 0" rpy="0 1.5707963 0"/><geometry><cylinder radius="0.03" length="0.3"/></geometry><material><color rgba="0.23 0.51 0.96 1"/></material></visual><visual><origin xyz="0.3 0 0"/><geometry><sphere radius="0.05"/></geometry><material><color rgba="0.86 0.15 0.15 1"/></material></visual></link>
          <joint name="shoulder" type="revolute"><parent link="base_link"/><child link="upper_arm"/><origin xyz="0 0 0.2"/><axis xyz="0 1 0"/><limit lower="-1.57" upper="1.57"/></joint>
          <joint name="elbow" type="revolute"><parent link="upper_arm"/><child link="forearm"/><origin xyz="0.4 0 0"/><axis xyz="0 1 0"/><limit lower="-2.5" upper="2.5"/></joint>
        </robot>
        """;

    /// <summary>Declare the static layers (frames, grid, axes, cloud, trail, pose, markers, arm) and, unless <see cref="SyntheticScene3DConfig.UseTimer"/> is off, start a timer that calls <see cref="Step"/> at the configured rate.</summary>
    public Task StartAsync(SourceContext ctx, object? config, CancellationToken ct = default)
    {
        _ctx = ctx; _cfg = config as SyntheticScene3DConfig ?? new SyntheticScene3DConfig();
        _pos = new float[_cfg.CloudPoints * 3]; _inten = new float[_cfg.CloudPoints];
        Emit(() =>
        {
            // static transforms and the marker set travel in the declarations, which the relay replays to late joiners
            ctx.Layers.DeclareLayer("tf", "frames", LayerJson.ToMeta(new { transforms = new[] { new { child = "laser", parent = "base_link", t = new[] { 0.2, 0.0, 0.35 }, q = new[] { 0.0, 0.0, 0.0, 1.0 } } } }));
            ctx.Layers.DeclareLayer("grid3d", "grid3d", LayerJson.ToMeta(new { size = 8, spacing = 1 }));
            ctx.Layers.DeclareLayer("axes", "axes", LayerJson.ToMeta(new { length = 0.6 }));
            ctx.Layers.DeclareLayer("lidar", "pointCloud3d", LayerJson.ToMeta(new { frame = "laser", pointSize = 3, colormap = "turbo" }));
            ctx.Layers.DeclareLayer("trail3d", "path3d", LayerJson.ToMeta(new { frame = "map", color = "#22d3ee", maxPoints = 600 }));
            ctx.Layers.DeclareLayer("robot", "pose3d", LayerJson.ToMeta(new { frame = "base_link", label = "robot", color = "#f59e0b", axisLength = 0.8 }));
            ctx.Layers.DeclareLayer("markers", "markers", LayerJson.ToMeta(new
            {
                markers = new object[]
                {
                    new { id = "dock", type = "cube", position = new[] { 4.0, 4.0, 0.25 }, scale = new[] { 1.0, 0.6, 0.5 }, color = "#3b82f6" },
                    new { id = "dock-label", type = "text", position = new[] { 4.0, 4.0, 0.9 }, text = "dock", color = "#e2e8f0" },
                    new { id = "goal", type = "sphere", position = new[] { -3.0, 2.0, 0.3 }, scale = new[] { 0.6, 0.6, 0.6 }, color = "#22c55e", opacity = 0.7 },
                    new { id = "pillar", type = "cylinder", position = new[] { 0.0, -4.0, 1.0 }, scale = new[] { 0.5, 0.5, 2.0 }, color = "#a855f7" },
                    new { id = "heading", type = "arrow", position = new[] { -3.0, 2.0, 0.3 }, orientation = new[] { 0.0, 0.0, 0.3826834, 0.9238795 }, scale = new[] { 1.2, 0.15, 0.15 }, color = "#eab308" },
                    new { id = "fence", type = "lineStrip", points = new[] { -5.0, -5, 0, 5, -5, 0, 5, 5, 0, -5, 5, 0, -5, -5, 0 }, scale = new[] { 1.0, 0, 0 }, color = "#f87171" },
                },
            }));
        });
        Emit(() =>
        {
            // the arm's frames hang off the robot: arm/base_link sits on base_link
            _arm = Urdf.Publish(ctx.Layers, Urdf.Parse(ArmUrdf), "arm/", markersId: "arm", framesId: "arm-tf");
            ctx.Layers.Push("tf", new { transforms = new[] { new { child = "arm/base_link", parent = "base_link", t = new[] { 0.0, 0.0, 0.3 }, q = new[] { 0.0, 0.0, 0.0, 1.0 } } } });
        });
        _t0 = DateTime.UtcNow;
        if (_cfg.UseTimer) _timer = new Timer(_ => Step((DateTime.UtcNow - _t0).TotalSeconds), null, 0, (int)(1000 / Math.Max(0.1, _cfg.RateHz)));
        return Task.CompletedTask;
    }

    /// <summary>Advance the simulation to time <paramref name="t"/> (seconds) and push the cloud, trail, pose and transform.</summary>
    public void Step(double t)
    {
        if (_ctx is null) return;
        _t = t;
        double a = t * 0.4, x = 3 * Math.Cos(a), y = 3 * Math.Sin(a);
        var n = _cfg.CloudPoints;
        var pos = new float[n * 3]; var inten = new float[n];
        for (var i = 0; i < n; i++)
        {
            var ring = i % 4; var ang = (double)(i / 4) / (n / 4) * Math.PI * 2 + t;
            var r = 2 + 0.6 * Math.Sin(ang * 3 + t) + (Math.Cos(ang) > 0.8 ? -1.2 : 0) + ring * 0.05;
            pos[3 * i] = (float)(r * Math.Cos(ang)); pos[3 * i + 1] = (float)(r * Math.Sin(ang)); pos[3 * i + 2] = (float)(0.1 * Math.Sin(ang * 5 + t * 2) + ring * 0.08);
            inten[i] = (float)r;
        }
        var q = Quat.FromEuler(0, 0, a + Math.PI / 2);
        Emit(() =>
        {
            _ctx.Layers.Push("tf", new { transforms = new[] { new { child = "base_link", parent = "map", t = new[] { x, y, 0 }, q = new[] { q.X, q.Y, q.Z, q.W }, time = t } } });
            _ctx.Layers.Push("lidar", new Dictionary<string, object?> { ["positions"] = pos, ["intensities"] = inten, ["stamp"] = t });
            _ctx.Layers.Push("trail3d", new { append = new[] { x, y, 0.02 } });
            _ctx.Layers.Push("robot", new { t = new[] { 0.0, 0, 0 }, q = new[] { 0.0, 0, 0, 1 }, stamp = t });
            _arm?.Invoke(new Dictionary<string, double> { ["shoulder"] = 0.6 * Math.Sin(t * 0.8) - 0.3, ["elbow"] = 1.2 + 0.8 * Math.Sin(t * 1.1) }, t);
        });
    }

    private void Emit(Action a) { if (_cfg.Dispatch is { } d) d(a); else a(); }
    /// <summary>Simulation time of the last <see cref="Step"/>, seconds.</summary>
    public double Time => _t;

    /// <summary>Stop the timer; declared layers stay in the sink.</summary>
    public Task StopAsync() { _timer?.Dispose(); _timer = null; return Task.CompletedTask; }
    /// <summary>Stops the timer.</summary>
    public void Dispose() => _timer?.Dispose();
}
