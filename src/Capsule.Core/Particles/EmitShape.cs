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

    private EmitShape(Kind kind, float a, float b)
    {
        _kind = kind;
        _a = a;
        _b = b;
    }

    /// <summary>Every particle starts on the same point. The default shape.</summary>
    public static EmitShape Point => default;

    /// <summary>A disc of <paramref name="radius"/>, filled and uniform by area.</summary>
    public static EmitShape Circle(float radius) => new(Kind.Circle, radius, 0f);

    /// <summary>The rim of a disc of <paramref name="radius"/>.</summary>
    public static EmitShape Ring(float radius) => new(Kind.Ring, radius, 0f);

    /// <summary>A rectangle of <paramref name="width"/> by <paramref name="height"/>, filled and centred on the emitter's offset.</summary>
    public static EmitShape Rect(float width, float height) => new(Kind.Rect, width, height);

    // The offset in the emitter's own space, about its Offset.
    internal Vector2 Sample(RandomSource random) => _kind switch
    {
        Kind.Circle => random.InsideUnitCircle() * _a,
        Kind.Ring => RingPoint(random) * _a,
        Kind.Rect => new Vector2(random.Range(-_a / 2f, _a / 2f), random.Range(-_b / 2f, _b / 2f)),
        _ => Vector2.Zero,
    };

    private static Vector2 RingPoint(RandomSource random)
    {
        float angle = random.Range(0f, MathF.Tau);

        return new Vector2(DeterministicMath.Cos(angle), DeterministicMath.Sin(angle));
    }
}
