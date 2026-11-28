using System.Numerics;
using System.Runtime.InteropServices;
using Capsule.Rendering;
using Capsule.UI;

namespace Capsule.Scenes;

// Parenting: linking an entity under another, and every check a parent write makes before the link.
public partial class Entity
{
    // Clears the parent, skipping the checks a public parent write makes.
    internal void Unparent()
    {
        Unlink();
        _parent = null;
        Invalidate(previous: true);
    }

    private void Link(Entity? parent)
    {
        Unlink();
        _parent = parent;

        if (parent is not null)
        {
            // Children that come and go unread would otherwise grow the parent's list by a slot each time.
            if (parent._firstChildHole >= 0)
            {
                parent.CompactChildren();
            }

            List<Entity> siblings = parent._children ??= [];
            _childSlot = siblings.Count;
            siblings.Add(this);
            parent.TrackMovement(_movementTrackers);

            if (OnScreen)
            {
                ScreenEntity.Reflow(parent);
            }
        }
    }

    // Every refusal comes before the first link.
    private void Reparent(Scene scene, Entity? parent)
    {
        scene.Drawing.ThrowIfDrawing("reparented an entity");

        // A removal asked of an ancestor refuses only once it lands. Until then the write takes this entity out of it.
        if (scene.IsRemovalRequested(this))
        {
            throw new InvalidOperationException(
                $"This {GetType().Name} cannot be reparented after Scene.Remove was called on it. Reparent it before requesting its removal, or let the removal take it.");
        }

        if (scene.IsDetaching(this))
        {
            throw new InvalidOperationException(
                $"The removal that takes this {GetType().Name} is already landing and would detach it. Reparent it before the step's removals land, or let the removal take it.");
        }

        if (parent is not null)
        {
            if (!ReferenceEquals(parent.SceneOrNull, scene))
            {
                throw new InvalidOperationException(parent.SceneOrNull is null
                    ? $"A {GetType().Name} is in a scene and cannot be placed by a {parent.GetType().Name} that is in none. Parent it once the {parent.GetType().Name} has joined this scene, or set the parent to null."
                    : $"A {GetType().Name} is in one scene and cannot be placed by a {parent.GetType().Name} in another. Parent it under an entity of its own scene, or set the parent to null.");
            }

            if (scene.IsRemovalPending(parent) || scene.IsDetaching(parent))
            {
                throw new InvalidOperationException(
                    $"A {parent.GetType().Name} is being removed and would take a {GetType().Name} placed by it along. Parent it under an entity that stays, or set the parent to null.");
            }

            RequireParentable(parent);
        }

        Transform2D local = _local;
        Transform2D previousLocal = _previousLocal;
        bool held = SceneOrNull is not null;
        if (held)
        {
            local = Beneath(parent, previous: false, World);
            previousLocal = Beneath(parent, previous: true, _previousWorld);
            if (!IsFinite(local) || !IsFinite(previousLocal))
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} cannot keep its world transform under that parent: a world scale has a zero axis, or a value is too large. Give the parent a scale on both axes, reduce the scales or positions in its ancestry, or set the parent to null.");
            }
        }

        Link(parent);
        _local = local;
        _previousLocal = previousLocal;
        Invalidate(previous: true);

        if (held)
        {
            scene.Reparented(this);
        }
    }

    // The local transform that composes under `parent`, or under none, into `world`.
    private static Transform2D Beneath(Entity? parent, bool previous, in Transform2D world) =>
        parent is null ? world : Within(previous ? parent._previousWorld : parent.World, world);

    private static bool IsFinite(in Transform2D transform) =>
        float.IsFinite(transform.Position.X) && float.IsFinite(transform.Position.Y)
        && float.IsFinite(transform.Rotation) && float.IsFinite(transform.Scale.X) && float.IsFinite(transform.Scale.Y);

    // Checks every reason a parent write can fail, before any link is made.
    private void RequireParentable(Entity parent)
    {
        if (Anchored)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is anchored at the world origin and cannot be placed by a parent.");
        }

        if (OwnSpace == RenderSpace.Screen && parent is not ScreenEntity)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} is on the screen layer and cannot be placed by the plain {parent.GetType().Name}. Make the parent a ScreenEntity, or parent a plain entity under this one instead.");
        }

        if (_scrollFactor != Vector2.One)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} carrying a scroll factor cannot be placed by a parent. Set the scroll factor on the root instead.");
        }

        if (SortedWithWorld)
        {
            throw new InvalidOperationException(
                $"A {GetType().Name} that sorts with the world cannot be placed by a parent. Set SortsWithWorld on the root instead.");
        }

        if (parent.ScrollFactor != Vector2.One)
        {
            RequireScrollable();
        }

        for (Entity? above = parent; above is not null; above = above._parent)
        {
            if (ReferenceEquals(above, this))
            {
                throw new InvalidOperationException(
                    $"A {GetType().Name} cannot be placed by itself or by one of its own descendants.");
            }

            RequireTurnable(above, above._local.Rotation);
            RequireScalable(above, above._local.Scale);
        }
    }

    // Throws if anything in this subtree forbids a scroll factor other than one.
    private void RequireScrollable()
    {
        if (FirstRefuser(TransformSupport.Scroll) is var (component, _))
        {
            throw Unscrollable(component);
        }
    }

    // Removes this entity from its parent's child list. Does not clear _parent.
    private void Unlink()
    {
        if (_parent is { } parent)
        {
            parent._children![_childSlot] = null!;
            parent._firstChildHole = parent._firstChildHole < 0 ? _childSlot : Math.Min(parent._firstChildHole, _childSlot);
            parent.TrackMovement(-_movementTrackers);

            if (OnScreen)
            {
                ScreenEntity.Reflow(parent);
            }
        }
    }

    // Closes the holes unlinks left, keeping parenting order, and renumbers what moved.
    private void CompactChildren()
    {
        Span<Entity> children = CollectionsMarshal.AsSpan(_children);
        int kept = _firstChildHole;
        for (int index = kept + 1; index < children.Length; index++)
        {
            Entity child = children[index];
            if (child is not null)
            {
                child._childSlot = kept;
                children[kept++] = child;
            }
        }

        _children!.RemoveRange(kept, _children.Count - kept);
        _firstChildHole = -1;
    }

    private InvalidOperationException Unscrollable(Component component) =>
        new($"A {component.GetType().Name} answers at its authored position while a scroll factor draws this {GetType().Name} elsewhere. Remove the {component.GetType().Name}, or set the scroll factor to one.");
}
