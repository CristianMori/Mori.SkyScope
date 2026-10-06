// Mori.SkyScope — SceneView model: a camera, a scene and the tools on top of it (pan, box zoom, measure, select), plus the overlays every map view wants — …
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Scene;

/// <summary>
/// SceneView model: a camera, a scene and the tools on top of it (pan, box zoom, measure, select), plus the overlays every
/// map view wants — box-zoom preview, measurement, selection ring, scale bar, cursor readout. Hosts feed it
/// <see cref="InputEvent"/>s and call <see cref="SceneController.Draw"/>. Mirrors <c>scene/controller.ts</c>; pinned by <c>spec/fixtures/scene-controller.json</c>.
/// </summary>
public enum SceneTool { Pan, BoxZoom, Measure, Select }

/// <summary>Colours (hex) and font of the scene HUD, shared by the 2D and 3D controllers.</summary>
/// <param name="Background">Clear colour.</param>
/// <param name="Overlay">Scale bar, gizmo ring and hover ring.</param>
/// <param name="OverlayText">Readouts and labels.</param>
/// <param name="Selection">Selection ring.</param>
/// <param name="Measure">Measurement endpoints, line and label.</param>
/// <param name="BoxZoom">Box-zoom rubber band.</param>
/// <param name="FontFamily">HUD font family.</param>
/// <param name="FontSize">HUD font size in pixels.</param>
public sealed record SceneTheme(string Background, string Overlay, string OverlayText, string Selection, string Measure, string BoxZoom, string FontFamily, double FontSize)
{
    /// <summary>Light background, dark overlays.</summary>
    public static readonly SceneTheme Light = new("#f8fafc", "#0f172a", "#0f172a", "#d97706", "#dc2626", "#2563eb", "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif", 11);
    /// <summary>Dark background, light overlays.</summary>
    public static readonly SceneTheme Dark = Light with { Background = "#020617", Overlay = "#f8fafc", OverlayText = "#f8fafc", Selection = "#fbbf24", Measure = "#f87171", BoxZoom = "#60a5fa" };
}

/// <summary>The 2D scene view model; see the file summary on <see cref="SceneTool"/>. Not thread-safe: feed events and draw from one thread.</summary>
public sealed class SceneController : Charts.IDrawable
{
    /// <summary>The camera the scene is drawn through; gestures mutate it.</summary>
    public Camera2D Camera { get; }
    /// <summary>The layers; add the host's layers here.</summary>
    public global::Mori.SkyScope.Core.Scene.Scene Scene { get; } = new();
    /// <summary>HUD colours and font.</summary>
    public SceneTheme Theme { get; set; } = SceneTheme.Light;
    /// <summary>World unit label for the scale bar and readouts.</summary>
    public string Unit { get; set; } = "m";
    /// <summary>Draw the scale bar in the bottom-left corner.</summary>
    public bool ShowScaleBar { get; set; } = true;
    /// <summary>Draw the cursor world-coordinate readout in the bottom-right corner.</summary>
    public bool ShowCursor { get; set; } = true;
    /// <summary>Hit-test tolerance in screen pixels for hover and selection.</summary>
    public double HitTolerance { get; set; } = 6;
    /// <summary>Padding in pixels left around the scene bounds by <see cref="FitAll"/>.</summary>
    public double FitPadding { get; set; } = 24;
    private InteractionState _interaction;
    private SceneTool _tool;
    /// <summary>Box-zoom rubber band (screen).</summary>
    public Rect? BoxPreview { get; private set; }
    /// <summary>Measurement endpoints (world), at most two.</summary>
    public List<Vec2> Measure { get; private set; } = [];
    /// <summary>Hit chosen with the Select tool; cleared by Escape.</summary>
    public HitResult? Selection { get; private set; }
    /// <summary>Hit under the pointer while the Select tool is active.</summary>
    public HitResult? Hover { get; private set; }
    /// <summary>Last pointer position in world units; null before the first move.</summary>
    public Vec2? Cursor { get; private set; }
    /// <summary>Chart time handed to layers.</summary>
    public double Now { get; set; }

    /// <summary>Creates the controller with its own camera unless one is given, starting with <paramref name="tool"/>.</summary>
    public SceneController(Camera2D? camera = null, SceneTool tool = SceneTool.Pan)
    {
        Camera = camera ?? new Camera2D();
        _tool = tool;
        _interaction = InteractionState.Initial(GestureFor(tool));
    }

    /// <summary>The current tool.</summary>
    public SceneTool ActiveTool => _tool;
    /// <summary>Switch tool; restarts the gesture state and clears the measurement unless staying in Measure.</summary>
    public void SetTool(SceneTool tool)
    {
        _tool = tool;
        _interaction = InteractionState.Initial(GestureFor(tool));
        if (tool != SceneTool.Measure) Measure = [];
    }
    private static global::Mori.SkyScope.Core.Scene.Tool GestureFor(SceneTool t) => t switch { SceneTool.Measure => global::Mori.SkyScope.Core.Scene.Tool.Cursor, SceneTool.Select => global::Mori.SkyScope.Core.Scene.Tool.Select, SceneTool.BoxZoom => global::Mori.SkyScope.Core.Scene.Tool.BoxZoom, _ => global::Mori.SkyScope.Core.Scene.Tool.Pan };

    /// <summary>Resize the camera viewport (pixels); <see cref="Draw"/> does this automatically.</summary>
    public void SetViewport(double width, double height) => Camera.SetViewport(width, height);
    /// <summary>Rotate the view by <paramref name="radians"/> (positive = clockwise on screen).</summary>
    public void RotateBy(double radians) => Camera.SetRotation(Camera.Rotation + radians);
    /// <summary>Set the absolute view rotation in radians (positive = clockwise on screen).</summary>
    public void SetRotation(double radians) => Camera.SetRotation(radians);
    /// <summary>Fit the union of the visible layers' bounds with <see cref="FitPadding"/>; false when the scene has no bounds.</summary>
    public bool FitAll()
    {
        if (Scene.Bounds() is not { } b) return false;
        Camera.FitBounds(b, FitPadding);
        return true;
    }
    /// <summary>Distance between the two measurement points (world units) or null.</summary>
    public double? MeasureDistance()
    {
        if (Measure.Count < 2) return null;
        double dx = Measure[1].X - Measure[0].X, dy = Measure[1].Y - Measure[0].Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
    /// <summary>Topmost hit at a screen point using <see cref="HitTolerance"/>.</summary>
    public HitResult? HitTest(double sx, double sy) => Scene.HitTest(sx, sy, Camera, Camera.Width, Camera.Height, HitTolerance, Now);

    /// <summary>Feed a platform event; returns true when something changed that needs a redraw.</summary>
    public bool Handle(InputEvent ev)
    {
        if (ev is InputEvent.KeyDown kd)
        {
            switch (kd.Key)
            {
                case "q": case "Q": RotateBy(-Math.PI / 12); return true;
                case "e": case "E": RotateBy(Math.PI / 12); return true;
                case "r": case "R": Camera.SetRotation(0); return true;
                case "f": case "F": return FitAll();
                case "Escape": Measure = []; Selection = null; break;
            }
        }
        var (state, effects) = Interaction.Reduce(_interaction, ev);
        _interaction = state;
        var changed = false;
        foreach (var e in effects)
        {
            if (Interaction.ApplyToCamera(Camera, e)) { changed = true; if (e is Effect.BoxZoom) BoxPreview = null; continue; }
            switch (e)
            {
                case Effect.BoxZoomPreview bp: BoxPreview = bp.Rect; changed = true; break;
                case Effect.BoxZoomCancel: BoxPreview = null; changed = true; break;
                case Effect.Hover h:
                    Cursor = Camera.Unproject(h.X, h.Y);
                    if (_tool == SceneTool.Select) Hover = HitTest(h.X, h.Y);
                    changed = true;
                    break;
                case Effect.Click c:
                    if (c.Button != 0) break;
                    if (_tool == SceneTool.Measure)
                    {
                        var w = Camera.Unproject(c.X, c.Y);
                        if (Measure.Count >= 2) Measure = [w]; else Measure.Add(w);
                        changed = true;
                    }
                    else if (_tool == SceneTool.Select) { Selection = HitTest(c.X, c.Y); changed = true; }
                    break;
                case Effect.Reset: changed = FitAll() || changed; break;
            }
        }
        return changed;
    }

    /// <summary>Scale-bar length: a 1-2-5 world length that spans about <paramref name="targetPx"/> pixels.</summary>
    public (double World, double Px) ScaleBar(double targetPx = 100)
    {
        var worldPerPx = 1 / Camera.Zoom;
        var step = Ticks.Spec(0, worldPerPx * targetPx, 1).Step;
        var world = step > 0 ? step : worldPerPx * targetPx;
        return (world, world * Camera.Zoom);
    }

    /// <summary>Resize the camera to the painter, clear with the theme background, draw the scene, then the overlays: hover and selection rings, measurement, box-zoom preview, scale bar and cursor readout.</summary>
    public void Draw(IPainter p, double width, double height)
    {
        SetViewport(width, height);
        var th = Theme; var text = new TextStyle(th.OverlayText) { Family = th.FontFamily, Size = th.FontSize };
        p.Clear(th.Background);
        Scene.Draw(p, Camera, Now);
        void Ring(HitResult h, string color) { var s = Camera.Project(h.World.X, h.World.Y); p.Circle(s.X, s.Y, 9, null, new Stroke(color) { Width = 2 }); }
        if (Hover is not null && (Selection is null || Hover.LayerId != Selection.LayerId || Hover.Index != Selection.Index)) Ring(Hover, th.Overlay);
        if (Selection is not null) Ring(Selection, th.Selection);
        if (Measure.Count > 0)
        {
            var a = Camera.Project(Measure[0].X, Measure[0].Y);
            p.Circle(a.X, a.Y, 4, new Fill(th.Measure));
            if (Measure.Count > 1)
            {
                var b = Camera.Project(Measure[1].X, Measure[1].Y);
                p.Line(a.X, a.Y, b.X, b.Y, new Stroke(th.Measure) { Width = 1.5, Dash = [6, 4] });
                p.Circle(b.X, b.Y, 4, new Fill(th.Measure));
                var d = MeasureDistance()!.Value; var label = $"{Ticks.FormatNumber(d, d >= 100 ? 0 : d >= 10 ? 1 : d >= 1 ? 2 : 3)} {Unit}";
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, w = p.MeasureText(label, text).Width + 10;
                p.Rect(mx - w / 2, my - 20, w, 16, new Fill(th.Background) { Opacity = 0.9 }, new Stroke(th.Measure), 3);
                p.Text(label, mx, my - 12, text with { Color = th.Measure, Align = TextAlign.Center, Baseline = TextBaseline.Middle });
            }
        }
        if (BoxPreview is { } r) p.Rect(r.X, r.Y, r.W, r.H, new Fill(th.BoxZoom) { Opacity = 0.1 }, new Stroke(th.BoxZoom) { Dash = [4, 3] });
        if (ShowScaleBar)
        {
            var (world, px) = ScaleBar(); double x = 12, y = height - 14;
            var ov = new Stroke(th.Overlay) { Width = 2 };
            p.Line(x, y, x + px, y, ov);
            p.Line(x, y - 4, x, y + 4, ov);
            p.Line(x + px, y - 4, x + px, y + 4, ov);
            p.Text($"{Ticks.FormatNumber(world, world < 1 ? 2 : 0)} {Unit}", x + px / 2, y - 6, text with { Align = TextAlign.Center, Baseline = TextBaseline.Bottom });
        }
        if (ShowCursor && Cursor is { } c)
            p.Text($"{Ticks.FormatNumber(c.X, 2)}, {Ticks.FormatNumber(c.Y, 2)} {Unit}", width - 10, height - 8, text with { Align = TextAlign.Right, Baseline = TextBaseline.Bottom });
    }
}
