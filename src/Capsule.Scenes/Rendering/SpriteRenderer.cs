using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one sprite, one texel per unit of the entity's space until
/// <see cref="Scale"/> says otherwise. The frame's pivot lands on the entity's position plus
/// <see cref="Offset"/>, and <see cref="Rotation"/> turns the frame about that point. Y-down, in
/// world units on a world entity and canvas pixels on a screen one.
/// </summary>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    private float _previousRotation;

    /// <summary>The frame drawn; swapped to animate, or to change a static frame.</summary>
    public Sprite Sprite { get; set; } = sprite;

    /// <summary>
    /// Added to the entity's position to give the point the frame's pivot lands on. In the entity's
    /// own units; zero by default.
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// Multiplies the frame's drawn extent per axis, about its pivot; <see cref="Vector2.One"/>,
    /// one texel per world unit, by default. Presentation only — a collider never reads it — and
    /// not a mirror: <see cref="FlipX"/> and <see cref="FlipY"/> are. A component that is not
    /// positive and finite draws nothing.
    /// </summary>
    public Vector2 Scale { get; set; } = Vector2.One;

    /// <summary>
    /// The turn of the frame about its pivot, in radians, clockwise positive in the Y-down space;
    /// zero by default. Presentation only, as <see cref="Scale"/> is: no collider reads it, and the
    /// entity carries no angle. The host interpolates it along the shortest arc from the value this
    /// property held at the top of the current step, which the engine retains, so a value written
    /// during a step turns over the frame that follows and a value written outside one — from
    /// <see cref="Component.OnStart"/>, a host, or the overlay — shows at once. A non-finite rotation
    /// draws nothing, and a turned frame does not tile: with a non-zero <see cref="Tiling"/> the
    /// frame draws nothing.
    /// </summary>
    public float Rotation
    {
        get;

        set
        {
            field = value;

            // Written outside a step, the pair has no step to be retained at the top of, so the
            // value is both ends of the next frame's interpolation.
            if (Entity?.Scene?.SteppingTick is null)
            {
                _previousRotation = value;
            }
        }
    }

    /// <summary>
    /// How far the frame repeats, per axis, in the entity's own units; zero, the default, draws it
    /// once. A finite extent covers that much from the frame's low edge towards +X or +Y at a period
    /// of the frame's drawn extent, cropping the copy at the far edge; <see cref="float.PositiveInfinity"/>
    /// repeats without bound on both sides of the frame, and draws it once where nothing culls. A
    /// component that is negative or NaN draws nothing, as a scale that is not positive does, and so
    /// does a non-zero tiling on a frame with a non-zero <see cref="Rotation"/>: a tiled frame does
    /// not turn.
    /// </summary>
    public Vector2 Tiling { get; set; }

    /// <summary>Whether the frame is mirrored horizontally about its pivot.</summary>
    public bool FlipX { get; set; }

    /// <summary>Whether the frame is mirrored vertically about its pivot.</summary>
    public bool FlipY { get; set; }

    /// <summary>Multiplied into every texel; white, which draws the texture as it is, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The rect the frame covers: its region at <see cref="Scale"/>, placed by the pivot a flip has
    /// mirrored, and extended to a finite <see cref="Tiling"/> — an unbounded axis reports the frame's
    /// own extent — in the space and on the terms <see cref="Renderer.Bounds"/> states. A frame with
    /// a non-zero <see cref="Rotation"/> reports the box of its bounding circle about the pivot, which
    /// covers it at every angle, rather than the tighter rect it draws. Empty where the frame draws
    /// nothing — a region with no texels, a scale or tiling that is not a scale or tiling, a
    /// non-finite rotation, or a turned frame that tiles.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || !(Tiling.X >= 0f) || !(Tiling.Y >= 0f))
            {
                return default;
            }

            if (Tiling != Vector2.Zero && Rotation != 0f)
            {
                return default;
            }

            // The rect at rest, not the one it swept: bounds answer for the entity's current position.
            Vector2 position = RenderPosition + Offset;

            if (!Intent(position, position, Rotation).TryGetSweptBounds(out Rect frame))
            {
                return default;
            }

            return new Rect(
                frame.Left,
                frame.Top,
                Tiling.X > 0f && float.IsFinite(Tiling.X) ? frame.Left + Tiling.X : frame.Right,
                Tiling.Y > 0f && float.IsFinite(Tiling.Y) ? frame.Top + Tiling.Y : frame.Bottom);
        }
    }

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        TextureHandle texture = Sprite.Texture;
        if (texture != default)
        {
            assets.Add(texture);
        }
    }

    /// <inheritdoc/>
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderPosition + Offset, RenderPosition + Offset, _previousRotation), Tiling);
    }

    // Retained here, never in Draw: the host may rewrite the frame between steps, and a retention
    // there would collapse the pair before the frame after the step had interpolated it.
    internal override void Retain() => _previousRotation = Rotation;

    private SpriteIntent Intent(Vector2 previousPosition, Vector2 position, float previousRotation)
    {
        Sprite frame = Sprite;

        return new SpriteIntent(
            frame,
            previousPosition,
            position,
            previousRotation,
            Rotation,
            new Vector2(frame.Region.Width, frame.Region.Height) * Scale,
            FlipX,
            FlipY,
            Color);
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        TextureRegion region = Sprite.Region;
        panel.Field("Sprite", string.Create(CultureInfo.InvariantCulture, $"({region.X}, {region.Y}) {region.Width}x{region.Height}"));
        panel.Field("Offset", Offset);
        panel.Field("Scale", Scale);
        panel.Field("Rotation", Rotation);
        panel.Field("Tiling", Tiling);
        panel.Field("Color", Color);
        panel.Toggle("FlipX", FlipX, on => FlipX = on);
        panel.Toggle("FlipY", FlipY, on => FlipY = on);
    }
}
