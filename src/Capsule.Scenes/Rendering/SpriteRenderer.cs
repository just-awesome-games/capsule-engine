using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one sprite, one texel per unit of the entity's space: the frame's pivot
/// lands on <see cref="Offset"/> placed by the entity's world transform, and the frame turns about
/// that point and is sized by the transform, a negative axis of whose scale mirrors the frame about
/// the pivot as a flip would. Y-down, in world units under a world root and canvas pixels under a
/// screen one. A frame's sockets are placed the same way, as child entities bound through
/// <see cref="Socket"/>.
/// </summary>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    private Sprite _sprite = sprite;
    private Vector2 _offset;
    private bool _flipX;
    private bool _flipY;

    // Allocated by the first Socket call: most renderers bind none.
    private List<SocketBinding>? _sockets;

    /// <summary>
    /// The frame drawn; swapped to animate, or to change a static frame. Writing it places every
    /// bound socket the frame carries, on <see cref="Socket"/>'s terms, before returning.
    /// </summary>
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
    /// The point in the entity's own space the frame's pivot lands on, placed by the entity's world
    /// transform; zero by default, which is the entity itself. Bound sockets follow it.
    /// </summary>
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
    /// How far the frame repeats, per axis, in the entity's own units; zero, the default, draws it
    /// once. A finite extent covers that much from the frame's low edge towards +X or +Y at a period
    /// of the frame's drawn extent, cropping the copy at the far edge; <see cref="float.PositiveInfinity"/>
    /// repeats without bound on both sides of the frame, and draws it once where nothing culls. A
    /// component that is negative or NaN draws nothing, as a world scale with a zero axis does, and
    /// so does a non-zero tiling on an entity whose <see cref="Entity.WorldTransform"/> is turned:
    /// a tiled frame does not turn.
    /// </summary>
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

    /// <summary>Multiplied into every texel; white, which draws the texture as it is, by default.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>
    /// The rect the frame covers: its region at the entity's world scale, placed by the pivot a
    /// flip has mirrored, and extended to a finite <see cref="Tiling"/> — an unbounded axis reports
    /// the frame's own extent — in the space and on the terms <see cref="Renderer.Bounds"/> states.
    /// A frame on an entity whose world rotation is not zero reports the box of its bounding
    /// circle about the pivot, which covers it at every angle, rather than the tighter rect it
    /// draws. Empty where the frame draws nothing — a region with no texels, a tiling that is not
    /// a tiling, a world scale with a zero axis, or a turned frame that tiles.
    /// </summary>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || !(Tiling.X >= 0f) || !(Tiling.Y >= 0f))
            {
                return default;
            }

            // The rect at rest, not the one it swept: bounds answer for the entity's current transform.
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
    /// The child entity that sits on the socket named <paramref name="name"/>: created under this
    /// renderer's entity on the first call, the same instance on every later one. The renderer
    /// owns it — a game reads its <see cref="Entity.WorldPosition"/> or parents its own entities
    /// under it, and neither removes nor reparents it; it leaves the scene with the entity it is
    /// under.
    /// <para>
    /// Its local position is <see cref="Offset"/> plus the socket's point taken from the frame's
    /// pivot, mirrored about that pivot by <see cref="FlipX"/> and <see cref="FlipY"/> exactly as
    /// the drawn frame is, so the entity's turn and scale place it as they place the frame.
    /// Rewritten on every write to <see cref="Sprite"/>, <see cref="Offset"/>, <see cref="FlipX"/>
    /// or <see cref="FlipY"/> as a teleport: a point is a property of a discrete frame and snaps as
    /// the frame does, never interpolating between two frames' points. A frame that does not carry
    /// the socket leaves the child where the last frame carrying it put it, and a later offset or
    /// flip re-places it from that frame's point; until any frame carries it, the child sits at the
    /// entity's origin. The frame it is read from is the one written this step, so a late step or a
    /// component attached after the animator reads the socket of the frame drawn.
    /// </para>
    /// </summary>
    /// <param name="name">The socket's name, as the sheet declared it; <c>CapsuleAssets.Sprites.&lt;Sheet&gt;.Sockets</c> spells each.</param>
    /// <exception cref="ArgumentException">The name is null or empty.</exception>
    /// <exception cref="InvalidOperationException">The renderer is attached to no entity; attach it first.</exception>
    public Entity Socket(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (Entity is not { } entity)
        {
            throw new InvalidOperationException(
                "A socket is a child of the renderer's entity; add the renderer to an entity before binding one.");
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
    public override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        view.Add(Intent(PreviousRenderTransform, RenderTransform), Tiling);
    }

    // A negative axis of the world scale is a mirror about the pivot, which is what a flip is, so
    // it folds into the flip and the extent stays the magnitude the backend draws.
    private SpriteIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Sprite frame = Sprite;
        Vector2 size = new Vector2(frame.Region.Width, frame.Region.Height) * current.Scale;

        return new SpriteIntent(
            frame,
            previous.Apply(Offset),
            current.Apply(Offset),
            previous.Rotation,
            current.Rotation,
            Vector2.Abs(size),
            FlipX ^ (size.X < 0f),
            FlipY ^ (size.Y < 0f),
            Color);
    }

    // Every binding re-reads its point from the frame, then is placed. Called on a frame write.
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

    // One scan of the frame's sockets: names are interned literals in a generated sheet, so the
    // comparison short-circuits on reference before it reads a character.
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

    // Every binding re-placed from the point it holds. Called on an offset or flip write.
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

    // The mirror is about the pivot, as the drawn frame's is; the parent's scale and turn are the
    // tree's to compose, so a facing written as a negative scale is never folded in twice.
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

    // One bound socket: the child it places and the last point a frame carried for it, kept so an
    // offset or flip written on a frame without the socket re-places the child from that point.
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
