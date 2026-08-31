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

        Assert.Equal(DeviceSnapshot.Of(Key.Space, Key.W, Key.A), latch.ConsumeStepSnapshot());
    }

    [Fact]
    public void AKeyTappedBetweenSteps_IsHeldForOneStepThenUp()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Of(Key.Space));
        latch.Observe(DeviceSnapshot.Empty);

        Assert.True(latch.ConsumeStepSnapshot().IsDown(Key.Space));
        Assert.True(latch.ConsumeStepSnapshot().IsEmpty);
    }

    [Fact]
    public void SeveralStepsDrainedInOneFrame_SeeTheSameSnapshot()
    {
        SnapshotLatch latch = new();
        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);

        latch.Observe(down);

        Assert.Equal(down, latch.ConsumeStepSnapshot());
        Assert.Equal(down, latch.ConsumeStepSnapshot());
        Assert.Equal(down, latch.ConsumeStepSnapshot());
    }

    [Fact]
    public void AReleaseObservedAfterAStep_LandsOnTheFollowingStep()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Of(Key.Space));
        latch.ConsumeStepSnapshot();

        latch.Observe(DeviceSnapshot.Empty);

        Assert.True(latch.ConsumeStepSnapshot().IsEmpty);
    }

    [Fact]
    public void AnAxis_TakesTheLatestObservedPositionRatherThanTheExtreme()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 1f));
        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 0.25f));
        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -0.5f));

        Assert.Equal(-0.5f, latch.ConsumeStepSnapshot().Axis(PadAxis.LeftStickX), 1e-6f);
    }
}
