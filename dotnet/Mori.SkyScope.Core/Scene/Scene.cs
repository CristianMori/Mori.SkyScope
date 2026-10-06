// Mori.SkyScope — The 2D scene: ordered layers, per-layer surface caching by projection version, hit testing.
// Author: Cristian Mori. Copyright 2026 Cristian Mori. Licensed under the Apache License, Version 2.0.

using Mori.SkyScope.Core.Paint;

namespace Mori.SkyScope.Core.Scene;

/// <summary>What a layer gets to draw with. Mirrors <c>LayerContext</c> in <c>scene/scene.ts</c>.</summary>
/// <param name="Painter">2D painter to draw with; for cacheable layers this is the layer's own offscreen surface.</param>
/// <param name="Projection">World → screen mapping.</param>
/// <param name="Width">Viewport width in pixels.</param>
/// <param name="Height">Viewport height in pixels.</param>
/// <param name="Now">Chart time in seconds (frame lookups, marker expiry).</param>
/// <param name="Opacity">Effective opacity: scene opacity × layer opacity, 0–1.</param>
public sealed record LayerContext(IPainter Painter, IProjection Projection, double Width, double Height, double Now, double Opacity)
{
    /// <summary>Present when the host renders 3D; 3D layers draw through it, the 2D painter draws the HUD.</summary>
    public Scene3D.IPainter3D? Painter3D { get; init; }
    /// <summary>The same context without the painter, for geometry helpers shared by drawing and hit testing.</summary>
    public HitContext Hit => new(Projection, Width, Height, Now);
}

/// <summary>What hit testing gets: the projection, the viewport size in pixels and the chart time in seconds.</summary>
public sealed record HitContext(IProjection Projection, double Width, double Height, double Now);

/// <summary>One hit reported by a layer.</summary>
/// <param name="LayerId">Id of the layer that was hit.</param>
/// <param name="World">World position of the hit (z = 0 for 2D layers).</param>
/// <param name="Screen">Probe position in screen pixels.</param>
/// <param name="Distance">Screen distance in pixels from the probe to the hit; 0 for area hits.</param>
public sealed record HitResult(string LayerId, Vec3 World, Vec2 Screen, double Distance)
{
    /// <summary>Layer-specific index: a point, segment, cell or shape index.</summary>
    public int? Index { get; init; }
    /// <summary>Layer-specific payload: a cell value, an intensity, a shape id, a pose.</summary>
    public object? Data { get; init; }
}

/// <summary>
/// A drawable, hit-testable member of a <see cref="Scene"/>. Layers with <see cref="Cacheable"/> are drawn
/// into an offscreen surface (via <see cref="IPainter.Layer"/>) reused until the layer is marked dirty or the
/// projection changes.
/// </summary>
public interface ILayer
{
    /// <summary>Unique within a scene; used for lookups, caching keys and hit results.</summary>
    string Id { get; }
    /// <summary>Short type name as used by <see cref="LayerJson"/>, for example <c>"polyline"</c>.</summary>
    string Kind { get; }
    /// <summary>Projection kinds the layer can draw under; it is skipped for the others.</summary>
    IReadOnlyList<CameraKind> CameraKinds { get; }
    /// <summary>Hidden layers are neither drawn, hit tested nor counted in bounds.</summary>
    bool Visible { get; set; }
    /// <summary>0–1, multiplied with the scene's opacity before drawing.</summary>
    double Opacity { get; set; }
    /// <summary>Draw into a reusable offscreen surface. Turn off for layers that change every frame.</summary>
    bool Cacheable { get; set; }
    /// <summary>Set to force a redraw of the cached surface on the next draw; the scene clears it after drawing.</summary>
    bool Dirty { get; set; }
    /// <summary>World-space bounding box for fit-all, or null when empty or unbounded.</summary>
    Rect? Bounds();
    /// <summary>Draw with the context's painter; the projection is already set up.</summary>
    void Draw(LayerContext ctx);
    /// <summary>The hit at screen point (<paramref name="sx"/>, <paramref name="sy"/>) within <paramref name="tolerance"/> pixels, or null.</summary>
    HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance);
}

/// <summary>Default layer implementation: visible, opaque, cacheable, dirty, 2D-only, without bounds or hits. Subclasses supply <see cref="Kind"/> and <see cref="Draw"/>.</summary>
public abstract class BaseLayer(string id) : ILayer
{
    /// <summary>Fixed at construction.</summary>
    public string Id { get; } = id;
    /// <summary>Short type name, see <see cref="ILayer.Kind"/>.</summary>
    public abstract string Kind { get; }
    /// <summary>2D only unless overridden.</summary>
    public virtual IReadOnlyList<CameraKind> CameraKinds => [CameraKind.TwoD];
    /// <summary>Defaults to visible.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>Defaults to opaque; 0–1.</summary>
    public double Opacity { get; set; } = 1;
    /// <summary>Defaults to cached; layers that move every frame set this to false.</summary>
    public bool Cacheable { get; set; } = true;
    /// <summary>Starts dirty so the first draw renders the surface.</summary>
    public bool Dirty { get; set; } = true;
    /// <summary>Request a redraw of the cached surface on the next draw.</summary>
    public void MarkDirty() => Dirty = true;
    /// <summary>No bounds unless overridden.</summary>
    public virtual Rect? Bounds() => null;
    /// <summary>Draw the layer; see <see cref="ILayer.Draw"/>.</summary>
    public abstract void Draw(LayerContext ctx);
    /// <summary>Never hits unless overridden.</summary>
    public virtual HitResult? HitTest(double sx, double sy, HitContext ctx, double tolerance) => null;
}

/// <summary>An ordered stack of layers sharing one projection. Pinned by <c>spec/fixtures/scene.json</c>.</summary>
public sealed class Scene
{
    private readonly List<ILayer> _layers = [];
    private int _lastProjectionVersion = -1;

    /// <summary>Layers in draw order, bottom first. A live view: do not mutate while iterating.</summary>
    public IReadOnlyList<ILayer> Layers => _layers;
    /// <summary>Scene-wide opacity, 0–1, multiplied into every layer's.</summary>
    public double Opacity { get; set; } = 1;

    /// <summary>Insert a layer at z position <paramref name="index"/> (0 = bottom); null or an index past the end appends. Throws when a layer with the same id is already present.</summary>
    /// <returns>This scene, for chaining.</returns>
    public Scene Add(ILayer layer, int? index = null)
    {
        if (_layers.Any(l => l.Id == layer.Id)) throw new InvalidOperationException($"layer \"{layer.Id}\" already in scene");
        if (index is null || index >= _layers.Count) _layers.Add(layer); else _layers.Insert(Math.Max(0, index.Value), layer);
        return this;
    }
    /// <summary>Remove the layer with this id; false when there is none.</summary>
    public bool Remove(string id) => _layers.RemoveAll(l => l.Id == id) > 0;
    /// <summary>The layer with this id, or null.</summary>
    public ILayer? Get(string id) => _layers.FirstOrDefault(l => l.Id == id);
    /// <summary>Move a layer to a new z position (0 = bottom).</summary>
    public bool Move(string id, int index)
    {
        var i = _layers.FindIndex(l => l.Id == id);
        if (i < 0) return false;
        var l = _layers[i]; _layers.RemoveAt(i);
        _layers.Insert(Math.Clamp(index, 0, _layers.Count), l);
        return true;
    }
    /// <summary>Layer ids bottom to top (a snapshot).</summary>
    public IReadOnlyList<string> Order() => _layers.Select(l => l.Id).ToList();

    /// <summary>Draw every visible layer that supports the projection's kind, bottom to top, without a 3D painter.</summary>
    public void Draw(IPainter painter, IProjection projection, double now) => Draw(painter, projection, now, null);
    /// <summary>Draw every visible layer that supports the projection's kind, bottom to top. Cacheable layers go through <see cref="IPainter.Layer"/> and are re-rendered only when dirty or when the projection version changed; <see cref="ILayer.Dirty"/> is cleared afterwards. <paramref name="now"/> is chart time in seconds.</summary>
    public void Draw(IPainter painter, IProjection projection, double now, Scene3D.IPainter3D? painter3d)
    {
        var projectionChanged = projection.Version != _lastProjectionVersion;
        _lastProjectionVersion = projection.Version;
        double width = painter.Width, height = painter.Height;
        foreach (var layer in _layers)
        {
            if (!layer.Visible || !layer.CameraKinds.Contains(projection.Kind)) continue;
            var ctx = new LayerContext(painter, projection, width, height, now, Opacity * layer.Opacity) { Painter3D = painter3d };
            if (layer.Cacheable)
            {
                painter.Layer($"layer:{layer.Id}", width, height, p => layer.Draw(ctx with { Painter = p }), 0, 0, layer.Dirty || projectionChanged);
                layer.Dirty = false;
            }
            else
            {
                painter.Save();
                layer.Draw(ctx);
                painter.Restore();
                layer.Dirty = false;
            }
        }
    }

    /// <summary>Topmost hit within <paramref name="tolerance"/> screen pixels.</summary>
    public HitResult? HitTest(double sx, double sy, IProjection projection, double width, double height, double tolerance = 6, double now = 0)
    {
        var ctx = new HitContext(projection, width, height, now);
        for (var i = _layers.Count - 1; i >= 0; i--)
        {
            var layer = _layers[i];
            if (!layer.Visible || !layer.CameraKinds.Contains(projection.Kind)) continue;
            if (layer.HitTest(sx, sy, ctx, tolerance) is { } hit) return hit;
        }
        return null;
    }

    /// <summary>Union of visible layers' world bounds (for fit-all).</summary>
    public Rect? Bounds()
    {
        Rect? r = null;
        foreach (var l in _layers) if (l.Visible) r = Geometry.RectUnion(r, l.Bounds());
        return r;
    }
}
