// Mori.SkyScope — The synthetic robot scene after a few seconds of driving, rendered offscreen (--scene out.png [--dark]).
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.IO;
using Mori.SkyScope.Core.Scene;
using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Render.Skia;
using SkiaSharp;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>The synthetic robot scene after a few seconds of driving, rendered offscreen (--scene out.png [--dark]).</summary>
public static class SceneSnapshot
{
    /// <summary>Steps the synthetic robot source for 18 s without a timer, fits the view, selects the robot, measures from dock to goal, and writes the PNG.</summary>
    /// <param name="path">Output PNG path.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <param name="dark">Use the dark scene theme.</param>
    public static void Render(string path, int width, int height, bool dark = false)
    {
        var controller = new SceneController { Theme = dark ? SceneTheme.Dark : SceneTheme.Light, Unit = "m" };
        controller.Scene.Add(new GridLayer("grid") { Stroke = new Core.Paint.Stroke(dark ? "#1e293b" : "#e2e8f0"), MajorStroke = new Core.Paint.Stroke(dark ? "#334155" : "#cbd5e1") });
        var sink = new SceneLayerSink(controller.Scene);
        var store = new SignalStore();
        var sim = new SyntheticSceneSource();
        sim.StartAsync(new SourceContext(store, sink, new LiveClock(), (_, _) => { }), new SyntheticSceneConfig { UseTimer = false }).GetAwaiter().GetResult();
        for (var i = 0; i < 180; i++) sim.Step(0.1);          // 18 s of driving → a trail and a fresh scan
        controller.SetViewport(width, height);
        controller.FitAll();
        controller.SetTool(SceneTool.Select);
        var bot = controller.Camera.Project(sim.Pose.X, sim.Pose.Y);
        controller.Handle(new InputEvent.PointerDown(bot.X, bot.Y, 0)); controller.Handle(new InputEvent.PointerUp(bot.X, bot.Y, 0));
        // Measuring last: switching tools away from Measure clears the measurement by design.
        controller.SetTool(SceneTool.Measure);
        var dock = controller.Camera.Project(2, 1.75); var goal = controller.Camera.Project(13, 2.5);
        foreach (var (x, y) in new[] { (dock.X, dock.Y), (goal.X, goal.Y) }) { controller.Handle(new InputEvent.PointerDown(x, y, 0)); controller.Handle(new InputEvent.PointerUp(x, y, 0)); }
        controller.Handle(new InputEvent.PointerMove(bot.X + 40, bot.Y - 30));

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var painter = new SkiaPainter(surface.Canvas, width, height)) controller.Draw(painter, width, height);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.OpenWrite(path);
        data.SaveTo(file);
        Console.WriteLine($"wrote {path} ({width}x{height})");
    }
}
