using System.Numerics;
using System.Runtime.CompilerServices;
using Capsule.Scenes;

namespace Capsule.Physics;

// The RestsOnCenter settle. The body sweeps a pose raised by the sink, and the settle sets how far
// below that pose the entity stands.
public sealed partial class KinematicBody2D
{
    // Where the body's sweeps start, the entity's place raised by the sink. A position the game set
    // itself clears the sink.
    private Vector2 Swept(Entity entity)
    {
        if (_sink == 0f)
        {
            return entity.WorldPosition;
        }

        if (entity.Position != _written)
        {
            _sink = 0f;
            return entity.WorldPosition;
        }

        return entity.WorldPosition - new Vector2(0f, _sink);
    }

    // Takes the sink out of a move that leaves the floor. A rise lifts the entity through the sink
    // before the swept pose moves. A fall that started in the air lowers the swept pose through it
    // while the entity falls as asked.
    private Vector2 Spend(Vector2 translation)
    {
        if (translation.Y < 0f)
        {
            float spent = MathF.Min(-translation.Y, _sink);
            _sink -= spent;
            translation.Y += spent;
        }
        else if (translation.Y > 0f && !IsOnFloor)
        {
            translation.Y += _sink;
            _sink = 0f;
        }

        return translation;
    }

    // Casts the entity's hanging half, the strip below the swept pose on its downhill side, along the
    // walk. A wall it meets cuts the walk short and stops the move as that wall would stop the box.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private MoveResult2D KeepClear(CollisionWorld2D world, in Shape2D shape, Vector2 origin, Vector2 swept, CollisionFilter blocking, MoveResult2D result)
    {
        Aabb2D bounds = shape.Bounds;
        float center = origin.X + ((bounds.Min.X + bounds.Max.X) * 0.5f);
        float top = origin.Y + bounds.Max.Y;
        float bottom = origin.Y + bounds.Max.Y + _sink;
        Aabb2D strip = _restSide < 0f
            ? new Aabb2D(new Vector2(center + CollisionTolerance.LinearSlop, top), new Vector2(origin.X + bounds.Max.X, bottom))
            : new Aabb2D(new Vector2(origin.X + bounds.Min.X, top), new Vector2(center - CollisionTolerance.LinearSlop, bottom));

        if (!world.Probe(Shape2D.Box(strip), result.Translation, blocking, out ShapeCastHit2D wall, _collider.Handle)
            || IsFloor(wall.Normal))
        {
            return result;
        }

        float reach = MathF.Max(0f, (MathF.Abs(result.Translation.X) * wall.Fraction) - CollisionTolerance.LinearSlop);
        result = Sweep(world, shape, origin, new Vector2(MathF.Sign(swept.X) * reach, swept.Y), blocking, false, false);

        int count = result.ContactCount;
        if (count == _found.Length)
        {
            Collider2D.Grow(ref _found, count + 1);
            Collider2D.Grow(ref _stopped, count + 1);
        }

        _found[count] = new Contact2D(wall.Target, wall.Point, wall.Normal, 0f);
        _stopped[count] = true;

        return new MoveResult2D(result.Translation, true, count + 1);
    }

    // Sets the sink after a move that ended on a floor with the swept pose at `pose`. The entity's bottom
    // center goes onto the floor under the center when that floor joins the one the pose rests on and
    // the hanging half meets no wall. Otherwise the entity keeps its `previous` bottom, held within the
    // allowance and the depth the move has already kept clear. KeepClear kept `cleared` clear along the
    // move. Returns the center's floor normal, or zero when the body holds its height.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Vector2 Settle(CollisionWorld2D world, in Shape2D shape, Vector2 pose, CollisionFilter blocking, float previous, in MoveResult2D move, float cleared)
    {
        int support = SupportIndex(move.ContactCount);
        if (support < 0)
        {
            return Vector2.Zero;
        }

        Aabb2D bounds = shape.Bounds;
        float half = (bounds.Max.X - bounds.Min.X) * 0.5f;
        float center = pose.X + bounds.Min.X + half;
        float bottom = pose.Y + bounds.Max.Y;
        float allowance = half * _floorTan;
        float reach = (2f * allowance) + CollisionTolerance.ContactSkin;
        float held = Math.Clamp(previous - bottom, 0f, allowance);
        float side = _restSide;
        _sink = MathF.Min(held, Kept(move.Translation.X, side, side, held, cleared));

        // The ray reaches twice the allowance. A pose a walk left hanging over a crest still finds the
        // floor below and holds the entity as low as the allowance lets it.
        if (!world.Raycast(new Vector2(center, bottom), -Up, reach, blocking, out RayHit2D floor, _collider.Handle)
            || !IsFloor(floor.Normal))
        {
            return Vector2.Zero;
        }

        // The swept pose stands a slop clear of its floor. On a flat floor the center is that close too.
        float depth = floor.Point.Y - bottom - CollisionTolerance.LinearSlop;
        if (depth < CollisionTolerance.LinearSlop)
        {
            _sink = 0f;
            _restSide = 0f;
            return floor.Normal;
        }

        // The pose rests on the side where the floor climbs toward it. Two flat floors leave only the
        // contact to say which side that is.
        Vector2 rest = _found[support].Normal;
        _restSide = MathF.Abs(floor.Normal.X) > FlatX ? -MathF.Sign(floor.Normal.X)
            : MathF.Abs(rest.X) > FlatX ? -MathF.Sign(rest.X)
            : MathF.Sign(_found[support].Point.X - center);

        float kept = Kept(move.Translation.X, _restSide, side, held, cleared);
        float hold = MathF.Min(held, kept);

        // A floor that runs on as one plane from the center to the resting corner needs no ledge test.
        bool plane = Vector2.Dot(floor.Normal, rest) >= 1f - PlaneTolerance
            && MathF.Abs(depth - (half * MathF.Abs(floor.Normal.X / floor.Normal.Y))) <= CollisionTolerance.ContactSkin;
        if (!plane && _restSide != 0f)
        {
            Vector2 from = new(center, floor.Point.Y - CollisionTolerance.LinearSlop);
            Vector2 chord = new Vector2(_restSide < 0f ? pose.X + bounds.Min.X : pose.X + bounds.Max.X, bottom) - from;
            if (world.Raycast(from, chord, chord.Length(), blocking, out RayHit2D wall, _collider.Handle) && !IsFloor(wall.Normal))
            {
                // A wall between the two floors is a ledge. An entity that stood below the wall has just
                // stepped up onto the ledge and stands on it.
                _sink = previous > wall.Point.Y ? 0f : hold;
                return Vector2.Zero;
            }

            // No ray meets the side of a floor that blocks only from above. The floors themselves show
            // whether one runs on into the other.
            if (RestsOnTopOnly(world, move.ContactCount) && !Joins(world, blocking, center, bottom, half, reach, floor))
            {
                _sink = hold;
                return Vector2.Zero;
            }
        }

        float sink = MathF.Min(depth, allowance);
        if (sink > kept + CollisionTolerance.LinearSlop && (_restSide == 0f || Hangs(world, blocking, center, bottom, half, sink)))
        {
            _sink = hold;
            return Vector2.Zero;
        }

        _sink = sink;

        return floor.Normal;
    }

    // How deep below the swept pose the move has already kept the hanging half clear, for a pose resting
    // on side `side` after a walk of `across` from one resting on `before`. A pose that moved toward its
    // resting side, or not across at all, hangs within the box the entity filled before, down to `held`.
    // One that moved the other way is clear as far as KeepClear swept it.
    private static float Kept(float across, float side, float before, float held, float cleared) =>
        across == 0f || MathF.Sign(across) == side ? held
        : side == before ? cleared
        : 0f;

    // Whether a wall stands in the hanging half of an entity `sink` below the swept pose. A sliver at the
    // center sweeps out through that half, below the pose.
    private bool Hangs(CollisionWorld2D world, CollisionFilter blocking, float center, float bottom, float half, float sink)
    {
        Aabb2D sliver = new(new Vector2(center - CollisionTolerance.LinearSlop, bottom), new Vector2(center + CollisionTolerance.LinearSlop, bottom + sink));
        Vector2 across = new(-_restSide * (half - (2f * CollisionTolerance.LinearSlop)), 0f);

        return world.Probe(Shape2D.Box(sliver), across, blocking, out ShapeCastHit2D wall, _collider.Handle)
            && !IsFloor(wall.Normal);
    }

    // Whether a floor the move stopped on blocks only from above.
    private bool RestsOnTopOnly(CollisionWorld2D world, int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (_stopped[index] && IsFloor(_found[index].Normal) && world.IsTopOnly(_found[index].Target))
            {
                return true;
            }
        }

        return false;
    }

    // Whether the floor under the resting corner runs on into `floor`, the floor under the center, with
    // no drop between. The two floors' lines must meet under the box, and the floor just to either side
    // of that point must lie on the matching line.
    private bool Joins(CollisionWorld2D world, CollisionFilter blocking, float center, float bottom, float half, float reach, in RayHit2D floor)
    {
        float corner = center + (_restSide * (half - CollisionTolerance.LinearSlop));
        if (!world.Raycast(new Vector2(corner, bottom), -Up, reach, blocking, out RayHit2D rest, _collider.Handle)
            || !IsFloor(rest.Normal))
        {
            return false;
        }

        Vector2 restNormal = rest.Normal;
        Vector2 centerNormal = floor.Normal;
        float restLine = Vector2.Dot(restNormal, rest.Point);
        float centerLine = Vector2.Dot(centerNormal, floor.Point);
        float cross = (restNormal.X * centerNormal.Y) - (restNormal.Y * centerNormal.X);
        if (MathF.Abs(cross) <= 1e-4f)
        {
            return MathF.Abs(Vector2.Dot(centerNormal, rest.Point) - centerLine) <= CollisionTolerance.ContactSkin;
        }

        float meet = ((restLine * centerNormal.Y) - (centerLine * restNormal.Y)) / cross;
        float along = (meet - center) * _restSide;
        if (along < -CollisionTolerance.ContactSkin || along > half + CollisionTolerance.ContactSkin)
        {
            return false;
        }

        return (along <= JoinProbe || Lies(world, blocking, meet - (_restSide * JoinProbe), bottom, reach, centerNormal, centerLine))
            && (along >= half - JoinProbe || Lies(world, blocking, meet + (_restSide * JoinProbe), bottom, reach, restNormal, restLine));
    }

    // Whether the floor straight below `x` has `normal` and lies on the line where that normal's dot
    // product with a point is `line`.
    private bool Lies(CollisionWorld2D world, CollisionFilter blocking, float x, float bottom, float reach, Vector2 normal, float line) =>
        world.Raycast(new Vector2(x, bottom), -Up, reach, blocking, out RayHit2D hit, _collider.Handle)
        && Vector2.Dot(hit.Normal, normal) >= 1f - PlaneTolerance
        && MathF.Abs(Vector2.Dot(normal, hit.Point) - line) <= CollisionTolerance.ContactSkin;

    // The index of the first contact that stopped a move on a floor, or -1.
    private int SupportIndex(int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (_stopped[index] && IsFloor(_found[index].Normal))
            {
                return index;
            }
        }

        return -1;
    }
}
