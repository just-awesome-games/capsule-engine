using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Assets;

namespace Capsule.Rendering;

/// <summary>
/// Mutable render intent, rewritten once per fixed step and read on draw frames: an ordered list
/// of sprites to draw. Text is on that list too — a <see cref="TextIntent"/> becomes one sprite per
/// glyph — so <see cref="Metrics"/> counts a run of text once per glyph.
/// </summary>
public sealed class FrameView
{
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

    /// <summary>Sprite-submission counts from the current rewrite.</summary>
    public RenderMetrics Metrics => new(_submitted, _sprites.Count);

    // Drops the ordered intent and resets Metrics, retaining capacity.
    internal void Clear()
    {
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

        _sprites.Add(sprite);
    }

    /// <summary>
    /// Lays <paramref name="text"/> out and adds one sprite per glyph, each culled and counted on
    /// its own. Glyphs are added in reading order, so a later one covers an earlier one where they
    /// overlap. A null font or empty text adds nothing.
    /// </summary>
    public void Add(in TextIntent text)
    {
        BitmapFont? font = text.Font;
        if (font is null || string.IsNullOrEmpty(text.Text))
        {
            return;
        }

        int lineHeight = font.LineHeight;
        ReadOnlySpan<TextureHandle> pages = font.Pages;

        foreach (GlyphPlacement placed in new GlyphRun(font, text.Text))
        {
            Glyph glyph = placed.Glyph;
            Vector2 offset = new Vector2(
                placed.PenX + glyph.XOffset,
                (placed.Line * lineHeight) + glyph.YOffset) * text.Scale;

            Add(new SpriteIntent(
                new Sprite(pages[glyph.Page], glyph.Region),
                text.PreviousPosition + offset,
                text.Position + offset,
                new Vector2(glyph.Region.Width, glyph.Region.Height) * text.Scale,
                FlipX: false,
                FlipY: false,
                text.Color));
        }
    }
}
