using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Physics;

// A grounded body's CeilingCornerCorrection: sliding a rise past the edge of a flat ceiling.
public sealed class CeilingCornerTests
{
    private const float Edge = 8f;

    private const float StartX = 40f;

    // The body's top starts at y = 40 and the ceiling's underside is at y = 36. A rise of 6 meets it after 4.
    private const float StartY = 40f;

    private const float Underside = 36f;

    private const float Rise = 6f;

    [Fact]
    public void CeilingCornerCorrection_RejectsANegativeDistance()
    {
        KinematicBody2D body = new(new BoxCollider2D(new Vector2(Edge, Edge)));

        Assert.Throws<ArgumentOutOfRangeException>(() => body.CeilingCornerCorrection = -1f);
    }

    // A body moving `way` across and Rise up meets a ceiling from `from` to `to`, measured from where the
    // across part leaves the body's left side. It shifts by `shift` and rises on, off the ceiling, when the
    // smaller overlap is within the correction. A tie shifts against the move across, and with no move across
    // it is stopped. An overlap past the correction, a correction of 0 and a floating body are stopped.
    [Theory]
    [InlineData(5f, 64f, 0f, 3f, false, -3f)]
    [InlineData(4.75f, 64f, 0f, 3f, false, float.NaN)]
    [InlineData(-64f, 3f, 0f, 3f, false, 3f)]
    [InlineData(-64f, 3.25f, 1f, 3f, false, float.NaN)]
    [InlineData(-64f, 2f, -1f, 3f, false, 2f)]
    [InlineData(3f, 6f, 0f, 6f, false, -5f)]
    [InlineData(2f, 5f, 0f, 6f, false, 5f)]
    [InlineData(2f, 6f, 1f, 6f, false, -6f)]
    [InlineData(2f, 6f, -1f, 6f, false, 6f)]
    [InlineData(2f, 6f, 0f, 6f, false, float.NaN)]
    [InlineData(7f, 64f, 0f, 0f, false, float.NaN)]
    [InlineData(7f, 64f, 0f, 3f, true, float.NaN)]
    public void ARiseIntoACeilingsEdge_ShiftsByTheSmallerOverlapWithinTheCorrection_OrIsStopped(
        float from, float to, float way, float correction, bool floating, float shift)
    {
        Scene scene = new Scene().Started();
        float walked = StartX + way;
        scene.Add(Ceiling(walked + from, walked + to));
        SceneFixtures.Body body = Rising(scene, correction, mover => mover.Mode = floating ? BodyMode.Floating : BodyMode.Grounded);

        body.Mover.Move(new Vector2(way, -Rise));

        bool corrects = !float.IsNaN(shift);
        Assert.Equal(corrects ? walked + shift : walked, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(corrects ? StartY - Rise : Underside, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(!corrects, body.Mover.IsOnCeiling);
    }

    // A ceiling reaching 2 in from the right would be cleared by a shift of 2 to the left. A wall 1 to the
    // left stops the shift, and a solid block over the shifted body stops the rest of the rise. The ceiling
    // then stops the rise where it met it. A one-way block there is passed from below, and the correction stands.
    [Theory]
    [InlineData(Obstacle.Wall, false)]
    [InlineData(Obstacle.Higher, false)]
    [InlineData(Obstacle.OneWayHigher, true)]
    public void AShiftOrRiseThatIsStopped_LeavesTheCeilingStoppingTheRise(Obstacle obstacle, bool corrects)
    {
        Scene scene = new Scene().Started();
        scene.Add(Ceiling(StartX + 6f, StartX + 64f));
        scene.Add(obstacle == Obstacle.Wall
            ? new Block(new Vector2(StartX - 9f, 0f), new Vector2(8f, 64f))
            : new Block(new Vector2(StartX - 4f, Underside - 17f), new Vector2(3f, 16f), oneWay: obstacle == Obstacle.OneWayHigher));
        SceneFixtures.Body body = Rising(scene, 3f);

        body.Mover.Move(new Vector2(0f, -Rise));

        Assert.Equal(corrects ? StartX - 2f : StartX, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(corrects ? StartY - Rise : Underside, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(!corrects, body.Mover.IsOnCeiling);
    }

    // The underside of a ceiling slants down to the right from 2 inside the body's top. The body's corner
    // meets the slant and the rise stops there.
    [Fact]
    public void ARiseIntoASlantedCeiling_IsStopped()
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(StartX + 6f, 20f), new(StartX + 16f, 20f), new(StartX + 16f, 35f), new(StartX + 6f, 30f)]));
        SceneFixtures.Body body = Rising(scene, 3f);

        body.Mover.Move(new Vector2(0f, -12f));

        Assert.Equal(StartX, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(31f, body.Position.Y, 2f * CollisionFixtures.Tolerance);
        Assert.True(body.Mover.IsOnCeiling);
    }

    // The ceiling's edge stands 1 right of the body, and a rise straight up clears it. The move across runs
    // first and carries the body 1 under the edge, and the correction takes that back.
    [Fact]
    public void ADriftThatCarriesTheBodyUnderAnEdge_IsCorrectedBack()
    {
        Scene scene = new Scene().Started();
        scene.Add(Ceiling(StartX + Edge + 1f, StartX + 64f));
        SceneFixtures.Body body = Rising(scene, 3f);

        body.Mover.Move(new Vector2(2f, -Rise));

        Assert.Equal(StartX + 1f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(StartY - Rise, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.False(body.Mover.IsOnCeiling);
    }

    // Seventeen ceilings overflow the first contact span, and the first pass skips the correction. The run
    // with room for them corrects past their edge, and the shift ends flush against sixteen walls, whose
    // contacts overflow the grown span. The move still resolves whole.
    [Fact]
    public void ACorrectionThatOnlyARunWithRoomForEveryContactFinds_OverflowsNoSpan()
    {
        Scene scene = new Scene().Started();
        for (int index = 0; index < 17; index++)
        {
            scene.Add(Ceiling(StartX + 4f, StartX + 64f));
        }

        for (int index = 0; index < 16; index++)
        {
            scene.Add(new Block(new Vector2(StartX - 12f, 0f), new Vector2(8f, 64f)));
        }

        SceneFixtures.Body body = Rising(scene, 4f);

        MoveResult2D result = body.Mover.Move(new Vector2(0f, -Rise));

        Assert.Equal(StartX - 4f, body.Position.X, CollisionFixtures.Tolerance);
        Assert.Equal(StartY - Rise, body.Position.Y, CollisionFixtures.Tolerance);
        Assert.Equal(result.ContactCount, body.Mover.MoveContacts.Length);
        Assert.False(body.Mover.IsOnCeiling);
    }

    // A body jumping from a 45 degree slope walks up it first, and the ceiling 1 above stops that walk. The
    // rise then corrects past the same ceiling's edge, and the walk's stop on it stopped nothing in the end.
    [Fact]
    public void ACorrectionPastACeilingTheWalkAcrossMet_LeavesTheBodyOffTheCeiling()
    {
        Scene scene = new Scene().Started();
        scene.Add(new Block([new(0f, 64f), new(64f, 0f), new(64f, 64f)]));
        SceneFixtures.Body body = CollisionFixtures.Grounded(scene, new Vector2(20f, 10f), setUp: mover => mover.CeilingCornerCorrection = 4f);
        Vector2 start = body.Position;
        scene.Add(new Block(new Vector2(start.X + 6f, start.Y - 17f), new Vector2(64f, 16f)));

        body.Mover.Move(new Vector2(2f, -Rise));

        Assert.True(body.Position.Y < start.Y - Rise + 1f, $"the rise stopped at {body.Position}");
        Assert.True(body.Position.X + Edge <= start.X + 6f + CollisionFixtures.Tolerance, $"the body ended under the ceiling at {body.Position}");
        Assert.False(body.Mover.IsOnCeiling);
    }

    public enum Obstacle
    {
        Wall,
        Higher,
        OneWayHigher,
    }

    // A ceiling 16 deep whose underside runs from `left` to `right` at Underside.
    private static Block Ceiling(float left, float right) =>
        new(new Vector2(left, Underside - 16f), new Vector2(right - left, 16f));

    // A grounded 8x8 body with CeilingCornerCorrection set, its corner at (StartX, StartY), with no floor under it.
    private static SceneFixtures.Body Rising(Scene scene, float correction, Action<KinematicBody2D>? setUp = null)
    {
        SceneFixtures.Body body = new(new Vector2(StartX, StartY), blocksOn: CollisionFixtures.Solid);
        body.Mover.Mode = BodyMode.Grounded;
        body.Mover.CeilingCornerCorrection = correction;
        setUp?.Invoke(body.Mover);
        scene.Add(body);

        return body;
    }
}
