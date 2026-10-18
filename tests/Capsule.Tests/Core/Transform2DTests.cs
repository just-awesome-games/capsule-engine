using System.Numerics;

namespace Capsule.Tests.Core;

public sealed class Transform2DTests
{
    private const float Tolerance = 1e-5f;

    // (10, -4) scales to (20, -12), a quarter turn takes that to (12, 20), and the offset lands it.
    [Fact]
    public void TransformPoint_ScalesThenTurnsThenOffsets()
    {
        Transform2D transform = new(new Vector2(100f, 50f), MathF.PI / 2f, new Vector2(2f, 3f));

        Vector2 placed = transform.TransformPoint(new Vector2(10f, -4f));

        Assert.Equal(112f, placed.X, Tolerance);
        Assert.Equal(70f, placed.Y, Tolerance);
    }

    [Fact]
    public void InverseTransformPoint_InvertsTransformPoint_AndHasNoAnswerOnAZeroAxis()
    {
        Transform2D transform = new(new Vector2(100f, 50f), 1.2f, new Vector2(2f, 0.5f));
        Vector2 world = new(130f, 20f);

        Vector2 local = transform.InverseTransformPoint(world);
        Vector2 back = transform.TransformPoint(local);

        Assert.Equal(world.X, back.X, Tolerance);
        Assert.Equal(world.Y, back.Y, Tolerance);
        Assert.False(float.IsFinite(new Transform2D(Vector2.Zero, 0f, new Vector2(0f, 1f)).InverseTransformPoint(new Vector2(5f, 5f)).X));
    }

    // No shear: the inner's position goes through the outer as a point, the turns add and the
    // scales multiply per axis, whatever the outer's scale does to a turned inner.
    [Fact]
    public void Compose_HasNoShear()
    {
        Transform2D outer = new(new Vector2(100f, 50f), 0.5f, new Vector2(2f, 3f));
        Transform2D inner = new(new Vector2(10f, -4f), 0.25f, new Vector2(0.5f, 0.5f));

        Transform2D composed = outer.Compose(inner);

        Assert.Equal(outer.TransformPoint(inner.Position), composed.Position);
        Assert.Equal(0.75f, composed.Rotation);
        Assert.Equal(new Vector2(1f, 1.5f), composed.Scale);
        AssertPlacesLike(new Transform2D(composed.Position, 0.75f, new Vector2(1f, 1.5f)), composed);
    }

    // A mirror conjugates a rotation: under a scale of negative determinant the inner turn runs
    // the other way, and its position is the mirrored point turned by the outer.
    [Fact]
    public void Compose_UnderAMirror_TurnsTheInnerTheOtherWay()
    {
        Transform2D mirror = new(new Vector2(30f, 40f), 0.7f, new Vector2(-1f, 1f));
        Transform2D inner = new(new Vector2(10f, 0f), 0.25f);

        Transform2D composed = mirror.Compose(inner);

        Assert.True(mirror.Mirrored);
        Assert.False(new Transform2D(Vector2.Zero, 0f, new Vector2(-1f, -1f)).Mirrored);
        Assert.Equal(0.7f - 0.25f, composed.Rotation);
        Assert.Equal(new Transform2D(new Vector2(30f, 40f), 0.7f).TransformPoint(new Vector2(-10f, 0f)), composed.Position);
        AssertPlacesLike(new Transform2D(composed.Position, 0.7f - 0.25f, new Vector2(-1f, 1f)), composed);
    }

    // The composed turn is carried as a sine and cosine derived from the parts' rather than
    // evaluated from the summed angle. A transform built fresh from the composed values places a
    // point the same way.
    private static void AssertPlacesLike(Transform2D expected, Transform2D actual)
    {
        Vector2 point = new(7f, -3f);
        Vector2 want = expected.TransformPoint(point);
        Vector2 got = actual.TransformPoint(point);

        Assert.Equal(want.X, got.X, Tolerance);
        Assert.Equal(want.Y, got.Y, Tolerance);
    }
}
