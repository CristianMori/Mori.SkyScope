// Mori.SkyScope — Mesh resource loaders for marker and robot-model meshes: binary and ASCII STL, glTF binary and COLLADA.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mori.SkyScope.Core.Charts;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>A loaded mesh resource: positions (xyz), normals, optional triangle indices and the material colour of its first primitive.</summary>
/// <param name="Positions">Interleaved xyz, three floats per vertex.</param>
/// <param name="Normals">Interleaved unit normals per vertex, or null when the format carried none.</param>
/// <param name="Indices">Triangle list into the vertices, or null when consecutive vertex triples form the triangles.</param>
/// <param name="Color">RGBA 0..1 of the first primitive's material (glTF base colour factor, Collada diffuse), or null when the format carried none; mesh markers without a colour of their own use it.</param>
public sealed record ParsedMesh(float[] Positions, float[]? Normals, uint[]? Indices, double[]? Color = null);

/// <summary>
/// Mesh resource loaders: binary and ASCII STL, glTF 2.0 (binary <c>.glb</c> and JSON <c>.gltf</c>; buffers embedded, as
/// <c>data:</c> URIs or external files through a resolver) with triangle primitives (node transforms applied, one merged
/// mesh, the first material's base colour) and COLLADA. Mirrors <c>scene3d/mesh-formats.ts</c>; pinned by
/// <c>spec/fixtures/mesh-formats.json</c>.
/// </summary>
public static partial class MeshFormats
{
    /// <summary>
    /// Resolves <paramref name="relative"/> against <paramref name="base"/> like a URL: absolute references (a scheme such as
    /// <c>package:</c> or <c>file:</c>, or a leading <c>/</c>) are returned as given; otherwise the last path segment of the base
    /// is replaced and <c>.</c>/<c>..</c> segments are collapsed (never above the scheme or the root). Same rules as the TS <c>joinUri</c>.
    /// </summary>
    public static string JoinUri(string @base, string relative)
    {
        if (SchemeRegex().IsMatch(relative) || relative.StartsWith('/')) return relative;
        var scheme = SchemeAuthorityRegex().Match(@base).Value;
        var rest = @base[scheme.Length..];
        var slash = rest.LastIndexOf('/');
        var dir = slash >= 0 ? rest[..(slash + 1)] : "";
        var outSegs = new List<string>();
        foreach (var seg in (dir + relative).Split('/'))
        {
            if (seg is "." or "") continue;
            if (seg == "..") { if (outSegs.Count > 0) outSegs.RemoveAt(outSegs.Count - 1); continue; }
            outSegs.Add(seg);
        }
        return (scheme.EndsWith('/') || scheme.Length == 0 ? scheme : scheme + "/") + string.Join('/', outSegs);
    }
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*:")]
    private static partial Regex SchemeRegex();
    [GeneratedRegex("^[a-zA-Z][a-zA-Z0-9+.-]*://[^/]*/?")]
    private static partial Regex SchemeAuthorityRegex();

    /// <summary><c>#rrggbb</c> of an RGBA 0..1 colour (alpha ignored), channels clamped and rounded.</summary>
    public static string HexOfColor(double[] c)
    {
        static int H(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255, MidpointRounding.AwayFromZero);
        return $"#{H(c[0]):x2}{H(c[1]):x2}{H(c[2]):x2}";
    }
    /// <summary>Binary or ASCII STL, detected from the header. Returns unindexed triangles with flat normals; throws <see cref="InvalidDataException"/> when a binary file is truncated.</summary>
    public static ParsedMesh ParseStl(ReadOnlySpan<byte> bytes)
    {
        var head = Encoding.UTF8.GetString(bytes[..Math.Min(80, bytes.Length)]);
        var isAscii = head.TrimStart().StartsWith("solid", StringComparison.Ordinal) && (bytes.Length < 84 || Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 2048)]).Contains("facet"));
        if (isAscii) return ParseAsciiStl(Encoding.UTF8.GetString(bytes));
        if (bytes.Length < 84) throw new InvalidDataException("stl: truncated");
        var n = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[80..]);
        if (84 + (long)n * 50 > bytes.Length) throw new InvalidDataException("stl: truncated");
        var pos = new float[n * 9];
        for (var i = 0; i < n; i++) { var o = 84 + i * 50 + 12; for (var k = 0; k < 9; k++) pos[i * 9 + k] = BinaryPrimitives.ReadSingleLittleEndian(bytes[(o + k * 4)..]); }
        return new ParsedMesh(pos, FaceNormals(pos), null);
    }

    [GeneratedRegex(@"vertex\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)")]
    private static partial Regex VertexRegex();

    private static ParsedMesh ParseAsciiStl(string text)
    {
        var o = new List<float>();
        foreach (Match m in VertexRegex().Matches(text)) for (var g = 1; g <= 3; g++) o.Add((float)double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture));
        var pos = o.Take(o.Count - o.Count % 9).ToArray();
        return new ParsedMesh(pos, FaceNormals(pos), null);
    }

    /// <summary>Flat per-face normals (unit), computed in double and stored as float32; indexed meshes get averaged vertex normals.</summary>
    public static float[] FaceNormals(float[] pos, uint[]? indices = null)
    {
        if (indices is not null)
        {
            var acc = new double[pos.Length];
            for (var t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = (int)indices[t] * 3, b = (int)indices[t + 1] * 3, c = (int)indices[t + 2] * 3;
                double ux = pos[b] - pos[a], uy = pos[b + 1] - pos[a + 1], uz = pos[b + 2] - pos[a + 2];
                double vx = pos[c] - pos[a], vy = pos[c + 1] - pos[a + 1], vz = pos[c + 2] - pos[a + 2];
                double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                foreach (var i in new[] { a, b, c }) { acc[i] += nx; acc[i + 1] += ny; acc[i + 2] += nz; }
            }
            var outN = new float[pos.Length];
            for (var i = 0; i < pos.Length; i += 3) { var l = Math.Sqrt(acc[i] * acc[i] + acc[i + 1] * acc[i + 1] + acc[i + 2] * acc[i + 2]); if (l == 0) l = 1; outN[i] = (float)(acc[i] / l); outN[i + 1] = (float)(acc[i + 1] / l); outN[i + 2] = (float)(acc[i + 2] / l); }
            return outN;
        }
        var o = new float[pos.Length];
        for (var s = 0; s + 8 < pos.Length; s += 9)
        {
            double ux = pos[s + 3] - pos[s], uy = pos[s + 4] - pos[s + 1], uz = pos[s + 5] - pos[s + 2];
            double vx = pos[s + 6] - pos[s], vy = pos[s + 7] - pos[s + 1], vz = pos[s + 8] - pos[s + 2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            var l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l == 0) l = 1; nx /= l; ny /= l; nz /= l;
            for (var k = 0; k < 3; k++) { o[s + 3 * k] = (float)nx; o[s + 3 * k + 1] = (float)ny; o[s + 3 * k + 2] = (float)nz; }
        }
        return o;
    }

    /// <summary>glTF 2.0 binary container. Triangle primitives of the default scene are merged into one indexed mesh with node transforms baked into positions and normals; other primitive modes are skipped. Buffers come from the BIN chunk, <c>data:</c> URIs or <paramref name="resolve"/>. Throws <see cref="InvalidDataException"/> on a bad magic, a missing JSON chunk or an unresolvable buffer.</summary>
    public static ParsedMesh ParseGlb(ReadOnlySpan<byte> bytes, Func<string, byte[]?>? resolve = null)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546c67) throw new InvalidDataException("glb: bad magic");
        var p = 12; JsonDocument? json = null; byte[]? bin = null;
        while (p + 8 <= bytes.Length)
        {
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[p..]); var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(p + 4)..]);
            var chunk = bytes.Slice(p + 8, len);
            if (type == 0x4e4f534a) json = JsonDocument.Parse(chunk.ToArray()); else if (type == 0x004e4942) bin = chunk.ToArray();
            p += 8 + len;
        }
        if (json is null) throw new InvalidDataException("glb: no JSON chunk");
        using (json) return Assemble(json.RootElement, bin, resolve);
    }

    /// <summary>glTF 2.0 JSON (<c>.gltf</c>). Like <see cref="ParseGlb"/>, with every buffer a <c>data:</c> URI (decoded here) or an external file fetched through <paramref name="resolve"/> (URI as written in the file). Throws <see cref="InvalidDataException"/> when the bytes are not a JSON object or a buffer cannot be resolved.</summary>
    public static ParsedMesh ParseGltf(ReadOnlySpan<byte> bytes, Func<string, byte[]?>? resolve = null)
    {
        JsonDocument json;
        try { json = JsonDocument.Parse(bytes.ToArray()); }
        catch (JsonException) { throw new InvalidDataException("gltf: not JSON"); }
        using (json)
        {
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("gltf: not JSON");
            return Assemble(json.RootElement, null, resolve);
        }
    }

    private static ParsedMesh Assemble(JsonElement doc, byte[]? bin, Func<string, byte[]?>? resolve)
    {
        var pos = new List<float>(); var nor = new List<float>(); var idx = new List<uint>(); var hasNormals = true; double[]? color = null;
        var accessors = doc.TryGetProperty("accessors", out var acs) ? acs.EnumerateArray().ToArray() : [];
        var views = doc.TryGetProperty("bufferViews", out var bvs) ? bvs.EnumerateArray().ToArray() : [];
        var nodes = doc.TryGetProperty("nodes", out var nds) ? nds.EnumerateArray().ToArray() : [];
        var meshes = doc.TryGetProperty("meshes", out var mss) ? mss.EnumerateArray().ToArray() : [];
        var materials = doc.TryGetProperty("materials", out var mts) ? mts.EnumerateArray().ToArray() : [];
        var bufferDefs = doc.TryGetProperty("buffers", out var bfs) ? bfs.EnumerateArray().ToArray() : [];
        var buffers = new Dictionary<int, byte[]>();
        static int Int(JsonElement e, string n, int d) => e.TryGetProperty(n, out var v) ? v.GetInt32() : d;
        byte[] Buffer(int i)
        {
            if (buffers.TryGetValue(i, out var cached)) return cached;
            var uri = i < bufferDefs.Length ? ChartJson.Str(bufferDefs[i], "uri") : null;
            byte[]? b;
            if (uri is null) b = bin ?? (i < bufferDefs.Length ? null : []);
            else if (uri.StartsWith("data:", StringComparison.Ordinal)) { var comma = uri.IndexOf(','); b = comma < 0 ? null : Convert.FromBase64String(uri[(comma + 1)..]); }
            else b = resolve?.Invoke(uri);
            if (b is null) throw new InvalidDataException($"gltf: cannot resolve buffer {uri ?? i.ToString(CultureInfo.InvariantCulture)}");
            buffers[i] = b;
            return b;
        }
        (double[] Data, int Comps) Accessor(int i)
        {
            var a = accessors[i]; var bv = views[Int(a, "bufferView", 0)];
            var comps = a.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
            var ct = a.GetProperty("componentType").GetInt32();
            var size = ct switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };
            var stride = Int(bv, "byteStride", comps * size); var b = Int(bv, "byteOffset", 0) + Int(a, "byteOffset", 0); var count = a.GetProperty("count").GetInt32();
            var o = new double[count * comps]; var span = Buffer(Int(bv, "buffer", 0)).AsSpan();
            for (var k = 0; k < count; k++) for (var c = 0; c < comps; c++)
            {
                var at = b + k * stride + c * size;
                o[k * comps + c] = ct switch
                {
                    5126 => BinaryPrimitives.ReadSingleLittleEndian(span[at..]), 5125 => BinaryPrimitives.ReadUInt32LittleEndian(span[at..]), 5123 => BinaryPrimitives.ReadUInt16LittleEndian(span[at..]),
                    5121 => span[at], 5122 => BinaryPrimitives.ReadInt16LittleEndian(span[at..]), _ => (sbyte)span[at],
                };
            }
            return (o, comps);
        }
        static double[] Nums(JsonElement e) => e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
        double[] NodeMatrix(JsonElement n)
        {
            if (n.TryGetProperty("matrix", out var mm)) return Nums(mm);
            var t = n.TryGetProperty("translation", out var te) ? Nums(te) : [0, 0, 0]; var q = n.TryGetProperty("rotation", out var re) ? Nums(re) : [0, 0, 0, 1]; var s = n.TryGetProperty("scale", out var se) ? Nums(se) : [1, 1, 1];
            double x = q[0], y = q[1], z = q[2], w = q[3];
            double xx = x * x, yy = y * y, zz = z * z, xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
            return [(1 - 2 * (yy + zz)) * s[0], 2 * (xy + wz) * s[0], 2 * (xz - wy) * s[0], 0, 2 * (xy - wz) * s[1], (1 - 2 * (xx + zz)) * s[1], 2 * (yz + wx) * s[1], 0, 2 * (xz + wy) * s[2], 2 * (yz - wx) * s[2], (1 - 2 * (xx + yy)) * s[2], 0, t[0], t[1], t[2], 1];
        }
        void Visit(int ni, double[] parent)
        {
            var n = nodes[ni]; var m = Mat4.Mul(parent, NodeMatrix(n));
            if (n.TryGetProperty("mesh", out var me))
                foreach (var prim in meshes[me.GetInt32()].GetProperty("primitives").EnumerateArray())
                {
                    if (Int(prim, "mode", 4) != 4 || !prim.GetProperty("attributes").TryGetProperty("POSITION", out var pa)) continue;
                    if (color is null && prim.TryGetProperty("material", out var mi) && mi.ValueKind == JsonValueKind.Number)
                    {
                        var m2 = mi.GetInt32();
                        var f = m2 >= 0 && m2 < materials.Length && materials[m2].TryGetProperty("pbrMetallicRoughness", out var pbr) ? ChartJson.Doubles(pbr, "baseColorFactor") : null;
                        color = f is { Length: >= 3 } ? [f[0], f[1], f[2], f.Length > 3 ? f[3] : 1] : [1, 1, 1, 1];
                    }
                    var basev = (uint)(pos.Count / 3);
                    var (P, _) = Accessor(pa.GetInt32());
                    for (var k = 0; k < P.Length; k += 3) { double x = P[k], y = P[k + 1], z = P[k + 2]; pos.Add((float)(m[0] * x + m[4] * y + m[8] * z + m[12])); pos.Add((float)(m[1] * x + m[5] * y + m[9] * z + m[13])); pos.Add((float)(m[2] * x + m[6] * y + m[10] * z + m[14])); }
                    if (prim.GetProperty("attributes").TryGetProperty("NORMAL", out var na))
                    {
                        var (N, _) = Accessor(na.GetInt32());
                        for (var k = 0; k < N.Length; k += 3) { double x = N[k], y = N[k + 1], z = N[k + 2]; nor.Add((float)(m[0] * x + m[4] * y + m[8] * z)); nor.Add((float)(m[1] * x + m[5] * y + m[9] * z)); nor.Add((float)(m[2] * x + m[6] * y + m[10] * z)); }
                    }
                    else hasNormals = false;
                    if (prim.TryGetProperty("indices", out var ie)) { var (I, _) = Accessor(ie.GetInt32()); foreach (var v in I) idx.Add(basev + (uint)v); }
                    else for (var k = 0; k < P.Length / 3; k++) idx.Add(basev + (uint)k);
                }
            if (n.TryGetProperty("children", out var ch)) foreach (var c in ch.EnumerateArray()) Visit(c.GetInt32(), m);
        }
        var scene = Int(doc, "scene", 0);
        IEnumerable<int> roots = doc.TryGetProperty("scenes", out var scs) && scs.GetArrayLength() > scene && scs[scene].TryGetProperty("nodes", out var rn) ? rn.EnumerateArray().Select(x => x.GetInt32()) : Enumerable.Range(0, nodes.Length);
        foreach (var r in roots) Visit(r, Mat4.Identity);
        var positions = pos.ToArray(); var indices = idx.ToArray();
        return new ParsedMesh(positions, hasNormals && nor.Count == pos.Count ? nor.ToArray() : FaceNormals(positions, indices), indices, color);
    }

    private static IEnumerable<XElement> Kids(XElement e, string name) => e.Elements().Where(c => c.Name.LocalName == name);
    private static XElement? Kid(XElement e, string name) => Kids(e, name).FirstOrDefault();
    private static double[] Nums(string? s) => string.IsNullOrWhiteSpace(s) ? [] : s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(t => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN).ToArray();
    private static double Num(string? s, double d) => s is null ? d : double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    private static string Ref(string? s) => s is not null && s.StartsWith('#') ? s[1..] : s ?? "";
    private static double At(double[] a, int i) => i >= 0 && i < a.Length ? a[i] : 0;

    /// <summary>Direction through the upper 3×3 of <paramref name="m"/>, renormalised (zero stays zero).</summary>
    private static (double X, double Y, double Z) TransformNormal(double[] m, double x, double y, double z)
    {
        double nx = m[0] * x + m[4] * y + m[8] * z, ny = m[1] * x + m[5] * y + m[9] * z, nz = m[2] * x + m[6] * y + m[10] * z;
        var l = Math.Sqrt(nx * nx + ny * ny + nz * nz); if (l == 0) l = 1;
        return (nx / l, ny / l, nz / l);
    }

    private sealed record ColladaSource(double[] Data, int Stride);

    /// <summary>
    /// COLLADA 1.4/1.5 (.dae). Reads <c>library_geometries</c> meshes (<c>source</c> float arrays with accessor stride,
    /// <c>vertices</c>, <c>triangles</c> and <c>polylist</c> with VERTEX/NORMAL inputs and offsets; polygons are triangulated as
    /// fans), places every <c>instance_geometry</c> of the visual scene through its node <c>matrix</c>/<c>translate</c>/
    /// <c>rotate</c>/<c>scale</c> chain (all geometries at the origin when no scene instances any), converts
    /// <c>Y_UP</c> to Z up as ROS does (x, y, z → x, −z, y) and merges everything into one unindexed mesh. Primitives
    /// without normals get flat face normals. The first primitive whose bound material (<c>instance_material</c> symbol →
    /// <c>library_materials</c> → <c>instance_effect</c> → <c>profile_COMMON</c> phong/lambert/blinn <c>diffuse/color</c>) has
    /// a colour gives the mesh its <see cref="ParsedMesh.Color"/>. Textures, controllers and animations are ignored. Throws
    /// <see cref="InvalidDataException"/> when the document has no COLLADA root.
    /// </summary>
    public static ParsedMesh ParseCollada(ReadOnlySpan<byte> bytes)
    {
        XElement? root;
        var text = Encoding.UTF8.GetString(bytes);
        if (text.StartsWith('﻿')) text = text[1..];
        try { root = XDocument.Parse(text).Root; }
        catch (System.Xml.XmlException) { root = null; }
        if (root is null || root.Name.LocalName != "COLLADA") throw new InvalidDataException("collada: no COLLADA root");
        var upAxis = (Kid(Kid(root, "asset") ?? root, "up_axis")?.Value ?? "Z_UP").Trim().ToUpperInvariant();
        double[] up = upAxis == "Y_UP" ? [1, 0, 0, 0, 0, 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1] : Mat4.Identity;

        var geometryOrder = new List<XElement>(); var geometries = new Dictionary<string, XElement>();
        foreach (var lib in Kids(root, "library_geometries")) foreach (var g in Kids(lib, "geometry")) { var id = g.Attribute("id")?.Value; if (id is not null) { geometries[id] = g; geometryOrder.Add(g); } }
        var nodesById = new Dictionary<string, XElement>();
        foreach (var lib in Kids(root, "library_nodes")) foreach (var n in Kids(lib, "node")) { var id = n.Attribute("id")?.Value; if (id is not null) nodesById[id] = n; }
        // materials: effect id → diffuse colour, material id → effect id
        var effectColors = new Dictionary<string, double[]>();
        foreach (var lib in Kids(root, "library_effects")) foreach (var e in Kids(lib, "effect"))
        {
            var id = e.Attribute("id")?.Value;
            if (id is null) continue;
            foreach (var profile in Kids(e, "profile_COMMON")) foreach (var tech in Kids(profile, "technique")) foreach (var shader in tech.Elements())
            {
                var sn = shader.Name.LocalName;
                if (sn != "phong" && sn != "lambert" && sn != "blinn") continue;
                var diffuse = Kid(shader, "diffuse");
                var v = diffuse is null ? [] : Nums(Kid(diffuse, "color")?.Value);
                if (v.Length >= 3 && !effectColors.ContainsKey(id)) effectColors[id] = [v[0], v[1], v[2], v.Length > 3 ? v[3] : 1];
            }
        }
        var materialEffects = new Dictionary<string, string>();
        foreach (var lib in Kids(root, "library_materials")) foreach (var m in Kids(lib, "material")) { var id = m.Attribute("id")?.Value; var fx = Kid(m, "instance_effect"); if (id is not null && fx is not null) materialEffects[id] = Ref(fx.Attribute("url")?.Value); }
        static Dictionary<string, string> Bindings(XElement? instance)
        {
            var o = new Dictionary<string, string>();
            if (instance is null) return o;
            foreach (var bm in Kids(instance, "bind_material")) foreach (var tc in Kids(bm, "technique_common")) foreach (var im in Kids(tc, "instance_material")) { var sym = im.Attribute("symbol")?.Value; if (sym is not null) o[sym] = Ref(im.Attribute("target")?.Value); }
            return o;
        }
        double[]? color = null;

        var pos = new List<float>(); var nor = new List<float>();
        void EmitGeometry(XElement g, double[] world, Dictionary<string, string> bound)
        {
            var mesh = Kid(g, "mesh");
            if (mesh is null) return;
            var sources = new Dictionary<string, ColladaSource>();
            foreach (var s in Kids(mesh, "source"))
            {
                var fa = Kid(s, "float_array"); var id = s.Attribute("id")?.Value;
                if (id is null || fa is null) continue;
                var acc = Kid(Kid(s, "technique_common") ?? s, "accessor");
                var stride = Num(acc?.Attribute("stride")?.Value, 3); if (double.IsNaN(stride) || stride == 0) stride = 3;
                sources[id] = new ColladaSource(Nums(fa.Value), Math.Max(1, (int)stride));
            }
            var verts = new Dictionary<string, (string Position, string? Normal)>();
            foreach (var v in Kids(mesh, "vertices"))
            {
                var id = v.Attribute("id")?.Value;
                var p = Kids(v, "input").FirstOrDefault(i => i.Attribute("semantic")?.Value == "POSITION"); var n = Kids(v, "input").FirstOrDefault(i => i.Attribute("semantic")?.Value == "NORMAL");
                if (id is not null && p is not null) verts[id] = (Ref(p.Attribute("source")?.Value), n is null ? null : Ref(n.Attribute("source")?.Value));
            }
            foreach (var prim in mesh.Elements())
            {
                var kind = prim.Name.LocalName;
                if (kind != "triangles" && kind != "polylist") continue;
                ColladaSource? posSrc = null, norSrc = null; int posOff = 0, norOff = 0, stride = 1;
                foreach (var inp in Kids(prim, "input"))
                {
                    var offD = Num(inp.Attribute("offset")?.Value, 0); var off = double.IsNaN(offD) ? 0 : (int)offD; stride = Math.Max(stride, off + 1);
                    var semantic = inp.Attribute("semantic")?.Value;
                    if (semantic == "VERTEX")
                    {
                        if (!verts.TryGetValue(Ref(inp.Attribute("source")?.Value), out var v)) continue;
                        posSrc = sources.GetValueOrDefault(v.Position); posOff = off;
                        if (v.Normal is not null && norSrc is null) { norSrc = sources.GetValueOrDefault(v.Normal); norOff = off; }
                    }
                    else if (semantic == "NORMAL") { norSrc = sources.GetValueOrDefault(Ref(inp.Attribute("source")?.Value)); norOff = off; }
                }
                if (posSrc is null) continue;
                if (color is null && prim.Attribute("material")?.Value is { } sym) { var mat = bound.GetValueOrDefault(sym) ?? sym; color = effectColors.GetValueOrDefault(materialEffects.GetValueOrDefault(mat) ?? ""); }
                ColladaSource ps = posSrc; ColladaSource? nsrc = norSrc;
                var p = Nums(Kid(prim, "p")?.Value);
                var corners = p.Length / stride;
                var polys = kind == "polylist" ? Nums(Kid(prim, "vcount")?.Value).Select(x => (int)x).ToList() : [];
                if (kind == "triangles") for (var t = 0; t + 2 < corners; t += 3) polys.Add(3);
                var start = pos.Count;
                var c = 0;
                void Corner(int k)
                {
                    var pi = (int)p[k * stride + posOff]; var s = ps.Stride;
                    double x = At(ps.Data, pi * s), y = At(ps.Data, pi * s + 1), z = At(ps.Data, pi * s + 2);
                    pos.Add((float)(world[0] * x + world[4] * y + world[8] * z + world[12])); pos.Add((float)(world[1] * x + world[5] * y + world[9] * z + world[13])); pos.Add((float)(world[2] * x + world[6] * y + world[10] * z + world[14]));
                    if (nsrc is not null)
                    {
                        var ni = (int)p[k * stride + norOff]; var ns = nsrc.Stride;
                        var (nx, ny, nz) = TransformNormal(world, At(nsrc.Data, ni * ns), At(nsrc.Data, ni * ns + 1), At(nsrc.Data, ni * ns + 2));
                        nor.Add((float)nx); nor.Add((float)ny); nor.Add((float)nz);
                    }
                }
                foreach (var vc in polys)
                {
                    if (c + vc > corners) break;
                    for (var i = 1; i + 1 < vc; i++) { Corner(c); Corner(c + i); Corner(c + i + 1); }
                    c += vc;
                }
                if (norSrc is null) nor.AddRange(FaceNormals(pos.Skip(start).ToArray()));
            }
        }

        static double[] NodeMatrix(XElement n)
        {
            var m = Mat4.Identity;
            foreach (var e in n.Elements())
            {
                var name = e.Name.LocalName;
                if (name != "matrix" && name != "translate" && name != "rotate" && name != "scale") continue;
                var v = Nums(e.Value);
                if (name == "matrix" && v.Length >= 16) m = Mat4.Mul(m, [v[0], v[4], v[8], v[12], v[1], v[5], v[9], v[13], v[2], v[6], v[10], v[14], v[3], v[7], v[11], v[15]]);
                else if (name == "translate" && v.Length >= 3) m = Mat4.Mul(m, Mat4.Translation(v[0], v[1], v[2]));
                else if (name == "scale" && v.Length >= 3) m = Mat4.Mul(m, Mat4.Scaling(v[0], v[1], v[2]));
                else if (name == "rotate" && v.Length >= 4)
                {
                    var l = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]); if (l == 0) l = 1;
                    double x = v[0] / l, y = v[1] / l, z = v[2] / l;
                    var a = v[3] * Math.PI / 180; double s = Math.Sin(a), co = Math.Cos(a), t = 1 - co;
                    m = Mat4.Mul(m, [t * x * x + co, t * x * y + s * z, t * x * z - s * y, 0, t * x * y - s * z, t * y * y + co, t * y * z + s * x, 0, t * x * z + s * y, t * y * z - s * x, t * z * z + co, 0, 0, 0, 0, 1]);
                }
            }
            return m;
        }
        var instanced = 0;
        void Visit(XElement n, double[] parent, int depth)
        {
            if (depth > 64) return;
            var m = Mat4.Mul(parent, NodeMatrix(n));
            foreach (var e in n.Elements())
            {
                switch (e.Name.LocalName)
                {
                    case "instance_geometry": if (geometries.TryGetValue(Ref(e.Attribute("url")?.Value), out var g)) { instanced++; EmitGeometry(g, m, Bindings(e)); } break;
                    case "instance_node": if (nodesById.TryGetValue(Ref(e.Attribute("url")?.Value), out var r)) Visit(r, m, depth + 1); break;
                    case "node": Visit(e, m, depth + 1); break;
                }
            }
        }
        var sceneUrl = Ref(Kid(Kid(root, "scene") ?? root, "instance_visual_scene")?.Attribute("url")?.Value);
        var scenes = Kids(root, "library_visual_scenes").SelectMany(l => Kids(l, "visual_scene")).ToList();
        var scene = scenes.FirstOrDefault(s => s.Attribute("id")?.Value == sceneUrl) ?? scenes.FirstOrDefault();
        if (scene is not null) foreach (var n in Kids(scene, "node")) Visit(n, up, 0);
        if (instanced == 0) foreach (var g in geometryOrder) EmitGeometry(g, up, Bindings(null));
        return new ParsedMesh(pos.ToArray(), nor.ToArray(), null, color);
    }

    /// <summary>Pick the parser by extension or content (<c>.glb</c>, <c>.gltf</c>, <c>.dae</c>, <c>.stl</c>); <paramref name="resolve"/> serves glTF external buffers (URI as written → bytes, or null).</summary>
    public static ParsedMesh ParseResource(ReadOnlySpan<byte> bytes, string name = "", Func<string, byte[]?>? resolve = null)
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".glb", StringComparison.Ordinal) || (bytes.Length >= 4 && bytes[0] == 0x67 && bytes[1] == 0x6c && bytes[2] == 0x54 && bytes[3] == 0x46)) return ParseGlb(bytes, resolve);
        if (lower.EndsWith(".gltf", StringComparison.Ordinal)) return ParseGltf(bytes, resolve);
        if (lower.EndsWith(".dae", StringComparison.Ordinal) || (bytes.Length > 0 && bytes[0] == 0x3c && Encoding.UTF8.GetString(bytes[..Math.Min(bytes.Length, 1024)]).Contains("COLLADA", StringComparison.Ordinal))) return ParseCollada(bytes);
        if (lower.EndsWith(".stl", StringComparison.Ordinal) || lower.Length == 0) return ParseStl(bytes);
        throw new NotSupportedException($"mesh: unsupported resource {name}");
    }

    /// <summary>A renderer mesh from a parsed resource (normals present, so markers can be lit).</summary>
    public static Mesh3D ToMesh(string key, ParsedMesh p) => new(key, p.Positions) { Normals = p.Normals ?? FaceNormals(p.Positions, p.Indices), Indices = p.Indices };
}
