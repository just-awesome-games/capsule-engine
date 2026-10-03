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
        Rect,
    }

    private readonly Kind _kind;
    private readonly float _a;
    private readonly float _b;

    // The snapped directions a ring spawns on. Zero places it anywhere on the rim.
    private readonly int _directions;

    private EmitShape(Kind kind, float a, float b, int directions = 0)
    {
        _kind = kind;
        _a = a;
        _b = b;
        _directions = directions;
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
        Kind.Rect => new Vector2(random.Range(-_a / 2f, _a / 2f), random.Range(-_b / 2f, _b / 2f)),
        _ => Vector2.Zero,
    };

    internal bool IsRing => _kind == Kind.Ring;

    // Where a ring burst starts: a direction index on a snapped ring, an angle in radians on a continuous one.
    internal float BurstStart(RandomSource random) => _directions > 0 ? random.Range(0, _directions) : random.Range(0f, MathF.Tau);

    // Particle index of count in a ring burst from start, spread evenly round the rim.
    internal Vector2 BurstPoint(float start, int index, int count) => _directions > 0
        ? DirectionPoint(((int)start + (int)((long)index * _directions / count)) % _directions)
        : AnglePoint(start + (index * MathF.Tau / count));

    private Vector2 DirectionPoint(int direction) => AnglePoint(direction * MathF.Tau / _directions);

    private Vector2 AnglePoint(float angle) => new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle)) * _a;
}
