using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one sprite at one texel per unit of the entity's space. The frame's pivot
/// lands on <see cref="Offset"/> as placed by the entity's world transform, and the frame turns
/// about that point and takes its scale from the transform.
/// </summary>
/// <remarks>
/// A negative scale axis mirrors the frame about the pivot, the same way a flip does. Coordinates
/// are Y-down, in world units under a world root and canvas pixels under a screen root. A frame's
/// sockets are placed the same way, as child entities bound through <see cref="Socket"/>.
/// </remarks>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    private Sprite _sprite = sprite;
    private Vector2 _offset;
    private bool _flipX;
    private bool _flipY;

    // The first Socket call allocates the list, because most renderers bind no sockets.
    private List<SocketBinding>? _sockets;

    /// <summary>
    /// The frame this renderer draws. Swap it to animate, or to change a static frame.
    /// </summary>
    /// <remarks>
    /// Writing it re-places every bound socket the new frame carries, under the rules
    /// <see cref="Socket"/> describes, before returning.
    /// </remarks>
    public Sprite Sprite
    {
        get => _sprite;

        set
        {
            _sprite = value;
            Rebind();
        }
    }

    /// <summary>
    /// The point in the entity's own space where the frame's pivot lands, placed by the entity's
    /// world transform. Zero by default, which puts it on the entity.
    /// </summary>
    /// <remarks>Bound sockets follow it.</remarks>
    public Vector2 Offset
    {
        get => _offset;

        set
        {
            _offset = value;
            Place();
        }
    }

    /// <summary>
    /// How far the frame repeats on each axis, in the entity's own units. Zero, the default, draws
    /// it once.
    /// </summary>
    /// <remarks>
    /// A finite extent covers that distance from the frame's low edge towards +X or +Y, repeating
    /// at the frame's drawn extent and cropping the last copy at the far edge.
    /// <see cref="float.PositiveInfinity"/> repeats without bound on both sides, and draws once
    /// when nothing culls. A negative or NaN component draws nothing, as does a world scale with a
    /// zero axis. A tiled frame cannot turn. A non-zero tiling on an entity whose
    /// <see cref="Entity.WorldTransform"/> is turned draws nothing.
    /// </remarks>
    public Vector2 Tiling { get; set; }

    /// <summary>Whether the frame is mirrored horizontally about its pivot. Bound sockets mirror with it.</summary>
    public bool FlipX
    {
        get => _flipX;

        set
        {
            _flipX = value;
            Place();
        }
    }

    /// <summary>Whether the frame is mirrored vertically about its pivot. Bound sockets mirror with it.</summary>
    public bool FlipY
    {
        get => _flipY;

        set
        {
            _flipY = value;
            Place();
        }
    }

    /// <summary>A tint multiplied into every texel. White by default, which draws the texture unchanged.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>How the frame's colour combines with what is already drawn. Alpha by default.</summary>
    public BlendMode Blend { get; set; }

    /// <summary>
    /// The rect the frame covers: its region at the entity's world scale, placed by the mirrored
    /// pivot and extended to a finite <see cref="Tiling"/>, in the space and under the rules
    /// <see cref="Renderer.Bounds"/> states. An unbounded axis reports the frame's own extent.
    /// </summary>
    /// <remarks>
    /// On an entity with a non-zero world rotation this reports the box of the frame's bounding
    /// circle about the pivot, which covers it at every angle, instead of the tighter rect it
    /// draws. Reads empty whenever the frame draws nothing: a region with no texels, an invalid
    /// tiling, a world scale with a zero axis, or a turned frame that tiles.
    /// </remarks>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || !(Tiling.X >= 0f) || !(Tiling.Y >= 0f))
            {
                return default;
            }

            // Bounds describe the entity's current transform, so this is the rect at rest and not
            // the swept one.
            Transform2D at = RenderTransform;
            if ((Tiling != Vector2.Zero && at.Rotation != 0f) || !Intent(at, at).TryGetSweptBounds(out Rect frame))
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

    /// <summary>
    /// Returns the child entity sitting on the socket named <paramref name="name"/>. The first call
    /// creates it under this renderer's entity and every later call returns the same instance.
    /// </summary>
    /// <remarks>
    /// The renderer owns it. Game code may read its <see cref="Entity.WorldPosition"/> or parent
    /// entities under it, but must not remove or reparent it. It leaves the scene with its parent.
    /// <para>
    /// Its local position is <see cref="Offset"/> plus the socket's point measured from the frame's
    /// pivot, mirrored about that pivot by <see cref="FlipX"/> and <see cref="FlipY"/> the same way
    /// the drawn frame is. The entity's turn and scale then place it as they place the frame. Every
    /// write to <see cref="Sprite"/>, <see cref="Offset"/>, <see cref="FlipX"/> or
    /// <see cref="FlipY"/> re-places it as a teleport. A socket point snaps with its frame and
    /// never interpolates between two frames' points. A frame that does not carry the socket leaves
    /// the child where the last frame carrying it put it, and a later offset or flip re-places it
    /// from that stored point. Until some frame carries it, the child sits at the entity's origin.
    /// The point comes from the frame written this step. A late step, or a component attached after
    /// the animator, reads the socket of the frame that will be drawn.
    /// </para>
    /// </remarks>
    /// <param name="name">The socket's name as the sheet declared it. The sheet's generated <c>Sockets</c> class lists them.</param>
    /// <exception cref="InvalidOperationException">The renderer is attached to no entity. Attach it first.</exception>
    public Entity Socket(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (Entity is not { } entity)
        {
            throw new InvalidOperationException(
                "This renderer is attached to no entity. Attach it before binding a socket, which becomes a child of that entity.");
        }

        _sockets ??= [];

        foreach (SocketBinding bound in _sockets)
        {
            if (bound.Name == name)
            {
                return bound.Child;
            }
        }

        SocketBinding binding = new(name, new Entity(entity) { Name = name });
        _sockets.Add(binding);
        Bind(binding);

        return binding.Child;
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
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderTransform, RenderTransform), Tiling);
    }

    // A negative world scale axis mirrors the frame about the pivot, the same effect as a flip, so fold
    // it into the flip flags and pass the backend the magnitude of the extent.
    private SpriteIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Sprite frame = Sprite;
        Vector2 size = new Vector2(frame.Region.Width, frame.Region.Height) * current.Scale;

        return new SpriteIntent(
            frame,
            previous.TransformPoint(Offset),
            current.TransformPoint(Offset),
            previous.Rotation,
            current.Rotation,
            Vector2.Abs(size),
            FlipX ^ (size.X < 0f),
            FlipY ^ (size.Y < 0f),
            Color,
            Blend);
    }

    // Re-reads every binding's point from the new frame and places it. Called on a frame write.
    private void Rebind()
    {
        if (_sockets is null)
        {
            return;
        }

        foreach (SocketBinding binding in _sockets)
        {
            Bind(binding);
        }
    }

    // Scans the frame's sockets once. Names are interned literals in a generated sheet, so the
    // comparison usually matches on reference without reading a character.
    private void Bind(SocketBinding binding)
    {
        ReadOnlySpan<SpriteSocket> carried = _sprite.Sockets.Span;
        binding.Carried = false;

        foreach (ref readonly SpriteSocket socket in carried)
        {
            if (socket.Name == binding.Name)
            {
                binding.Point = socket.Point;
                binding.Pivot = _sprite.Pivot;
                binding.Carried = true;
                binding.Placed = true;
                break;
            }
        }

        Place(binding);
    }

    // Re-places every binding from the point it already holds. Called on an offset or flip write.
    private void Place()
    {
        if (_sockets is null)
        {
            return;
        }

        foreach (SocketBinding binding in _sockets)
        {
            Place(binding);
        }
    }

    // Mirrors about the pivot, matching the drawn frame. The entity tree composes the parent's scale and
    // turn, which keeps a facing written as a negative scale from folding in twice.
    private void Place(SocketBinding binding)
    {
        if (!binding.Placed)
        {
            return;
        }

        Vector2 fromPivot = binding.Point - binding.Pivot;
        Vector2 local = _offset + new Vector2(_flipX ? -fromPivot.X : fromPivot.X, _flipY ? -fromPivot.Y : fromPivot.Y);

        if (local != binding.Child.Position)
        {
            binding.Child.Teleport(local);
        }
    }

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        TextureRegion region = Sprite.Region;
        panel.Field("Sprite", string.Create(CultureInfo.InvariantCulture, $"({region.X}, {region.Y}) {region.Width}x{region.Height}"));
        panel.Field("Offset", Offset);
        panel.Field("Tiling", Tiling);
        panel.Field("Color", Color);
        panel.Field("Blend", Blend.ToString());
        panel.Toggle("FlipX", FlipX, on => FlipX = on);
        panel.Toggle("FlipY", FlipY, on => FlipY = on);

        if (_sockets is null)
        {
            return;
        }

        foreach (SocketBinding binding in _sockets)
        {
            panel.Field(
                "Socket " + binding.Name,
                DebugPanel.Format(binding.Child.Position)
                    + (binding.Carried ? " on this frame" : binding.Placed ? " held from an earlier frame" : " on no frame yet"));
        }
    }

    // One bound socket: the child it places and the last point a frame carried for it. The stored point
    // lets an offset or flip re-place the child even while the current frame lacks the socket.
    private sealed class SocketBinding(string name, Entity child)
    {
        internal readonly string Name = name;
        internal readonly Entity Child = child;
        internal Vector2 Point;
        internal Vector2 Pivot;
        internal bool Placed;
        internal bool Carried;
    }
}
