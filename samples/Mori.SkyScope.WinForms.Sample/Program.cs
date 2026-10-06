// Mori.SkyScope — Entry of the Windows Forms sample: the dashboard window, or an offscreen render with synthetic data.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

namespace Mori.SkyScope.WinForms.Sample;

/// <summary>
/// <c>Mori.SkyScope.WinForms.Sample [--ws ws://host:5055/ws] [--gpu]</c> shows the dashboard;
/// <c>--snapshot out.png [--width 1200] [--height 720]</c> renders it offscreen with synthetic data and exits;
/// <c>--screen out.png [--gpu] [--wait 4]</c> shows the live dashboard, captures the screen after the wait and exits.
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
            return Snapshot.Screen(shot, Arg("--ws") ?? "ws://localhost:5055/ws", gpu ? RenderingMode.Gpu : RenderingMode.Cpu, w, h, wait) ? 0 : 1;
        }
        Application.Run(new MainForm(Arg("--ws") ?? "ws://localhost:5055/ws", gpu ? RenderingMode.Gpu : RenderingMode.Cpu));
        return 0;
    }
}
