// Mori.SkyScope — LZ4 decompression (block and frame formats) for MCAP chunks with compression "lz4".
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Buffers.Binary;

namespace Mori.SkyScope.Core.Mcap;

/// <summary>LZ4 decompression (block and frame formats) for MCAP chunks with compression "lz4". Checksums are not verified. Mirrors <c>recording/lz4.ts</c>.</summary>
public static class Lz4
{
    private const uint FrameMagic = 0x184d2204, SkippableMin = 0x184d2a50, SkippableMax = 0x184d2a5f;

    /// <summary>Decode one LZ4 block from <c>src[start, end)</c> into <paramref name="dst"/> at <paramref name="dstPos"/>; returns the new destination position.</summary>
    public static int DecodeBlock(ReadOnlySpan<byte> src, int start, int end, Span<byte> dst, int dstPos)
    {
        int i = start, o = dstPos;
        while (i < end)
        {
            var token = src[i++];
            int lit = token >> 4;
            if (lit == 15) { byte b; do { b = src[i++]; lit += b; } while (b == 255); }
            if (o + lit > dst.Length || i + lit > end) throw new InvalidDataException("lz4: block overruns its output or input");
            src.Slice(i, lit).CopyTo(dst[o..]); i += lit; o += lit;
            if (i >= end) break;
            int offset = src[i] | (src[i + 1] << 8); i += 2;
            if (offset == 0 || offset > o - dstPos) throw new InvalidDataException("lz4: bad match offset");
            int len = token & 15;
            if (len == 15) { byte b; do { b = src[i++]; len += b; } while (b == 255); }
            len += 4;
            if (o + len > dst.Length) throw new InvalidDataException("lz4: match overruns the output");
            var from = o - offset;
            if (offset >= len) { dst.Slice(from, len).CopyTo(dst[o..]); o += len; }
            else for (var k = 0; k < len; k++) dst[o++] = dst[from++];
        }
        return o;
    }

    /// <summary>Decode an LZ4 frame (possibly several, skippable frames allowed) into a buffer of <paramref name="expectedSize"/> bytes.</summary>
    public static byte[] DecodeFrame(ReadOnlySpan<byte> bytes, int expectedSize)
    {
        var dst = new byte[expectedSize];
        int i = 0, o = 0;
        while (i + 4 <= bytes.Length)
        {
            var magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]); i += 4;
            if (magic is >= SkippableMin and <= SkippableMax) { var n = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]); i += 4 + n; continue; }
            if (magic != FrameMagic) throw new InvalidDataException("lz4: not a frame");
            var flg = bytes[i]; i += 2;
            if ((flg >> 6) != 1) throw new InvalidDataException("lz4: unsupported frame version");
            bool blockChecksum = (flg & 0x10) != 0, contentSize = (flg & 0x08) != 0, contentChecksum = (flg & 0x04) != 0, dictId = (flg & 0x01) != 0;
            if (contentSize) i += 8;
            if (dictId) i += 4;
            i += 1;
            while (true)
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes[i..]); i += 4;
                if (size == 0) break;
                var uncompressed = (size & 0x80000000) != 0; var n = (int)(size & 0x7fffffff);
                if (uncompressed) { bytes.Slice(i, n).CopyTo(dst.AsSpan(o)); o += n; }
                else o = DecodeBlock(bytes, i, i + n, dst, o);
                i += n;
                if (blockChecksum) i += 4;
            }
            if (contentChecksum) i += 4;
        }
        return o == expectedSize ? dst : dst[..o];
    }

    /// <summary>MCAP chunk payload → records: frame format when it starts with the frame magic, otherwise a raw block.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> bytes, int uncompressedSize)
    {
        if (bytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) == FrameMagic) return DecodeFrame(bytes, uncompressedSize);
        var dst = new byte[uncompressedSize];
        var n = DecodeBlock(bytes, 0, bytes.Length, dst, 0);
        return n == uncompressedSize ? dst : dst[..n];
    }
}
