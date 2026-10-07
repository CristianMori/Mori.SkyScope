// Mori.SkyScope — SkyScopeFrame v1 binary codec.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Mori.SkyScope.Core.Streaming;

/// <summary>
/// SkyScopeFrame v1 binary codec. Mirrors <c>encodeFrame</c>/<c>decodeFrame</c> in <c>@cmori/skyscope-core</c>
/// (see that file for the byte layout); pinned by the golden files in <c>spec/frames/</c>.
/// </summary>
public static class FrameCodec
{
    /// <summary>"SKSF" as a little-endian u32 at offset 0.</summary>
    public const uint Magic = 0x46534B53;
    /// <summary>Wire format version written and accepted.</summary>
    public const ushort Version = 1;
    /// <summary>Size of the frame header.</summary>
    public const int HeaderBytes = 24;
    /// <summary>Size of each channel header.</summary>
    public const int ChannelHeaderBytes = 8;

    internal static int Pad8(int n) => (n + 7) & ~7;

    /// <summary>Encodes into a new array of exactly <see cref="SkyScopeFrame.ByteLength"/> bytes.</summary>
    public static byte[] Encode(SkyScopeFrame frame)
    {
        var bytes = new byte[frame.ByteLength];
        Encode(frame, bytes);
        return bytes;
    }

    /// <summary>Encodes into <paramref name="target"/> and returns the bytes written; throws when the target is too small.</summary>
    public static int Encode(SkyScopeFrame frame, Span<byte> target)
    {
        var len = frame.ByteLength;
        if (target.Length < len) throw new ArgumentException($"target too small: {target.Length} < {len}");
        var b = target[..len];
        BinaryPrimitives.WriteUInt32LittleEndian(b, Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(b[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(b[6..], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b[8..], frame.Seq);
        BinaryPrimitives.WriteUInt32LittleEndian(b[12..], (uint)frame.Channels.Count);
        BinaryPrimitives.WriteDoubleLittleEndian(b[16..], frame.T0);
        var off = HeaderBytes;
        foreach (var ch in frame.Channels)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b[off..], ch.Id);
            b[off + 2] = (byte)ch.Encoding;
            b[off + 3] = 0;
            BinaryPrimitives.WriteUInt32LittleEndian(b[(off + 4)..], (uint)ch.Count);
            var p = off + ChannelHeaderBytes;
            switch (ch.Encoding)
            {
                case FrameEncoding.Timestamped:
                    p = Write(b, p, ch.Times); p = Write(b, p, ch.Values); break;
                case FrameEncoding.Regular:
                    BinaryPrimitives.WriteDoubleLittleEndian(b[p..], ch.TStart);
                    BinaryPrimitives.WriteDoubleLittleEndian(b[(p + 8)..], ch.Dt);
                    p = Write(b, p + 16, ch.Values); break;
                case FrameEncoding.Quantized:
                    BinaryPrimitives.WriteDoubleLittleEndian(b[p..], ch.TStart);
                    BinaryPrimitives.WriteDoubleLittleEndian(b[(p + 8)..], ch.Dt);
                    BinaryPrimitives.WriteSingleLittleEndian(b[(p + 16)..], ch.Scale);
                    BinaryPrimitives.WriteSingleLittleEndian(b[(p + 20)..], ch.Offset);
                    p = Write(b, p + 24, ch.Q); break;
                default: throw new ArgumentOutOfRangeException(nameof(frame), $"bad-encoding:{(byte)ch.Encoding}");
            }
            var end = off + ch.ByteLength;
            b[p..end].Clear();
            off = end;
        }
        return len;
    }

    /// <summary>Parses a frame; throws <see cref="FormatException"/> on bad magic, version or encoding, or truncation.</summary>
    public static SkyScopeFrame Decode(ReadOnlySpan<byte> b)
    {
        if (b.Length < HeaderBytes) throw new FormatException("truncated-frame");
        if (BinaryPrimitives.ReadUInt32LittleEndian(b) != Magic) throw new FormatException("bad-magic");
        var version = BinaryPrimitives.ReadUInt16LittleEndian(b[4..]);
        if (version != Version) throw new FormatException($"unsupported-version:{version}");
        var seq = BinaryPrimitives.ReadUInt32LittleEndian(b[8..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(b[12..]);
        var t0 = BinaryPrimitives.ReadDoubleLittleEndian(b[16..]);
        var channels = new List<FrameChannel>((int)Math.Min(count, 4096));
        var off = HeaderBytes;
        for (var i = 0; i < count; i++)
        {
            if (off + ChannelHeaderBytes > b.Length) throw new FormatException("truncated-frame");
            var id = BinaryPrimitives.ReadUInt16LittleEndian(b[off..]);
            var code = b[off + 2];
            if (code > 2) throw new FormatException($"bad-encoding:{code}");
            var n = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(b[(off + 4)..]));
            var p = off + ChannelHeaderBytes;
            FrameChannel ch;
            switch ((FrameEncoding)code)
            {
                case FrameEncoding.Timestamped:
                    Need(b, p, 12 * n);
                    ch = FrameChannel.Timestamped(id, Read<double>(b, p, n), Read<float>(b, p + 8 * n, n));
                    p += 12 * n; break;
                case FrameEncoding.Regular:
                    Need(b, p, 16 + 4 * n);
                    ch = FrameChannel.Regular(id, BinaryPrimitives.ReadDoubleLittleEndian(b[p..]), BinaryPrimitives.ReadDoubleLittleEndian(b[(p + 8)..]), Read<float>(b, p + 16, n));
                    p += 16 + 4 * n; break;
                default:
                    Need(b, p, 24 + 2 * n);
                    ch = FrameChannel.Quantized(id, BinaryPrimitives.ReadDoubleLittleEndian(b[p..]), BinaryPrimitives.ReadDoubleLittleEndian(b[(p + 8)..]),
                        BinaryPrimitives.ReadSingleLittleEndian(b[(p + 16)..]), BinaryPrimitives.ReadSingleLittleEndian(b[(p + 20)..]), Read<short>(b, p + 24, n));
                    p += 24 + 2 * n; break;
            }
            channels.Add(ch);
            off += Pad8(p - off);
        }
        return new SkyScopeFrame(seq, t0, channels);
    }

    private static void Need(ReadOnlySpan<byte> b, int p, int n)
    {
        if (p + n > b.Length) throw new FormatException("truncated-frame");
    }

    private static int Write<T>(Span<byte> b, int p, T[] arr) where T : unmanaged
    {
        var src = MemoryMarshal.AsBytes(arr.AsSpan());
        src.CopyTo(b[p..]);
        if (!BitConverter.IsLittleEndian)
        {
            var size = src.Length / Math.Max(arr.Length, 1);
            for (var i = 0; i < arr.Length; i++) b.Slice(p + i * size, size).Reverse();
        }
        return p + src.Length;
    }

    private static T[] Read<T>(ReadOnlySpan<byte> b, int p, int n) where T : unmanaged
    {
        var size = Marshal.SizeOf<T>();
        var src = b.Slice(p, n * size);
        if (BitConverter.IsLittleEndian) return MemoryMarshal.Cast<byte, T>(src).ToArray();
        var result = new T[n];
        var dst = MemoryMarshal.AsBytes(result.AsSpan());
        src.CopyTo(dst);
        for (var i = 0; i < n; i++) dst.Slice(i * size, size).Reverse();
        return result;
    }
}
