// Mori.SkyScope — The 3D rendering contract: versioned mesh buffers, materials and the painter interface.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>
/// A versioned vertex buffer: positions are interleaved xyz, colours RGBA bytes per vertex. Bump <see cref="Version"/>
/// after changing any array so renderers re-upload. Mirrors <c>Mesh3D</c> in <c>scene3d/painter3d.ts</c>.
/// </summary>
public sealed class Mesh3D(string key, float[] positions)
{
    private static int _seq;
    /// <summary>A mesh with a process-unique key when none is given.</summary>
    public static Mesh3D Create(float[] positions, string? key = null) => new(key ?? $"mesh:{Interlocked.Increment(ref _seq)}", positions);
    /// <summary>Renderer cache key; stable for the mesh's lifetime and unique among live meshes.</summary>
    public string Key { get; } = key;
    /// <summary>Increment after changing any array so renderers re-upload.</summary>
    public int Version { get; set; }
    /// <summary>Interleaved xyz, three floats per vertex.</summary>
    public float[] Positions { get; set; } = positions;
    /// <summary>RGBA, four bytes per vertex; null uses the material colour alone.</summary>
    public byte[]? Colors { get; set; }
    /// <summary>Interleaved unit normals, three floats per vertex; needed for lit triangles.</summary>
    public float[]? Normals { get; set; }
    /// <summary>Index list into the vertices; null draws vertices in order.</summary>
    public uint[]? Indices { get; set; }
    /// <summary>Vertices (or indices) to draw; null draws the whole buffer.</summary>
    public int? Count { get; set; }
    /// <summary>Elements drawn: <see cref="Count"/>, else the index count, else the vertex count.</summary>
    public int VertexCount => Count ?? (Indices?.Length ?? Positions.Length / 3);
    /// <summary>FNV-1a of each array's little-endian bytes — how fixtures compare buffers across cores.</summary>
    public (uint Positions, uint? Colors, uint? Normals, uint? Indices) Hash() => (
        RecordingPainter.Fnv1a(MemoryMarshal.AsBytes(Positions.AsSpan()).ToArray()),
        Colors is null ? null : RecordingPainter.Fnv1a(Colors),
        Normals is null ? null : RecordingPainter.Fnv1a(MemoryMarshal.AsBytes(Normals.AsSpan()).ToArray()),
        Indices is null ? null : RecordingPainter.Fnv1a(MemoryMarshal.AsBytes(Indices.AsSpan()).ToArray()));
}

/// <summary>How a mesh is drawn. Mirrors <c>Material3D</c>.</summary>
public sealed record Material3D
{
    /// <summary>Flat colour, multiplied with per-vertex colours when both are present.</summary>
    public string Color { get; init; } = "#ffffff";
    /// <summary>0–1, multiplied with per-vertex alpha when present.</summary>
    public double Opacity { get; init; } = 1;
    /// <summary>Points: diameter in pixels.</summary>
    public double PointSize { get; init; } = 3;
    /// <summary>Lines: width in pixels.</summary>
    public double LineWidth { get; init; } = 1;
    /// <summary>False draws on top of everything (overlays).</summary>
    public bool DepthTest { get; init; } = true;
    /// <summary>Triangles: simple head-light shading from normals.</summary>
    public bool Lit { get; init; }
    /// <summary>Model matrix applied to positions.</summary>
    public double[]? Model { get; init; }
    /// <summary>White, opaque, depth-tested, unlit.</summary>
    public static readonly Material3D Default = new();
}

/// <summary>
/// The 3D rendering contract: a renderer (OpenTK, and WebGL2 on the TS side) uploads each mesh once per version
/// and draws it. Mirrors <c>Painter3D</c> in <c>scene3d/painter3d.ts</c>.
/// </summary>
public interface IPainter3D
{
    /// <summary>Viewport width in logical pixels.</summary>
    double Width { get; }
    /// <summary>Viewport height in logical pixels.</summary>
    double Height { get; }
    /// <summary>Device pixels per logical pixel.</summary>
    double PixelRatio { get; }
    /// <summary>Start a frame with column-major view and projection matrices; <paramref name="clear"/> is a hex colour to clear with, or null to keep the current contents.</summary>
    void Begin(double[] view, double[] proj, string? clear = null);
    /// <summary>One square point per vertex, sized by <see cref="Material3D.PointSize"/>.</summary>
    void Points(Mesh3D mesh, Material3D? material = null);
    /// <summary>Line list (pairs) or, with <paramref name="strip"/>, a connected polyline.</summary>
    void Lines(Mesh3D mesh, Material3D? material = null, bool strip = false);
    /// <summary>Triangle list, indexed when the mesh has indices; shaded when the material is lit and normals are present.</summary>
    void Triangles(Mesh3D mesh, Material3D? material = null);
    /// <summary>A textured quad; <paramref name="corners"/> holds 4 × xyz: bottom-left, bottom-right, top-right, top-left.</summary>
    void Image(IImageHandle image, ReadOnlySpan<double> corners, Material3D? material = null);
    /// <summary>Finish the frame started by <see cref="Begin"/>.</summary>
    void End();
}

/// <summary>Records what it is asked to draw as JSON identical to the TS <c>RecordingPainter3D</c>.</summary>
public sealed class RecordingPainter3D(double width, double height, double pixelRatio = 1) : IPainter3D
{
    /// <summary>Recorded operations in call order; each is an object with an <c>op</c> field.</summary>
    public JsonArray Ops { get; } = [];
    /// <summary>Viewport width in logical pixels.</summary>
    public double Width { get; } = width;
    /// <summary>Viewport height in logical pixels.</summary>
    public double Height { get; } = height;
    /// <summary>Device pixels per logical pixel.</summary>
    public double PixelRatio { get; } = pixelRatio;

    private static JsonArray? Mat(double[]? m) => m is null ? null : new JsonArray(m.Select(v => (JsonNode)RecordingPainter.R3(v)).ToArray());
    private static JsonObject Material(Material3D? m)
    {
        m ??= Material3D.Default;
        return new JsonObject { ["color"] = m.Color, ["opacity"] = m.Opacity, ["pointSize"] = m.PointSize, ["lineWidth"] = m.LineWidth, ["depthTest"] = m.DepthTest, ["lit"] = m.Lit, ["model"] = Mat(m.Model) };
    }
    private void Geom(string op, Mesh3D mesh, Material3D? material, bool? strip = null)
    {
        var h = mesh.Hash();
        var o = new JsonObject
        {
            ["op"] = op, ["key"] = mesh.Key, ["version"] = mesh.Version, ["count"] = mesh.VertexCount,
            ["hash"] = new JsonObject { ["positions"] = h.Positions, ["colors"] = h.Colors, ["normals"] = h.Normals, ["indices"] = h.Indices },
            ["material"] = Material(material),
        };
        if (strip is { } s) o["strip"] = s;
        Ops.Add(o);
    }
    /// <summary>Records a <c>begin</c> op with the matrices rounded to three decimals.</summary>
    public void Begin(double[] view, double[] proj, string? clear = null) => Ops.Add(new JsonObject { ["op"] = "begin", ["view"] = Mat(view), ["proj"] = Mat(proj), ["clear"] = clear });
    /// <summary>Records a <c>points</c> op with the mesh key, version, count and buffer hashes.</summary>
    public void Points(Mesh3D mesh, Material3D? material = null) => Geom("points", mesh, material);
    /// <summary>Records a <c>lines</c> op including the <c>strip</c> flag.</summary>
    public void Lines(Mesh3D mesh, Material3D? material = null, bool strip = false) => Geom("lines", mesh, material, strip);
    /// <summary>Records a <c>triangles</c> op.</summary>
    public void Triangles(Mesh3D mesh, Material3D? material = null) => Geom("triangles", mesh, material);
    /// <summary>Records an <c>image</c> op with the image size and rounded corners; pixel data is not recorded.</summary>
    public void Image(IImageHandle image, ReadOnlySpan<double> corners, Material3D? material = null)
    {
        var c = new JsonArray(); foreach (var v in corners) c.Add(RecordingPainter.R3(v));
        Ops.Add(new JsonObject { ["op"] = "image", ["imageWidth"] = image.Width, ["imageHeight"] = image.Height, ["corners"] = c, ["material"] = Material(material) });
    }
    /// <summary>Records an <c>end</c> op.</summary>
    public void End() => Ops.Add(new JsonObject { ["op"] = "end" });
}
