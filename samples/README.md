# Samples

One dashboard per supported host, all fed by the same demo server. Start the server first, then any of the others;
every one of them connects to `ws://localhost:5055/ws` by default.

```bash
dotnet run --project samples/Mori.SkyScope.DemoServer -- --channels 16 --rate 1000
```

| Sample | Language / host | Run | Shows |
|---|---|---|---|
| `Mori.SkyScope.DemoServer` | C#, ASP.NET Core | `dotnet run --project samples/Mori.SkyScope.DemoServer` | Synthetic channels at 1 kHz, five digital I/O channels, a synthetic 2D and 3D robot scene relayed as layers, MCAP recording on the server (`--record`, `/record` endpoints). Options: `--channels`, `--rate`, `--batch-ms`, `--seed`, `--quantized`, `--scene3d false`, `--urls http://0.0.0.0:5055`. |
| `react-sample` | TypeScript, React | `npm run sample` → http://localhost:5173 | The full dashboard: signal tree, trend chart with the navigator, 2D and 3D scene views, nine gauges, four analytic charts, live spectrogram, recording and playback (`?focus=trend` shows only tree and chart, `?ws=` another server, `?mcap=` a served recording). |
| `vanilla-ts` | TypeScript, no framework | `npm run sample:vanilla` → http://localhost:5174 | The DOM views used directly: `TrendChartView`, `SignalTreePanel`, `GaugeView` over a `SignalStore` fed by `WebSocketFrameSource`. A hand-made channel list as a second drag source, the chart's `onChannelDrop` hook reporting every drop, and `addChannels` from a button. |
| `Mori.SkyScope.Blazor.Sample` | C#, Blazor Server | `dotnet run --project samples/Mori.SkyScope.Blazor.Sample` → http://localhost:5218 | The same dashboard with the Razor components: configuration crosses interop as JSON, data streams straight into the browser; a server-pushed spectrogram. |
| `Mori.SkyScope.Wpf.Sample` | C#, WPF | `dotnet run --project samples/Mori.SkyScope.Wpf.Sample -- --ws ws://localhost:5055/ws` | The desktop dashboard on SkiaSharp: tree, trend chart, 3D scene through OpenTK, gauges, analytic charts, MCAP recording and playback. Offscreen renders: `--snapshot`, `--logic`, `--charts`, `--scene out.png` (`--dark`, `--width`, `--height`). |
| `Mori.SkyScope.WinForms.Sample` | C#, Windows Forms | `dotnet run --project samples/Mori.SkyScope.WinForms.Sample -- --ws ws://localhost:5055/ws --gpu` | The chart configured entirely in `MainForm.Designer.cs` the way the designer writes it; a GPU toggle that re-attaches every control's surface; `--snapshot out.png` (offscreen, synthetic data), `--screen out.png [--gpu]` (live capture composed from each control's own pixels) and `--bench [--points N] [--seconds S] [--cpu] [--uncapped] [--shot out.png]` (frame rates of the trend chart and the 3D view with a random point cloud). |

`demo-recording.csv` is a 20 s recording the playback modes can open.

The React and plain TypeScript samples resolve the packages to their sources through Vite aliases, so edits in
`ts/packages` show up on reload without a build. The .NET samples reference the projects in `dotnet/` directly.
