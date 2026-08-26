using System.Numerics;
using System.Runtime.InteropServices;

namespace Capsule.Rendering;

/// <summary>
/// Everything a simulation wants drawn. A simulation holds one and rewrites it once per
/// fixed step — <see cref="Clear"/>, set frame intent, then <see cref="AddQuad"/> — while
/// the renderer reads it on every draw frame; rewriting anywhere but the step tears.
/// <see cref="Clear"/> retains the quad capacity, so the steady state allocates nothing.
/// </summary>
public sealed class FrameView
{
    private readonly List<QuadIntent> _quads = [];
    private int _totalQuads;

    private CameraView _camera;
    private float _cameraLeft;
    private float _cameraTop;
    private float _cameraRight;
    private float _cameraBottom;
    private bool _hasCullBounds;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.</summary>
    public CameraView Camera
    {
        get => _camera;
        set
        {
            _camera = value;

            Vector2 halfSize = value.Size / 2f;
            _cameraLeft = value.Center.X - halfSize.X;
            _cameraTop = value.Center.Y - halfSize.Y;
            _cameraRight = value.Center.X + halfSize.X;
            _cameraBottom = value.Center.Y + halfSize.Y;

            _hasCullBounds =
                value.Size.X > 0f &&
                value.Size.Y > 0f &&
                float.IsFinite(_cameraLeft) &&
                float.IsFinite(_cameraTop) &&
                float.IsFinite(_cameraRight) &&
                float.IsFinite(_cameraBottom);
        }
    }

    /// <summary>The colour behind world render intent. Black by default.</summary>
    public ColorRgba ClearColor { get; set; } = ColorRgba.Black;

    /// <summary>How world textures are filtered. Linear by default.</summary>
    public TextureSampling Sampling
    {
        get => _sampling;
        set
        {
            if (value is not TextureSampling.Linear and not TextureSampling.Point)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown texture sampling mode.");
            }

            _sampling = value;
        }
    }

    /// <summary>The quads to draw, in the order added. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<QuadIntent> Quads => CollectionsMarshal.AsSpan(_quads);

    /// <summary>Quad counts from the current rewrite.</summary>
    public RenderMetrics Metrics => new(_totalQuads, _quads.Count);

    /// <summary>
    /// Drops every quad and resets <see cref="Metrics"/>, keeping capacity and render intent.
    /// </summary>
    public void Clear()
    {
        _quads.Clear();
        _totalQuads = 0;
    }

    /// <summary>
    /// Adds a quad when its swept bounds cross the camera. A camera without finite positive
    /// bounds leaves culling disabled, allowing a view to be assembled before its camera opens.
    /// </summary>
    public void AddQuad(in QuadIntent quad)
    {
        _totalQuads++;

        if (!_hasCullBounds || IsVisible(quad))
        {
            _quads.Add(quad);
        }
    }

    private bool IsVisible(in QuadIntent quad)
    {
        if (!(quad.Size.X > 0f) || !(quad.Size.Y > 0f))
        {
            return false;
        }

        float left = MathF.Min(quad.PreviousPosition.X, quad.Position.X);
        float top = MathF.Min(quad.PreviousPosition.Y, quad.Position.Y);
        float right = MathF.Max(quad.PreviousPosition.X, quad.Position.X) + quad.Size.X;
        float bottom = MathF.Max(quad.PreviousPosition.Y, quad.Position.Y) + quad.Size.Y;

        return
            float.IsFinite(left) &&
            float.IsFinite(top) &&
            float.IsFinite(right) &&
            float.IsFinite(bottom) &&
            right > _cameraLeft &&
            bottom > _cameraTop &&
            left < _cameraRight &&
            top < _cameraBottom;
    }
}
