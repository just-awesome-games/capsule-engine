using System.Numerics;
using Capsule.Scenes;

namespace Capsule.Physics;

// Riding and pushing. A collider carries the bodies that ride it and shoves the bodies its move drives
// into.
public abstract partial class Collider2D
{
    // A collider no body is moved by only follows its entity. One that is carries its riders by the
    // same translation and then shoves the bodies it moved into.
    internal override void OnEntityMoved()
    {
        FollowScale();

        if (_world is not { } world)
        {
            return;
        }

        Vector2 position = Entity!.WorldPosition;
        bool moves = _riders.Count > 0 || (_scene!.Physics.MovedByLayers & (1UL << _layerIndex)) != 0;

        // A move made from inside this collider's own carry, by a Crushed handler, only follows the
        // entity. Carrying again would reuse the buffer the outer carry is reading.
        if (!moves || _pushing)
        {
            world.SetPosition(_handle, position);
            return;
        }

        Vector2 from = world.PositionOf(_handle);
        Vector2 motion = position - from;
        world.SetPosition(_handle, position);
        if (motion == Vector2.Zero)
        {
            return;
        }

        _pushing = true;
        try
        {
            CarryRiders(motion);

            // A handler raised during the carry may have disabled or detached this collider.
            if (_world is not null)
            {
                ShoveBodies(world, from, motion);
            }
        }
        finally
        {
            _pushing = false;
            _riders.Compact();
        }
    }

    internal void AddRider(KinematicBody2D body) => _riders.Add(body);

    internal void RemoveRider(KinematicBody2D body) => _riders.Remove(body, _pushing);

    // A carried rider's colliders move in turn, which carries whatever rides them. The count is read
    // once. A body that lands here mid-carry waits for the next move.
    private void CarryRiders(Vector2 motion)
    {
        int count = _riders.Count;
        for (int index = 0; index < count && _world is not null; index++)
        {
            if (_riders[index] is not { } rider || !rider.IsMovedBy(_layerIndex) || MovesWith(rider.Entity))
            {
                continue;
            }

            rider.Carry(motion);
        }
    }

    // Shoves each body this collider's move drove into by the travel left after meeting it, in slot
    // order. A rider whose carry took the whole motion is passed by, and one a carry could not clear is
    // checked. The move is swept from where the collider was. A body thinner than the move is still met.
    private void ShoveBodies(CollisionWorld2D world, Vector2 from, Vector2 motion)
    {
        if ((_scene!.Physics.MovedByLayers & (1UL << _layerIndex)) == 0)
        {
            return;
        }

        // A body resting on its center stands below the pose it sweeps. The reach extends down to it.
        Aabb2D path = _local.Translated(from).Bounds.Union(world.WorldShapeOf(_handle).Bounds)
            .Expanded(CollisionTolerance.ContactSkin);
        Aabb2D reach = new(path.Min, path.Max + new Vector2(0f, _scene.Physics.DeepestSink));

        // The bodies gathered sit in the scene's shared buffer from start on. A shove a handler raises
        // gathers past them, and each shove gives its entries back as it ends.
        ScenePhysics physics = _scene.Physics;
        int start = physics.ShovedUsed;
        try
        {
            int changes = physics.BodyChanges;
            int layer = _layerIndex;
            int count = GatherNear(world, physics, start, reach, physics.LayersMovedBy(layer));

            // The slot of the last body judged. A gather made again passes over the ones up to it.
            int judged = -1;
            for (int index = 0; index < count && _world is not null; index++)
            {
                ColliderHandle near = physics.Shoved[start + index];
                if (near.Index <= judged)
                {
                    continue;
                }

                judged = near.Index;
                if (!world.Contains(near)
                    || world.UserDataOf(near) is not Collider2D { Body: { } body }
                    || !body.IsMovedBy(_layerIndex)
                    || MovesWith(body.Entity)
                    || body.CarriedWholeBy(this)
                    || !world.SweepPair(
                        _handle,
                        from,
                        motion,
                        near,
                        body.SweptOffset(),
                        out float fraction,
                        out Vector2 normal,
                        out Vector2 point))
                {
                    continue;
                }

                // The sweep reports the body's normal. The pusher's surface faces the other way.
                body.Shove(this, motion * (1f - fraction), -normal, point);

                // A handler the shove raised may have changed which bodies this collider moves, or the
                // layer it is on. The bodies past this one are gathered again as they now stand.
                if (_scene is { } scene && (scene.Physics.BodyChanges != changes || _layerIndex != layer))
                {
                    changes = scene.Physics.BodyChanges;
                    layer = _layerIndex;
                    count = GatherNear(world, physics, start, reach, scene.Physics.LayersMovedBy(layer));
                    index = -1;
                }
            }
        }
        finally
        {
            physics.ShovedUsed = start;
        }
    }

    // Fills the scene's shove buffer from `start` with the colliders on `layers` whose boxes meet
    // `reach`, in slot order, claims those entries and returns how many there are.
    private int GatherNear(CollisionWorld2D world, ScenePhysics physics, int start, in Aabb2D reach, ulong layers)
    {
        ref ColliderHandle[] near = ref physics.Shoved;
        int count = world.CollidersNear(reach, layers, _handle, near.AsSpan(start));
        if (start + count > near.Length)
        {
            Grow(ref near, start + count);
            count = world.CollidersNear(reach, layers, _handle, near.AsSpan(start));
        }

        physics.ShovedUsed = start + count;
        return count;
    }

    // Whether an entity is this collider's own or one of its ancestors, which move with it already.
    internal bool MovesWith(Entity? other)
    {
        for (Entity? entity = Entity; entity is not null; entity = entity.Parent)
        {
            if (ReferenceEquals(entity, other))
            {
                return true;
            }
        }

        return false;
    }

    // Lets go of every rider as this collider leaves the world.
    private void ReleaseRiders()
    {
        for (int index = 0; index < _riders.Count; index++)
        {
            _riders.Take(index)?.ForgetFloor(this);
        }

        _riders.Clear();
    }
}
