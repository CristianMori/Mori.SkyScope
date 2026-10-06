// Mori.SkyScope — Entry of the Windows Forms sample: the dashboard window, or an offscreen render with synthetic data.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.WinForms.Sample;

/// <summary>
/// <c>Mori.SkyScope.WinForms.Sample [--ws ws://host:5055/ws] [--gpu]</c> shows the dashboard;
/// <c>--snapshot out.png [--width 1200] [--height 720]</c> renders it offscreen with synthetic data and exits;
/// <c>--screen out.png [--gpu] [--wait 4] [--editor]</c> shows the live dashboard (with the chart editor open when asked), captures it after the wait and exits;
/// <c>--bench [--points 200000] [--seconds 5] [--cpu] [--uncapped] [--shot out.png]</c> shows it with synthetic data and a random point cloud, prints the frame rates (and saves the 3D view's last frame) and exits.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var gpu = args.Contains("--gpu");
        if (Arg("--snapshot") is { } path)
        {
            var w = int.TryParse(Arg("--width"), out var pw) ? pw : 1200; var h = int.TryParse(Arg("--height"), out var ph) ? ph : 720;
            return Snapshot.Render(path, w, h) ? 0 : 1;
        }
        if (Arg("--screen") is { } shot)
        {
            var w = int.TryParse(Arg("--width"), out var pw) ? pw : 1400; var h = int.TryParse(Arg("--height"), out var ph) ? ph : 820;
            var wait = double.TryParse(Arg("--wait"), out var pt) ? pt : 4;
            return Snapshot.Screen(shot, Arg("--ws") ?? "ws://localhost:5055/ws", gpu ? RenderingMode.Gpu : RenderingMode.Cpu, w, h, wait, args.Contains("--editor")) ? 0 : 1;
        }
        if (args.Contains("--bench"))
        {
            var points = int.TryParse(Arg("--points"), out var pn) ? pn : 200_000; var seconds = double.TryParse(Arg("--seconds"), out var ps) ? ps : 5;
            return Bench.Run(points, seconds, args.Contains("--cpu") ? RenderingMode.Cpu : RenderingMode.Gpu, args.Contains("--uncapped"), Arg("--shot")) ? 0 : 1;
        }
        Application.Run(new MainForm(Arg("--ws") ?? "ws://localhost:5055/ws", gpu ? RenderingMode.Gpu : RenderingMode.Cpu));
        return 0;
    }
}
