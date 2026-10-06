// Mori.SkyScope — URDF robot descriptions → markers (one per visual, in the link's frame) and frame-tree transforms (one per joint, driven by joint positions).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Globalization;
using System.Xml.Linq;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Sources;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>An <c>&lt;origin&gt;</c> element.</summary>
/// <param name="Xyz">Translation [x, y, z] in metres.</param>
/// <param name="Rpy">Roll, pitch, yaw in radians (ZYX convention).</param>
public readonly record struct UrdfOrigin(double[] Xyz, double[] Rpy);
/// <summary>A <c>&lt;geometry&gt;</c> child; one of the nested records.</summary>
public abstract record UrdfGeometry
{
    /// <summary>A box with full extents <paramref name="Size"/> [x, y, z] in metres.</summary>
    public sealed record Box(double[] Size) : UrdfGeometry;
    /// <summary>A cylinder along z, metres.</summary>
    public sealed record Cylinder(double Radius, double Length) : UrdfGeometry;
    /// <summary>A sphere, metres.</summary>
    public sealed record Sphere(double Radius) : UrdfGeometry;
    /// <summary>A mesh resource by URI (<c>package://</c>, <c>file://</c> or relative), with a per-axis <paramref name="Scale"/>.</summary>
    public sealed record Mesh(string Filename, double[] Scale) : UrdfGeometry;
}
/// <summary>One <c>&lt;visual&gt;</c> of a link.</summary>
/// <param name="Name">Optional visual name, used in marker ids.</param>
/// <param name="Origin">Pose of the geometry in the link frame.</param>
/// <param name="Geometry">The shape.</param>
/// <param name="Color">Hex colour from the material, or null when none.</param>
/// <param name="Opacity">Material alpha, 0–1; 1 when none.</param>
public sealed record UrdfVisual(string? Name, UrdfOrigin Origin, UrdfGeometry Geometry, string? Color, double Opacity);
/// <summary>A <c>&lt;link&gt;</c>: a frame with zero or more visuals.</summary>
public sealed record UrdfLink(string Name, List<UrdfVisual> Visuals);
/// <summary>A <c>&lt;joint&gt;</c> connecting two links.</summary>
/// <param name="Name">Joint name, the key in joint-position dictionaries.</param>
/// <param name="Type"><c>fixed</c>, <c>revolute</c>, <c>continuous</c>, <c>prismatic</c> or another URDF type (treated as fixed).</param>
/// <param name="Parent">Parent link name.</param>
/// <param name="Child">Child link name.</param>
/// <param name="Origin">Child frame in the parent frame at rest.</param>
/// <param name="Axis">Motion axis [x, y, z] in the child frame; [1, 0, 0] when unspecified.</param>
/// <param name="Lower">Lower limit (radians or metres), or null.</param>
/// <param name="Upper">Upper limit (radians or metres), or null.</param>
public sealed record UrdfJoint(string Name, string Type, string Parent, string Child, UrdfOrigin Origin, double[] Axis, double? Lower, double? Upper);
/// <summary>A parsed URDF document.</summary>
/// <param name="Name">The <c>robot</c> name attribute.</param>
/// <param name="Links">Links in document order.</param>
/// <param name="Joints">Joints in document order.</param>
/// <param name="Materials">Named top-level materials: hex colour and alpha.</param>
public sealed record RobotModel(string Name, List<UrdfLink> Links, List<UrdfJoint> Joints, Dictionary<string, (string Color, double Opacity)> Materials);

/// <summary>
/// URDF robot descriptions → markers (one per visual, in the link's frame) and frame-tree transforms (one per joint,
/// driven by joint positions). Mirrors <c>scene3d/urdf.ts</c>; pinned by <c>spec/fixtures/urdf.json</c>.
/// </summary>
public static class Urdf
{
    private static double Num(string? s, double d) => s is not null && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;
    private static double[] Nums(string? s, int n, double fill)
    {
        var parts = (s ?? "").Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Select(p => Num(p, fill)).ToList();
        while (parts.Count < n) parts.Add(fill);
        return parts.Take(n).ToArray();
    }
    private static UrdfOrigin Origin(XElement? e) => new(Nums(e?.Attribute("xyz")?.Value, 3, 0), Nums(e?.Attribute("rpy")?.Value, 3, 0));
    private static string Hex2(double v) => ((int)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToString("x2");

    /// <summary>Parse a URDF document. Missing attributes take URDF defaults; a visual without a known geometry becomes a 1 cm box. Throws on malformed XML or an empty document.</summary>
    public static RobotModel Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var robot = doc.Root ?? throw new InvalidDataException("urdf: empty document");
        var materials = new Dictionary<string, (string Color, double Opacity)>();
        (string Color, double Opacity)? MaterialOf(XElement? e)
        {
            if (e is null) return null;
            var c = e.Element("color");
            if (c?.Attribute("rgba")?.Value is { } rgba) { var v = Nums(rgba, 4, 1); return ($"#{Hex2(v[0])}{Hex2(v[1])}{Hex2(v[2])}", v[3]); }
            return e.Attribute("name")?.Value is { } n && materials.TryGetValue(n, out var m) ? m : null;
        }
        foreach (var m in robot.Elements("material")) if (m.Attribute("name")?.Value is { } n && MaterialOf(m) is { } c) materials[n] = c;
        var links = robot.Elements("link").Select(l => new UrdfLink(l.Attribute("name")?.Value ?? "", l.Elements("visual").Select(v =>
        {
            var g = v.Element("geometry"); var mat = MaterialOf(v.Element("material"));
            UrdfGeometry geometry = g?.Element("box") is { } box ? new UrdfGeometry.Box(Nums(box.Attribute("size")?.Value, 3, 1))
                : g?.Element("cylinder") is { } cyl ? new UrdfGeometry.Cylinder(Num(cyl.Attribute("radius")?.Value, 0.5), Num(cyl.Attribute("length")?.Value, 1))
                : g?.Element("sphere") is { } sph ? new UrdfGeometry.Sphere(Num(sph.Attribute("radius")?.Value, 0.5))
                : g?.Element("mesh") is { } mesh ? new UrdfGeometry.Mesh(mesh.Attribute("filename")?.Value ?? "", Nums(mesh.Attribute("scale")?.Value, 3, 1))
                : new UrdfGeometry.Box([0.01, 0.01, 0.01]);
            return new UrdfVisual(v.Attribute("name")?.Value, Origin(v.Element("origin")), geometry, mat?.Color, mat?.Opacity ?? 1);
        }).ToList())).ToList();
        var joints = robot.Elements("joint").Select(j =>
        {
            var limit = j.Element("limit");
            return new UrdfJoint(j.Attribute("name")?.Value ?? "", j.Attribute("type")?.Value ?? "fixed", j.Element("parent")?.Attribute("link")?.Value ?? "", j.Element("child")?.Attribute("link")?.Value ?? "",
                Origin(j.Element("origin")), Nums(j.Element("axis")?.Attribute("xyz")?.Value ?? "1 0 0", 3, 0),
                limit?.Attribute("lower")?.Value is { } lo ? Num(lo, 0) : null, limit?.Attribute("upper")?.Value is { } up ? Num(up, 0) : null);
        }).ToList();
        return new RobotModel(robot.Attribute("name")?.Value ?? "", links, joints, materials);
    }

    /// <summary>The root link: the one no joint names as a child.</summary>
    public static string? RootLink(RobotModel model)
    {
        var children = model.Joints.Select(j => j.Child).ToHashSet();
        return model.Links.FirstOrDefault(l => !children.Contains(l.Name))?.Name;
    }

    private static Quat RpyQuat(double[] rpy) => Quat.FromEuler(rpy[0], rpy[1], rpy[2]);

    /// <summary>One marker per visual, in the link's frame; <paramref name="prefix"/> namespaces frame names and marker ids.</summary>
    public static List<Marker> Markers(RobotModel model, string prefix = "", string defaultColor = "#9ca3af")
    {
        var o = new List<Marker>();
        foreach (var link in model.Links)
            for (var i = 0; i < link.Visuals.Count; i++)
            {
                var v = link.Visuals[i]; var q = RpyQuat(v.Origin.Rpy);
                var id = $"{prefix}{link.Name}/{v.Name ?? i.ToString(CultureInfo.InvariantCulture)}";
                Marker Base(MarkerType type, Vec3 scale, string? mesh = null) => new(id, type) { Frame = prefix + link.Name, Position = new(v.Origin.Xyz[0], v.Origin.Xyz[1], v.Origin.Xyz[2]), Orientation = q, Scale = scale, Color = v.Color ?? defaultColor, Opacity = v.Opacity, MeshResource = mesh };
                o.Add(v.Geometry switch
                {
                    UrdfGeometry.Box b => Base(MarkerType.Cube, new(b.Size[0], b.Size[1], b.Size[2])),
                    UrdfGeometry.Cylinder c => Base(MarkerType.Cylinder, new(c.Radius * 2, c.Radius * 2, c.Length)),
                    UrdfGeometry.Sphere s => Base(MarkerType.Sphere, new(s.Radius * 2, s.Radius * 2, s.Radius * 2)),
                    UrdfGeometry.Mesh m => Base(MarkerType.Mesh, new(m.Scale[0], m.Scale[1], m.Scale[2]), m.Filename),
                    _ => Base(MarkerType.Cube, new(0.01, 0.01, 0.01)),
                });
            }
        return o;
    }

    /// <summary>Child-in-parent transforms for every joint at the given joint positions (missing = 0); static when <paramref name="time"/> is null.</summary>
    public static List<FrameTransformMessage> Transforms(RobotModel model, IReadOnlyDictionary<string, double>? positions = null, string prefix = "", double? time = null)
    {
        return model.Joints.Select(j =>
        {
            var q0 = RpyQuat(j.Origin.Rpy); var pos = positions?.GetValueOrDefault(j.Name) ?? 0;
            var axis = new Vec3(j.Axis[0], j.Axis[1], j.Axis[2]);
            var q = q0; var t = new Vec3(j.Origin.Xyz[0], j.Origin.Xyz[1], j.Origin.Xyz[2]);
            if (j.Type is "revolute" or "continuous" && pos != 0) q = q0 * Quat.FromAxisAngle(axis, pos);
            else if (j.Type == "prismatic" && pos != 0) t = Math3.Add(t, q0.Rotate(Math3.Scale(axis, pos)));
            return new FrameTransformMessage(prefix + j.Child, prefix + j.Parent, [t.X, t.Y, t.Z], [q.X, q.Y, q.Z, q.W], time);
        }).ToList();
    }

    /// <summary>Declare a model on a sink (its visuals as a marker set, its joints as static transforms at rest); the returned action drives the joints.</summary>
    public static Action<IReadOnlyDictionary<string, double>, double?> Publish(ILayerSink sink, RobotModel model, string prefix = "", string defaultColor = "#9ca3af", string? markersId = null, string? framesId = null)
    {
        var fid = framesId ?? prefix + "tf"; var mid = markersId ?? prefix + "robot";
        sink.DeclareLayer(fid, "frames", LayerJson.ToMeta(new { transforms = Transforms(model, null, prefix).Select(TfJson).ToArray() }));
        sink.DeclareLayer(mid, "markers", LayerJson.ToMeta(new { markers = Markers(model, prefix, defaultColor).Select(MarkerJson).ToArray() }));
        return (positions, time) => sink.Push(fid, new { transforms = Transforms(model, positions, prefix, time).Select(TfJson).ToArray() });
    }
    /// <summary>Push mesh resources a model references, resolved by <paramref name="resolve"/> (URI → file bytes, or null when unavailable), into a <c>meshes</c> layer. Files a resource refers to (glTF external buffers) are resolved the same way, relative to the resource's URI; the material colour travels with the mesh.</summary>
    public static int PublishMeshes(ILayerSink sink, RobotModel model, Func<string, byte[]?> resolve, string layerId = "meshes")
    {
        var n = 0; var declared = false;
        foreach (var uri in MeshUris(model))
        {
            var bytes = resolve(uri);
            if (bytes is null) continue;
            var parsed = MeshFormats.ParseResource(bytes, uri, rel => resolve(MeshFormats.JoinUri(uri, rel)));
            if (!declared) { sink.DeclareLayer(layerId, "meshes"); declared = true; }
            var payload = new Dictionary<string, object?> { ["uri"] = uri, ["positions"] = parsed.Positions };
            if (parsed.Normals is not null) payload["normals"] = parsed.Normals;
            if (parsed.Indices is not null) payload["indices"] = parsed.Indices;
            if (parsed.Color is not null) payload["color"] = parsed.Color;
            sink.Push(layerId, payload); n++;
        }
        return n;
    }
    private static object TfJson(FrameTransformMessage t) => new { child = t.Child, parent = t.Parent, t = t.T, q = t.Q, time = t.Time };
    private static object MarkerJson(Marker m) => new
    {
        id = m.Id, type = m.Type switch { MarkerType.Cube => "cube", MarkerType.Sphere => "sphere", MarkerType.Cylinder => "cylinder", MarkerType.Arrow => "arrow", MarkerType.LineList => "lineList", MarkerType.LineStrip => "lineStrip", MarkerType.Points => "points", MarkerType.Text => "text", _ => "mesh" },
        frame = m.Frame, position = new[] { m.Position.X, m.Position.Y, m.Position.Z }, orientation = new[] { m.Orientation.X, m.Orientation.Y, m.Orientation.Z, m.Orientation.W }, scale = new[] { m.Scale.X, m.Scale.Y, m.Scale.Z },
        color = m.Color, opacity = m.Opacity, meshResource = m.MeshResource,
    };

    /// <summary>Mesh resource URIs a model needs (deduplicated, in link order).</summary>
    public static List<string> MeshUris(RobotModel model)
    {
        var o = new List<string>();
        foreach (var l in model.Links) foreach (var v in l.Visuals) if (v.Geometry is UrdfGeometry.Mesh m && !o.Contains(m.Filename)) o.Add(m.Filename);
        return o;
    }
}
