using Capsule.Input;

namespace Capsule.Tests.Input;

public sealed class SnapshotLatchTests
{
    private static readonly InputAction Jump = new("Jump");

    [Fact]
    public void AFreshLatch_ConsumesNothingHeld()
    {
        SnapshotLatch latch = new();

        Assert.True(latch.ConsumeStepSnapshot().IsEmpty);
    }

    [Fact]
    public void AnObservedFrame_IsWhatTheNextStepConsumes()
    {
        SnapshotLatch latch = new();

        latch.Observe(DeviceSnapshot.Of(Key.Space));

        Assert.Equal(DeviceSnapshot.Of(Key.Space), latch.ConsumeStepSnapshot());
    }

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
    public void AHeldKey_StaysDownAcrossEveryStep()
    {
        SnapshotLatch latch = new();
        DeviceSnapshot down = DeviceSnapshot.Of(Key.Space);

        for (int step = 0; step < 4; step++)
        {
            latch.Observe(down);

            Assert.Equal(down, latch.ConsumeStepSnapshot());
        }
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
