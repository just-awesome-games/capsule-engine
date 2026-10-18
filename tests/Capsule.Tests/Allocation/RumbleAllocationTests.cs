using Capsule.Input;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class RumbleAllocationTests
{
    // A step that plays, holds, retunes and stops pulses and the host's read of the level after it:
    // every public call, with the table full so every play evicts and every read walks all of it.
    [Fact]
    public void AStepThatDrivesEveryCallAndReadsTheLevel_AllocatesNothing()
    {
        Rumble rumble = new();
        InputState input = new(new ActionBindings());
        RumblePulse hit = new(0.8f, 0.5f, 0.25f) { LeftTrigger = 0.6f };

        Assert.Equal(0, StepMeasurement.Bytes(tick => Step(rumble, input, hit, tick), warmup: 100, measured: 1000));
        Assert.False(rumble.Level.IsZero);
    }

    private static void Step(Rumble rumble, InputState input, in RumblePulse hit, long tick)
    {
        rumble.BeginStep(new StepContext(StepMeasurement.StepSeconds, input, tick));
        rumble.Play(0.5f, 0.2f, 0.1f);
        rumble.Play(in hit);
        RumbleHandle held = rumble.Hold(0.2f, 0f);
        rumble.Set(held, 0.3f, 0.1f);
        rumble.Set(held, new RumbleLevel(0.3f, 0.1f, 0f, 0f));
        rumble.Stop(held);
        _ = rumble.Level;

        // Every tenth step clears the table. The loop also measures a refill.
        if (tick % 10 == 0)
        {
            rumble.Stop();
            rumble.Play(in hit);
        }
    }
}
