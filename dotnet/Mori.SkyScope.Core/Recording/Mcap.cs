// Mori.SkyScope — Minimal MCAP (https://mcap.dev) writer and reader — enough for SkyScope recordings and for reading unchunked or uncompressed-chunk files …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Buffers.Binary;
using System.Text;

namespace Mori.SkyScope.Core.Mcap;

/// <summary>
/// Minimal MCAP (https://mcap.dev) writer and reader — enough for SkyScope recordings and for reading unchunked or
/// uncompressed-chunk files written by other tools. Writes: magic, Header, Schema/Channel/Message records, DataEnd, a summary
/// (schemas, channels, statistics, summary offsets) and the Footer; no CRCs. By default no chunks (byte-identical with the
/// TypeScript writer); with <see cref="McapWriterOptions.Compression"/> the records go into zstd chunks with message and
/// chunk indexes. Reads: everything above plus Chunk records with compression "", "lz4" or "zstd" (zstd through the
/// ZstdSharp.Port package; any other compression raises a clear error). Mirrors <c>recording/mcap.ts</c>; pinned by
/// <c>spec/fixtures/mcap.json</c> and <c>spec/mcap/*.mcap</c>.
/// </summary>
public static class McapOp
{
    public const byte Header = 1, Footer = 2, Schema = 3, Channel = 4, Message = 5, Chunk = 6, MessageIndex = 7, ChunkIndex = 8, Attachment = 9, AttachmentIndex = 10, Statistics = 11, Metadata = 12, MetadataIndex = 13, SummaryOffset = 14, DataEnd = 15;
}

/// <summary>Conversions between seconds and MCAP nanosecond timestamps.</summary>
public static class McapTime
{
    /// <summary>Seconds (double) → nanoseconds, exact to the nanosecond for epoch-scale times (same arithmetic as the TS core).</summary>
    public static ulong SecondsToNs(double t) { var whole = Math.Floor(t); return (ulong)whole * 1_000_000_000UL + (ulong)Math.Round((t - whole) * 1e9); }
    /// <summary>Nanoseconds → seconds (double).</summary>
    public static double NsToSeconds(ulong ns) => (double)(ns / 1_000_000_000UL) + (ns % 1_000_000_000UL) / 1e9;
}

/// <summary>Growable little-endian byte writer.</summary>
public sealed class ByteWriter
{
    private byte[] _buf = new byte[4096];
    /// <summary>Bytes written so far.</summary>
    public int Length { get; private set; }
    private void Ensure(int n) { if (Length + n <= _buf.Length) return; var size = _buf.Length * 2; while (size < Length + n) size *= 2; Array.Resize(ref _buf, size); }
    /// <summary>Appends one byte.</summary>
    public void U8(byte v) { Ensure(1); _buf[Length++] = v; }
    /// <summary>Appends a little-endian u16.</summary>
    public void U16(ushort v) { Ensure(2); BinaryPrimitives.WriteUInt16LittleEndian(_buf.AsSpan(Length), v); Length += 2; }
    /// <summary>Appends a little-endian u32.</summary>
    public void U32(uint v) { Ensure(4); BinaryPrimitives.WriteUInt32LittleEndian(_buf.AsSpan(Length), v); Length += 4; }
    /// <summary>Appends a little-endian u64.</summary>
    public void U64(ulong v) { Ensure(8); BinaryPrimitives.WriteUInt64LittleEndian(_buf.AsSpan(Length), v); Length += 8; }
    /// <summary>Appends raw bytes.</summary>
    public void Bytes(ReadOnlySpan<byte> b) { Ensure(b.Length); b.CopyTo(_buf.AsSpan(Length)); Length += b.Length; }
    /// <summary>u32 length + UTF-8.</summary>
    public void Str(string s) { var b = Encoding.UTF8.GetBytes(s); U32((uint)b.Length); Bytes(b); }
    /// <summary>u32 byte length + bytes.</summary>
    public void Data(ReadOnlySpan<byte> b) { U32((uint)b.Length); Bytes(b); }
    /// <summary>Writes a record: opcode, u64 length, content.</summary>
    public void Record(byte op, ByteWriter content) { U8(op); U64((ulong)content.Length); Bytes(content.Span); }
    /// <summary>The bytes written so far (valid until the next write).</summary>
    public ReadOnlySpan<byte> Span => _buf.AsSpan(0, Length);
    /// <summary>Copy of the bytes written so far.</summary>
    public byte[] ToArray() => _buf.AsSpan(0, Length).ToArray();
}

/// <summary>Little-endian reader over a slice of a byte array; no bounds checks beyond the array's own.</summary>
/// <param name="bytes">Backing array.</param>
/// <param name="start">First position.</param>
/// <param name="end">Position after the last readable byte; the array length when negative.</param>
public sealed class ByteReader(byte[] bytes, int start = 0, int end = -1)
{
    /// <summary>Backing array.</summary>
    public readonly byte[] Bytes = bytes;
    /// <summary>Next position to read.</summary>
    public int Pos = start;
    /// <summary>Position after the last readable byte.</summary>
    public readonly int End = end < 0 ? bytes.Length : end;
    /// <summary>Bytes left before <see cref="End"/>.</summary>
    public int Remaining => End - Pos;
    /// <summary>Reads one byte.</summary>
    public byte U8() => Bytes[Pos++];
    /// <summary>Reads a little-endian u16.</summary>
    public ushort U16() { var v = BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(Pos)); Pos += 2; return v; }
    /// <summary>Reads a little-endian u32.</summary>
    public uint U32() { var v = BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(Pos)); Pos += 4; return v; }
    /// <summary>Reads a little-endian u64.</summary>
    public ulong U64() { var v = BinaryPrimitives.ReadUInt64LittleEndian(Bytes.AsSpan(Pos)); Pos += 8; return v; }
    /// <summary>Copies the next <paramref name="n"/> bytes.</summary>
    public byte[] Take(int n) { var b = Bytes.AsSpan(Pos, n).ToArray(); Pos += n; return b; }
    /// <summary>Reads a u32 length followed by UTF-8 text.</summary>
    public string Str() { var n = (int)U32(); var s = Encoding.UTF8.GetString(Bytes, Pos, n); Pos += n; return s; }
    /// <summary>Reads a u32 length followed by that many bytes.</summary>
    public byte[] Data() => Take((int)U32());
}

/// <summary>A Schema record: 1-based id, name, encoding (e.g. jsonschema) and the schema bytes.</summary>
public sealed record McapSchema(ushort Id, string Name, string Encoding, byte[] Data);
/// <summary>A Channel record: 0-based id, the schema it uses, topic, message encoding and string metadata.</summary>
public sealed record McapChannel(ushort Id, ushort SchemaId, string Topic, string MessageEncoding, IReadOnlyDictionary<string, string> Metadata);
/// <summary>A Message record with log and publish times converted to seconds.</summary>
public sealed record McapMessage(ushort ChannelId, uint Sequence, double LogTime, double PublishTime, byte[] Data);
/// <summary>Contents of a parsed file: header fields, schemas and channels by id, and messages sorted by log time.</summary>
public sealed class McapFile
{
    /// <summary>Header profile string.</summary>
    public string Profile { get; set; } = "";
    /// <summary>Header library string.</summary>
    public string Library { get; set; } = "";
    /// <summary>Schemas by id; the first definition of an id wins.</summary>
    public Dictionary<ushort, McapSchema> Schemas { get; } = [];
    /// <summary>Channels by id; the first definition of an id wins.</summary>
    public Dictionary<ushort, McapChannel> Channels { get; } = [];
    /// <summary>Messages in log-time order (stable with respect to file order).</summary>
    public List<McapMessage> Messages { get; } = [];
    /// <summary>Earliest message log time in seconds; 0 when there are no messages.</summary>
    public double MessageStart { get; set; } = double.PositiveInfinity;
    /// <summary>Latest message log time in seconds; 0 when there are no messages.</summary>
    public double MessageEnd { get; set; } = double.NegativeInfinity;
}

/// <summary>Where a writer puts its records: an in-memory buffer or a counting pass-through to a stream.</summary>
internal interface IOutput
{
    /// <summary>Bytes written so far.</summary>
    int Length { get; }
    /// <summary>Appends raw bytes.</summary>
    void Bytes(ReadOnlySpan<byte> b);
    /// <summary>Appends one MCAP record: opcode, 8-byte length, content.</summary>
    void Record(byte op, ByteWriter content);
    /// <summary>The whole file for an in-memory output; empty for a stream output.</summary>
    byte[] ToArray();
}
/// <summary>Collects the file in memory.</summary>
internal sealed class BufferOutput : IOutput
{
    private readonly ByteWriter _w = new();
    /// <inheritdoc/>
    public int Length => _w.Length;
    /// <inheritdoc/>
    public void Bytes(ReadOnlySpan<byte> b) => _w.Bytes(b);
    /// <inheritdoc/>
    public void Record(byte op, ByteWriter content) => _w.Record(op, content);
    /// <inheritdoc/>
    public byte[] ToArray() => _w.ToArray();
}
/// <summary>Writes straight to a stream, counting bytes; used for recordings larger than memory.</summary>
internal sealed class StreamOutput(Stream stream) : IOutput
{
    /// <inheritdoc/>
    public int Length { get; private set; }
    /// <inheritdoc/>
    public void Bytes(ReadOnlySpan<byte> b) { stream.Write(b); Length += b.Length; }
    /// <inheritdoc/>
    public void Record(byte op, ByteWriter content) { Span<byte> h = stackalloc byte[9]; h[0] = op; BinaryPrimitives.WriteUInt64LittleEndian(h[1..], (ulong)content.Length); Bytes(h); Bytes(content.Span); }
    /// <inheritdoc/>
    public byte[] ToArray() { stream.Flush(); return []; }
}

/// <summary>Chunk compression of an <see cref="McapWriter"/>.</summary>
public enum McapCompression
{
    /// <summary>No chunks: every record goes straight to the file, byte-identical with the TypeScript writer.</summary>
    None,
    /// <summary>Records are grouped into Chunk records compressed with zstd (level 3, through ZstdSharp.Port).</summary>
    Zstd,
}

/// <summary>Header strings and chunking of an <see cref="McapWriter"/>.</summary>
public sealed record McapWriterOptions
{
    /// <summary>Header profile string.</summary>
    public string Profile { get; init; } = "skyscope";
    /// <summary>Header library string.</summary>
    public string Library { get; init; } = "Mori.SkyScope 0.1.0";
    /// <summary>Chunk compression; <see cref="McapCompression.None"/> (the default) writes no chunks at all.</summary>
    public McapCompression Compression { get; init; } = McapCompression.None;
    /// <summary>Uncompressed bytes a chunk collects before it is compressed and written (default 1 MiB); ignored without compression.</summary>
    public int ChunkBytes { get; init; } = 1 << 20;
}

/// <summary>
/// Deterministic writer: the same calls produce the same bytes in both cores, whether buffered or streamed. With
/// <see cref="McapWriterOptions.Compression"/> set, schema, channel and message records are collected into chunks of
/// about <see cref="McapWriterOptions.ChunkBytes"/>, each written as a compressed Chunk record followed by one Message
/// Index record per channel, with a Chunk Index per chunk in the summary section (what the MCAP specification asks of
/// a chunked, indexed file).
/// </summary>
public sealed class McapWriter
{
    /// <summary>The 8-byte MCAP magic that opens and closes a file.</summary>
    public static readonly byte[] Magic = [0x89, 0x4d, 0x43, 0x41, 0x50, 0x30, 0x0d, 0x0a];
    private readonly IOutput _out;
    /// <summary>True when records go to a stream as they are written (the file is never held in memory).</summary>
    public bool Streaming { get; }
    /// <summary>Chunk compression in use.</summary>
    public McapCompression Compression { get; }
    private readonly int _chunkBytes;
    private readonly List<McapSchema> _schemas = [];
    private readonly List<McapChannel> _channels = [];
    private readonly Dictionary<ushort, uint> _sequences = [];
    private readonly Dictionary<ushort, ulong> _counts = [];
    private ulong _messageCount;
    private ulong _start = ulong.MaxValue, _end;
    private bool _finished;
    // chunking state: the pending chunk's records, its message time span and per-channel (log time, offset) index
    private ByteWriter? _chunk;
    private ulong _chunkStart = ulong.MaxValue, _chunkEnd;
    private readonly SortedDictionary<ushort, List<(ulong Time, ulong Offset)>> _chunkIndex = [];
    private readonly List<ByteWriter> _chunkIndexes = [];

    /// <summary>Buffers the file in memory; <see cref="Finish"/> returns it.</summary>
    /// <param name="profile">Header profile string.</param>
    /// <param name="library">Header library string.</param>
    public McapWriter(string profile = "skyscope", string library = "Mori.SkyScope 0.1.0") : this(null, profile, library) { }
    /// <param name="stream">Write through to this stream instead of buffering; <see cref="Finish"/> then returns an empty array.</param>
    /// <param name="profile">Header profile string.</param>
    /// <param name="library">Header library string.</param>
    public McapWriter(Stream? stream, string profile = "skyscope", string library = "Mori.SkyScope 0.1.0") : this(stream, new McapWriterOptions { Profile = profile, Library = library }) { }
    /// <param name="stream">Write through to this stream instead of buffering; <see cref="Finish"/> then returns an empty array.</param>
    /// <param name="options">Header strings and chunking.</param>
    public McapWriter(Stream? stream, McapWriterOptions options)
    {
        _out = stream is null ? new BufferOutput() : new StreamOutput(stream);
        Streaming = stream is not null;
        Compression = options.Compression;
        _chunkBytes = Math.Max(1, options.ChunkBytes);
        if (Compression != McapCompression.None) _chunk = new ByteWriter();
        _out.Bytes(Magic);
        var h = new ByteWriter(); h.Str(options.Profile); h.Str(options.Library);
        _out.Record(McapOp.Header, h);
    }

    /// <summary>Bytes written so far, including those already streamed out and the uncompressed records of the pending chunk.</summary>
    public int Length => _out.Length + (_chunk?.Length ?? 0);

    /// <summary>A data-section record: straight to the output, or into the pending chunk when chunking.</summary>
    private void Emit(byte op, ByteWriter content) { if (_chunk is null) _out.Record(op, content); else _chunk.Record(op, content); }

    /// <summary>Writes a Schema record and returns its 1-based id.</summary>
    public ushort AddSchema(string name, string encoding, byte[] data)
    {
        var s = new McapSchema((ushort)(_schemas.Count + 1), name, encoding, data);
        _schemas.Add(s); Emit(McapOp.Schema, SchemaRecord(s));
        return s.Id;
    }
    /// <summary>Writes a Schema record with UTF-8 text content and returns its 1-based id.</summary>
    public ushort AddSchema(string name, string encoding, string data) => AddSchema(name, encoding, Encoding.UTF8.GetBytes(data));
    /// <summary>Writes a Channel record and returns its 0-based id; metadata is written in ordinal key order.</summary>
    public ushort AddChannel(string topic, ushort schemaId, string messageEncoding, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var c = new McapChannel((ushort)_channels.Count, schemaId, topic, messageEncoding, metadata ?? new Dictionary<string, string>());
        _channels.Add(c); _sequences[c.Id] = 0; _counts[c.Id] = 0;
        Emit(McapOp.Channel, ChannelRecord(c));
        return c.Id;
    }
    /// <summary>Writes a Message with publish time equal to log time.</summary>
    public void AddMessage(ushort channelId, ulong logTimeNs, ReadOnlySpan<byte> data) => AddMessage(channelId, logTimeNs, logTimeNs, data);
    /// <summary>Writes a Message record; the per-channel sequence number increments automatically. Throws after <see cref="Finish"/> or for an unknown channel.</summary>
    public void AddMessage(ushort channelId, ulong logTimeNs, ulong publishTimeNs, ReadOnlySpan<byte> data)
    {
        if (_finished) throw new InvalidOperationException("McapWriter: already finished");
        if (!_sequences.TryGetValue(channelId, out var seq)) throw new ArgumentException($"McapWriter: unknown channel {channelId}", nameof(channelId));
        _sequences[channelId] = seq + 1; _counts[channelId]++; _messageCount++;
        if (logTimeNs < _start) _start = logTimeNs;
        if (logTimeNs > _end) _end = logTimeNs;
        var m = new ByteWriter(); m.U16(channelId); m.U32(seq); m.U64(logTimeNs); m.U64(publishTimeNs); m.Bytes(data);
        if (_chunk is null) { _out.Record(McapOp.Message, m); return; }
        if (!_chunkIndex.TryGetValue(channelId, out var entries)) _chunkIndex[channelId] = entries = [];
        entries.Add((logTimeNs, (ulong)_chunk.Length));
        if (logTimeNs < _chunkStart) _chunkStart = logTimeNs;
        if (logTimeNs > _chunkEnd) _chunkEnd = logTimeNs;
        _chunk.Record(McapOp.Message, m);
        if (_chunk.Length >= _chunkBytes) FlushChunk();
    }

    /// <summary>Compresses the pending chunk and writes it as a Chunk record, its Message Index records and remembers its Chunk Index.</summary>
    private void FlushChunk()
    {
        if (_chunk is null || _chunk.Length == 0) return;
        var records = _chunk.Span;
        var compression = Compression switch { McapCompression.Zstd => "zstd", _ => "" };
        var compressed = Compression switch { McapCompression.Zstd => ZstdCompress(records), _ => records.ToArray() };
        var start = _chunkStart == ulong.MaxValue ? 0 : _chunkStart;
        var c = new ByteWriter(); c.U64(start); c.U64(_chunkEnd); c.U64((ulong)records.Length); c.U32(0); c.Str(compression); c.U64((ulong)compressed.Length); c.Bytes(compressed);
        var chunkOffset = (ulong)_out.Length;
        _out.Record(McapOp.Chunk, c);
        var chunkLength = (ulong)_out.Length - chunkOffset;
        var indexOffsets = new ByteWriter();
        var indexStart = _out.Length;
        foreach (var (channel, entries) in _chunkIndex)
        {
            indexOffsets.U16(channel); indexOffsets.U64((ulong)_out.Length);
            var mi = new ByteWriter(); mi.U16(channel);
            var list = new ByteWriter(); foreach (var (time, offset) in entries) { list.U64(time); list.U64(offset); }
            mi.Data(list.Span);
            _out.Record(McapOp.MessageIndex, mi);
        }
        var ci = new ByteWriter();
        ci.U64(start); ci.U64(_chunkEnd); ci.U64(chunkOffset); ci.U64(chunkLength); ci.Data(indexOffsets.Span); ci.U64((ulong)(_out.Length - indexStart));
        ci.Str(compression); ci.U64((ulong)compressed.Length); ci.U64((ulong)records.Length);
        _chunkIndexes.Add(ci);
        _chunk = new ByteWriter(); _chunkStart = ulong.MaxValue; _chunkEnd = 0; _chunkIndex.Clear();
    }

    /// <summary>One zstd frame (level 3) holding the chunk's records.</summary>
    private static byte[] ZstdCompress(ReadOnlySpan<byte> records)
    {
        using var compressor = new ZstdSharp.Compressor(3);
        return compressor.Wrap(records).ToArray();
    }

    /// <summary>Flushes the pending chunk, then writes DataEnd, the summary section (schemas, channels, chunk indexes, statistics), summary offsets, footer and magic. Returns the file (empty when streaming).</summary>
    public byte[] Finish()
    {
        if (_finished) return _out.ToArray();
        _finished = true;
        FlushChunk();
        var de = new ByteWriter(); de.U32(0); _out.Record(McapOp.DataEnd, de);
        var summaryStart = (ulong)_out.Length;
        var groups = new List<(byte Op, ulong Start, ulong Length)>();
        void Group(byte op, Action write) { var s = _out.Length; write(); if (_out.Length > s) groups.Add((op, (ulong)s, (ulong)(_out.Length - s))); }
        Group(McapOp.Schema, () => { foreach (var s in _schemas) _out.Record(McapOp.Schema, SchemaRecord(s)); });
        Group(McapOp.Channel, () => { foreach (var c in _channels) _out.Record(McapOp.Channel, ChannelRecord(c)); });
        Group(McapOp.ChunkIndex, () => { foreach (var ci in _chunkIndexes) _out.Record(McapOp.ChunkIndex, ci); });
        Group(McapOp.Statistics, () =>
        {
            var st = new ByteWriter();
            st.U64(_messageCount); st.U16((ushort)_schemas.Count); st.U32((uint)_channels.Count); st.U32(0); st.U32(0); st.U32((uint)_chunkIndexes.Count);
            st.U64(_start == ulong.MaxValue ? 0 : _start); st.U64(_end);
            var cm = new ByteWriter(); foreach (var c in _channels) { cm.U16(c.Id); cm.U64(_counts[c.Id]); }
            st.Data(cm.Span);
            _out.Record(McapOp.Statistics, st);
        });
        var summaryOffsetStart = (ulong)_out.Length;
        foreach (var g in groups) { var so = new ByteWriter(); so.U8(g.Op); so.U64(g.Start); so.U64(g.Length); _out.Record(McapOp.SummaryOffset, so); }
        var f = new ByteWriter(); f.U64(summaryStart); f.U64(summaryOffsetStart); f.U32(0);
        _out.Record(McapOp.Footer, f);
        _out.Bytes(Magic);
        return _out.ToArray();
    }

    private static ByteWriter SchemaRecord(McapSchema s) { var w = new ByteWriter(); w.U16(s.Id); w.Str(s.Name); w.Str(s.Encoding); w.Data(s.Data); return w; }
    private static ByteWriter ChannelRecord(McapChannel c)
    {
        var w = new ByteWriter(); w.U16(c.Id); w.U16(c.SchemaId); w.Str(c.Topic); w.Str(c.MessageEncoding);
        var md = new ByteWriter(); foreach (var k in c.Metadata.Keys.Order(StringComparer.Ordinal)) { md.Str(k); md.Str(c.Metadata[k]); }
        w.Data(md.Span);
        return w;
    }
}

/// <summary>Reads whole MCAP files from memory into an <see cref="McapFile"/>.</summary>
public static class McapReader
{
    /// <summary>Parse a whole file from memory. Summary records are ignored; chunks may be uncompressed, lz4 or zstd.</summary>
    public static McapFile Read(byte[] bytes)
    {
        if (bytes.Length < 16 || !bytes.AsSpan(0, 8).SequenceEqual(McapWriter.Magic)) throw new InvalidDataException("not an MCAP file (bad magic)");
        var file = new McapFile();
        ReadRecords(file, new ByteReader(bytes, McapWriter.Magic.Length), false);
        if (file.Messages.Count == 0) { file.MessageStart = 0; file.MessageEnd = 0; }
        var ordered = file.Messages.OrderBy(m => m.LogTime).ToList(); // stable, like Array.prototype.sort
        file.Messages.Clear(); file.Messages.AddRange(ordered);
        return file;
    }

    private static void ReadRecords(McapFile file, ByteReader r, bool nested)
    {
        while (r.Remaining >= 9)
        {
            var op = r.U8(); var len = (int)r.U64(); var start = r.Pos; var end = start + len;
            if (end > r.End) throw new InvalidDataException($"MCAP: truncated record (opcode {op})");
            var body = new ByteReader(r.Bytes, start, end);
            switch (op)
            {
                case McapOp.Header: file.Profile = body.Str(); file.Library = body.Str(); break;
                case McapOp.Schema: { var id = body.U16(); if (!file.Schemas.ContainsKey(id)) file.Schemas[id] = new McapSchema(id, body.Str(), body.Str(), body.Data()); break; }
                case McapOp.Channel:
                    {
                        var id = body.U16();
                        if (!file.Channels.ContainsKey(id))
                        {
                            var schemaId = body.U16(); var topic = body.Str(); var enc = body.Str();
                            var mdLen = (int)body.U32(); var md = new ByteReader(r.Bytes, body.Pos, body.Pos + mdLen);
                            var metadata = new Dictionary<string, string>(); while (md.Remaining > 0) { var k = md.Str(); metadata[k] = md.Str(); }
                            file.Channels[id] = new McapChannel(id, schemaId, topic, enc, metadata);
                        }
                        break;
                    }
                case McapOp.Message:
                    {
                        var channelId = body.U16(); var seq = body.U32(); var logTime = McapTime.NsToSeconds(body.U64()); var pubTime = McapTime.NsToSeconds(body.U64());
                        file.Messages.Add(new McapMessage(channelId, seq, logTime, pubTime, r.Bytes.AsSpan(body.Pos, end - body.Pos).ToArray()));
                        if (logTime < file.MessageStart) file.MessageStart = logTime;
                        if (logTime > file.MessageEnd) file.MessageEnd = logTime;
                        break;
                    }
                case McapOp.Chunk:
                    {
                        if (nested) throw new InvalidDataException("MCAP: nested chunk");
                        body.U64(); body.U64(); var uncompressed = (long)body.U64(); body.U32();
                        var compression = body.Str(); var recordsLength = (int)body.U64();
                        if (compression == "")
                        {
                            if (recordsLength != uncompressed && uncompressed != 0) throw new InvalidDataException("MCAP: chunk size mismatch");
                            ReadRecords(file, new ByteReader(r.Bytes, body.Pos, body.Pos + recordsLength), true);
                        }
                        else if (compression == "lz4")
                        {
                            var decoded = Lz4.Decompress(r.Bytes.AsSpan(body.Pos, recordsLength), (int)uncompressed);
                            ReadRecords(file, new ByteReader(decoded), true);
                        }
                        else if (compression == "zstd")
                        {
                            ReadRecords(file, new ByteReader(ZstdDecompress(r.Bytes.AsSpan(body.Pos, recordsLength), uncompressed)), true);
                        }
                        else throw new NotSupportedException($"MCAP: chunk compression \"{compression}\" is not supported (uncompressed, lz4 and zstd chunks are)");
                        break;
                    }
                case McapOp.DataEnd: return;
                case McapOp.Footer: return;
            }
            r.Pos = end;
        }
    }

    /// <summary>Decodes a zstd-compressed chunk into a buffer of <paramref name="uncompressedSize"/> bytes (the frame's own content size when the chunk header says 0).</summary>
    private static byte[] ZstdDecompress(ReadOnlySpan<byte> src, long uncompressedSize)
    {
        using var decompressor = new ZstdSharp.Decompressor();
        if (uncompressedSize <= 0) return decompressor.Unwrap(src).ToArray();
        var dst = new byte[uncompressedSize];
        var n = decompressor.Unwrap(src, dst);
        if (n != dst.Length) throw new InvalidDataException("MCAP: chunk size mismatch");
        return dst;
    }
}
