using System.Numerics;

namespace Capsule.Physics;

// One move in progress: where the shape stands, what it has applied, and the contacts it has written.
// Every pass appends to the same contact span. A stopped pass marks its contacts in `stopped` when the
// caller keeps one.
internal ref struct MoveSweep
{
    // The most sweeps one slide makes, the first and then one along each surface that stopped it.
    internal const int MaxPasses = 4;

    private readonly CollisionWorld2D _world;
    private readonly Shape2D _shape;
    private readonly CollisionFilter _filter;
    private readonly ColliderHandle _ignore;
    private readonly bool _throughOneWay;
    private readonly Span<Contact2D> _contacts;
    private readonly Span<bool> _stopped;
    private Vector2 _at;

    internal MoveSweep(
        CollisionWorld2D world,
        in Shape2D shape,
        Vector2 origin,
        CollisionFilter filter,
        ColliderHandle ignore,
        bool throughOneWay,
        Span<Contact2D> contacts,
        Span<bool> stopped = default)
    {
        _world = world;
        _shape = shape;
        _filter = filter;
        _ignore = ignore;
        _throughOneWay = throughOneWay;
        _contacts = contacts;
        _stopped = stopped;
        _at = origin;
    }

    internal Vector2 Applied { get; private set; }

    internal bool Blocked { get; private set; }

    internal int Written { get; private set; }

    internal int Found { get; private set; }

    internal readonly MoveResult2D Result => new(Applied, Blocked, Found);

    // What is left of a translation once the part driving into a surface is taken out.
    internal static Vector2 AlongSurface(Vector2 translation, Vector2 normal) =>
        translation - (normal * Vector2.Dot(translation, normal));

    // Moves as far along the translation as the shape can go and slides the rest along what stopped it.
    internal void Slide(Vector2 translation)
    {
        for (int pass = 0; pass < MaxPasses && translation != Vector2.Zero; pass++)
        {
            MovePass step = Pass(translation);
            if (!step.Blocked)
            {
                break;
            }

            translation = AlongSurface(translation - step.Moved, step.Normal);
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
    // leaves everything as it was when there is none.
    internal void Snap(Vector2 translation, float minCos)
    {
        MovePass step = _world.Pass(_shape, _at, translation, _filter, _contacts[Written..], _ignore, _throughOneWay);
        if (step.Blocked && -Vector2.Dot(step.Normal, Vector2.Normalize(translation)) >= minCos)
        {
            Take(step);
        }
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
