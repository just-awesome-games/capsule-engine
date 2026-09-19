using System.Globalization;
using System.Numerics;

namespace Capsule;

/// <summary>
/// A place, a turn and a size in the Y-down plane. <see cref="Position"/> is in the units of the
/// space it sits in, <see cref="Rotation"/> is radians clockwise positive, and <see cref="Scale"/> is
/// per axis. Placing a point applies scale, then turn, then offset. Two transforms compose without
/// shear: the turns add, the scales multiply per axis, and the inner position passes through the
/// outer as a point. A mirror, meaning one negative scale axis, conjugates the inner turn, which
/// keeps a flipped figure's parts turning the way they were drawn. Equality compares position,
/// rotation and scale.
/// <para>
/// The cosine and sine of the turn are stored alongside it, evaluated once through
/// <see cref="DeterministicMath"/>, so composition is only multiply-adds and is the same on every
/// platform.
/// </para>
/// </summary>
public readonly record struct Transform2D
{
    private readonly float _cos;
    private readonly float _sin;

    /// <summary>A place and a turn at scale one.</summary>
    public Transform2D(Vector2 position, float rotation = 0f)
        : this(position, rotation, Vector2.One)
    {
    }

    /// <summary>A place, a turn and a size.</summary>
    /// <param name="position">The offset, in the units of the space this transform sits in.</param>
    /// <param name="rotation">The turn, in radians, clockwise positive in the Y-down plane.</param>
    /// <param name="scale">The size per axis. Zero and negative axes are allowed.</param>
    public Transform2D(Vector2 position, float rotation, Vector2 scale)
        : this(position, rotation, scale, rotation == 0f ? 1f : DeterministicMath.Cos(rotation), rotation == 0f ? 0f : DeterministicMath.Sin(rotation))
    {
    }

    private Transform2D(Vector2 position, float rotation, Vector2 scale, float cos, float sin)
    {
        Position = position;
        Rotation = rotation;
        Scale = scale;
        _cos = cos;
        _sin = sin;
    }

    /// <summary>No offset, no turn and scale one, so placing a point through it leaves the point alone.</summary>
    public static Transform2D Identity => new(Vector2.Zero);

    /// <summary>The offset, in the units of the space this transform sits in.</summary>
    public Vector2 Position { get; }

    /// <summary>The turn, in radians, clockwise positive in the Y-down plane.</summary>
    public float Rotation { get; }

    /// <summary>The size per axis.</summary>
    public Vector2 Scale { get; }

    /// <summary>Whether this transform flips handedness, which is true when one axis of <see cref="Scale"/> is negative.</summary>
    public bool Mirrored => Scale.X * Scale.Y < 0f;

    /// <summary>A point of the placed space expressed in this transform's space, after scale, turn and offset.</summary>
    public Vector2 TransformPoint(Vector2 local)
    {
        Vector2 scaled = local * Scale;

        return new Vector2(
            Position.X + (scaled.X * _cos) - (scaled.Y * _sin),
            Position.Y + (scaled.X * _sin) + (scaled.Y * _cos));
    }

    /// <summary>
    /// The inverse of <see cref="TransformPoint"/>, giving the point in the placed space that lands on
    /// <paramref name="world"/>. It is not finite on an axis whose scale is zero, because no point in
    /// the placed space reaches a world point off that axis.
    /// </summary>
    public Vector2 InverseTransformPoint(Vector2 world)
    {
        Vector2 offset = world - Position;

        return new Vector2(
            (offset.X * _cos) + (offset.Y * _sin),
            (offset.Y * _cos) - (offset.X * _sin)) / Scale;
    }

    /// <summary>
    /// This transform placing <paramref name="local"/>. The result is the transform of something whose
    /// own values are <paramref name="local"/>'s inside the space this one places.
    /// </summary>
    public Transform2D Compose(Transform2D local)
    {
        // A mirror conjugates the inner turn, so its sine flips with its rotation. Its cosine is even.
        float localSin = Mirrored ? -local._sin : local._sin;
        float cos = (_cos * local._cos) - (_sin * localSin);
        float sin = (_sin * local._cos) + (_cos * localSin);

        return new(TransformPoint(local.Position), Rotation + (Mirrored ? -local.Rotation : local.Rotation), Scale * local.Scale, cos, sin);
    }

    // This transform at another place and size, reusing the turn it already evaluated.
    internal Transform2D With(Vector2 position, Vector2 scale) => new(position, Rotation, scale, _cos, _sin);

    /// <inheritdoc/>
    public bool Equals(Transform2D other) =>
        Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Position, Rotation, Scale);

    /// <summary>The three values as <c>(x, y) r rotation s (x, y)</c>, in the invariant culture and shortest round-trip form.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"({Position.X}, {Position.Y}) r {Rotation} s ({Scale.X}, {Scale.Y})");
}
