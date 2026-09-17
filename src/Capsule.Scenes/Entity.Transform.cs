using System.Globalization;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.UI;

namespace Capsule.Scenes;

// The entity's transform: the local and previous local values, the cached world transforms, and
// every write, retention and refusal over them.
public partial class Entity
{
    private Transform2D _local = Transform2D.Identity;
    private Transform2D _previousLocal = Transform2D.Identity;

    // The composed world, recomposed on read from the nearest valid ancestor down once a write
    // above marks it stale; the previous world is kept current instead, since what changes it is
    // rare. Invariant: a stale entity's descendants are stale.
    private Transform2D _world;
    private Transform2D _previousWorld;
    private bool _worldStale = true;

    /// <summary>
    /// The entity's place, turn and size, local to the <see cref="Parent"/> where there is one:
    /// <see cref="Transform2D.Position"/> in the entity's own units — world units, canvas pixels
    /// from the anchor on a <see cref="ScreenEntity"/>, the parent's space under a parent;
    /// <see cref="Transform2D.Rotation"/> in radians, clockwise positive, zero by default;
    /// <see cref="Transform2D.Scale"/> per axis, one by default, zero and negative axes allowed.
    /// <see cref="WorldTransform"/> is the parent's world transform placing this one, on
    /// <see cref="Transform2D.Then(Transform2D)"/>'s terms, and equal to it on a root. Only
    /// presentation honours the turn and the scale: every renderer beneath is placed, turned and
    /// sized by the world transform, and anything that collides follows world position alone.
    /// <para>
    /// Written, all three values are set as one write — validated once, every collider beneath
    /// re-placed, and rolled back together if one refuses the position — and the world transform
    /// of every entity beneath is recomposed on its next read; a read after no write in the
    /// ancestry costs a flag test. A turn or a scale written outside a step is both ends of the
    /// next frame's interpolation, so it shows at once.
    /// </para>
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position, rotation or scale is not finite.</exception>
    /// <exception cref="ArgumentException">A collider on this entity or a descendant cannot be placed there; nothing changes.</exception>
    /// <exception cref="InvalidOperationException">
    /// The entity is anchored to the world origin; the rotation is not zero over a subtree
    /// holding a collider, body, notifier or a renderer whose intent cannot turn; or the scale is
    /// not one over a subtree holding a collider, body or notifier. The message names the entity
    /// carrying the value.
    /// </exception>
    public Transform2D Transform
    {
        get => _local;
        set => Store(value.Position, value.Rotation, value.Scale);
    }

    /// <summary><see cref="Transform"/>'s position, written on its terms.</summary>
    public Vector2 Position
    {
        get => _local.Position;
        set => Store(value, _local.Rotation, _local.Scale);
    }

    /// <summary><see cref="Transform"/>'s rotation, written on its terms.</summary>
    public float Rotation
    {
        get => _local.Rotation;
        set => Store(_local.Position, value, _local.Scale);
    }

    /// <summary><see cref="Transform"/>'s scale, written on its terms.</summary>
    public Vector2 Scale
    {
        get => _local.Scale;
        set => Store(_local.Position, _local.Rotation, value);
    }

    /// <summary>
    /// <see cref="Transform"/> as of the previous step. Engine-managed: the scene retains it, and
    /// the world transform it composes, at the top of every step, and every renderer interpolates
    /// from it by the frame alpha; <see cref="Teleport"/> and a turn or scale written outside a
    /// step collapse it onto the current value.
    /// </summary>
    public Transform2D PreviousTransform => _previousLocal;

    /// <summary>
    /// <see cref="Transform"/> placed by every ancestor's: where the entity is in the world, or on
    /// the canvas under a <see cref="ScreenEntity"/> root. Cached on <see cref="Transform"/>'s terms.
    /// </summary>
    public Transform2D WorldTransform => World;

    /// <summary>
    /// <see cref="WorldTransform"/>'s position. Written, it sets the <see cref="Position"/> that
    /// lands there under the current parent, on <see cref="Transform"/>'s terms.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite, or no finite local reaches it because an ancestor's scale is zero on an axis.</exception>
    /// <exception cref="ArgumentException">A collider on this entity or a descendant cannot be placed there; nothing moves.</exception>
    /// <exception cref="InvalidOperationException">The entity is anchored to the world origin.</exception>
    public Vector2 WorldPosition
    {
        get => World.Position;
        set => Position = _parent is { } parent ? parent.World.Unapply(value) : value;
    }

    // The composed world, recomposed here from the nearest valid ancestor down.
    internal ref readonly Transform2D World
    {
        get
        {
            if (_worldStale)
            {
                _world = _parent is { } parent ? parent.World.Then(_local) : _local;
                _worldStale = false;
            }

            return ref _world;
        }
    }

    // The world transform as of the previous step: the world copied as the step retains it, and
    // recomposed from the previous locals wherever one was overwritten outside a step.
    internal ref readonly Transform2D PreviousWorld => ref _previousWorld;

    /// <summary>
    /// Moves immediately, with no interpolation from the old position: collapses
    /// <see cref="PreviousTransform"/> onto the current transform once the position is written.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The position is not finite.</exception>
    /// <exception cref="ArgumentException">A tracked collider cannot be placed there; nothing moves.</exception>
    /// <exception cref="InvalidOperationException">The entity is anchored to the world origin.</exception>
    public void Teleport(Vector2 position)
    {
        Position = position;
        _previousLocal = _local;
        Invalidate(previous: true);
    }
    // The top of a step, parent before child as the scene walks: the locals are retained, and the
    // world they compose is copied as the previous world.
    internal void Retain()
    {
        _previousLocal = _local;
        _previousWorld = World;
    }

    // Refuses a turn of `rotation` on `carrier` over this subtree, where any component beneath
    // cannot turn. Nothing to check for an unturned value.
    private void RequireTurnable(Entity carrier, float rotation)
    {
        if (rotation != 0f && FirstRefuser(TransformSupport.Rotation) is var (component, holder))
        {
            throw Turned(component, holder, carrier, rotation);
        }
    }

    private void RequireScalable(Entity carrier, Vector2 scale)
    {
        if (scale != Vector2.One && FirstRefuser(TransformSupport.Scale) is var (component, holder))
        {
            throw Scaled(component, holder, carrier, scale);
        }
    }

    // The first component in this subtree whose support lacks `needed`, with the entity holding
    // it; null where none does.
    private (Component Component, Entity Holder)? FirstRefuser(TransformSupport needed)
    {
        foreach (Component component in Components)
        {
            if ((component.Supports & needed) == 0)
            {
                return (component, this);
            }
        }

        foreach (Entity child in Children)
        {
            if (child.FirstRefuser(needed) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    // The one write of the locals, on every property's terms. Colliders beneath may refuse the
    // position, in which case every value — and every previous value overwritten outside a step —
    // is returned to what it was.
    private void Store(Vector2 position, float rotation, Vector2 scale)
    {
        if (Anchored)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is anchored at the world origin, its cells world coordinates, and cannot be moved, turned or scaled.");
        }

        RequireFinite(position, rotation, scale);

        Transform2D held = _local;
        Transform2D heldPrevious = _previousLocal;
        bool turned = rotation != held.Rotation;
        bool scaled = scale != held.Scale;

        if (turned)
        {
            RequireTurnable(this, rotation);
        }

        if (scaled)
        {
            RequireScalable(this, scale);
        }

        // Only a changed turn evaluates its sine and cosine; a move or a resize keeps them.
        _local = turned ? new Transform2D(position, rotation, scale) : held.With(position, scale);

        // Written outside a step, a turn or a scale has no step to be retained at the top of, so a
        // changed one is both ends of the next frame's interpolation.
        bool retained = (turned || scaled) && Scene?.SteppingTick is null;
        if (retained)
        {
            _previousLocal = _local.With(heldPrevious.Position, scale);
        }

        Invalidate(retained);

        if (_movementTrackers == 0)
        {
            return;
        }

        try
        {
            NotifyMoved();
        }
        catch
        {
            _local = held;
            _previousLocal = heldPrevious;
            Invalidate(previous: true);
            NotifyMoved();
            throw;
        }
    }

    // Marks this subtree's world stale, stopping where a subtree already is: by the invariant,
    // everything beneath it is too. With `previous`, the walk stops nowhere and re-derives what a
    // parent change or a previous-value write leaves wrong beneath — the root pointer and the
    // previous world, parent first.
    private void Invalidate(bool previous)
    {
        if (_worldStale && !previous)
        {
            return;
        }

        _worldStale = true;

        if (previous)
        {
            _root = _parent?._root ?? this;
            _previousWorld = _parent is { } parent ? parent._previousWorld.Then(_previousLocal) : _previousLocal;
        }

        foreach (Entity child in Children)
        {
            child.Invalidate(previous);
        }
    }

    private static void RequireFinite(Vector2 position, float rotation, Vector2 scale)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "An entity's position must be finite; a NaN or infinite one spreads to everything that reads it.");
        }

        if (!float.IsFinite(rotation))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "An entity's rotation must be finite; a NaN or infinite one spreads to everything placed by it.");
        }

        if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "An entity's scale must be finite on both axes; a NaN or infinite one spreads to everything placed by it.");
        }
    }

    private static InvalidOperationException Turned(Component component, Entity holder, Entity carrier, float rotation) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"A {component.GetType().Name} on a {holder.GetType().Name} cannot be turned: {carrier.GetType().Name} carries a rotation of {rotation}, which every entity under it inherits."));

    private static InvalidOperationException Scaled(Component component, Entity holder, Entity carrier, Vector2 scale) =>
        new($"A {component.GetType().Name} on a {holder.GetType().Name} cannot be scaled: {carrier.GetType().Name} carries a scale of {DebugPanel.Format(scale)}, which every entity under it inherits.");

    // Every collider on this entity, then every branch below holding one.
    private void NotifyMoved()
    {
        foreach (Component component in LiveComponents)
        {
            component.OnEntityMoved();
        }

        foreach (Entity child in Children)
        {
            if (child._movementTrackers > 0)
            {
                child.NotifyMoved();
            }
        }
    }
}
