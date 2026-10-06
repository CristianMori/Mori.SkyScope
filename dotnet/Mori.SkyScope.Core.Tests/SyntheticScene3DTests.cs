// Mori.SkyScope — The synthetic 3D source drives the 3D layers through the JSON/binary layer contract (the same path the relay and MCAP use).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.Core.Tests;

/// <summary>The synthetic 3D source drives the 3D layers through the JSON/binary layer contract (the same path the relay and MCAP use).</summary>
public sealed class SyntheticScene3DTests
{
    /// <summary>Two manual steps populate every 3D layer kind in the expected order with the expected frames, and a binary layer push round-trips through the wire format into a second scene.</summary>
    [Fact]
    public async Task StepsIntoASceneThroughTheLayerContract()
    {
        var scene = new Scene.Scene();
        var sink = new SceneLayerSink(scene, env: new LayerJson.LayerEnv(FixedFrame: "map"));
        var src = new SyntheticScene3DSource();
        await src.StartAsync(new SourceContext(new NullSignalSink(), sink, new ManualClock(), (_, _) => { }), new SyntheticScene3DConfig { UseTimer = false, CloudPoints = 400 });
        src.Step(1.0); src.Step(1.5);

        Assert.Equal(["tf", "grid3d", "axes", "lidar", "trail3d", "robot", "markers", "arm-tf", "arm"], scene.Order());
        Assert.Equal(["arm/base_link", "arm/forearm", "arm/upper_arm", "base_link", "laser", "map"], sink.Frames.FrameIds());
        Assert.Equal(4, Assert.IsType<MarkerLayer>(scene.Get("arm")).Count);
        var cloud = Assert.IsType<PointCloud3DLayer>(scene.Get("lidar"));
        Assert.Equal(400, cloud.PointCount); Assert.Equal("laser", cloud.Frame); Assert.Equal(1.5, cloud.Stamp); Assert.Equal(1600, cloud.Mesh.Colors!.Length);
        Assert.Equal(2, Assert.IsType<Path3DLayer>(scene.Get("trail3d")).PointCount);
        Assert.Equal(6, Assert.IsType<MarkerLayer>(scene.Get("markers")).Count);
        Assert.Equal("robot", Assert.IsType<Pose3DLayer>(scene.Get("robot")).Label);

        // the cloud is placed through laser → base_link(t) → map
        var b = scene.Bounds3()!.Value;
        Assert.True(b.Max.X > 3 && b.Min.X < -3, $"bounds {b}");

        // a binary push survives the wire format and comes back as the same typed arrays
        var bytes = LayerMessage.Encode("lidar", new Dictionary<string, object?> { ["positions"] = cloud.Mesh.Positions, ["stamp"] = 2.0 });
        var (id, payload) = LayerMessage.Decode(bytes);
        Assert.Equal("lidar", id); Assert.Equal(cloud.Mesh.Positions, (float[])payload["positions"]!);
        var scene2 = new Scene.Scene(); var sink2 = new SceneLayerSink(scene2, env: new LayerJson.LayerEnv(sink.Frames, "map"));
        sink2.DeclareLayer("lidar", "pointCloud3d", LayerJson.ToMeta(new { frame = "laser" }));
        sink2.Push(id, payload);
        Assert.Equal(400, Assert.IsType<PointCloud3DLayer>(scene2.Get("lidar")).PointCount);
    }

    private sealed class NullSignalSink : ISignalSink
    {
        public void DeclareChannel(ChannelInfo info) { }
        public void PushFrame(SkyScopeFrame frame) { }
    }
}
