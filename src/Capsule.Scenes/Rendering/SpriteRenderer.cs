using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
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
/// sockets are placed the same way, as child entities bound through <see cref="Socket"/>, and its
/// boxes as colliders bound through <see cref="Box"/>. Detaching the renderer disables its boxes, and
/// attaching it to the same entity again re-places them. Attaching it to any other entity throws
/// <see cref="InvalidOperationException"/>.
/// </remarks>
/// <param name="sprite">The frame to draw.</param>
public sealed class SpriteRenderer(Sprite sprite) : Renderer
{
    private Sprite _sprite = sprite;
    private Vector2 _offset;
    private bool _flipX;
    private bool _flipY;

    // The first Socket or Box call allocates its list, because most renderers bind neither.
    private List<SocketBinding>? _sockets;
    private List<BoxBinding>? _boxes;

    /// <summary>
    /// The frame this renderer draws. Swap it to animate, or to change a static frame.
    /// </summary>
    /// <remarks>
    /// Writing it re-places every bound socket the new frame carries and every bound box, under the
    /// rules <see cref="Socket"/> and <see cref="Box"/> describe, before returning.
    /// </remarks>
    public Sprite Sprite
    {
        get => _sprite;

        set
        {
            _sprite = value;
            Place(frameChanged: true);
        }
    }

    /// <summary>
    /// The point in the entity's own space where the frame's pivot lands, placed by the entity's
    /// world transform. Zero by default, which puts it on the entity.
    /// </summary>
    /// <remarks>Bound sockets and boxes follow it.</remarks>
    public Vector2 Offset
    {
        get => _offset;

        set
        {
            _offset = value;
            Place(frameChanged: false);
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

    /// <summary>Whether the frame is mirrored horizontally about its pivot. Bound sockets and boxes mirror with it.</summary>
    public bool FlipX
    {
        get => _flipX;

        set
        {
            _flipX = value;
            Place(frameChanged: false);
        }
    }

    /// <summary>Whether the frame is mirrored vertically about its pivot. Bound sockets and boxes mirror with it.</summary>
    public bool FlipY
    {
        get => _flipY;

        set
        {
            _flipY = value;
            Place(frameChanged: false);
        }
    }

    /// <summary>A tint multiplied into every texel. White by default, which draws the texture unchanged.</summary>
    public ColorRgba Color { get; set; } = ColorRgba.White;

    /// <summary>How the frame's colour combines with what is already drawn. Alpha by default.</summary>
    public BlendMode Blend { get; set; }

    internal override bool Steps => false;

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
        Read(binding);
        Place(binding);

        return binding.Child;
    }

    /// <summary>
    /// Returns the collider covering the box named <paramref name="name"/>. The first call creates it
    /// on a child entity of this renderer's entity, and every later call returns the same instance.
    /// </summary>
    /// <remarks>
    /// The renderer writes the collider's <see cref="Collider2D.Enabled"/>, <see cref="Collider2D.Offset"/>
    /// and <see cref="BoxCollider2D.Size"/>, and owns the child entity, which is named for the box and
    /// leaves the scene with its parent. The game sets its <see cref="Collider2D.Layer"/>,
    /// <see cref="Collider2D.Detects"/>, <see cref="Collider2D.ReportsContacts"/>,
    /// <see cref="Collider2D.OneWay"/> and handlers. A game's own write to the three the renderer
    /// writes lasts until the renderer next places the box.
    /// <para>
    /// On a frame carrying the box the collider is enabled and covers the box's rect. The rect is
    /// measured from the frame's pivot, moved by <see cref="Offset"/> and mirrored about the pivot by
    /// <see cref="FlipX"/> and <see cref="FlipY"/>, in the entity's own space. On a frame without the
    /// box, and before any frame carries it, the collider is disabled. Unlike a socket, a box does not
    /// hold over a frame that lacks it. Every write to <see cref="Sprite"/>, <see cref="Offset"/>,
    /// <see cref="FlipX"/> or <see cref="FlipY"/> re-places it before returning. The box reports a
    /// contact on the step the animator enters its frame.
    /// </para>
    /// </remarks>
    /// <example>
    /// A hurt box that reports what damages it:
    /// <code>
    /// BoxCollider2D hurt = sprite.Box(CapsuleAssets.Sprites.Actors.EnemySheet.Boxes.Hurt);
    /// hurt.Layer = CollisionLayers.EnemyHurt;
    /// hurt.Detects = new(CollisionLayers.Damaging);
    /// hurt.ReportsContacts = true;
    /// hurt.ContactEntered += OnHurt;
    /// </code>
    /// </example>
    /// <param name="name">The box's name as the sheet declared it. The sheet's generated <c>Boxes</c> class lists them.</param>
    /// <exception cref="InvalidOperationException">The renderer is attached to no entity. Attach it first.</exception>
    public BoxCollider2D Box(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (Entity is not { } entity)
        {
            throw new InvalidOperationException(
                "This renderer is attached to no entity. Attach it before binding a box, whose collider sits on a child of that entity.");
        }

        _boxes ??= [];

        foreach (BoxBinding bound in _boxes)
        {
            if (bound.Name == name)
            {
                return bound.Collider;
            }
        }

        // The collider is attached before the child is parented. A turned ancestry then refuses the
        // child before it is linked, and nothing is left behind.
        BoxBinding binding = new(name);
        Read(binding);
        binding.Collider = new BoxCollider2D(Spans(binding) ? Extent(binding.Area) : Vector2.One) { Enabled = false };
        Entity child = new() { Name = name };
        child.Add(binding.Collider);
        child.Parent = entity;

        _boxes.Add(binding);
        Place(binding);

        return binding.Collider;
    }

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        assets.Add(Sprite.Texture);
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

    // Scans the frame's sockets once. Names are interned literals in a generated sheet, so the
    // comparison usually matches on reference without reading a character.
    private void Read(SocketBinding binding)
    {
        ReadOnlySpan<SpriteSocket> carried = _sprite.Marks is { } marks ? marks.Sockets : [];
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
    }

    private void Read(BoxBinding binding)
    {
        ReadOnlySpan<SpriteBox> carried = _sprite.Marks is { } marks ? marks.Boxes : [];
        binding.Carried = false;

        foreach (ref readonly SpriteBox box in carried)
        {
            if (box.Name == binding.Name)
            {
                binding.Area = box.Area;
                binding.Pivot = _sprite.Pivot;
                binding.Carried = true;
                break;
            }
        }
    }

    private void Place(bool frameChanged)
    {
        if (_sockets is not null)
        {
            foreach (SocketBinding binding in _sockets)
            {
                if (frameChanged)
                {
                    Read(binding);
                }

                Place(binding);
            }
        }

        if (_boxes is not null)
        {
            foreach (BoxBinding binding in _boxes)
            {
                if (frameChanged)
                {
                    Read(binding);
                }

                Place(binding);
            }
        }
    }

    // Bound boxes return to the world with the renderer. Their children never left the entity.
    internal override void OnAttachedTo(Entity entity)
    {
        base.OnAttachedTo(entity);
        if (_boxes is null)
        {
            return;
        }

        foreach (BoxBinding binding in _boxes)
        {
            if (!ReferenceEquals(binding.Collider.Entity?.Parent, entity))
            {
                throw new InvalidOperationException(
                    $"This renderer's box '{binding.Name}' sits under the entity it was bound on, not this one. Bind boxes on a new renderer for this entity.");
            }
        }

        foreach (BoxBinding binding in _boxes)
        {
            Place(binding);
        }
    }

    // Bound boxes leave the world with the renderer. Sockets stay where they are.
    internal override void OnDetachingFrom(Entity entity)
    {
        base.OnDetachingFrom(entity);
        if (_boxes is null)
        {
            return;
        }

        foreach (BoxBinding binding in _boxes)
        {
            binding.Collider.Enabled = false;
        }
    }

    // Writes the rect in the entity's own space, mirrored about the pivot as the frame is drawn. The
    // child sits at the entity's origin, and the collider scales the rect from there. Only changed
    // values are written.
    private void Place(BoxBinding binding)
    {
        BoxCollider2D collider = binding.Collider;
        if (Entity is null || !Spans(binding))
        {
            collider.Enabled = false;
            return;
        }

        Rect area = binding.Area;
        Vector2 pivot = binding.Pivot;
        Vector2 size = Extent(area);
        Vector2 offset = _offset + new Vector2(
            _flipX ? pivot.X - area.Right : area.Left - pivot.X,
            _flipY ? pivot.Y - area.Bottom : area.Top - pivot.Y);

        if (size != collider.Size)
        {
            collider.Size = size;
        }

        if (offset != collider.Offset)
        {
            collider.Offset = offset;
        }

        collider.Enabled = true;
    }

    // A hand-built frame may carry a rect too thin to be a box. It leaves the collider disabled.
    private static bool Spans(BoxBinding binding)
    {
        Vector2 size = Extent(binding.Area);
        return binding.Carried && size.X > CollisionTolerance.LinearSlop && size.Y > CollisionTolerance.LinearSlop;
    }

    private static Vector2 Extent(Rect area) => new(area.Right - area.Left, area.Bottom - area.Top);

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

        foreach (SocketBinding binding in _sockets ?? [])
        {
            panel.Field(
                "Socket " + binding.Name,
                DebugPanel.Format(binding.Child.Position)
                    + (binding.Carried ? " on this frame" : binding.Placed ? " held from an earlier frame" : " on no frame yet"));
        }

        foreach (BoxBinding binding in _boxes ?? [])
        {
            panel.Field("Box " + binding.Name, binding.Carried ? "on this frame" : "off");
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

    // One bound box: the collider it places and the rect the current frame carries for it, if any.
    private sealed class BoxBinding(string name)
    {
        internal readonly string Name = name;
        internal BoxCollider2D Collider = null!;
        internal Rect Area;
        internal Vector2 Pivot;
        internal bool Carried;
    }
}
