// Mori.SkyScope — SkyScopeLayer v1 — the binary form of a layer push whose payload carries typed arrays.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Mori.SkyScope.Core.Streaming;

/// <summary>
/// SkyScopeLayer v1 — the binary form of a layer push whose payload carries typed arrays. Mirrors
/// <c>streaming/layer-message.ts</c>; pinned by <c>spec/fixtures/layer-message.json</c> and <c>spec/frames/layer-*.bin</c>.
/// A payload is an <c>IReadOnlyDictionary&lt;string, object?&gt;</c>; values that are <c>float[]</c>, <c>double[]</c>,
/// <c>byte[]</c>, <c>ushort[]</c>, <c>short[]</c>, <c>uint[]</c> or <c>int[]</c> become attachments, everything else is JSON.
/// </summary>
public static class LayerMessage
{
    /// <summary>"SKSL" as a little-endian u32 at offset 0.</summary>
    public const uint Magic = 0x4c534b53;
    /// <summary>Wire format version written and accepted.</summary>
    public const ushort Version = 1;
    /// <summary>Size of the message header.</summary>
    public const int HeaderBytes = 16;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static int Pad8(int n) => (n + 7) & ~7;
    /// <summary>By element type, not by pattern: the CLR treats <c>short[]</c> and <c>ushort[]</c> (and <c>int[]</c>/<c>uint[]</c>) as interchangeable in type tests.</summary>
    private static int Dtype(Array a) => Type.GetTypeCode(a.GetType().GetElementType()) switch { TypeCode.Single => 0, TypeCode.Double => 1, TypeCode.Byte => 2, TypeCode.UInt16 => 3, TypeCode.Int16 => 4, TypeCode.UInt32 => 5, TypeCode.Int32 => 6, _ => -1 };
    private static int ElementSize(int dtype) => dtype switch { 0 => 4, 1 => 8, 2 => 1, 3 => 2, 4 => 2, 5 => 4, 6 => 4, _ => throw new InvalidDataException($"layer message: unknown dtype {dtype}") };

    /// <summary>True when the payload is a dictionary with at least one typed-array value.</summary>
    public static bool HasBinary(object? payload)
    {
        if (payload is not IReadOnlyDictionary<string, object?> d) return false;
        foreach (var v in d.Values) if (v is Array a && Dtype(a) >= 0) return true;
        return false;
    }

    /// <summary>True when the bytes start with the layer-message magic.</summary>
    public static bool IsLayerMessage(ReadOnlySpan<byte> bytes) => bytes.Length >= HeaderBytes && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == Magic;

    /// <summary>Encodes a layer id and payload: typed-array values become attachments, everything else is serialised as JSON.</summary>
    public static byte[] Encode(string id, IReadOnlyDictionary<string, object?> payload)
    {
        var json = new Dictionary<string, object?>();
        var attachments = new List<(byte[] Name, int Dtype, Array Data)>();
        foreach (var (k, v) in payload)
        {
            if (v is Array a && Dtype(a) is var dt && dt >= 0) attachments.Add((Encoding.UTF8.GetBytes(k), dt, a));
            else json[k] = v;
        }
        var idBytes = Encoding.UTF8.GetBytes(id); var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(json, Json);
        if (idBytes.Length > 0xffff || attachments.Count > 0xffff) throw new InvalidDataException("layer message: id or attachment count too large");
        var len = Pad8(HeaderBytes + idBytes.Length + jsonBytes.Length);
        foreach (var (name, dt, data) in attachments) len += Pad8(8 + name.Length) + Pad8(data.Length * ElementSize(dt));
        var o = new byte[len]; var s = o.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s, Magic); BinaryPrimitives.WriteUInt16LittleEndian(s[4..], Version); BinaryPrimitives.WriteUInt16LittleEndian(s[6..], 0);
        BinaryPrimitives.WriteUInt16LittleEndian(s[8..], (ushort)idBytes.Length); BinaryPrimitives.WriteUInt16LittleEndian(s[10..], (ushort)attachments.Count); BinaryPrimitives.WriteUInt32LittleEndian(s[12..], (uint)jsonBytes.Length);
        idBytes.CopyTo(s[HeaderBytes..]); jsonBytes.CopyTo(s[(HeaderBytes + idBytes.Length)..]);
        var p = Pad8(HeaderBytes + idBytes.Length + jsonBytes.Length);
        foreach (var (name, dt, data) in attachments)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(s[p..], (ushort)name.Length); o[p + 2] = (byte)dt; o[p + 3] = 0; BinaryPrimitives.WriteUInt32LittleEndian(s[(p + 4)..], (uint)data.Length);
            name.CopyTo(s[(p + 8)..]);
            p += Pad8(8 + name.Length);
            var bytes = Bytes(data, dt);
            if (!BitConverter.IsLittleEndian && dt != 2) { var es = ElementSize(dt); for (var i = 0; i < data.Length; i++) bytes.Slice(i * es, es).Reverse(); }
            bytes.CopyTo(s[p..]);
            p += Pad8(bytes.Length);
        }
        return o;
    }
    private static Span<byte> Bytes(Array a, int dtype) => dtype switch
    {
        0 => MemoryMarshal.AsBytes(((float[])a).AsSpan()), 1 => MemoryMarshal.AsBytes(((double[])a).AsSpan()), 2 => (byte[])a, 3 => MemoryMarshal.AsBytes(((ushort[])a).AsSpan()),
        4 => MemoryMarshal.AsBytes(((short[])a).AsSpan()), 5 => MemoryMarshal.AsBytes(((uint[])a).AsSpan()), _ => MemoryMarshal.AsBytes(((int[])a).AsSpan()),
    };
    private static Array Read(ReadOnlySpan<byte> src, int dtype, int n)
    {
        switch (dtype)
        {
            case 0: { var a = new float[n]; src.CopyTo(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
            case 1: { var a = new double[n]; src.CopyTo(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
            case 2: return src.ToArray();
            case 3: { var a = new ushort[n]; src.CopyTo(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
            case 4: { var a = new short[n]; src.CopyTo(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
            case 5: { var a = new uint[n]; src.CopyTo(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
            case 6: { var a = new int[n]; src.CopyTo(MemoryMarshal.AsBytes(a.AsSpan())); return a; }
            default: throw new InvalidDataException($"layer message: unknown dtype {dtype}");
        }
    }

    /// <summary>Decode; JSON fields come back as <see cref="JsonElement"/>s, attachments as typed arrays.</summary>
    public static (string Id, Dictionary<string, object?> Payload) Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic) throw new InvalidDataException("layer message: bad magic");
        var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if (version != Version) throw new InvalidDataException($"layer message: unsupported version {version}");
        int idLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes[8..]), count = BinaryPrimitives.ReadUInt16LittleEndian(bytes[10..]), jsonLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        var id = Encoding.UTF8.GetString(bytes.Slice(HeaderBytes, idLen));
        var payload = new Dictionary<string, object?>();
        using (var doc = JsonDocument.Parse(bytes.Slice(HeaderBytes + idLen, jsonLen).ToArray()))
            foreach (var prop in doc.RootElement.EnumerateObject()) payload[prop.Name] = prop.Value.Clone();
        var p = Pad8(HeaderBytes + idLen + jsonLen);
        for (var i = 0; i < count; i++)
        {
            if (p + 8 > bytes.Length) throw new InvalidDataException("layer message: truncated");
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(bytes[p..]), dtype = bytes[p + 2], n = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[(p + 4)..]);
            var es = ElementSize(dtype);
            var name = Encoding.UTF8.GetString(bytes.Slice(p + 8, nameLen));
            p += Pad8(8 + nameLen);
            var byteLen = n * es;
            if (p + byteLen > bytes.Length) throw new InvalidDataException("layer message: truncated");
            var data = Read(bytes.Slice(p, byteLen), dtype, n);
            if (!BitConverter.IsLittleEndian && dtype != 2) { var b = Bytes(data, dtype); for (var k = 0; k < n; k++) b.Slice(k * es, es).Reverse(); }
            payload[name] = data;
            p += Pad8(byteLen);
        }
        return (id, payload);
    }
}
