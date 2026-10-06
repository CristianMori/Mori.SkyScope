// Mori.SkyScope — Application entry of the WPF sample: window or offscreen render depending on the command line.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using System.Windows;

namespace Mori.SkyScope.Wpf.Sample;

/// <summary>
/// Application entry. Without arguments it opens the dashboard window; <c>--snapshot</c>, <c>--logic</c>, <c>--charts</c> and
/// <c>--scene</c> render a PNG offscreen and exit; <c>--csv</c> or <c>--mcap</c> play a recording back; <c>--ws</c> picks the stream URL.
/// </summary>
public partial class App : Application
{
    /// <summary>Parses the command line and either renders one of the offscreen snapshots and shuts down, or shows the main window.</summary>
    private void OnStartup(object sender, StartupEventArgs e)
    {
        // --snapshot out.png [--width 1200] [--height 720]: render the dashboard offscreen with SkiaSharp and exit.
        var args = e.Args;
        var snap = Array.IndexOf(args, "--snapshot");
        if (snap >= 0 && snap + 1 < args.Length)
        {
            int Arg(string name, int dflt) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : dflt; }
            Snapshot.Render(args[snap + 1], Arg("--width", 1200), Arg("--height", 720));
            Shutdown(0);
            return;
        }
        var logic = Array.IndexOf(args, "--logic");
        if (logic >= 0 && logic + 1 < args.Length)
        {
            int Arg2(string name, int dflt) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : dflt; }
            LogicSnapshot.Render(args[logic + 1], Arg2("--width", 1400), Arg2("--height", 800));
            Shutdown(0);
            return;
        }
        // --charts out.png [--width 1400] [--height 900] [--dark]: the analytic charts page, offscreen.
        var charts = Array.IndexOf(args, "--charts");
        if (charts >= 0 && charts + 1 < args.Length)
        {
            int Arg3(string name, int dflt) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : dflt; }
            ChartsSnapshot.Render(args[charts + 1], Arg3("--width", 1400), Arg3("--height", 900), Array.IndexOf(args, "--dark") >= 0);
            Shutdown(0);
            return;
        }
        // --scene out.png [--width 1000] [--height 700] [--dark]: the synthetic robot scene, offscreen.
        var sceneArg = Array.IndexOf(args, "--scene");
        if (sceneArg >= 0 && sceneArg + 1 < args.Length)
        {
            int Arg4(string name, int dflt) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : dflt; }
            SceneSnapshot.Render(args[sceneArg + 1], Arg4("--width", 1000), Arg4("--height", 700), Array.IndexOf(args, "--dark") >= 0);
            Shutdown(0);
            return;
        }
        // --csv recording.csv: play a CSV recording back instead of connecting to a stream.
        // --mcap recording.mcap: play an MCAP recording (signals and scene) back instead of connecting to a stream.
        var csv = Array.IndexOf(args, "--csv");
        var mcap = Array.IndexOf(args, "--mcap");
        var ws = Array.IndexOf(args, "--ws");
        new MainWindow(ws >= 0 && ws + 1 < args.Length ? args[ws + 1] : "ws://localhost:5055/ws", csv >= 0 && csv + 1 < args.Length ? args[csv + 1] : null, mcap >= 0 && mcap + 1 < args.Length ? args[mcap + 1] : null).Show();
    }
}
