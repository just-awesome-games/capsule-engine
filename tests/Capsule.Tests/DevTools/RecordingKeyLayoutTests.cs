using Capsule.Input;
using Capsule.Runtime;
using Capsule.Tests.Input;
using static Capsule.Tests.DevTools.RecordingFixtures;

namespace Capsule.Tests.DevTools;

// A keyboard layout change is input: the run reads it from the top of a step, the recording holds it
// at that step, and a replay reads the same characters on the same ticks.
public sealed class RecordingKeyLayoutTests
{
    private const int Steps = 12;

    // The change lands before the step of tick 5. The recording stores it there, and a replay of the saved
    // run reads the US characters before it and AZERTY's from it, as the played run did.
    [Fact]
    public void AKeyLayoutChangedMidRun_IsRecordedAndReplaysFromTheSameTick()
    {
        List<string?> played = [];
        using RecordedRun run = new(Builder(() => new Reading { Characters = played }));
        for (int step = 0; step < Steps; step++)
        {
            if (step == 5)
            {
                run.Scheduler.ChangeKeyLayout(InputFixtures.Azerty());
            }

            run.Step(DeviceSnapshot.Empty);
        }

        string?[] expected = [.. Enumerable.Repeat("W", 5), .. Enumerable.Repeat("Z", Steps - 5)];
        Recording saved = run.Saved();
        List<string?> replayed = [];

        HeadlessRunResult result = Builder(() => new Reading { Characters = replayed }).RunHeadless(saved);

        Assert.Null(saved.Header.KeyLayout);
        Assert.Equal(expected, played);
        Assert.Equal(expected, replayed);
        Assert.Null(result.DivergedAt);
    }

    // A change reported inside a step waits for the next step. No step reads two layouts.
    [Fact]
    public void AKeyLayoutChangedDuringAStep_AppliesFromTheNextStep()
    {
        List<string?> seen = [];
        RecordedRun? run = null;
        using RecordedRun created = new(Builder(() => new Reading
        {
            Characters = seen,
            AtStep = tick =>
            {
                if (tick == 3)
                {
                    run!.Scheduler.ChangeKeyLayout(InputFixtures.Azerty());
                }
            },
        }));
        run = created;

        for (int step = 0; step < 6; step++)
        {
            run.Step(DeviceSnapshot.Empty);
        }

        Assert.Equal(["W", "W", "W", "W", "Z", "Z"], seen);
    }
}
