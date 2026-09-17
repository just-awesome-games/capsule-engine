using System.Numerics;

namespace Capsule.Tests.Runtime;

public sealed class Transform2DTests
{
    private const float Tolerance = 1e-5f;

    // Scale, then turn, then offset — through the deterministic sine, so the placed point is the
    // same on every platform and exact against the same arithmetic.
    [Fact]
    public void Apply_ScalesThenTurnsThenOffsets()
    {
        Transform2D transform = new(new Vector2(100f, 50f), 0.5f, new Vector2(2f, 3f));

        float cos = DeterministicMath.Cos(0.5f);
        float sin = DeterministicMath.Sin(0.5f);
        Vector2 scaled = new(20f, -12f);

        Assert.Equal(
            new Vector2(100f + (scaled.X * cos) - (scaled.Y * sin), 50f + (scaled.X * sin) + (scaled.Y * cos)),
            transform.Apply(new Vector2(10f, -4f)));
    }

    [Fact]
    public void Unapply_InvertsApply_AndHasNoAnswerOnAZeroAxis()
    {
        Transform2D transform = new(new Vector2(100f, 50f), 1.2f, new Vector2(2f, 0.5f));
        Vector2 world = new(130f, 20f);

        Vector2 local = transform.Unapply(world);
        Vector2 back = transform.Apply(local);

        Assert.Equal(world.X, back.X, Tolerance);
        Assert.Equal(world.Y, back.Y, Tolerance);
        Assert.False(float.IsFinite(new Transform2D(Vector2.Zero, 0f, new Vector2(0f, 1f)).Unapply(new Vector2(5f, 5f)).X));
    }

    // No shear: the inner's position goes through the outer as a point, the turns add and the
    // scales multiply per axis, whatever the outer's scale does to a turned inner.
    [Fact]
    public void Then_ComposesWithoutShear()
    {
        Transform2D outer = new(new Vector2(100f, 50f), 0.5f, new Vector2(2f, 3f));
        Transform2D inner = new(new Vector2(10f, -4f), 0.25f, new Vector2(0.5f, 0.5f));

        Transform2D composed = outer.Then(inner);

        Assert.Equal(outer.Apply(inner.Position), composed.Position);
        Assert.Equal(0.75f, composed.Rotation);
        Assert.Equal(new Vector2(1f, 1.5f), composed.Scale);
    }

    // A mirror conjugates a rotation: under a scale of negative determinant the inner turn runs
    // the other way, and its position is the mirrored point turned by the outer.
    [Fact]
    public void Then_UnderAMirror_TurnsTheInnerTheOtherWay()
    {
        Transform2D mirror = new(new Vector2(30f, 40f), 0.7f, new Vector2(-1f, 1f));
        Transform2D inner = new(new Vector2(10f, 0f), 0.25f);

        Transform2D composed = mirror.Then(inner);

        Assert.True(mirror.Mirrored);
        Assert.False(new Transform2D(Vector2.Zero, 0f, new Vector2(-1f, -1f)).Mirrored);
        Assert.Equal(0.7f - 0.25f, composed.Rotation);
        Assert.Equal(new Transform2D(new Vector2(30f, 40f), 0.7f).Apply(new Vector2(-10f, 0f)), composed.Position);
    }

    [Fact]
    public void Identity_PlacesNothing_AndEqualityIsByTheThreeValues()
    {
        Assert.Equal(new Vector2(3f, 4f), Transform2D.Identity.Apply(new Vector2(3f, 4f)));
        Assert.Equal(Transform2D.Identity, Transform2D.Identity.Then(Transform2D.Identity));
        Assert.Equal(new Transform2D(new Vector2(1f, 2f), 0.5f), new Transform2D(new Vector2(1f, 2f), 0.5f, Vector2.One));
        Assert.NotEqual(new Transform2D(new Vector2(1f, 2f), 0.5f), new Transform2D(new Vector2(1f, 2f), 0.5f, new Vector2(2f, 1f)));
        Assert.Equal("(1.5, -2) r 0.5 s (2, 1)", new Transform2D(new Vector2(1.5f, -2f), 0.5f, new Vector2(2f, 1f)).ToString());
    }
}
