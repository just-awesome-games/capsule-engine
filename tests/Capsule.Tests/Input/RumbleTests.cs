using Capsule.Input;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Input;

public sealed class RumbleTests
{
    private const float Tolerance = 1e-5f;

    // A tenth of a second: six steps at the run's default rate.
    private const float Tenth = 0.1f;
    private const long TenthEnds = 6;

    [Fact]
    public void ATimedPulse_EndsAfterTheStepsItsSecondsImply()
    {
        Rumble rumble = new();
        RumbleHandle pulse = rumble.Play(1f, 0f, Tenth);

        Advance(rumble, TenthEnds - 1);
        Assert.True(rumble.IsLive(pulse));
        Assert.True(rumble.Level.Low > 0f);

        Advance(rumble, TenthEnds);
        Assert.False(rumble.IsLive(pulse));
        Assert.Equal(RumbleLevel.Zero, rumble.Level);
    }

    [Fact]
    public void ADecayingPulse_ReadsFullOnTheStepItIsPlayedAndFallsLinearly()
    {
        Rumble rumble = new();
        Advance(rumble, 10);

        rumble.Play(1f, 0.5f, Tenth);
        Assert.Equal(1f, rumble.Level.Low, Tolerance);
        Assert.Equal(0.5f, rumble.Level.High, Tolerance);

        Advance(rumble, 13);
        Assert.Equal(0.5f, rumble.Level.Low, Tolerance);
        Assert.Equal(0.25f, rumble.Level.High, Tolerance);
    }

    [Fact]
    public void AnUnfadedPulse_HoldsFullThenCuts()
    {
        Rumble rumble = new();
        rumble.Play(new RumblePulse(1f, 0f, Tenth) { Fade = RumbleFade.None });

        Advance(rumble, TenthEnds - 1);
        Assert.Equal(1f, rumble.Level.Low);

        Advance(rumble, TenthEnds);
        Assert.Equal(0f, rumble.Level.Low);
    }

    [Fact]
    public void TheLevel_IsThePerMotorMaximumAndASmallerPulseOutlivesALouderOne()
    {
        Rumble rumble = new();
        rumble.Play(new RumblePulse(1f, 0f, Tenth) { Fade = RumbleFade.None });
        rumble.Play(new RumblePulse(0.3f, 0.6f, 1f) { Fade = RumbleFade.None, RightTrigger = 0.2f });

        Assert.Equal(new RumbleLevel(1f, 0.6f, 0f, 0.2f), rumble.Level);

        Advance(rumble, TenthEnds);
        Assert.Equal(new RumbleLevel(0.3f, 0.6f, 0f, 0.2f), rumble.Level);
    }

    [Fact]
    public void AHold_OutlivesAnyNumberOfStepsUntilStopped()
    {
        Rumble rumble = new();
        RumbleHandle held = rumble.Hold(0.4f, 0.2f);

        Advance(rumble, 100_000);
        Assert.True(rumble.IsLive(held));
        Assert.Equal(new RumbleLevel(0.4f, 0.2f, 0f, 0f), rumble.Level);

        rumble.Stop(held);
        Assert.False(rumble.IsLive(held));
        Assert.Equal(RumbleLevel.Zero, rumble.Level);
    }

    [Fact]
    public void Set_RetunesAPulseWithoutRestartingItsClock()
    {
        Rumble rumble = new();
        RumbleHandle pulse = rumble.Play(new RumblePulse(1f, 0f, Tenth) { Fade = RumbleFade.None });

        Advance(rumble, 3);
        rumble.Set(pulse, new RumbleLevel(0.5f, 0.5f, 0.25f, 0f));

        Advance(rumble, TenthEnds - 1);
        Assert.Equal(new RumbleLevel(0.5f, 0.5f, 0.25f, 0f), rumble.Level);

        Advance(rumble, TenthEnds);
        Assert.False(rumble.IsLive(pulse));
    }

    [Fact]
    public void Volume_ScalesTheLevelAndZeroSilencesWithoutEndingAnything()
    {
        Rumble rumble = new();
        RumbleHandle held = rumble.Hold(1f, 0.5f);

        rumble.Volume = 0.5f;
        Assert.Equal(new RumbleLevel(0.5f, 0.25f, 0f, 0f), rumble.Level);

        rumble.Volume = 0f;
        Assert.True(rumble.Level.IsZero);
        Assert.True(rumble.IsLive(held));
    }

    [Fact]
    public void StopWithNoHandle_EndsEveryPulse()
    {
        Rumble rumble = new();
        RumbleHandle held = rumble.Hold(1f, 1f);
        RumbleHandle timed = rumble.Play(1f, 1f, 1f);

        rumble.Stop();

        Assert.False(rumble.IsLive(held));
        Assert.False(rumble.IsLive(timed));
        Assert.Equal(RumbleLevel.Zero, rumble.Level);
    }

    [Fact]
    public void AHandleToAnEndedPulse_IsIgnored()
    {
        Rumble rumble = new();
        RumbleHandle pulse = rumble.Play(1f, 1f, Tenth);
        Advance(rumble, TenthEnds);

        // The freed slot is reused, and the stale handle must not reach the pulse living there now.
        RumbleHandle next = rumble.Hold(0.2f, 0f);
        rumble.Set(pulse, 1f, 1f);
        rumble.Stop(pulse);
        rumble.Set(RumbleHandle.None, 1f, 1f);

        Assert.False(rumble.IsLive(pulse));
        Assert.True(rumble.IsLive(next));
        Assert.Equal(new RumbleLevel(0.2f, 0f, 0f, 0f), rumble.Level);
    }

    [Fact]
    public void WithNoSlotFree_ThePulseWithTheLowestPeakIsEvicted()
    {
        Rumble rumble = new();
        RumbleHandle[] loud = new RumbleHandle[Rumble.MaxPulses - 1];
        for (int i = 0; i < loud.Length; i++)
        {
            loud[i] = rumble.Hold(0.5f, 0f);
        }

        RumbleHandle quiet = rumble.Hold(0f, 0.1f);
        RumbleHandle latest = rumble.Play(0.9f, 0f, 1f);

        Assert.False(latest.IsNone);
        Assert.False(rumble.IsLive(quiet));
        Assert.True(rumble.IsLive(latest));
        Assert.All(loud, handle => Assert.True(rumble.IsLive(handle)));
    }

    // Four steps at 60 Hz spend a fifteenth of a second, the change arriving on the fourth. At 120 Hz
    // the remaining thirtieth is four more steps, so the pulse ends on tick 8 and not on the tick 6 a
    // plain tick count would give.
    [Fact]
    public void AChangedStepLength_KeepsAPulsesRemainingSeconds()
    {
        Rumble rumble = new();
        RumbleHandle pulse = rumble.Play(new RumblePulse(1f, 0f, Tenth) { Fade = RumbleFade.None });

        Advance(rumble, 3);
        Advance(rumble, 4, stepHertz: 120);
        Advance(rumble, 7, stepHertz: 120);
        Assert.True(rumble.IsLive(pulse));

        Advance(rumble, 8, stepHertz: 120);
        Assert.False(rumble.IsLive(pulse));
    }

    [Theory]
    [InlineData(1.5f, 0f, 0.1f)]
    [InlineData(0f, float.NaN, 0.1f)]
    [InlineData(0f, 0f, 0f)]
    [InlineData(0f, 0f, float.PositiveInfinity)]
    public void AnAmplitudeOutsideTheUnitRangeOrANonPositiveDuration_IsRefused(float low, float high, float seconds)
    {
        Rumble rumble = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => rumble.Play(low, high, seconds));
        Assert.Throws<ArgumentOutOfRangeException>(() => rumble.Play(new RumblePulse(low, high, seconds)));
    }

    private static void Advance(Rumble rumble, long tick) => rumble.BeginStep(SceneFixtures.Step(tick));

    private static void Advance(Rumble rumble, long tick, int stepHertz) =>
        rumble.BeginStep(new StepContext(1.0 / stepHertz, new InputState(new ActionBindings()), tick));
}
