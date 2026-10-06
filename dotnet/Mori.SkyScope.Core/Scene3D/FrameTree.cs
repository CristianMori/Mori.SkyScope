// Mori.SkyScope — tf-style frame tree: timed rigid transforms between named frames, lookups at a stamp, static and dynamic entries.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>Rigid transform of a child frame expressed in its parent: rotate by <see cref="Q"/>, then translate by <see cref="T"/>.</summary>
public readonly record struct Transform3(Vec3 T, Quat Q)
{
    /// <summary>No rotation and no translation.</summary>
    public static readonly Transform3 Identity = new(default, Quat.Identity);
}

/// <summary>
/// A tf2-style tree of coordinate frames with timed transforms. Mirrors <c>scene3d/frame-tree.ts</c>;
/// pinned by <c>spec/fixtures/frame-tree.json</c>.
/// </summary>
public sealed class FrameTree(int maxSamples = 200)
{
    private sealed record Sample(double Time, Vec3 T, Quat Q);
    private sealed class Entry(string parent) { public string Parent = parent; public List<Sample> Samples = []; public bool Static; }
    private readonly Dictionary<string, Entry> _frames = [];
    private int _version;

    /// <summary>Timed samples kept per frame; the oldest are dropped beyond this.</summary>
    public int MaxSamples { get; } = maxSamples;
    /// <summary>Incremented by every mutation; layers rebuild their geometry when it changes.</summary>
    public int Version => _version;

    /// <summary>Record the child's transform in its parent. A null <paramref name="time"/> makes the entry static, replacing any history; a timed sample is inserted in time order (the dynamic history replaces a static entry). Re-parenting discards earlier samples; <c>child == parent</c> is ignored.</summary>
    public void Set(string child, string parent, Transform3 tf, double? time = null)
    {
        if (child == parent) return;
        if (!_frames.TryGetValue(child, out var e) || e.Parent != parent) { e = new Entry(parent); _frames[child] = e; }
        var s = new Sample(time ?? 0, tf.T, tf.Q);
        if (time is null) { e.Samples = [s]; e.Static = true; }
        else
        {
            if (e.Static) { e.Samples = []; e.Static = false; }
            var arr = e.Samples; var t = time.Value;
            if (arr.Count == 0 || t >= arr[^1].Time) arr.Add(s);
            else { var i = arr.Count - 1; while (i >= 0 && arr[i].Time > t) i--; arr.Insert(i + 1, s); }
            if (arr.Count > MaxSamples) arr.RemoveRange(0, arr.Count - MaxSamples);
        }
        _version++;
    }
    /// <summary>Forget a frame's parent link and samples; its children keep pointing at it.</summary>
    public void Remove(string frame) { if (_frames.Remove(frame)) _version++; }
    /// <summary>Forget every frame.</summary>
    public void Clear() { if (_frames.Count > 0) { _frames.Clear(); _version++; } }

    /// <summary>True when the frame has a parent entry; roots that only appear as parents return false.</summary>
    public bool Has(string frame) => _frames.ContainsKey(frame);
    /// <summary>The parent frame id, or null for roots and unknown frames.</summary>
    public string? Parent(string frame) => _frames.TryGetValue(frame, out var e) ? e.Parent : null;
    /// <summary>Every frame that has a parent, plus every parent, sorted ordinally.</summary>
    public List<string> FrameIds()
    {
        var s = new HashSet<string>();
        foreach (var (c, e) in _frames) { s.Add(c); s.Add(e.Parent); }
        return s.Order(StringComparer.Ordinal).ToList();
    }
    /// <summary>Frames without a parent, sorted ordinally.</summary>
    public List<string> Roots() => FrameIds().Where(f => !_frames.ContainsKey(f)).ToList();
    /// <summary>Time of the newest dynamic sample, or null for static or unknown frames.</summary>
    public double? LatestTime(string frame) => _frames.TryGetValue(frame, out var e) && !e.Static && e.Samples.Count > 0 ? e.Samples[^1].Time : null;

    /// <summary>The child's transform in its parent at <paramref name="time"/> (latest when null); null when unknown.</summary>
    public Transform3? TransformAt(string child, double? time = null)
    {
        if (!_frames.TryGetValue(child, out var e) || e.Samples.Count == 0) return null;
        var arr = e.Samples;
        if (e.Static || time is null || arr.Count == 1) { var s = arr[^1]; return new(s.T, s.Q); }
        var t = time.Value;
        if (t <= arr[0].Time) return new(arr[0].T, arr[0].Q);
        if (t >= arr[^1].Time) { var s = arr[^1]; return new(s.T, s.Q); }
        int lo = 0, hi = arr.Count - 1;
        while (hi - lo > 1) { var mid = (lo + hi) >> 1; if (arr[mid].Time <= t) lo = mid; else hi = mid; }
        var a = arr[lo]; var b = arr[hi];
        var span = b.Time - a.Time; var u = span > 0 ? (t - a.Time) / span : 0;
        return new(Math3.Lerp(a.T, b.T, u), Quat.Slerp(a.Q, b.Q, u));
    }

    private (string Root, double[] M)? ToRoot(string frame, double? time)
    {
        var m = Mat4.Identity; var f = frame;
        var seen = new HashSet<string>();
        while (true)
        {
            if (!_frames.TryGetValue(f, out var e)) return (f, m);
            if (!seen.Add(f)) return null;
            var tf = TransformAt(f, time);
            if (tf is null) return null;
            m = Mat4.Mul(Mat4.FromPose(tf.Value.T, tf.Value.Q), m);
            f = e.Parent;
        }
    }

    /// <summary>Matrix mapping points expressed in <paramref name="source"/> into <paramref name="target"/>, or null when not connected.</summary>
    public double[]? Lookup(string target, string source, double? time = null)
    {
        if (target == source) return Mat4.Identity;
        var s = ToRoot(source, time); var t = ToRoot(target, time);
        if (s is null || t is null || s.Value.Root != t.Value.Root) return null;
        var inv = Mat4.Invert(t.Value.M);
        return inv is null ? null : Mat4.Mul(inv, s.Value.M);
    }
    /// <summary>True when <see cref="Lookup"/> would succeed (both frames reach the same root).</summary>
    public bool CanTransform(string target, string source, double? time = null) => Lookup(target, source, time) is not null;
}
