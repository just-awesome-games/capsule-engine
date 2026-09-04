using System.Runtime.InteropServices;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, rewritten once per fixed step and read on draw frames: an ordered stream
/// of render commands over one typed pool per kind of thing that draws.
/// </summary>
public sealed class FrameView
{
    private readonly List<RenderCommand> _commands = [];
    private readonly List<SpriteIntent> _sprites = [];

    private int _submitted;

    private CameraView _camera;
    private ViewBounds _cullBounds;
    private bool _hasCullBounds;

    private TextureSampling _sampling = TextureSampling.Linear;

    /// <summary>The world region on screen. A non-positive <see cref="CameraView.Size"/> draws nothing.</summary>
    public CameraView Camera
    {
        get => _camera;
        internal set
        {
            _camera = value;
            _cullBounds = value.SweptBounds;

            // A camera that spans nothing has swept bounds only where it also moved, and a
            // sliver of a rect is not a region anything should be culled against.
            _hasCullBounds = value.Size.X > 0f && value.Size.Y > 0f && !_cullBounds.IsEmpty;
        }
    }

    /// <summary>The colour behind world render intent. Black by default.</summary>
    public ColorRgba ClearColor { get; internal set; } = ColorRgba.Black;

    /// <summary>How world textures are filtered. Linear by default.</summary>
    public TextureSampling Sampling
    {
        get => _sampling;
        internal set
        {
            if (value is not TextureSampling.Linear and not TextureSampling.Point)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown texture sampling mode.");
            }

            _sampling = value;
        }
    }

    /// <summary>The sprites to draw, in the order added. Invalidated by the next mutation.</summary>
    public ReadOnlySpan<SpriteIntent> Sprites => CollectionsMarshal.AsSpan(_sprites);

    /// <summary>Render-command counts from the current rewrite.</summary>
    public RenderMetrics Metrics => new(_submitted, _commands.Count);

    /// <summary>What to draw and in what order, each naming its kind's pool and its place in it.</summary>
    internal ReadOnlySpan<RenderCommand> Commands => CollectionsMarshal.AsSpan(_commands);

    /// <summary>Drops every pool and the stream, and resets <see cref="Metrics"/>, retaining capacity.</summary>
    internal void Clear()
    {
        _commands.Clear();
        _sprites.Clear();
        _submitted = 0;
    }

    /// <summary>Adds a sprite when its swept bounds cross the camera; an unset camera disables culling.</summary>
    public void Add(in SpriteIntent sprite)
    {
        _submitted++;

        if (_hasCullBounds &&
            !(sprite.TryGetSweptBounds(out ViewBounds swept) && swept.Intersects(_cullBounds)))
        {
            return;
        }

        _commands.Add(new RenderCommand(RenderKind.Sprite, _sprites.Count));
        _sprites.Add(sprite);
    }
}
