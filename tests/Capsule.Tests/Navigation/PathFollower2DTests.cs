using System.Numerics;
using Capsule.Navigation;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Navigation;

public sealed class PathFollower2DTests
{
    // Sixty units a second is one unit a default step.
    private const float UnitAStep = 60f;
    private const float Tolerance = 1e-3f;

    private static readonly Vector2 Origin = new(100f, 50f);
    private static readonly Path2D Track = new(Vector2.Zero, new Vector2(10f, 0f));

    // The path is relative to where the entity stands as the follower starts.
    [Fact]
    public void AFollower_TravelsItsPathFromWhereItsEntityStarted_AndOnceHoldsTheEnd()
    {
        PathFollower2D follower = new(Track) { Speed = UnitAStep };
        SceneFixtures.Holder holder = new(Origin, follower);
        using SimulationHost run = SceneFixtures.Simulate(holder);

        run.Step(4);

        Assert.Equal(Origin.X + 4f, holder.Position.X, Tolerance);
        Assert.Equal(Origin.Y, holder.Position.Y);
        Assert.False(follower.IsFinished);

        run.Step(10);

        Assert.Equal(Origin + new Vector2(10f, 0f), holder.Position);
        Assert.True(follower.IsFinished);
    }

    [Theory]
    [InlineData(LoopMode.Repeat, UnitAStep, 0f, 13, 3f)]
    [InlineData(LoopMode.Repeat, -UnitAStep, 0f, 3, 7f)]
    [InlineData(LoopMode.PingPong, UnitAStep, 0f, 13, 7f)]
    [InlineData(LoopMode.PingPong, UnitAStep, 0f, 23, 3f)]
    [InlineData(LoopMode.PingPong, -UnitAStep, 10f, 13, 3f)]
    [InlineData(LoopMode.Once, -UnitAStep, 10f, 13, 0f)]
    public void EachLoopMode_TurnsAtTheEndsAsItSays(LoopMode loop, float speed, float offset, int steps, float expected)
    {
        PathFollower2D follower = new(Track) { Speed = speed, Loop = loop, Offset = offset };
        using SimulationHost run = SceneFixtures.Simulate(new SceneFixtures.Holder(Vector2.Zero, follower));

        run.Step(steps);

        Assert.Equal(expected, follower.Offset, Tolerance);
        Assert.Equal(expected, follower.Entity!.Position.X, Tolerance);
    }

    // A closed path ends where it starts and wraps in place. An open one jumps, and the jump draws no motion.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ARepeatPastTheLastPoint_JumpsWithNoMotionDrawnOnlyOnAnOpenPath(bool closed, bool jumps)
    {
        Path2D path = closed ? new(Vector2.Zero, new Vector2(10f, 0f), Vector2.Zero) : Track;
        PathFollower2D follower = new(path) { Speed = UnitAStep, Loop = LoopMode.Repeat, Offset = path.Length - 0.5f };
        SceneFixtures.Holder holder = new(Vector2.Zero, follower);
        using SimulationHost run = SceneFixtures.Simulate(holder);

        run.Step();

        Assert.Equal(jumps, holder.PreviousTransform.Position == holder.Position);
    }

    [Fact]
    public void WritingTheOffset_PlacesTheEntityAtOnce_ClampedToThePath()
    {
        PathFollower2D follower = new(Track);
        SceneFixtures.Holder holder = new(Origin, follower);
        using SimulationHost run = SceneFixtures.Simulate(holder);

        follower.Offset = 4f;

        Assert.Equal(Origin + new Vector2(4f, 0f), holder.Position);

        follower.Offset = 99f;

        Assert.Equal(10f, follower.Offset);
        Assert.Equal(Origin + new Vector2(10f, 0f), holder.Position);
    }

    // A component keeps its state while its entity is out of a scene.
    [Fact]
    public void AnEntityRejoiningAScene_CarriesOnFromWhereItLeftThePath()
    {
        PathFollower2D follower = new(Track) { Speed = UnitAStep };
        SceneFixtures.Holder holder = new(Origin, follower);
        using SimulationHost run = SceneFixtures.Simulate(holder);
        run.Step(5);
        float offset = follower.Offset;

        run.Scene.Remove(holder);
        run.Scene.Add(holder);
        run.Step();

        Assert.Equal(offset + 1f, follower.Offset, Tolerance);
        Assert.Equal(Origin + Track.PointAt(offset + 1f), holder.Position);
    }

    [Fact]
    public void ADetachedFollower_TakesAnOffsetWithoutMovingItsFormerEntity()
    {
        PathFollower2D follower = new(Track) { Speed = UnitAStep };
        SceneFixtures.Holder holder = new(Origin, follower);
        using SimulationHost run = SceneFixtures.Simulate(holder);
        run.Step();
        holder.Remove(follower);
        Vector2 left = holder.Position;

        follower.Offset = 5f;

        Assert.Equal(5f, follower.Offset);
        Assert.Equal(left, holder.Position);
    }
}
