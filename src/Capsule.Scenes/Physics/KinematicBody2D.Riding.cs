using System.Numerics;
using Capsule.Scenes;
using Capsule.Tiles;

namespace Capsule.Physics;

// Riding, carrying, shoving and crushing. A body rides the floor its last move stopped on and is moved
// by the colliders on its MovedBy layers.
public sealed partial class KinematicBody2D
{
    // Sweeps a riding body along its floor's motion with its own blocking filter. A body writing its
    // own position is never moved again. A rider is in a scene with its collider registered, because
    // every path that breaks either ends the ride first.
    internal void Carry(Vector2 motion)
    {
        _carriedWhole = false;
        if (_moving)
        {
            return;
        }

        RecordCarry(motion);

        CollisionWorld2D world = _collider.World!;
        Entity entity = Entity!;
        MoveResult2D result = world.Move(
            world.ShapeOf(_collider.Handle),
            Swept(entity),
            motion,
            Filter,
            default,
            _collider.Handle);

        Displace(entity, result.Translation);
        _carriedWhole = result.Translation == motion;
    }

    // Whether this body rides `floor` and its carry took the floor's whole motion. The floor's path then
    // cannot reach the body.
    internal bool CarriedWholeBy(Collider2D floor) => _carriedWhole && ReferenceEquals(_floor, floor);

    // Carries the body one step of its cell's surface velocity. A cell that now holds another tile ends
    // the ride.
    internal void CarryOnCell(in StepContext context)
    {
        TileContact2D cell = _floorCell!.Value;
        if (!ReferenceEquals(cell.Type, _floorTile))
        {
            Unride();
            return;
        }

        CarryOnSurface(cell.Map.SurfaceVelocityAt(cell.X, cell.Y) * context.DeltaSeconds, context.Tick);
    }

    // Carries the body by its floor's surface motion, swept as its own move is, and finds its floor again.
    // Only the first surface to carry the body in a step moves it.
    internal void CarryOnSurface(Vector2 motion, long tick)
    {
        if (_surfaceTick == tick)
        {
            return;
        }

        _surfaceTick = tick;
        RecordCarry(motion);
        CollisionWorld2D world = _collider.World!;
        Entity entity = Entity!;
        Travel(world, entity, motion, Filter, false, out _);
        Refind(world, entity);
    }

    // Adds a carry to the step's sum. A new step keeps the last sum as the prior one when the two steps are
    // consecutive.
    private void RecordCarry(Vector2 motion)
    {
        int step = _scene!.StepsBegun;
        if (step != _carriedStep)
        {
            _priorMotion = step == unchecked(_carriedStep + 1) ? _carriedMotion : Vector2.Zero;
            _carriedStep = step;
            _carriedMotion = Vector2.Zero;
        }

        _carriedMotion += motion;
    }

    // Finds the floor under the body after a carry, by a probe down the contact skin, and rides it when it
    // carries. The floor's normal steers the next walk. A body standing on no carrying floor leaves its ride.
    private void Refind(CollisionWorld2D world, Entity entity)
    {
        MoveSweep probe = new(world, world.ShapeOf(_collider.Handle), Swept(entity), Filter, _collider.Handle, false, _found, _stopped);
        probe.Pass(-Up * CollisionTolerance.ContactSkin);
        bool floored = false;
        for (int index = 0; index < probe.Written; index++)
        {
            if (!_stopped[index] || !IsFloor(_found[index].Normal))
            {
                continue;
            }

            if (!floored)
            {
                floored = true;
                _walkNormal = _found[index].Normal;
            }

            ColliderContact2D contact = Collider2D.Describe(world, _found[index]);
            if (MovedByFilter.Admits(contact.Layer) && Carries(contact))
            {
                Ride(contact.OtherCollider, contact.Tile);
                return;
            }
        }

        Unride();
    }

    // Shoves the body by what is left of the pusher's move after meeting it, or leaves a rider where
    // its carry put it. A shove that falls short by more than the mover's slop raises Crushed with the
    // pusher's surface where the two met, and the shortfall along its normal as the depth.
    // A collider moved to another entity still names this body, which then has nothing to shove.
    internal void Shove(Collider2D pusher, Vector2 remainder, Vector2 normal, Vector2 point)
    {
        if (_moving || Entity is not { } entity || !ReferenceEquals(_collider.Entity, entity))
        {
            return;
        }

        Vector2 applied = Vector2.Zero;
        if (!ReferenceEquals(_floor, pusher))
        {
            CollisionWorld2D sweeping = _collider.World!;
            MoveResult2D result = sweeping.MovePast(
                sweeping.ShapeOf(_collider.Handle),
                Swept(entity),
                remainder,
                Filter,
                _collider.Handle,
                pusher.Handle);

            applied = result.Translation;
            Displace(entity, applied);
        }

        float shortfall = Vector2.Dot(remainder - applied, normal);
        if (shortfall > CollisionTolerance.LinearSlop && pusher.World is { } world)
        {
            Contact2D contact = new(
                CollisionTarget.ForCollider(pusher.Handle, world.LayerOf(pusher.Handle)),
                point,
                normal,
                shortfall);
            Crushed?.Invoke(Collider2D.Describe(world, contact));
        }
    }

    // How far the pose this body sweeps stands from its registered collider. A pusher meets that pose.
    internal Vector2 SweptOffset() =>
        _sink != 0f && Entity is { } entity ? Swept(entity) - entity.WorldPosition : Vector2.Zero;

    // Called by a collider this body rides as it leaves the world. The collider has already let go.
    internal void ForgetFloor(Collider2D floor)
    {
        if (ReferenceEquals(_floor, floor))
        {
            _floor = null;
        }
    }

    // Called by a map collider this body rides as it leaves the world. The collider has already let go.
    internal void ForgetCell(TileMapCollider2D grid)
    {
        if (ReferenceEquals(_floorGrid, grid))
        {
            _floorGrid = null;
            _floorCell = null;
            _floorTile = null;
        }
    }

    // Replaces the effective moved-by layers, keeping the scene's union current. A changed set drops
    // the riding link, which the next Move finds again.
    private void SetMovedBy(CollisionFilter effective)
    {
        if (effective == MovedByFilter)
        {
            return;
        }

        _scene!.Physics.CountMovedBy(MovedByFilter, -1);
        MovedByFilter = effective;
        _scene.Physics.CountMovedBy(effective, 1);
        Unride();
    }

    private void Ride(Collider2D? floor, TileContact2D? cell)
    {
        if (!ReferenceEquals(_floor, floor))
        {
            _carriedMotion = Vector2.Zero;
            _priorMotion = Vector2.Zero;
            LeaveFloor();
            if (floor is not null)
            {
                floor.AddRider(this);
                _floor = floor;
            }
        }

        TileMapCollider2D? grid = cell?.Map.AttachedCollider;
        if (!ReferenceEquals(_floorGrid, grid))
        {
            _carriedMotion = Vector2.Zero;
            _priorMotion = Vector2.Zero;
            _floorGrid?.RemoveRider(this);
            grid?.AddRider(this);
            _floorGrid = grid;
        }

        _floorCell = cell;
        _floorTile = cell?.Type;
    }

    // Clears the riding link both ways.
    internal void Unride() => Ride(null, null);

    private void LeaveFloor()
    {
        if (_floor is { } floor)
        {
            _floor = null;
            floor.RemoveRider(this);
        }
    }
}
