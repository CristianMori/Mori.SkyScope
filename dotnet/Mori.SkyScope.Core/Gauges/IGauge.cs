// Mori.SkyScope — Anything that paints itself into a rectangle — every gauge and input.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Gauges;

/// <summary>Anything that paints itself into a rectangle — every gauge and input. Lets hosts (WPF, snapshots) treat them uniformly.</summary>
public interface IGaugeDrawable : Charts.IDrawable
{
}

/// <summary>A gauge whose displayed state approaches a target over time (needle damping).</summary>
public interface IAnimatedGauge : IGaugeDrawable
{
    /// <summary>Advances the animation by <paramref name="dt"/> seconds.</summary>
    void Step(double dt);
    /// <summary>True while the displayed state still differs from the target; hosts keep scheduling frames while it is.</summary>
    bool Animating { get; }
}
