// Mori.SkyScope — Mesh resource loaders for marker and robot-model meshes: binary and ASCII STL, and glTF binary.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>A loaded mesh resource: positions (xyz), normals, optional triangle indices.</summary>
/// <param name="Positions">Interleaved xyz, three floats per vertex.</param>
/// <param name="Normals">Interleaved unit normals per vertex, or null when the format carried none.</param>
/// <param name="Indices">Triangle list into the vertices, or null when consecutive vertex triples form the triangles.</param>
public sealed record ParsedMesh(float[] Positions, float[]? Normals, uint[]? Indices);

/// <summary>
/// Mesh resource loaders: binary and ASCII STL, and glTF 2.0 binary (GLB) with triangle primitives (node transforms
/// applied, one merged mesh). Mirrors <c>scene3d/mesh-formats.ts</c>; pinned by <c>spec/fixtures/mesh-formats.json</c>.
/// </summary>
public static partial class MeshFormats
{
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

    /// <summary>glTF 2.0 binary container. Triangle primitives of the default scene are merged into one indexed mesh with node transforms baked into positions and normals; other primitive modes are skipped. Throws <see cref="InvalidDataException"/> on a bad magic or missing JSON chunk.</summary>
    public static ParsedMesh ParseGlb(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x46546c67) throw new InvalidDataException("glb: bad magic");
        var p = 12; JsonDocument? json = null; byte[] bin = [];
        while (p + 8 <= bytes.Length)
        {
            var len = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[p..]); var type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(p + 4)..]);
            var chunk = bytes.Slice(p + 8, len);
            if (type == 0x4e4f534a) json = JsonDocument.Parse(chunk.ToArray()); else if (type == 0x004e4942) bin = chunk.ToArray();
            p += 8 + len;
        }
        if (json is null) throw new InvalidDataException("glb: no JSON chunk");
        using (json) return Assemble(json.RootElement, bin);
    }

    private static ParsedMesh Assemble(JsonElement doc, byte[] bin)
    {
        var pos = new List<float>(); var nor = new List<float>(); var idx = new List<uint>(); var hasNormals = true;
        var accessors = doc.TryGetProperty("accessors", out var acs) ? acs.EnumerateArray().ToArray() : [];
        var views = doc.TryGetProperty("bufferViews", out var bvs) ? bvs.EnumerateArray().ToArray() : [];
        var nodes = doc.TryGetProperty("nodes", out var nds) ? nds.EnumerateArray().ToArray() : [];
        var meshes = doc.TryGetProperty("meshes", out var mss) ? mss.EnumerateArray().ToArray() : [];
        static int Int(JsonElement e, string n, int d) => e.TryGetProperty(n, out var v) ? v.GetInt32() : d;
        (double[] Data, int Comps) Accessor(int i)
        {
            var a = accessors[i]; var bv = views[Int(a, "bufferView", 0)];
            var comps = a.GetProperty("type").GetString() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 1 };
            var ct = a.GetProperty("componentType").GetInt32();
            var size = ct switch { 5120 or 5121 => 1, 5122 or 5123 => 2, _ => 4 };
            var stride = Int(bv, "byteStride", comps * size); var b = Int(bv, "byteOffset", 0) + Int(a, "byteOffset", 0); var count = a.GetProperty("count").GetInt32();
            var o = new double[count * comps]; var span = bin.AsSpan();
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
        return new ParsedMesh(positions, hasNormals && nor.Count == pos.Count ? nor.ToArray() : FaceNormals(positions, indices), indices);
    }

    /// <summary>Pick the parser by extension or content.</summary>
    public static ParsedMesh ParseResource(ReadOnlySpan<byte> bytes, string name = "")
    {
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".glb", StringComparison.Ordinal) || (bytes.Length >= 4 && bytes[0] == 0x67 && bytes[1] == 0x6c && bytes[2] == 0x54 && bytes[3] == 0x46)) return ParseGlb(bytes);
        if (lower.EndsWith(".stl", StringComparison.Ordinal) || lower.Length == 0) return ParseStl(bytes);
        throw new NotSupportedException($"mesh: unsupported resource {name}");
    }

    /// <summary>A renderer mesh from a parsed resource (normals present, so markers can be lit).</summary>
    public static Mesh3D ToMesh(string key, ParsedMesh p) => new(key, p.Positions) { Normals = p.Normals ?? FaceNormals(p.Positions, p.Indices), Indices = p.Indices };
}
