// Mori.SkyScope — 3D view model: an orbit camera, a scene, a frame tree and the tools on top (orbit, measure, select) plus the HUD.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;
using Mori.SkyScope.Core.Scales;
using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>What a primary-button click does: <c>Orbit</c> only drags, <c>Measure</c> picks measurement points, <c>Select</c> picks a hit. Dragging always orbits, pans or dollies regardless of tool.</summary>
public enum Scene3DTool { Orbit, Measure, Select }

/// <summary>
/// 3D view model: an orbit camera, a scene, a frame tree and the tools on top (orbit, measure, select) plus the HUD.
/// Hosts feed it <see cref="InputEvent"/>s, call <see cref="Draw3D"/> with an <see cref="IPainter3D"/> and
/// <see cref="DrawHud"/> with the 2D painter on top. Mirrors <c>scene3d/controller3d.ts</c>;
/// pinned by <c>spec/fixtures/scene3d-controller.json</c>.
/// </summary>
public sealed class Scene3DController
{
    private enum Gesture { Orbit, Pan, Dolly }
    private sealed class Press(double x, double y, int button, Gesture? gesture) { public double X = x, Y = y; public int Button = button; public Gesture? Gesture = gesture; public bool Dragging; public Vec2 Last = new(x, y); }

    /// <summary>The orbit camera; gestures mutate it.</summary>
    public Camera3D Camera { get; }
    /// <summary>The layers; 3D layers draw through the painter given to <see cref="Draw3D"/>.</summary>
    public global::Mori.SkyScope.Core.Scene.Scene Scene { get; } = new();
    /// <summary>Frame tree shared with the layers (hand it to a <see cref="Scene.SceneLayerSink"/> through <c>LayerEnv</c>).</summary>
    public FrameTree Frames { get; }
    /// <summary>Frame the scene is drawn in; shown in the HUD readout.</summary>
    public string FixedFrame { get; set; }
    /// <summary>HUD colours and font; defaults to a dark background.</summary>
    public SceneTheme Theme { get; set; } = SceneTheme.Light with { Background = "#0b1220", Overlay = "#e2e8f0", OverlayText = "#e2e8f0" };
    /// <summary>World unit label for the measurement and cursor readouts.</summary>
    public string Unit { get; set; } = "m";
    /// <summary>Draw the axis gizmo in the bottom-right corner.</summary>
    public bool ShowGizmo { get; set; } = true;
    /// <summary>Draw the cursor readout and the frame / projection mode in the bottom-left corner.</summary>
    public bool ShowCursor { get; set; } = true;
    /// <summary>Hit-test tolerance in screen pixels for hover and selection.</summary>
    public double HitTolerance { get; set; } = 6;
    /// <summary>Radians of orbit per pixel dragged.</summary>
    public double OrbitSpeed { get; set; } = 0.005;
    /// <summary>Base of the dolly factor <c>base ^ (deltaY / divisor)</c> for wheel and right-drag.</summary>
    public double WheelBase { get; set; } = 2;
    /// <summary>Wheel delta (or twice the drag pixels) that yields one factor of <see cref="WheelBase"/>.</summary>
    public double WheelDivisor { get; set; } = 400;
    /// <summary>Pixels a press may move and still count as a click.</summary>
    public double ClickSlop { get; set; } = 3;
    private Scene3DTool _tool;
    private Press? _press;
    private bool _spaceHeld;
    private readonly (double Yaw, double Pitch, double Distance) _home;
    /// <summary>Measurement endpoints in world units, at most two; a third click starts over.</summary>
    public List<Vec3> Measure { get; private set; } = [];
    /// <summary>Hit chosen with the Select tool; cleared by Escape.</summary>
    public HitResult? Selection { get; private set; }
    /// <summary>Hit under the pointer while the Select tool is active.</summary>
    public HitResult? Hover { get; private set; }
    /// <summary>Ground-plane point under the pointer; null when the ray misses the ground.</summary>
    public Vec3? Cursor { get; private set; }
    /// <summary>Chart time in seconds handed to layers (frame lookups, marker expiry).</summary>
    public double Now { get; set; }

    /// <summary>Creates the controller; the camera's initial yaw, pitch and distance become the home view for <see cref="ResetView"/>.</summary>
    public Scene3DController(Camera3D? camera = null, Scene3DTool tool = Scene3DTool.Orbit, FrameTree? frames = null, string fixedFrame = "map")
    {
        Camera = camera ?? new Camera3D();
        _home = (Camera.Yaw, Camera.Pitch, Camera.Distance);
        Frames = frames ?? new FrameTree(); FixedFrame = fixedFrame; _tool = tool;
    }

    /// <summary>The current tool.</summary>
    public Scene3DTool ActiveTool => _tool;
    /// <summary>Switch tool; clears the measurement unless staying in Measure and the hover unless staying in Select.</summary>
    public void SetTool(Scene3DTool tool) { _tool = tool; if (tool != Scene3DTool.Measure) Measure = []; if (tool != Scene3DTool.Select) Hover = null; }
    /// <summary>Resize the camera viewport (pixels); <see cref="Draw3D"/> does this automatically.</summary>
    public void SetViewport(double width, double height) => Camera.SetViewport(width, height);
    /// <summary>Fit the union of the visible 3D layers' bounds; false when the scene has no bounds.</summary>
    public bool FitAll() { if (Scene.Bounds3() is not { } b) return false; Camera.FitBox(b); return true; }
    /// <summary>Restore the home yaw, pitch and distance; the target is kept.</summary>
    public void ResetView() { Camera.SetOrbit(_home.Yaw, _home.Pitch); Camera.SetDistance(_home.Distance); }
    /// <summary>Look straight down with +y up on screen.</summary>
    public void TopDown() => Camera.SetOrbit(-Math.PI / 2, Math.PI / 2);
    /// <summary>Distance between the two measurement points in world units, or null.</summary>
    public double? MeasureDistance() => Measure.Count < 2 ? null : Math3.Length(Math3.Sub(Measure[1], Measure[0]));
    /// <summary>Topmost hit at a screen point using <see cref="HitTolerance"/>.</summary>
    public HitResult? HitTest(double sx, double sy) => Scene.HitTest(sx, sy, Camera, Camera.Width, Camera.Height, HitTolerance, Now);

    private Gesture? GestureFor(int button, Modifiers m)
    {
        if (button == 1 || (button == 0 && (_spaceHeld || m.Shift))) return Gesture.Pan;
        if (button == 2) return Gesture.Dolly;
        if (button == 0) return Gesture.Orbit;
        return null;
    }

    /// <summary>Feed a platform event; true when something changed that needs a redraw.</summary>
    public bool Handle(InputEvent ev)
    {
        switch (ev)
        {
            case InputEvent.KeyDown k:
                switch (k.Key)
                {
                    case " ": _spaceHeld = true; return false;
                    case "f" or "F": return FitAll();
                    case "r" or "R": ResetView(); return true;
                    case "t" or "T": TopDown(); return true;
                    case "o" or "O": Camera.SetOrtho(!Camera.Ortho); return true;
                    case "Escape": { var had = Measure.Count > 0 || Selection is not null; Measure = []; Selection = null; _press = null; return had; }
                    default: return false;
                }
            case InputEvent.KeyUp ku: if (ku.Key == " ") _spaceHeld = false; return false;
            case InputEvent.PointerDown pd:
                if (_press is not null) return false;
                _press = new Press(pd.X, pd.Y, pd.Button, GestureFor(pd.Button, pd.Modifiers));
                return false;
            case InputEvent.PointerMove pm:
                {
                    var p = _press;
                    if (p is null)
                    {
                        Cursor = Camera.GroundPoint(pm.X, pm.Y);
                        if (_tool == Scene3DTool.Select) Hover = HitTest(pm.X, pm.Y);
                        return true;
                    }
                    if (!p.Dragging && Math.Abs(pm.X - p.X) <= ClickSlop && Math.Abs(pm.Y - p.Y) <= ClickSlop) return false;
                    p.Dragging = true;
                    double dx = pm.X - p.Last.X, dy = pm.Y - p.Last.Y;
                    p.Last = new Vec2(pm.X, pm.Y);
                    switch (p.Gesture)
                    {
                        case Gesture.Orbit: Camera.Orbit(-dx * OrbitSpeed, dy * OrbitSpeed); return true;
                        case Gesture.Pan: Camera.Pan(dx, dy); return true;
                        case Gesture.Dolly: Camera.Dolly(Math.Pow(WheelBase, dy / WheelDivisor * 2)); return true;
                        default: return false;
                    }
                }
            case InputEvent.PointerUp pu:
                {
                    var p = _press; _press = null;
                    if (p is null || p.Dragging || p.Button != 0) return false;
                    if (_tool == Scene3DTool.Measure)
                    {
                        var w = HitTest(pu.X, pu.Y)?.World ?? Camera.GroundPoint(pu.X, pu.Y);
                        if (w is not { } ww) return false;
                        if (Measure.Count >= 2) Measure = [ww]; else Measure.Add(ww);
                        return true;
                    }
                    if (_tool == Scene3DTool.Select) { Selection = HitTest(pu.X, pu.Y); return true; }
                    return false;
                }
            case InputEvent.PointerCancel: _press = null; return false;
            case InputEvent.Wheel w: Camera.DollyAt(w.X, w.Y, Math.Pow(WheelBase, w.DeltaY / WheelDivisor)); return true;
            case InputEvent.DoubleClick: return FitAll();
            default: return false;
        }
    }

    /// <summary>The GL pass: resize the camera, begin the frame with the theme background, draw the scene (3D layers through <paramref name="p3"/>, their labels through <paramref name="p2"/>) and end it.</summary>
    public void Draw3D(IPainter3D p3, double width, double height, IPainter p2)
    {
        SetViewport(width, height);
        p3.Begin(Camera.View(), Camera.Projection(), Theme.Background);
        Scene.Draw(p2, Camera, Now, p3);
        p3.End();
    }

    /// <summary>The 2D pass on top: hover and selection rings, measurement, gizmo and cursor readout. Call after <see cref="Draw3D"/>.</summary>
    public void DrawHud(IPainter p, double width, double height)
    {
        var th = Theme; var cam = Camera; var text = new TextStyle(th.OverlayText) { Family = th.FontFamily, Size = th.FontSize };
        void Ring(HitResult h, string color) { var s = cam.Project3(h.World.X, h.World.Y, h.World.Z); if (s.Visible) p.Circle(s.X, s.Y, 9, null, new Stroke(color) { Width = 2 }); }
        if (Hover is not null && (Selection is null || Hover.LayerId != Selection.LayerId || Hover.Index != Selection.Index)) Ring(Hover, th.Overlay);
        if (Selection is not null) Ring(Selection, th.Selection);
        if (Measure.Count > 0)
        {
            var a = cam.Project3(Measure[0].X, Measure[0].Y, Measure[0].Z);
            p.Circle(a.X, a.Y, 4, new Fill(th.Measure));
            if (Measure.Count > 1)
            {
                var b = cam.Project3(Measure[1].X, Measure[1].Y, Measure[1].Z);
                p.Line(a.X, a.Y, b.X, b.Y, new Stroke(th.Measure) { Width = 1.5, Dash = [6, 4] });
                p.Circle(b.X, b.Y, 4, new Fill(th.Measure));
                var d = MeasureDistance()!.Value; var label = $"{Ticks.FormatNumber(d, d >= 100 ? 0 : d >= 10 ? 1 : d >= 1 ? 2 : 3)} {Unit}";
                double mx = (a.X + b.X) / 2, my = (a.Y + b.Y) / 2, w = p.MeasureText(label, text).Width + 10;
                p.Rect(mx - w / 2, my - 20, w, 16, new Fill(th.Background) { Opacity = 0.9 }, new Stroke(th.Measure) { Width = 1 }, 3);
                p.Text(label, mx, my - 12, text with { Color = th.Measure, Align = TextAlign.Center, Baseline = TextBaseline.Middle });
            }
        }
        if (ShowGizmo) DrawGizmo(p, width - 40, height - 40, 26);
        if (ShowCursor)
        {
            var s = Cursor is { } c ? $"{Ticks.FormatNumber(c.X, 2)}, {Ticks.FormatNumber(c.Y, 2)}, {Ticks.FormatNumber(c.Z, 2)} {Unit}" : "";
            var mode = $"{FixedFrame} · {(cam.Ortho ? "ortho" : "persp")}";
            p.Text(s.Length > 0 ? $"{s}  ·  {mode}" : mode, 10, height - 8, text with { Align = TextAlign.Left, Baseline = TextBaseline.Bottom });
        }
    }

    private void DrawGizmo(IPainter p, double cx, double cy, double r)
    {
        var v = Camera.View();
        var axes = new List<(string Name, double X, double Y, double Z)> { ("x", v[0], v[1], v[2]), ("y", v[4], v[5], v[6]), ("z", v[8], v[9], v[10]) };
        var colors = new Dictionary<string, string> { ["x"] = "#dc2626", ["y"] = "#16a34a", ["z"] = "#2563eb" };
        axes.Sort((a, b) => a.Z.CompareTo(b.Z));
        p.Circle(cx, cy, r + 8, new Fill(Theme.Background) { Opacity = 0.6 });
        foreach (var (name, ax, ay, _) in axes)
        {
            double ex = cx + ax * r, ey = cy - ay * r;
            p.Line(cx, cy, ex, ey, new Stroke(colors[name]) { Width = 2 });
            p.Circle(ex, ey, 5, new Fill(colors[name]));
            p.Text(name, ex, ey, new TextStyle("#ffffff") { Family = Theme.FontFamily, Size = 9, Align = TextAlign.Center, Baseline = TextBaseline.Middle, Weight = "bold" });
        }
    }
}
