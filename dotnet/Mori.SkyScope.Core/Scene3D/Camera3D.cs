// Mori.SkyScope — An orbit camera: target + distance + yaw + pitch, z up, perspective or orthographic.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Scene;

namespace Mori.SkyScope.Core.Scene3D;

/// <summary>A world point after projection.</summary>
/// <param name="X">Screen x in pixels.</param>
/// <param name="Y">Screen y in pixels, downwards.</param>
/// <param name="Depth">NDC depth in [−1, 1], near to far.</param>
/// <param name="Visible">True when the point is in front of the camera and within the clip range; <paramref name="X"/> and <paramref name="Y"/> are unreliable otherwise.</param>
public readonly record struct Projected3(double X, double Y, double Depth, bool Visible);

/// <summary>
/// An orbit camera: target + distance + yaw + pitch, z up, perspective or orthographic. Implements the
/// dimension-agnostic <see cref="IProjection"/> (kind 3D): <see cref="Project"/>/<see cref="Unproject"/> work on the
/// ground plane z = 0. Mirrors <c>scene3d/camera3d.ts</c>; pinned by <c>spec/fixtures/camera3d.json</c>.
/// </summary>
public sealed class Camera3D : IProjection
{
    private const double MaxPitch = Math.PI / 2 - 1e-3;
    private double _distance, _yaw, _pitch;
    private int _version;
    private (int Version, double[] View, double[] Proj, double[] ViewProj, double[] Inv)? _cache;

    /// <summary>Creates an orbit camera. Distances are world units, angles radians; <paramref name="distance"/> is clamped to [<paramref name="minDistance"/>, <paramref name="maxDistance"/>] and <paramref name="pitch"/> just short of ±π/2. The default looks at the origin from the −x side, slightly above the ground.</summary>
    public Camera3D(double width = 1, double height = 1, Vec3? target = null, double distance = 10, double yaw = Math.PI, double pitch = 0.6, double fov = Math.PI / 4, double near = 0.05, double far = 5000, bool ortho = false, double minDistance = 1e-3, double maxDistance = 1e6)
    {
        Width = width; Height = height; Target = target ?? default;
        MinDistance = minDistance; MaxDistance = maxDistance;
        _distance = Math.Clamp(distance, minDistance, maxDistance); _yaw = yaw; _pitch = Math.Clamp(pitch, -MaxPitch, MaxPitch);
        Fov = fov; Near = near; Far = far; Ortho = ortho;
    }

    /// <summary>Always <see cref="CameraKind.ThreeD"/>.</summary>
    public CameraKind Kind => CameraKind.ThreeD;
    /// <summary>Incremented by every setter and gesture that changes the view.</summary>
    public int Version => _version;
    /// <summary>Viewport width in pixels.</summary>
    public double Width { get; private set; }
    /// <summary>Viewport height in pixels.</summary>
    public double Height { get; private set; }
    /// <summary>Orbit centre in world units; also the point <see cref="Pan"/> moves.</summary>
    public Vec3 Target { get; private set; }
    /// <summary>Eye-to-target distance in world units.</summary>
    public double Distance => _distance;
    /// <summary>Azimuth of the eye around z, radians: the eye sits at target + distance · (cos pitch · cos yaw, cos pitch · sin yaw, sin pitch).</summary>
    public double Yaw => _yaw;
    /// <summary>Elevation of the eye above the ground plane, radians, clamped just short of ±π/2.</summary>
    public double Pitch => _pitch;
    /// <summary>Vertical field of view in radians; in orthographic mode it sets the view height at the target distance.</summary>
    public double Fov { get; private set; }
    /// <summary>Near clip distance in world units (perspective only).</summary>
    public double Near { get; set; }
    /// <summary>Far clip distance in world units.</summary>
    public double Far { get; set; }
    /// <summary>Orthographic instead of perspective projection.</summary>
    public bool Ortho { get; private set; }
    /// <summary>Lower bound for <see cref="Distance"/>.</summary>
    public double MinDistance { get; }
    /// <summary>Upper bound for <see cref="Distance"/>.</summary>
    public double MaxDistance { get; }
    /// <summary>Viewport width over height.</summary>
    public double Aspect => Width / Math.Max(1e-9, Height);
    private void Touch() => _version++;

    private Vec3 OrbitDir() { var cp = Math.Cos(_pitch); return new(cp * Math.Cos(_yaw), cp * Math.Sin(_yaw), Math.Sin(_pitch)); }
    /// <summary>Eye position in world units.</summary>
    public Vec3 Eye() => Math3.Add(Target, Math3.Scale(OrbitDir(), _distance));

    /// <summary>Resize the viewport (pixels); a no-op when unchanged.</summary>
    public void SetViewport(double width, double height) { if (width == Width && height == Height) return; Width = width; Height = height; Touch(); }
    /// <summary>Move the orbit centre.</summary>
    public void SetTarget(double x, double y, double z) { Target = new(x, y, z); Touch(); }
    /// <summary>Set the eye-to-target distance, clamped to the configured range.</summary>
    public void SetDistance(double d) { _distance = Math.Clamp(d, MinDistance, MaxDistance); Touch(); }
    /// <summary>Set yaw and pitch (radians); pitch is clamped just short of ±π/2.</summary>
    public void SetOrbit(double yaw, double pitch) { _yaw = yaw; _pitch = Math.Clamp(pitch, -MaxPitch, MaxPitch); Touch(); }
    /// <summary>Set the vertical field of view (radians), clamped to (0, π).</summary>
    public void SetFov(double fov) { Fov = Math.Clamp(fov, 0.01, Math.PI - 0.01); Touch(); }
    /// <summary>Switch between orthographic and perspective; a no-op when unchanged.</summary>
    public void SetOrtho(bool ortho) { if (ortho == Ortho) return; Ortho = ortho; Touch(); }

    private (double[] View, double[] Proj, double[] ViewProj, double[] Inv) Matrices()
    {
        if (_cache is { } c && c.Version == _version) return (c.View, c.Proj, c.ViewProj, c.Inv);
        var view = Mat4.LookAt(Eye(), Target, new Vec3(0, 0, 1));
        double[] proj;
        if (Ortho) { var h = _distance * Math.Tan(Fov / 2); var w = h * Aspect; proj = Mat4.Ortho(-w, w, -h, h, -Far, Far); }
        else proj = Mat4.Perspective(Fov, Aspect, Near, Far);
        var vp = Mat4.Mul(proj, view);
        var inv = Mat4.Invert(vp) ?? Mat4.Identity;
        _cache = (_version, view, proj, vp, inv);
        return (view, proj, vp, inv);
    }
    /// <summary>World → eye matrix, column-major <c>double[16]</c>; cached per version.</summary>
    public double[] View() => Matrices().View;
    /// <summary>Eye → clip matrix, column-major <c>double[16]</c>; cached per version.</summary>
    public double[] Projection() => Matrices().Proj;
    /// <summary>World → clip matrix (projection × view), column-major <c>double[16]</c>; cached per version.</summary>
    public double[] ViewProj() => Matrices().ViewProj;

    /// <summary>World point → screen pixels (y down) + NDC depth.</summary>
    public Projected3 Project3(double x, double y, double z)
    {
        var m = Matrices().ViewProj;
        var w = m[3] * x + m[7] * y + m[11] * z + m[15];
        double cx = m[0] * x + m[4] * y + m[8] * z + m[12], cy = m[1] * x + m[5] * y + m[9] * z + m[13], cz = m[2] * x + m[6] * y + m[10] * z + m[14];
        var iw = w == 0 ? 1 : 1 / w;
        double nx = cx * iw, ny = cy * iw, nz = cz * iw;
        return new((nx + 1) / 2 * Width, (1 - ny) / 2 * Height, nz, w > 0 && nz >= -1 && nz <= 1);
    }
    /// <summary>Screen pixels and NDC <paramref name="depth"/> (−1 near, 1 far) → world point.</summary>
    public Vec3 Unproject3(double sx, double sy, double depth) => Mat4.Point(Matrices().Inv, sx / Width * 2 - 1, 1 - sy / Height * 2, depth);
    /// <summary>World ray through a screen pixel, from the near plane towards the far plane.</summary>
    public Ray Ray(double sx, double sy) { var a = Unproject3(sx, sy, -1); var b = Unproject3(sx, sy, 1); return new(a, Math3.Normalize(Math3.Sub(b, a))); }
    /// <summary>Where the pixel's ray meets the ground plane z = 0, or null when it misses (looking at or above the horizon).</summary>
    public Vec3? GroundPoint(double sx, double sy) { var r = Ray(sx, sy); var t = r.Plane(new Vec3(0, 0, 1), 0); return t is { } tt ? r.At(tt) : null; }

    /// <summary>Ground-plane point (z = 0) → screen pixels.</summary>
    public Vec2 Project(double wx, double wy) { var p = Project3(wx, wy, 0); return new(p.X, p.Y); }
    /// <summary>Screen pixels → ground-plane point; when the ray misses the ground, the point at the target distance along the ray is used instead.</summary>
    public Vec2 Unproject(double sx, double sy)
    {
        if (GroundPoint(sx, sy) is { } g) return new(g.X, g.Y);
        var p = Ray(sx, sy).At(_distance);
        return new(p.X, p.Y);
    }

    /// <summary>World units per screen pixel at the target distance (vertical).</summary>
    public double WorldPerPixel() => 2 * _distance * Math.Tan(Fov / 2) / Math.Max(1, Height);

    /// <summary>Rotate the eye around the target by the given deltas in radians.</summary>
    public void Orbit(double dyaw, double dpitch) => SetOrbit(_yaw + dyaw, _pitch + dpitch);
    /// <summary>Drag the target in the camera's screen plane by (<paramref name="dx"/>, <paramref name="dy"/>) pixels, so the content follows the pointer.</summary>
    public void Pan(double dx, double dy)
    {
        var v = Matrices().View; var upp = WorldPerPixel();
        Vec3 right = new(v[0], v[4], v[8]), up = new(v[1], v[5], v[9]);
        Target = Math3.Add(Target, Math3.Add(Math3.Scale(right, -dx * upp), Math3.Scale(up, dy * upp)));
        Touch();
    }
    /// <summary>Multiply the distance by <paramref name="factor"/> (less than 1 moves closer).</summary>
    public void Dolly(double factor) => SetDistance(_distance * factor);
    /// <summary>Dolly about the world point under (sx, sy): that point stays fixed on screen.</summary>
    public void DollyAt(double sx, double sy, double factor)
    {
        var r = Ray(sx, sy);
        var t = r.Plane(new Vec3(0, 0, 1), 0);
        var p = t is { } tt ? r.At(tt) : r.At(_distance);
        var f = Math.Clamp(_distance * factor, MinDistance, MaxDistance) / _distance;
        Target = Math3.Add(p, Math3.Scale(Math3.Sub(Target, p), f));
        _distance *= f;
        Touch();
    }
    /// <summary>Look at <paramref name="center"/> from far enough that a sphere of <paramref name="radius"/> fills the smaller viewport dimension; orientation is kept.</summary>
    public void FitSphere(Vec3 center, double radius)
    {
        Target = center;
        var r = Math.Max(radius, 1e-6); var k = Math.Min(1, Aspect);
        var d = Ortho ? r / (Math.Tan(Fov / 2) * k) : r / (Math.Sin(Fov / 2) * k);
        _distance = Math.Clamp(d, MinDistance, MaxDistance);
        Touch();
    }
    /// <summary>Fit the box's bounding sphere; see <see cref="FitSphere"/>.</summary>
    public void FitBox(Box3 box) { var (c, r) = box.Sphere(); FitSphere(c, r); }
}
