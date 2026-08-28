using System.Runtime.InteropServices;

namespace Capsule.Rendering;

/// <summary>Mutable render intent, rewritten once per fixed step and read on draw frames.</summary>
public sealed class FrameView
{
    private readonly List<QuadIntent> _quads = [];
    private int _totalQuads;

    private CameraView _camera;
    private ViewBounds _cullBounds;
    private bool _hasCullBounds;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.</summary>
    public CameraView Camera
    {
        get => _camera;
        set
        {
            _camera = value;
            _cullBounds = value.SweptBounds;

            // A camera that spans nothing has swept bounds only where it also moved, and a
            // sliver of a rect is not a region anything should be culled against.
            _hasCullBounds = value.Size.X > 0f && value.Size.Y > 0f && !_cullBounds.IsEmpty;
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

    /// <summary>Drops every quad and resets <see cref="Metrics"/>, retaining capacity.</summary>
    public void Clear()
    {
        _quads.Clear();
        _totalQuads = 0;
    }

    /// <summary>Adds a quad when its swept bounds cross the camera; an unset camera disables culling.</summary>
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
        // Tested on the size itself, never left to the swept rect: travel widens that rect, so a
        // quad moving further than a negative extent still measures positive area there and would
        // reach the renderer to be drawn inverted. NaN fails this comparison too.
        if (!(quad.Size.X > 0f) || !(quad.Size.Y > 0f))
        {
            return false;
        }

        ViewBounds swept = new(
            MathF.Min(quad.PreviousPosition.X, quad.Position.X),
            MathF.Min(quad.PreviousPosition.Y, quad.Position.Y),
            MathF.Max(quad.PreviousPosition.X, quad.Position.X) + quad.Size.X,
            MathF.Max(quad.PreviousPosition.Y, quad.Position.Y) + quad.Size.Y);

        // What remains for IsEmpty is a corner or an extent that is not finite.
        return !swept.IsEmpty && swept.Intersects(_cullBounds);
    }
}
