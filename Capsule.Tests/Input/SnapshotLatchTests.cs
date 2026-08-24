using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class SnapshotLatchTests
{
    private static readonly InputAction Jump = new("Jump");

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
        // The frame samples once; the extra steps must not invent a release.
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
        // A stick is a level, not an edge: the step wants where it is now, not where
        // it passed through since the last one.
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 1f));
        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, 0.25f));
        latch.Observe(DeviceSnapshot.Empty.WithAxis(PadAxis.LeftStickX, -0.5f));

        Assert.Equal(-0.5f, latch.ConsumeStepSnapshot().Axis(PadAxis.LeftStickX), 1e-6f);
    }

    [Fact]
    public void ATapBetweenSteps_ReachesTheSimulationAsOnePressAndOneRelease()
    {
        InputState input = new(new ActionBindings().Bind(Jump, Key.Space));
        SnapshotLatch latch = new();
        int presses = 0;
        int releases = 0;

        // Three frames at a render rate above the step rate, only the last of which
        // drains steps: without the latch the tap would be gone by then.
        latch.Observe(DeviceSnapshot.Of(Key.Space));
        latch.Observe(DeviceSnapshot.Empty);
        latch.Observe(DeviceSnapshot.Empty);

        for (int step = 0; step < 2; step++)
        {
            input.Advance(latch.ConsumeStepSnapshot());
            presses += input.WasPressed(Jump) ? 1 : 0;
            releases += input.WasReleased(Jump) ? 1 : 0;
        }

        Assert.Equal(1, presses);
        Assert.Equal(1, releases);
        Assert.False(input.IsHeld(Jump));
    }
}
