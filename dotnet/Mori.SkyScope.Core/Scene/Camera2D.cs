// Mori.SkyScope — Maps world coordinates to screen pixels.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scales;

namespace Mori.SkyScope.Core.Scene;

/// <summary>Dimensionality of a projection: <c>TwoD</c> for cameras and scale projections over a plane, <c>ThreeD</c> for orbit cameras. A scene skips layers whose <see cref="ILayer.CameraKinds"/> do not include the active kind.</summary>
public enum CameraKind { TwoD, ThreeD }

/// <summary>Maps world coordinates to screen pixels. <see cref="Version"/> increments on every change so cached layers know when to redraw.</summary>
public interface IProjection
{
    /// <summary>Which camera family produced this projection; decides which layers a scene draws under it.</summary>
    CameraKind Kind { get; }
    /// <summary>Monotonic change counter. Every mutation that alters the mapping must increment it so that cached layer surfaces are invalidated.</summary>
    int Version { get; }
    /// <summary>World coordinates to screen pixels (origin top-left, y down).</summary>
    Vec2 Project(double wx, double wy);
    /// <summary>Screen pixels back to world coordinates; for a 3D projection this is the point on the ground plane z = 0.</summary>
    Vec2 Unproject(double sx, double sy);
}

/// <summary>A 2D camera: center + zoom + rotation over a viewport. Mirrors <c>scene/camera.ts</c>; pinned by <c>spec/fixtures/camera.json</c>.</summary>
public sealed class Camera2D : IProjection
{
    private double _zoom, _rotation;
    private int _version;
    private (int Version, Mat3 M, Mat3 Inv)? _cache;

    /// <summary>Creates a camera over a viewport of <paramref name="width"/> × <paramref name="height"/> pixels centred on (<paramref name="centerX"/>, <paramref name="centerY"/>) world units. The initial zoom is clamped to [<paramref name="minZoom"/>, <paramref name="maxZoom"/>].</summary>
    public Camera2D(double width = 1, double height = 1, double centerX = 0, double centerY = 0, double zoom = 1, double rotation = 0, bool flipY = true, double minZoom = 1e-6, double maxZoom = 1e6)
    {
        Width = width; Height = height; CenterX = centerX; CenterY = centerY;
        MinZoom = minZoom; MaxZoom = maxZoom; _zoom = Math.Clamp(zoom, minZoom, maxZoom); _rotation = rotation; FlipY = flipY;
    }

    /// <summary>Always <see cref="CameraKind.TwoD"/>.</summary>
    public CameraKind Kind => CameraKind.TwoD;
    /// <summary>Incremented by every setter and gesture that changes the mapping.</summary>
    public int Version => _version;
    /// <summary>Viewport width in pixels.</summary>
    public double Width { get; private set; }
    /// <summary>Viewport height in pixels.</summary>
    public double Height { get; private set; }
    /// <summary>World x coordinate shown at the centre of the viewport.</summary>
    public double CenterX { get; private set; }
    /// <summary>World y coordinate shown at the centre of the viewport.</summary>
    public double CenterY { get; private set; }
    /// <summary>Pixels per world unit.</summary>
    public double Zoom => _zoom;
    /// <summary>Radians, clockwise on screen.</summary>
    public double Rotation => _rotation;
    /// <summary>World y grows upwards (maps, robotics).</summary>
    public bool FlipY { get; }
    /// <summary>Lower bound, in pixels per world unit, applied to every zoom change.</summary>
    public double MinZoom { get; }
    /// <summary>Upper bound, in pixels per world unit, applied to every zoom change.</summary>
    public double MaxZoom { get; }

    private void Touch() => _version++;

    /// <summary>Resize the viewport (pixels). A no-op, without a version bump, when the size is unchanged.</summary>
    public void SetViewport(double width, double height) { if (width == Width && height == Height) return; Width = width; Height = height; Touch(); }
    /// <summary>Move the world point shown at the viewport centre.</summary>
    public void SetCenter(double x, double y) { CenterX = x; CenterY = y; Touch(); }
    /// <summary>Set pixels per world unit, clamped to [<see cref="MinZoom"/>, <see cref="MaxZoom"/>].</summary>
    public void SetZoom(double zoom) { _zoom = Math.Clamp(zoom, MinZoom, MaxZoom); Touch(); }
    /// <summary>Set the view rotation in radians, positive clockwise on screen.</summary>
    public void SetRotation(double radians) { _rotation = radians; Touch(); }

    /// <summary>World → screen: T(viewport/2) · R · S(zoom, ±zoom) · T(−center).</summary>
    public Mat3 Matrix()
    {
        if (_cache is { } c && c.Version == _version) return c.M;
        var m = Mat3.Translation(Width / 2, Height / 2) * Mat3.Rotation(_rotation) * Mat3.Scaling(_zoom, FlipY ? -_zoom : _zoom) * Mat3.Translation(-CenterX, -CenterY);
        _cache = (_version, m, m.Invert() ?? Mat3.Identity);
        return m;
    }
    /// <summary>Screen → world matrix, cached together with <see cref="Matrix"/>; identity when the forward matrix is singular.</summary>
    public Mat3 Inverse() { Matrix(); return _cache!.Value.Inv; }

    /// <summary>World → screen through <see cref="Matrix"/>.</summary>
    public Vec2 Project(double wx, double wy) => Matrix().Apply(wx, wy);
    /// <summary>Screen → world through <see cref="Inverse"/>.</summary>
    public Vec2 Unproject(double sx, double sy) => Inverse().Apply(sx, sy);

    /// <summary>Drag the content by (dx, dy) screen pixels.</summary>
    public void Pan(double dx, double dy)
    {
        var d = Inverse().ApplyLinear(dx, dy);
        CenterX -= d.X; CenterY -= d.Y; Touch();
    }

    /// <summary>Multiply zoom by <paramref name="factor"/>, keeping the world point under (sx, sy) fixed on screen.</summary>
    public void ZoomAt(double sx, double sy, double factor)
    {
        var w = Unproject(sx, sy);
        SetZoom(_zoom * factor);
        var p = Project(w.X, w.Y);
        Pan(sx - p.X, sy - p.Y);
    }

    /// <summary>Fit a world rectangle into the viewport with <paramref name="padding"/> pixels on each side (respects rotation).</summary>
    public void FitBounds(Rect r, double padding = 0)
    {
        if (!(r.W >= 0) || !(r.H >= 0)) return;
        var box = Geometry.TransformRect(Mat3.Rotation(_rotation) * Mat3.Scaling(1, FlipY ? -1 : 1), r);
        double availW = Math.Max(1, Width - 2 * padding), availH = Math.Max(1, Height - 2 * padding);
        var zoom = Math.Min(box.W > 0 ? availW / box.W : double.PositiveInfinity, box.H > 0 ? availH / box.H : double.PositiveInfinity);
        CenterX = r.X + r.W / 2; CenterY = r.Y + r.H / 2;
        _zoom = Math.Clamp(double.IsFinite(zoom) ? zoom : _zoom, MinZoom, MaxZoom);
        Touch();
    }

    /// <summary>Zoom to a screen-space rectangle (box zoom).</summary>
    public void FitScreenRect(Rect r, double padding = 0) => FitBounds(UnprojectRect(r), padding);

    /// <summary>Axis-aligned world rectangle covering the viewport.</summary>
    public Rect WorldBounds() => UnprojectRect(new Rect(0, 0, Width, Height));

    private Rect UnprojectRect(Rect r)
    {
        Span<double> pts = stackalloc double[8];
        var corners = new[] { (r.X, r.Y), (r.Right, r.Y), (r.Right, r.Bottom), (r.X, r.Bottom) };
        for (var i = 0; i < 4; i++) { var w = Unproject(corners[i].Item1, corners[i].Item2); pts[2 * i] = w.X; pts[2 * i + 1] = w.Y; }
        return Geometry.RectFromPoints(pts)!.Value;
    }
}

/// <summary>A projection built from two axis scales — how chart plot areas host scene layers.</summary>
public sealed class ScaleProjection(Scale xScale, Scale yScale) : IProjection
{
    private int _version;
    /// <summary>Horizontal scale: world x → screen x.</summary>
    public Scale XScale { get; private set; } = xScale;
    /// <summary>Vertical scale: world y → screen y (screen scales usually run top-down).</summary>
    public Scale YScale { get; private set; } = yScale;
    /// <summary>Always <see cref="CameraKind.TwoD"/>.</summary>
    public CameraKind Kind => CameraKind.TwoD;
    /// <summary>Incremented by <see cref="SetScales"/>.</summary>
    public int Version => _version;
    /// <summary>Swap both scales (after a zoom or a resize of the plot area) and bump the version.</summary>
    public void SetScales(Scale x, Scale y) { XScale = x; YScale = y; _version++; }
    /// <summary>Apply each scale independently.</summary>
    public Vec2 Project(double wx, double wy) => new(XScale.Apply(wx), YScale.Apply(wy));
    /// <summary>Invert each scale independently.</summary>
    public Vec2 Unproject(double sx, double sy) => new(XScale.Invert(sx), YScale.Invert(sy));
}
