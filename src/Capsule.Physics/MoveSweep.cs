using System.Numerics;
using System.Runtime.CompilerServices;

namespace Capsule.Physics;

// One move in progress: where the shape stands, what it has applied, and the contacts it has written.
// Every pass appends to the same contact span. A stopped pass marks its contacts in `stopped` when the
// caller keeps one.
internal ref struct MoveSweep(
    CollisionWorld2D world,
    in Shape2D shape,
    Vector2 origin,
    CollisionFilter filter,
    ColliderHandle ignore,
    bool throughOneWay,
    Span<Contact2D> contacts,
    Span<bool> stopped = default)
{
    // The most sweeps one slide makes, the first and then one along each surface that stopped it.
    internal const int MaxPasses = 4;

    private readonly CollisionWorld2D _world = world;
    private readonly Shape2D _shape = shape;
    private readonly CollisionFilter _filter = filter;
    private readonly ColliderHandle _ignore = ignore;
    private readonly bool _throughOneWay = throughOneWay;
    private readonly Span<Contact2D> _contacts = contacts;
    private readonly Span<bool> _stopped = stopped;
    private Vector2 _at = origin;

    internal Vector2 Applied { get; private set; }

    internal bool Blocked { get; private set; }

    internal int Written { get; private set; }

    internal int Found { get; private set; }

    internal readonly MoveResult2D Result => new(Applied, Blocked, Found);

    internal readonly Vector2 At => _at;

    // Whether the shape is an axis-aligned box, whose sides stand upright.
    internal readonly bool IsBox => _shape.Kind == ShapeKind2D.Box;

    // The lowest the shape reaches where the move stands, as a Y-down coordinate.
    internal readonly float Bottom => _at.Y + _shape.Bounds.Max.Y;

    // How far the shape reaches to either side where the move stands.
    internal readonly float Left => _at.X + _shape.Bounds.Min.X;

    internal readonly float Right => _at.X + _shape.Bounds.Max.X;

    // The span across X of the flat underside of what a contact touched.
    internal readonly bool Underside(in Contact2D contact, out float left, out float right) =>
        _world.Underside(contact.Target, out left, out right);

    // What is left of a translation once the part driving into a surface is taken out.
    internal static Vector2 AlongSurface(Vector2 translation, Vector2 normal) =>
        translation - (normal * Vector2.Dot(translation, normal));

    // Moves as far along the translation as the shape can go and slides the rest along what stopped it.
    internal void Slide(Vector2 translation)
    {
        SlidePlanes planes = new(translation);
        for (int pass = 0; pass < MaxPasses && translation != Vector2.Zero; pass++)
        {
            MovePass step = Pass(translation);
            if (!step.Blocked)
            {
                break;
            }

            translation = planes.Next(translation, step);
        }
    }

    // One pass from where the move stands.
    internal MovePass Pass(Vector2 translation)
    {
        MovePass step = _world.Pass(_shape, _at, translation, _filter, _contacts[Written..], _ignore, _throughOneWay);
        Take(step);
        Blocked |= step.Blocked;

        return step;
    }

    // Moves onto a surface the translation reaches whose normal faces back along it within `minCos`, and
    // leaves everything as it was when there is none. Returns whether it moved onto one.
    internal bool Snap(Vector2 translation, float minCos)
    {
        MovePass step = _world.Pass(_shape, _at, translation, _filter, _contacts[Written..], _ignore, _throughOneWay);
        if (step.Blocked && -Vector2.Dot(step.Normal, Vector2.Normalize(translation)) >= minCos)
        {
            Take(step);
            return true;
        }

        return false;
    }

    private void Take(in MovePass step)
    {
        if (!_stopped.IsEmpty)
        {
            _stopped.Slice(Written, step.Written).Fill(step.Blocked);
        }

        Written += step.Written;
        Found += step.Found;
        _at += step.Moved;
        Applied += step.Moved;
    }
}

// One sweep of a move: how far it went, whether a surface stopped it and that surface's normal, and
// how many contacts it wrote into its span and found in all.
internal readonly record struct MovePass(Vector2 Moved, bool Blocked, Vector2 Normal, int Written, int Found);

// The surfaces one move has met and the share of its translation still to go. Each pass's leftover is the original
// translation's remaining share slid along one surface met that keeps clear of every other, the newest first. None
// keeping clear means the move is wedged where surfaces facing into each other meet, and it stops there. Sliding the
// leftover along the newest surface alone would climb an earlier one and fall back on the next step.
internal ref struct SlidePlanes(Vector2 wanted)
{
    // How far a slide may lean into a surface, as a share of its length, and still count as clear of it. A
    // projection onto one surface rounds against another square to it by a few units in the last place.
    private const float Lean = 1e-5f;

    private readonly Vector2 _wanted = wanted;
    private Normals _met;
    private int _count;

    // The share of the wanted translation the passes so far have not moved.
    private float _left = 1f;

    // The translation the next pass sweeps, after translation stopped against step's surface.
    internal Vector2 Next(Vector2 translation, in MovePass step)
    {
        _left *= (translation - step.Moved).Length() / translation.Length();
        if (_count < MoveSweep.MaxPasses)
        {
            _met[_count++] = step.Normal;
        }

        Vector2 rest = _wanted * _left;
        for (int index = _count - 1; index >= 0; index--)
        {
            Vector2 along = MoveSweep.AlongSurface(rest, _met[index]);
            if (ClearOfAll(along, index))
            {
                return along;
            }
        }

        return Vector2.Zero;
    }

    private readonly bool ClearOfAll(Vector2 along, int except)
    {
        float lean = along.Length() * Lean;
        for (int index = 0; index < _count; index++)
        {
            if (index != except && Vector2.Dot(along, _met[index]) < -lean)
            {
                return false;
            }
        }

        return true;
    }

    [InlineArray(MoveSweep.MaxPasses)]
    private struct Normals
    {
        private Vector2 _element0;
    }
}
