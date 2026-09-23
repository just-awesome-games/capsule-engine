using System.Globalization;
using System.Numerics;
using Capsule.Diagnostics;
using Capsule.UI;

namespace Capsule.Scenes;

// The entity's transform: the local and previous local values, the cached world transforms, and the
// writes, saves and validation over them.
public partial class Entity
{
    private Transform2D _local = Transform2D.Identity;
    private Transform2D _previousLocal = Transform2D.Identity;

    // The composed world transform. A write above marks it stale, and the next read recomposes it from
    // the nearest valid ancestor down. The previous world is kept current instead, because few writes
    // change it. Invariant: a stale entity's descendants are also stale.
    private Transform2D _world;
    private Transform2D _previousWorld;
    private bool _worldStale = true;

    /// <summary>
    /// The entity's place, turn and size, local to the <see cref="Parent"/> when it has one. The
    /// position is in the entity's own units: world units normally, canvas pixels from the anchor
    /// on a <see cref="ScreenEntity"/>, and the parent's space under a parent.
    /// </summary>
    /// <remarks>
    /// The rotation is in radians and clockwise positive. The scale is per axis, and zero and
    /// negative axes are allowed. The turn and the scale reach presentation only. Anything that
    /// collides follows world position.
    /// <para>
    /// All three values land in one write. The write re-places every collider beneath, and all
    /// three roll back together if a collider rejects the position. A turn or a scale written
    /// outside a step becomes both ends of the next frame's interpolation and shows immediately.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">A collider on this entity or a descendant cannot be placed there. Nothing changes.</exception>
    /// <exception cref="InvalidOperationException">
    /// The entity is anchored to the world origin, or the rotation is non-zero over a subtree holding a
    /// collider, body, notifier or a renderer that cannot turn, or the scale is not one over a subtree
    /// holding a collider, body or notifier.
    /// </exception>
    public Transform2D Transform
    {
        get => _local;
        set => Store(value.Position, value.Rotation, value.Scale);
    }

    /// <summary><see cref="Transform"/>'s position. Writes follow the same rules as <see cref="Transform"/>.</summary>
    public Vector2 Position
    {
        get => _local.Position;
        set => Store(value, _local.Rotation, _local.Scale);
    }

    /// <summary><see cref="Transform"/>'s rotation. Writes follow the same rules as <see cref="Transform"/>.</summary>
    public float Rotation
    {
        get => _local.Rotation;
        set => Store(_local.Position, value, _local.Scale);
    }

    /// <summary><see cref="Transform"/>'s scale. Writes follow the same rules as <see cref="Transform"/>.</summary>
    public Vector2 Scale
    {
        get => _local.Scale;
        set => Store(_local.Position, _local.Rotation, value);
    }

    // Transform as of the previous step, saved at the top of every step. Renderers interpolate from it by
    // the frame alpha. Teleport and joining a scene collapse it onto the current value. A turn or scale
    // written outside a step collapses its rotation and scale and keeps its position.
    internal Transform2D PreviousTransform => _previousLocal;

    /// <summary>
    /// <see cref="Transform"/> composed with every ancestor's, giving where the entity sits in the world,
    /// or on the canvas under a <see cref="ScreenEntity"/> root.
    /// </summary>
    public Transform2D WorldTransform => World;

    /// <summary>
    /// <see cref="WorldTransform"/>'s position. Writing it sets the <see cref="Position"/> that lands
    /// there under the current parent, following the rules <see cref="Transform"/> describes.
    /// </summary>
    public Vector2 WorldPosition
    {
        get => World.Position;
        set => Position = _parent is { } parent ? parent.World.InverseTransformPoint(value) : value;
    }

    // The composed world transform, recomposed here from the nearest valid ancestor down.
    internal ref readonly Transform2D World
    {
        get
        {
            if (_worldStale)
            {
                _world = _parent is { } parent ? parent.World.Compose(_local) : _local;
                _worldStale = false;
            }

            return ref _world;
        }
    }

    // The world transform as of the previous step. The step copies it, and it is recomposed from the
    // previous locals wherever one was overwritten outside a step.
    internal ref readonly Transform2D PreviousWorld => ref _previousWorld;

    /// <summary>
    /// Moves the entity to <paramref name="position"/>, local to its <see cref="Parent"/>, with no
    /// interpolation from the old position.
    /// </summary>
    /// <remarks>
    /// The write follows the rules <see cref="Transform"/> describes. The frame after it draws the entity
    /// at the new position with no motion between.
    /// </remarks>
    public void Teleport(Vector2 position)
    {
        Position = position;
        _previousLocal = _local;
        Invalidate(previous: true);
    }
    // Called at the top of a step, parent before child as the scene walks. It saves the locals and
    // copies the world transform they compose as the previous world.
    internal void SavePrevious()
    {
        _previousLocal = _local;
        _previousWorld = World;
    }

    // Throws if `carrier` turning by `rotation` would turn a component in this subtree that cannot turn.
    // A rotation of zero needs no check.
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

    // Returns the first component in this subtree that does not support `needed`, along with the entity
    // holding it, or null when every component supports it.
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

    // The single write path for the local values, shared by every transform property. A collider beneath
    // may reject the position, and then every value is restored, including the previous ones.
    private void Store(Vector2 position, float rotation, Vector2 scale)
    {
        if (Anchored)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is anchored at the world origin. Move its cells instead of the entity.");
        }

        RequireFinite(position, rotation, scale);

        Transform2D held = _local;
        Transform2D heldPrevious = _previousLocal;
        bool turned = rotation != held.Rotation;
        bool scaled = scale != held.Scale;

        // Only the first turn or first scale walks the subtree. Add rejects a component that cannot turn
        // or scale under a turned or scaled ancestor, and a parent write checks the new subtree against
        // every ancestor, and an entity that is already turned or scaled holds no component that objects.
        if (turned && held.Rotation == 0f)
        {
            RequireTurnable(this, rotation);
        }

        if (scaled && held.Scale == Vector2.One)
        {
            RequireScalable(this, scale);
        }

        // Only a changed rotation recomputes its sine and cosine. A move or a resize reuses them.
        _local = turned ? new Transform2D(position, rotation, scale) : held.With(position, scale);

        // A turn or a scale written outside a step has no step top to be saved at, so make it both ends
        // of the next frame's interpolation.
        bool saved = (turned || scaled) && SceneOrNull?.SteppingTick is null;
        if (saved)
        {
            _previousLocal = _local.With(heldPrevious.Position, scale);
        }

        Invalidate(saved);

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

    // Marks this subtree's world transform stale, stopping at any entity already stale, because the
    // invariant makes everything beneath it stale too. With `previous`, the walk visits every descendant
    // and rebuilds what a parent change or a previous-value write left wrong: the root pointer and the
    // previous world, parent first. It also stales the composed tint and visibility, which a parent
    // change moves.
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
            _appearanceStale = true;
            _previousWorld = _parent is { } parent ? parent._previousWorld.Compose(_previousLocal) : _previousLocal;
        }

        foreach (Entity child in Children)
        {
            child.Invalidate(previous);
        }
    }

    // A NaN or infinite value would spread to everything this entity places, so check all three before
    // writing any of them.
    private static void RequireFinite(Vector2 position, float rotation, Vector2 scale)
    {
        Guard.Finite(position, nameof(position));
        Guard.Finite(rotation, nameof(rotation));
        Guard.Finite(scale, nameof(scale));
    }

    private static InvalidOperationException Turned(Component component, Entity holder, Entity carrier, float rotation) =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"A {component.GetType().Name} on a {holder.GetType().Name} cannot be turned, and {carrier.GetType().Name} carries a rotation of {rotation} that every entity under it inherits. Clear that rotation or move the component out of the subtree."));

    private static InvalidOperationException Scaled(Component component, Entity holder, Entity carrier, Vector2 scale) =>
        new($"A {component.GetType().Name} on a {holder.GetType().Name} cannot be scaled, and {carrier.GetType().Name} carries a scale of {DebugPanel.Format(scale)} that every entity under it inherits. Reset that scale to one or move the component out of the subtree.");

    // Notifies every component on this entity, then recurses into each branch that holds a collider.
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
