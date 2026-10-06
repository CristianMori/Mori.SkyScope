// Mori.SkyScope — Offscreen render of the Windows Forms dashboard with synthetic data (no window, no server): --snapshot out.png.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Sources;
using Mori.SkyScope.Core.Streaming;

namespace Mori.SkyScope.WinForms.Sample;

/// <summary>Creates the form without showing it, fills its store with a few seconds of synthetic signals, paints every control into a bitmap and saves it.</summary>
internal static class Snapshot
{
    /// <summary>Renders the dashboard to <paramref name="path"/> at the given client size; returns false when the form could not be created.</summary>
    public static bool Render(string path, int width, int height)
    {
        try
        {
            using var form = new MainForm("ws://localhost:1/ws");
            form.ClientSize = new Size(width, height);
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-10000, -10000); form.ShowInTaskbar = false;
            form.Show();   // handles must exist for the surfaces to paint; the window sits off screen
            Application.DoEvents();
            Fill(form);
            Application.DoEvents();
            using var whole = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
            var client = Rectangle.Intersect(new Rectangle(form.PointToScreen(Point.Empty) - (Size)form.Location, form.ClientSize), new Rectangle(Point.Empty, whole.Size));
            using var bmp = whole.Clone(client, whole.PixelFormat);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"wrote {path} ({bmp.Width}x{bmp.Height})");
            form.Close();
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return false; }
    }

    /// <summary>Shows the live dashboard on screen, waits, captures its client area from the screen (the only way to capture OpenGL content) and exits.</summary>
    public static bool Screen(string path, string wsUrl, RenderingMode rendering, int width, int height, double waitSeconds, bool editor = false)
    {
        try
        {
            using var form = new MainForm(wsUrl, rendering);
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(0, 0);
            form.Show();
            form.ClientSize = new Size(width, height);
            if (editor) form.ShowEditor();
            var until = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
            form.Activate(); Application.DoEvents();
            // GDI paints the chrome; every SkyScope control is asked for its own pixels (read back from OpenGL on the GPU path), which also works on a desktop no one is looking at.
            // Form.DrawToBitmap includes the window frame, so the client area is cut out of a full-size render.
            using var whole = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(whole, new Rectangle(Point.Empty, form.Size));
            var client = Rectangle.Intersect(new Rectangle(form.PointToScreen(Point.Empty) - (Size)form.Location, form.ClientSize), new Rectangle(Point.Empty, whole.Size));
            using var bmp = whole.Clone(client, whole.PixelFormat);
            var modes = new List<string>();
            using (var g = Graphics.FromImage(bmp))
                foreach (var host in Hosts(form))
                {
                    modes.Add($"{host.GetType().Name}={host.EffectiveRendering}");
                    using var pixels = host.Snapshot();
                    if (pixels is not null) g.DrawImageUnscaled(pixels, form.PointToClient(host.PointToScreen(Point.Empty)));
                }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"wrote {path} ({bmp.Width}x{bmp.Height}) requested={rendering} {string.Join(" ", modes.Distinct())}");
            form.Close();
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return false; }
    }

    /// <summary>Twelve seconds of the demo server's waveforms, pushed straight into the form's store.</summary>
    internal static void Fill(MainForm form)
    {
        var chart = (TrendChartControl)FindControl(form, typeof(TrendChartControl))!;
        var store = chart.Store;
        var channels = SyntheticSourceConfig.AutoChannels(16, 1).ToList();
        channels.AddRange(new (int Id, string Name, double Hz)[] { (200, "io/pump", 0.2), (201, "io/valve", 0.37) }
            .Select(d => new SyntheticChannel(d.Id, new SynthSpec(Waveform.Square) { Frequency = d.Hz, Amplitude = 0.5, Offset = 0.5, Noise = 0 }, d.Name, null, ChannelKind.Digital)));
        channels.AddRange([new SyntheticChannel(100, new SynthSpec(Waveform.Sine) { Frequency = 0.05, Amplitude = 4, Offset = 6 }, "amr-1/pose/x", "m"), new SyntheticChannel(101, new SynthSpec(Waveform.Sine) { Frequency = 0.05, Amplitude = 3, Offset = 5, Phase = 1.5 }, "amr-1/pose/y", "m"), new SyntheticChannel(103, new SynthSpec(Waveform.Triangle) { Frequency = 0.1, Amplitude = 0.3, Offset = 0.5 }, "amr-1/speed", "m/s")]);
        const double rate = 200; var dt = 1 / rate; const int count = 2400;
        foreach (var c in channels) store.DeclareChannel(new ChannelInfo(c.Id, c.Name ?? $"ch{c.Id}") { Unit = c.Unit, Kind = c.Kind, Rate = rate, Timing = ChannelTiming.Regular });
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        var frames = channels.Select(c => FrameChannel.Regular((ushort)c.Id, now - count * dt, dt, Synth.Synthesize(c.Spec, now - count * dt, dt, count, 0))).ToList();
        store.PushFrame(new SkyScopeFrame(1, now - count * dt, frames));
        chart.Model.Now = now;
        chart.Model.SetCursor('a', now - 9); chart.Model.SetCursor('b', now - 3);
        chart.Redraw();
    }

    private static IEnumerable<SkiaHostControl> Hosts(Control root)
    {
        foreach (Control c in root.Controls) { if (c is SkiaHostControl h) yield return h; else foreach (var n in Hosts(c)) yield return n; }
    }

    internal static Control? FindControl(Control root, Type type)
    {
        foreach (Control c in root.Controls) { if (type.IsInstanceOfType(c)) return c; if (FindControl(c, type) is { } found) return found; }
        return null;
    }
}
