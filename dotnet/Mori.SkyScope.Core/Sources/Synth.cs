// Mori.SkyScope — Deterministic waveform synthesis (sine, square, triangle, sawtooth, ramp, noise) shared by demos and benches.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.Core.Sources;

/// <summary>Base shapes in [−1, 1] over one period, except <c>Ramp</c> (the time itself) and <c>Noise</c> (zero base, noise only).</summary>
public enum Waveform { Sine, Square, Triangle, Sawtooth, Ramp, Noise }

/// <summary>Parameters of one synthetic signal: <c>Offset + Amplitude · base(Frequency · t + Phase) + noise</c>.</summary>
/// <param name="Waveform">The base shape.</param>
public sealed record SynthSpec(Waveform Waveform)
{
    /// <summary>Hz.</summary>
    public double Frequency { get; init; } = 1;
    /// <summary>Multiplier of the base waveform.</summary>
    public double Amplitude { get; init; } = 1;
    /// <summary>Constant added to the signal.</summary>
    public double Offset { get; init; }
    /// <summary>Radians.</summary>
    public double Phase { get; init; }
    /// <summary>Amplitude of added uniform noise in [-Noise, +Noise].</summary>
    public double Noise { get; init; }
    /// <summary>Noise seed; the same seed and sample index always give the same noise.</summary>
    public int Seed { get; init; }
}

/// <summary>
/// Deterministic waveform generator, identical in both cores (pinned by <c>spec/fixtures/synthetic.json</c>).
/// Noise is a stateless integer hash of (seed, sample index) so any batch can be regenerated independently.
/// </summary>
public static class Synth
{
    /// <summary>Uniform [0, 1) from a 32-bit hash of (seed, index). uint32 ops only so TS matches bit-for-bit.</summary>
    public static double Hash01(int seed, int index)
    {
        unchecked
        {
            var u = ((uint)seed ^ 0x9E3779B9u) * 0x85EBCA6Bu ^ (uint)index * 0xC2B2AE35u;
            u ^= u >> 16; u *= 0x7FEB352Du;
            u ^= u >> 15; u *= 0x846CA68Bu;
            u ^= u >> 16;
            return u / 4294967296.0;
        }
    }

    private static double Frac(double x) => x - Math.Floor(x);

    /// <summary>One sample at time <paramref name="t"/> (seconds); <paramref name="index"/> is the absolute sample index that seeds the noise.</summary>
    public static double Value(SynthSpec spec, double t, int index)
    {
        var x = spec.Frequency * t + spec.Phase / (2 * Math.PI);
        var b = spec.Waveform switch
        {
            Waveform.Sine => Math.Sin(2 * Math.PI * spec.Frequency * t + spec.Phase),
            Waveform.Square => Frac(x) < 0.5 ? 1 : -1,
            Waveform.Triangle => 4 * Math.Abs(Frac(x) - 0.5) - 1,
            Waveform.Sawtooth => 2 * Frac(x) - 1,
            Waveform.Ramp => t,
            _ => 0,
        };
        var v = spec.Offset + spec.Amplitude * b;
        if (spec.Noise > 0) v += spec.Noise * (Hash01(spec.Seed, index) * 2 - 1);
        return v;
    }

    /// <summary><paramref name="count"/> samples from <paramref name="tStart"/> every <paramref name="dt"/>, with absolute sample indices from <paramref name="startIndex"/> (for noise).</summary>
    public static float[] Synthesize(SynthSpec spec, double tStart, double dt, int count, int startIndex = 0)
    {
        var result = new float[count];
        for (var k = 0; k < count; k++) result[k] = (float)Value(spec, tStart + k * dt, startIndex + k);
        return result;
    }
}
