using System.Globalization;
using System.Numerics;
using Capsule.Assets;
using Capsule.Diagnostics;
using Capsule.Physics;
using Capsule.Scenes;

namespace Capsule.Rendering;

/// <summary>
/// Draws its entity as one sprite, or that sprite tiled across <see cref="Tiling"/>, at one texel
/// per unit of the entity's space.
/// </summary>
/// <remarks>
/// The frame's pivot lands on <see cref="Offset"/> as placed by the entity's world transform, and
/// the frame turns about that point and takes its scale from the transform. A negative scale axis
/// mirrors the frame about the pivot, the same way a flip does. Coordinates are Y-down, in world
/// units under a world root and canvas pixels under a screen root. A frame's
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

    // The first Socket, Box or Variant call allocates this, because most renderers use none of them.
    private Extras? _extras;

    /// <summary>
    /// The frame this renderer holds, which it draws unless <see cref="Variant"/> maps it. Swap it to
    /// animate, or to change a static frame.
    /// </summary>
    /// <remarks>
    /// Writing it re-places every bound socket the new frame carries and every bound box, under the
    /// rules <see cref="Socket"/> and <see cref="Box"/> describe, before returning. Sockets and boxes
    /// always come from this frame, whatever <see cref="Variant"/> draws in its place.
    /// </remarks>
    public Sprite Sprite
    {
        get => _sprite;

        set
        {
            _sprite = value;
            Resolve();
            Place(frameChanged: true);
        }
    }

    /// <summary>
    /// The other look this renderer draws <see cref="Sprite"/> through, and null by default, which
    /// draws <see cref="Sprite"/> itself.
    /// </summary>
    /// <remarks>
    /// A frame the variant maps draws as its mapped sprite, or draws nothing where the variant maps it
    /// to nothing. A frame it does not map draws as itself. <see cref="Sprite"/>, sockets and boxes
    /// still read the held frame. A variant never moves a socket or a box. <see cref="Bounds"/>
    /// covers the frame drawn. A variant held when the scene collects its assets is preloaded with
    /// the scene. One set later loads each texture on its first draw, and its owner preloads it by
    /// adding <see cref="SpriteVariant.Textures"/> in its own <c>CollectAssets</c>.
    /// </remarks>
    /// <example>
    /// A hat drawn over an avatar, one drawing for each of the avatar's frames:
    /// <code>
    /// SpriteRenderer hat = new(CapsuleAssets.Sprites.AvatarSheet.Frames.Idle0)
    /// {
    ///     Variant = CapsuleAssets.Sprites.AvatarHatSheet.Variant,
    /// };
    /// </code>
    /// </example>
    public SpriteVariant? Variant
    {
        get => _extras?.Look?.Variant;

        set
        {
            if (value is not null)
            {
                (_extras ??= new()).Look = new VariantLook(value);
                Resolve();
            }
            else if (_extras is { } extras)
            {
                extras.Look = null;
            }
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
    /// How far the frame tiles on each axis, in the entity's own units. Zero, the default, draws it
    /// once.
    /// </summary>
    /// <remarks>
    /// A finite extent covers that distance from the frame's low edge towards +X or +Y, repeating
    /// at the frame's drawn size and cropping the last copy at the far edge.
    /// <see cref="float.PositiveInfinity"/> repeats without bound on both sides. A negative or NaN
    /// component draws nothing, as does a non-zero tiling on an entity whose
    /// <see cref="Entity.WorldTransform"/> is turned.
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
    /// draws. It measures the frame drawn, which <see cref="Variant"/> may replace. Reads empty
    /// whenever the frame draws nothing: a variant mapping it to nothing, a region with no texels,
    /// an invalid tiling, a world scale with a zero axis, or a turned frame that tiles.
    /// </remarks>
    public override Rect Bounds
    {
        get
        {
            if (Entity is null || DrawnFrame is null || !(Tiling.X >= 0f) || !(Tiling.Y >= 0f))
            {
                return default;
            }

            // Bounds describe the entity's current transform. This is the rect at rest, not the swept one.
            Transform2D at = RenderTransform;
            if ((Tiling != Vector2.Zero && at.Rotation != 0f) || !Intent(at, at).TryGetSweptBounds(out Rect frame))
            {
                return default;
            }

            return Rect.FromEdges(
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
    /// The renderer owns the child. Game code may read its <see cref="Entity.WorldPosition"/> or parent
    /// entities under it, but must not remove or reparent it. It leaves the scene with its parent.
    /// <para>
    /// Its local position is <see cref="Offset"/> plus the socket's point measured from the frame's
    /// pivot, mirrored by <see cref="FlipX"/> and <see cref="FlipY"/> as the frame is. Every write to
    /// <see cref="Sprite"/>, <see cref="Offset"/>, <see cref="FlipX"/> or <see cref="FlipY"/> teleports
    /// it. A frame that does not carry the socket leaves the child at the last point a frame carried.
    /// Until some frame carries it, the child sits at the entity's origin. A component stepping after
    /// the animator reads the socket of the frame that will be drawn.
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

        List<SocketBinding> sockets = (_extras ??= new()).Sockets ??= [];

        foreach (SocketBinding bound in sockets)
        {
            if (bound.Name == name)
            {
                return bound.Child;
            }
        }

        SocketBinding binding = new(name, new Entity(entity) { Name = name });
        sockets.Add(binding);
        Read(binding);
        Place(binding);

        return binding.Child;
    }

    /// <summary>
    /// Returns the collider covering the box named <paramref name="name"/>. The first call attaches it
    /// to this renderer's entity, and every later call returns the same instance.
    /// </summary>
    /// <remarks>
    /// A contact with the box reports that entity. The renderer writes the collider's <see cref="Collider2D.Enabled"/>,
    /// <see cref="Collider2D.Offset"/> and <see cref="BoxCollider2D.Size"/>, and a game's own write to
    /// those lasts until the renderer next places the box. The game sets the rest, such as
    /// <see cref="Collider2D.Layer"/>, <see cref="Collider2D.Detects"/> and the handlers.
    /// <para>
    /// On a frame carrying the box the collider is enabled and covers the box's rect, measured from the
    /// frame's pivot, moved by <see cref="Offset"/> and mirrored by <see cref="FlipX"/> and
    /// <see cref="FlipY"/> in the entity's own space. On a frame without the box, and before any frame
    /// carries it, the collider is disabled. Every write to <see cref="Sprite"/>, <see cref="Offset"/>,
    /// <see cref="FlipX"/> or <see cref="FlipY"/> re-places it. The box reports a contact on the step
    /// the animator enters its frame.
    /// </para>
    /// </remarks>
    /// <example>
    /// A door's reach box that reports what walks into it:
    /// <code>
    /// BoxCollider2D reach = sprite.Box(CapsuleAssets.Sprites.Props.DoorSheet.Boxes.Reach);
    /// reach.Layer = CollisionLayers.Triggers;
    /// reach.Detects = new(CollisionLayers.Actors);
    /// reach.ReportsContacts = true;
    /// reach.ContactEntered += OnApproached;
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
                "This renderer is attached to no entity. Attach it before binding a box, whose collider sits on that entity.");
        }

        List<BoxBinding> boxes = (_extras ??= new()).Boxes ??= [];

        foreach (BoxBinding bound in boxes)
        {
            if (bound.Name == name)
            {
                return bound.Collider;
            }
        }

        // A turned ancestry refuses the collider before the binding is kept, and nothing is left behind.
        BoxBinding binding = new(name);
        Read(binding);
        binding.Collider = new BoxCollider2D(Spans(binding) ? Extent(binding.Area) : Vector2.One) { Enabled = false };
        entity.Add(binding.Collider);

        boxes.Add(binding);
        Place(binding);

        return binding.Collider;
    }

    /// <inheritdoc/>
    protected internal override void CollectAssets(AssetCollection assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        if (DrawnFrame is { } drawn)
        {
            assets.Add(drawn.Texture);
        }

        if (_extras?.Look is { } look)
        {
            assets.Add(look.Variant.Textures);
        }
    }

    /// <inheritdoc/>
    protected internal override void Draw(FrameView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        // Every sprite in a frame passes here. Copying the nullable frame per draw, and the code that grows
        // this method past what the JIT inlines, cost the frame build about a quarter.
        if (_extras?.Look is { Drawn: null })
        {
            return;
        }

        view.Add(Intent(PreviousRenderTransform, RenderTransform), Tiling);
    }

    // The frame drawn: the held one, or what Variant maps it to, and null where that is nothing.
    private Sprite? DrawnFrame => _extras?.Look is { } look ? look.Drawn : _sprite;

    // Settled on every write to Sprite or Variant, and never by a draw.
    private void Resolve()
    {
        if (_extras?.Look is not { } look)
        {
            return;
        }

        look.Drawn = look.Variant.TryGet(_sprite, out Sprite? drawn) ? drawn : _sprite;
    }

    // A negative world scale axis mirrors the frame about the pivot as a flip does. It folds into the flip
    // flags, and the renderer draws the magnitude of the extent.
    private SpriteIntent Intent(in Transform2D previous, in Transform2D current)
    {
        Sprite frame = _extras?.Look is { } look ? look.Drawn.GetValueOrDefault() : _sprite;
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

    // Names are interned literals in a generated sheet. The comparison usually matches on reference
    // without reading a character.
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
        if (_extras?.Sockets is { } sockets)
        {
            foreach (SocketBinding binding in sockets)
            {
                if (frameChanged)
                {
                    Read(binding);
                }

                Place(binding);
            }
        }

        if (_extras?.Boxes is { } boxes)
        {
            foreach (BoxBinding binding in boxes)
            {
                if (frameChanged)
                {
                    Read(binding);
                }

                Place(binding);
            }
        }
    }

    // Bound boxes return to the world with the renderer. Their colliders never left the entity.
    internal override void OnAttachedTo(Entity entity)
    {
        base.OnAttachedTo(entity);
        if (_extras?.Boxes is not { } boxes)
        {
            return;
        }

        foreach (BoxBinding binding in boxes)
        {
            if (!ReferenceEquals(binding.Collider.Entity, entity))
            {
                throw new InvalidOperationException(
                    $"This renderer's box '{binding.Name}' sits on the entity it was bound on, not this one. Bind boxes on a new renderer for this entity.");
            }
        }

        foreach (BoxBinding binding in boxes)
        {
            Place(binding);
        }
    }

    // Bound boxes leave the world with the renderer. Sockets stay where they are.
    internal override void OnDetachingFrom(Entity entity)
    {
        base.OnDetachingFrom(entity);
        if (_extras?.Boxes is not { } boxes)
        {
            return;
        }

        foreach (BoxBinding binding in boxes)
        {
            binding.Collider.Enabled = false;
        }
    }

    // Writes the rect in the entity's own space, mirrored about the pivot as the frame is drawn, and
    // the collider scales it from the entity's origin. Only changed values are written.
    private void Place(BoxBinding binding)
    {
        BoxCollider2D collider = binding.Collider;
        if (Entity is null || !Spans(binding))
        {
            collider.Enabled = false;
            return;
        }

        Rect area = binding.Area;
        Vector2 size = Extent(area);
        Vector2 offset = _offset + FromPivot(new Vector2(area.Left, area.Top), new Vector2(area.Right, area.Bottom), binding.Pivot);

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

    // The entity tree composes the parent's scale and turn, which keeps a facing written as a negative
    // scale from folding in twice.
    private void Place(SocketBinding binding)
    {
        if (!binding.Placed)
        {
            return;
        }

        Vector2 local = _offset + FromPivot(binding.Point, binding.Point, binding.Pivot);

        if (local != binding.Child.Position)
        {
            binding.Child.Teleport(local);
        }
    }

    // The low corner of the span from `low` to `high`, measured from `pivot` and mirrored about it on a
    // flipped axis as the drawn frame is. A point is a span whose two corners coincide.
    private Vector2 FromPivot(Vector2 low, Vector2 high, Vector2 pivot) => new(
        _flipX ? pivot.X - high.X : low.X - pivot.X,
        _flipY ? pivot.Y - high.Y : low.Y - pivot.Y);

    /// <inheritdoc/>
    protected internal override void OnDebugPanel(DebugPanel panel)
    {
        base.OnDebugPanel(panel);
        TextureRegion region = Sprite.Region;
        panel.Field("Sprite", string.Create(CultureInfo.InvariantCulture, $"({region.X}, {region.Y}) {region.Width}x{region.Height}"));
        if (_extras?.Look is { } look)
        {
            panel.Field(
                "Variant draws",
                look.Drawn is { Region: var drawn } ? string.Create(CultureInfo.InvariantCulture, $"({drawn.X}, {drawn.Y}) {drawn.Width}x{drawn.Height}") : "nothing");
        }

        panel.Field("Offset", Offset);
        panel.Field("Tiling", Tiling);
        panel.Field("Color", Color);
        panel.Field("Blend", Blend.ToString());
        panel.Toggle("FlipX", FlipX, on => FlipX = on);
        panel.Toggle("FlipY", FlipY, on => FlipY = on);

        foreach (SocketBinding binding in _extras?.Sockets ?? [])
        {
            panel.Field(
                "Socket " + binding.Name,
                DebugPanel.Format(binding.Child.Position)
                    + (binding.Carried ? " on this frame" : binding.Placed ? " held from an earlier frame" : " on no frame yet"));
        }

        foreach (BoxBinding binding in _extras?.Boxes ?? [])
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

    // What a renderer holds only once it binds a socket or a box or draws through a variant.
    private sealed class Extras
    {
        internal List<SocketBinding>? Sockets;
        internal List<BoxBinding>? Boxes;
        internal VariantLook? Look;
    }

    // The variant set and what it resolves the held frame to, null where that is nothing.
    private sealed class VariantLook(SpriteVariant variant)
    {
        internal readonly SpriteVariant Variant = variant;
        internal Sprite? Drawn;
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
