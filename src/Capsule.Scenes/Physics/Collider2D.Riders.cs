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

    // A surface with a velocity is a floor that moves zero and carries its riders by one step of it.
    internal override void OnEngineStep(in StepContext context)
    {
        if (_surfaceVelocity == Vector2.Zero || _riders.Count == 0 || _pushing)
        {
            return;
        }

        _pushing = true;
        try
        {
            CarryRiders(_surfaceVelocity * context.DeltaSeconds, context.Tick);
        }
        finally
        {
            _pushing = false;
            _riders.Compact();
        }
    }

    // A carried rider's colliders move in turn, which carries whatever rides them. The count is read
    // once. A body that lands here mid-carry waits for the next move. A surface carry passes its step's
    // tick.
    private void CarryRiders(Vector2 motion, long? surfaceTick = null)
    {
        int count = _riders.Count;
        for (int index = 0; index < count && _world is not null; index++)
        {
            if (_riders[index] is not { } rider || !rider.IsMovedBy(_layerIndex) || MovesWith(rider.Entity))
            {
                continue;
            }

            if (surfaceTick is { } tick)
            {
                rider.CarryOnSurface(motion, tick);
            }
            else
            {
                rider.Carry(motion);
            }
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

        _near ??= new ColliderHandle[8];
        int count = world.CollidersNear(reach, _scene.Physics.BodyLayers, _handle, _near);
        if (count > _near.Length)
        {
            Grow(ref _near, count);
            count = world.CollidersNear(reach, _scene.Physics.BodyLayers, _handle, _near);
        }

        for (int index = 0; index < count && _world is not null; index++)
        {
            ColliderHandle near = _near[index];
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
        }
    }

    // Whether an entity is this collider's own or one of its ancestors, which move with it already.
    private bool MovesWith(Entity? other)
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
