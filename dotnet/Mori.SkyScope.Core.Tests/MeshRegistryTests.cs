// Mori.SkyScope — The mesh registry finds a glTF's external buffer among registered sibling files and keeps the material colour for mesh markers.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Scene3D;

namespace Mori.SkyScope.Core.Tests;

/// <summary>Mirrors <c>mesh-registry.test.ts</c>: URI joining, sibling-file resolution, the resolver fallback and the marker colour fallback.</summary>
public sealed class MeshRegistryTests
{
    private static readonly string Dir = Path.Combine(Fixtures.Fixtures.Directory, "..", "meshes");
    private static byte[] Load(string name) => File.ReadAllBytes(Path.Combine(Dir, name));

    /// <summary>Relative references resolve against the base like a URL; absolute ones pass through.</summary>
    [Fact]
    public void JoinsUrisLikeAUrl()
    {
        Assert.Equal("package://robot/meshes/arm.bin", MeshFormats.JoinUri("package://robot/meshes/arm.gltf", "arm.bin"));
        Assert.Equal("package://robot/bin/arm.bin", MeshFormats.JoinUri("package://robot/meshes/arm.gltf", "../textures/../bin/arm.bin"));
        Assert.Equal("meshes/arm.bin", MeshFormats.JoinUri("meshes/arm.gltf", "./arm.bin"));
        Assert.Equal("arm.bin", MeshFormats.JoinUri("arm.gltf", "arm.bin"));
        Assert.Equal("file:///tmp/arm.bin", MeshFormats.JoinUri("package://robot/meshes/arm.gltf", "file:///tmp/arm.bin"));
        Assert.Equal("/abs/arm.bin", MeshFormats.JoinUri("package://robot/meshes/arm.gltf", "/abs/arm.bin"));
    }

    /// <summary>A .gltf with an external buffer loads once its sibling is registered or the resolver answers, and the registry keeps the material colour.</summary>
    [Fact]
    public void LoadsAGltfThroughSiblingFilesAndTheResolver()
    {
        var reg = new MeshRegistry();
        var ex = Assert.Throws<InvalidDataException>(() => reg.Load("package://robot/meshes/box-external.gltf", Load("box-external.gltf")));
        Assert.Equal("gltf: cannot resolve buffer box-external.bin", ex.Message);
        reg.RegisterFiles("package://robot/meshes", new Dictionary<string, byte[]> { ["box-external.bin"] = Load("box-external.bin") });
        var mesh = reg.Load("package://robot/meshes/box-external.gltf", Load("box-external.gltf"));
        Assert.Equal(8, mesh.Positions.Length / 3);
        Assert.Equal("#3399e6", reg.ColorOf("package://robot/meshes/box-external.gltf"));
        var asked = "";
        reg.Resolver = uri => { asked = uri; return Load("box-external.bin"); };
        reg.Load("package://other/box-external.glb", Load("box-external.glb"));
        Assert.Equal("package://other/box-external.bin", asked);
        Assert.Equal("#3399e6", reg.ColorOf("package://other/box-external.glb"));
        Assert.Null(reg.ColorOf("missing"));
    }

    /// <summary>Mesh markers without a colour draw with the resource's colour; a marker colour wins; other markers stay white.</summary>
    [Fact]
    public void MeshMarkersFallBackToTheResourceColour()
    {
        var reg = new MeshRegistry();
        reg.Load("tri.gltf", Load("tri.gltf"));
        var layer = new MarkerLayer("m") { Meshes = reg };
        layer.SetMarkers([new Marker("a", MarkerType.Mesh) { MeshResource = "tri.gltf" }, new Marker("b", MarkerType.Mesh) { MeshResource = "tri.gltf", Color = "#00ff00" }, new Marker("c", MarkerType.Cube)]);
        var camera = new Camera3D(100, 100);
        var p3 = new RecordingPainter3D(100, 100);
        p3.Begin(camera.View(), camera.Projection(), null);
        layer.Draw(new LayerContext(new RecordingPainter(100, 100), camera, 100, 100, 0, 1) { Painter3D = p3 });
        p3.End();
        var colors = p3.Ops.Where(o => o!["op"]!.GetValue<string>() == "triangles").Select(o => o!["material"]!["color"]!.GetValue<string>()).ToList();
        Assert.Equal(["#cc331a", "#00ff00", "#ffffff"], colors);
    }
}
