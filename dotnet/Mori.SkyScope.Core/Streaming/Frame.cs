// Mori.SkyScope — SkyScopeFrame records: a frame, its channels and the three sample encodings.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Streaming;

/// <summary>Sample layout of a channel: explicit times, a regular rate, or a regular rate with int16-quantized values.</summary>
public enum FrameEncoding : byte { Timestamped = 0, Regular = 1, Quantized = 2 }

/// <summary>One channel's samples inside a <see cref="SkyScopeFrame"/>. Which fields apply depends on <see cref="Encoding"/>.</summary>
public sealed class FrameChannel
{
    /// <summary>Channel id.</summary>
    public ushort Id { get; init; }
    /// <summary>Which fields carry the samples.</summary>
    public FrameEncoding Encoding { get; init; }
    /// <summary>Timestamped only: time per sample in seconds.</summary>
    public double[] Times { get; init; } = [];
    /// <summary>Timestamped and Regular: f32 samples.</summary>
    public float[] Values { get; init; } = [];
    /// <summary>Regular and Quantized: time of the first sample in seconds.</summary>
    public double TStart { get; init; }
    /// <summary>Regular and Quantized: sample interval in seconds.</summary>
    public double Dt { get; init; }
    /// <summary>Quantized: value = q × Scale + Offset.</summary>
    public float Scale { get; init; }
    /// <summary>Quantized: value = q × Scale + Offset.</summary>
    public float Offset { get; init; }
    /// <summary>Quantized only: int16 codes.</summary>
    public short[] Q { get; init; } = [];

    /// <summary>Number of samples in the channel.</summary>
    public int Count => Encoding == FrameEncoding.Quantized ? Q.Length : Values.Length;

    /// <summary>Builds a timestamped channel; the arrays must have the same length.</summary>
    public static FrameChannel Timestamped(ushort id, double[] times, float[] values)
    {
        if (times.Length != values.Length) throw new ArgumentException("times/values length mismatch");
        return new() { Id = id, Encoding = FrameEncoding.Timestamped, Times = times, Values = values };
    }

    /// <summary>Builds a regular-rate channel.</summary>
    public static FrameChannel Regular(ushort id, double tStart, double dt, float[] values)
        => new() { Id = id, Encoding = FrameEncoding.Regular, TStart = tStart, Dt = dt, Values = values };

    /// <summary>Builds a quantized regular-rate channel.</summary>
    public static FrameChannel Quantized(ushort id, double tStart, double dt, float scale, float offset, short[] q)
        => new() { Id = id, Encoding = FrameEncoding.Quantized, TStart = tStart, Dt = dt, Scale = scale, Offset = offset, Q = q };

    /// <summary>Values as f32 for any encoding (dequantizes; f32 arithmetic to match the TS side bit-for-bit).</summary>
    public float[] GetValues()
    {
        if (Encoding != FrameEncoding.Quantized) return Values;
        var result = new float[Q.Length];
        for (var i = 0; i < result.Length; i++) result[i] = (float)((float)(Q[i] * Scale) + Offset);
        return result;
    }

    /// <summary>Encoded size in bytes including the channel header, padded to a multiple of 8.</summary>
    public int ByteLength
    {
        get
        {
            var body = Encoding switch
            {
                FrameEncoding.Timestamped => 12 * Count,
                FrameEncoding.Regular => 16 + 4 * Count,
                _ => 24 + 2 * Count,
            };
            return FrameCodec.Pad8(FrameCodec.ChannelHeaderBytes + body);
        }
    }
}

/// <summary>A batch of samples for many channels — the unit of the data plane.</summary>
/// <param name="Seq">Frame sequence number from the producer.</param>
/// <param name="T0">Reference time of the frame in seconds.</param>
/// <param name="Channels">Channels in wire order.</param>
public sealed record SkyScopeFrame(uint Seq, double T0, IReadOnlyList<FrameChannel> Channels)
{
    /// <summary>Encoded size in bytes: the frame header plus every channel.</summary>
    public int ByteLength => FrameCodec.HeaderBytes + Channels.Sum(c => c.ByteLength);
}
