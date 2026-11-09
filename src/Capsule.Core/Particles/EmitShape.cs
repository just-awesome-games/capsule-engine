using System.Numerics;

namespace Capsule.Particles;

/// <summary>Where a spawned particle starts, sampled about an emitter's <c>Offset</c> in its own space.</summary>
public readonly record struct EmitShape
{
    private enum Kind : byte
    {
        Point,
        Circle,
        Ring,
        Arc,
        Rect,
    }

    private readonly Kind _kind;
    private readonly float _a;
    private readonly float _b;

    // The snapped directions a ring spawns on. Zero places it anywhere on the rim.
    private readonly int _directions;

    // An arc's centre, in radians from the emitter's own +X.
    private readonly float _centre;

    private EmitShape(Kind kind, float a, float b, int directions = 0, float centre = 0f)
    {
        _kind = kind;
        _a = a;
        _b = b;
        _directions = directions;
        _centre = centre;
    }

    /// <summary>Every particle starts on the same point. The default shape.</summary>
    public static EmitShape Point => default;

    /// <summary>A disc of <paramref name="radius"/>, filled and uniform by area.</summary>
    public static EmitShape Circle(float radius)
    {
        Guard.NonNegative(radius, nameof(radius));

        return new(Kind.Circle, radius, 0f);
    }

    /// <summary>The rim of a disc of <paramref name="radius"/>.</summary>
    public static EmitShape Ring(float radius)
    {
        Guard.NonNegative(radius, nameof(radius));

        return new(Kind.Ring, radius, 0f);
    }

    /// <summary>
    /// The rim of a disc of <paramref name="radius"/> at <paramref name="directions"/> evenly spaced
    /// directions, the first along the emitter's own +X.
    /// </summary>
    public static EmitShape Ring(float radius, int directions)
    {
        Guard.NonNegative(radius, nameof(radius));
        ArgumentOutOfRangeException.ThrowIfLessThan(directions, 1);

        return new(Kind.Ring, radius, 0f, directions);
    }

    /// <summary>
    /// The rim of a disc of <paramref name="radius"/> across <paramref name="degrees"/> of arc centred on
    /// <paramref name="direction"/>, in the emitter's own space as its launch direction is.
    /// </summary>
    /// <remarks>An arc of 360 degrees is the whole rim. The entity's turn turns it, as it turns the launch direction.</remarks>
    /// <example>
    /// Motes drawn in from a fan on the left to the emitter's centre, where they end:
    /// <code>
    /// Add(new ParticleEmitter(mote, capacity: 32)
    /// {
    ///     Shape = EmitShape.Arc(120f, -Vector2.UnitX, 45f),
    ///     RadialSpeed = new FloatRange(-90f, -60f),
    ///     KillZone = Shape2D.Circle(Vector2.Zero, 4f),
    ///     Lifetime = new FloatRange(3f, 3f),
    ///     Rate = 12f,
    /// });
    /// </code>
    /// </example>
    /// <param name="radius">The disc's radius, in world units.</param>
    /// <param name="direction">The arc's centre line. Any non-zero length works.</param>
    /// <param name="degrees">The arc's full width about <paramref name="direction"/>.</param>
    public static EmitShape Arc(float radius, Vector2 direction, float degrees)
    {
        Guard.NonNegative(radius, nameof(radius));
        Guard.Finite(direction, nameof(direction));
        if (direction == Vector2.Zero)
        {
            throw new ArgumentException("An arc centres on a direction, and a zero vector has none. Pass a non-zero one.", nameof(direction));
        }

        if (!(degrees > 0f && degrees <= 360f))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), degrees, "An arc spans more than 0 and at most 360 degrees. Use Ring for the whole rim.");
        }

        return new(Kind.Arc, radius, float.DegreesToRadians(degrees), centre: DeterministicMath.Atan2(direction.Y, direction.X));
    }

    /// <summary>A rectangle of <paramref name="width"/> by <paramref name="height"/>, filled and centred on the emitter's offset.</summary>
    public static EmitShape Rect(float width, float height)
    {
        Guard.NonNegative(width, nameof(width));
        Guard.NonNegative(height, nameof(height));

        return new(Kind.Rect, width, height);
    }

    // The offset in the emitter's own space, about its Offset.
    internal Vector2 Sample(RandomSource random) => _kind switch
    {
        Kind.Circle => random.InsideUnitCircle() * _a,
        Kind.Ring => _directions > 0 ? DirectionPoint(random.Range(0, _directions)) : AnglePoint(random.Range(0f, MathF.Tau)),
        Kind.Arc => AnglePoint(_centre + random.Range(-_b / 2f, _b / 2f)),
        Kind.Rect => new Vector2(random.Range(-_a / 2f, _a / 2f), random.Range(-_b / 2f, _b / 2f)),
        _ => Vector2.Zero,
    };

    // A ring's burst spreads round its rim and an arc's across its width, where other shapes sample each particle.
    internal bool SpreadsBursts => _kind is Kind.Ring or Kind.Arc;

    // Where a ring burst starts: a direction index on a snapped ring, an angle in radians on a continuous one. An arc's
    // spread is fixed and draws nothing.
    internal float BurstStart(RandomSource random) =>
        _kind == Kind.Arc ? 0f : _directions > 0 ? random.Range(0, _directions) : random.Range(0f, MathF.Tau);

    // Particle index of count in a burst from start: evenly round a ring's rim, or centred in equal parts of an arc.
    internal Vector2 BurstPoint(float start, int index, int count) =>
        _kind == Kind.Arc ? AnglePoint(_centre + (_b * (((index + 0.5f) / count) - 0.5f)))
        : _directions > 0 ? DirectionPoint(((int)start + (int)((long)index * _directions / count)) % _directions)
        : AnglePoint(start + (index * MathF.Tau / count));

    private Vector2 DirectionPoint(int direction) => AnglePoint(direction * MathF.Tau / _directions);

    private Vector2 AnglePoint(float angle) => new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle)) * _a;
}
