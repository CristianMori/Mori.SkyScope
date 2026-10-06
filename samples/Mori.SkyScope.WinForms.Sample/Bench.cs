// Mori.SkyScope — Frame-rate measurement of the Windows Forms dashboard on screen, with synthetic signals and a random point cloud: --bench.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Diagnostics;
using Mori.SkyScope.Core.Scene3D;

namespace Mori.SkyScope.WinForms.Sample;

/// <summary>
/// Shows the dashboard (no server needed), fills the trend chart with synthetic signals, puts a grid and a random point
/// cloud into the 3D view, lets both animate for a while and prints how many frames per second each one painted.
/// By default the controls repaint from their own timers (33 ms for the chart, 16 ms for the 3D view) through the
/// message pump, as the live dashboard does, so the rates are capped the same way and measured against wall time;
/// <c>--uncapped</c> paints each control frame after frame with <see cref="SkiaHostControl.PaintNow"/> instead and
/// divides its frames by the time spent in its own paints, so each rate is what that control alone could sustain.
/// A desktop whose window manager delivers no paint messages (a session no one is looking at) is detected during
/// the warm-up and measured the uncapped way, which the output says.
/// </summary>
internal static class Bench
{
    /// <summary>Runs the measurement and prints one line; returns false when the form could not be created.</summary>
    /// <param name="points">Points in the random cloud.</param>
    /// <param name="seconds">Measurement length, after a one-second warm-up.</param>
    /// <param name="rendering">Surface requested for every control, the 3D view included (on the CPU it paints only its notice).</param>
    /// <param name="uncapped">Paint both controls frame after frame instead of from their timers.</param>
    /// <param name="shot">Path of a PNG to save the 3D view's pixels to after the measurement, to check what was drawn; null saves nothing.</param>
    public static bool Run(int points, double seconds, RenderingMode rendering, bool uncapped, string? shot = null)
    {
        try
        {
            using var form = new MainForm("ws://localhost:1/ws", rendering);
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0);
            form.Show();
            var area = Screen.FromControl(form).WorkingArea;
            form.ClientSize = new Size(Math.Min(1400, area.Width - 20), Math.Min(820, area.Height - 60));
            var chart = (TrendChartControl)Snapshot.FindControl(form, typeof(TrendChartControl))!;
            var scene = (SceneControl3D)Snapshot.FindControl(form, typeof(SceneControl3D))!;
            if (scene.Parent?.Parent is SplitContainer split) split.SplitterDistance = split.Width / 2;   // chart and 3D view side by side, half the width each
            scene.Rendering = rendering;
            Application.DoEvents();
            Snapshot.Fill(form);

            if (!scene.Scene.Layers.OfType<Grid3DLayer>().Any()) scene.Scene.Add(new Grid3DLayer("grid"));
            var cloud = new PointCloud3DLayer("bench-cloud") { PointSize = 2 };
            cloud.SetPoints(RandomCloud(points, out var intensities), intensities);
            scene.Scene.Add(cloud);
            scene.FitAll();
            scene.Animate = true;

            var note = uncapped ? ", uncapped" : "";
            var warm = Pump(1, chart, scene, uncapped);   // warm-up: shaders, buffers and the JIT
            if (!uncapped && warm.ChartFrames == 0 && warm.SceneFrames == 0)
            {
                // nothing painted through the pump: the window manager is not sending paint messages on this desktop
                uncapped = true; note = ", uncapped (no paint messages on this desktop)";
                Pump(1, chart, scene, uncapped);
            }
            var m = Pump(seconds, chart, scene, uncapped);
            var sceneNote = scene.EffectiveRendering == RenderingMode.Gpu ? "" : " (no OpenGL: notice only)";
            var sizes = $"{chart.ClientSize.Width}x{chart.ClientSize.Height} + {scene.ClientSize.Width}x{scene.ClientSize.Height} px";
            var gl = chart.EffectiveRendering == RenderingMode.Gpu && SkiaHostControl.OpenGlRenderer is { } r ? $" on {r}" + (scene.Samples > 0 ? $", {scene.Samples} samples per pixel" : ", no multisampling") : "";
            Console.WriteLine($"trend: {m.ChartFrames / m.ChartSeconds:0.0} fps, scene3d: {m.SceneFrames / m.SceneSeconds:0.0} fps{sceneNote}, points {points}, rendering {chart.EffectiveRendering}{note}, {sizes}{gl}");
            if (shot is not null) { using var pixels = scene.Snapshot(); if (pixels is not null) { pixels.Save(shot, System.Drawing.Imaging.ImageFormat.Png); Console.WriteLine($"wrote {shot} ({pixels.Width}x{pixels.Height})"); } }
            form.Close();
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return false; }
    }

    /// <summary>Frames each control painted and the seconds to divide them by: wall time through the pump, the control's own paint time when uncapped.</summary>
    private readonly record struct Measure(long ChartFrames, double ChartSeconds, long SceneFrames, double SceneSeconds);

    /// <summary>Keeps the message pump alive for <paramref name="seconds"/>, painting both controls each pass when <paramref name="uncapped"/>.</summary>
    private static Measure Pump(double seconds, TrendChartControl chart, SceneControl3D scene, bool uncapped)
    {
        long chart0 = chart.FramesPainted, scene0 = scene.FramesPainted;
        var wall = Stopwatch.StartNew(); var inChart = new Stopwatch(); var inScene = new Stopwatch();
        while (wall.Elapsed.TotalSeconds < seconds)
        {
            if (uncapped)
            {
                inChart.Start(); chart.PaintNow(); inChart.Stop();
                inScene.Start(); scene.PaintNow(); inScene.Stop();
            }
            Application.DoEvents();
            if (!uncapped) Thread.Sleep(1);
        }
        var elapsed = wall.Elapsed.TotalSeconds;
        return new(chart.FramesPainted - chart0, uncapped ? inChart.Elapsed.TotalSeconds : elapsed, scene.FramesPainted - scene0, uncapped ? inScene.Elapsed.TotalSeconds : elapsed);
    }

    /// <summary>Uniformly random points in a 10 x 10 x 3 m box above the grid, with the height as intensity; the same cloud for every run.</summary>
    private static float[] RandomCloud(int n, out float[] intensities)
    {
        var rnd = new Random(1);
        var pos = new float[n * 3]; intensities = new float[n];
        for (var i = 0; i < n; i++)
        {
            pos[3 * i] = (float)(rnd.NextDouble() * 10 - 5); pos[3 * i + 1] = (float)(rnd.NextDouble() * 10 - 5);
            var z = (float)(rnd.NextDouble() * 3); pos[3 * i + 2] = z; intensities[i] = z;
        }
        return pos;
    }
}
