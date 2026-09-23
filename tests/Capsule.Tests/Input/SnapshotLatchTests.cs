using System.Numerics;
using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class SnapshotLatchTests
{
    [Fact]
    public void FramesThatDrainNoStep_AccumulateIntoTheNextStep()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Of(Key.Space));
        latch.Observe(DeviceSnapshot.Of(Key.W));
        latch.Observe(DeviceSnapshot.Of(Key.A));

        Assert.Equal(DeviceSnapshot.Of(Key.Space, Key.W, Key.A), latch.Consume());
    }

    [Fact]
    public void AKeyTappedBetweenSteps_IsHeldForOneStepThenUp()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Of(Key.Space));
        latch.Observe(DeviceSnapshot.Empty);

        Assert.True(latch.Consume().IsDown(Key.Space));
        Assert.True(latch.Consume().IsEmpty);
    }

    [Fact]
    public void SeveralStepsDrainedInOneFrame_SeeTheSameSnapshot()
    {
        SnapshotLatch latch = new();
        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);

        latch.Observe(down);

        Assert.Equal(down, latch.Consume());
        Assert.Equal(down, latch.Consume());
        Assert.Equal(down, latch.Consume());
    }

    [Fact]
    public void SeveralStepsDrainedInOneFrame_SpendTheWheelOnTheFirst()
    {
        SnapshotLatch latch = new();
        DeviceSnapshot flick = DeviceSnapshot.Of(Key.Space).WithScroll(new Vector2(0f, 3f));

        latch.Observe(flick);

        Assert.Equal(new Vector2(0f, 3f), latch.Consume().Scroll);

        DeviceSnapshot second = latch.Consume();
        Assert.Equal(Vector2.Zero, second.Scroll);
        Assert.True(second.IsDown(Key.Space));
    }

    [Fact]
    public void AWindowFocusLossBetweenSteps_SurvivesToTheNextStep()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Empty.WithWindowFocus(false));
        latch.Observe(DeviceSnapshot.Empty);

        Assert.False(latch.Consume().HasWindowFocus);
        Assert.True(latch.Consume().HasWindowFocus);
    }

    [Fact]
    public void AReleaseObservedAfterAStep_LandsOnTheFollowingStep()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Of(Key.Space));
        latch.Consume();

        latch.Observe(DeviceSnapshot.Empty);

        Assert.True(latch.Consume().IsEmpty);
    }

    [Fact]
    public void AnAxis_TakesTheLatestObservedPositionRatherThanTheExtreme()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 1f));
        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 0.25f));
        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -0.5f));

        Assert.Equal(-0.5f, latch.Consume().Axis(PadAxis.LeftStickX), 1e-6f);
    }
}
