using Capsule.Animation;

namespace Capsule.Tests.Animation;

public sealed class TweenTests
{
    private const float Tolerance = 1e-6f;

    // The tick contract: Start is tick 0 of n, and the nth step is the end, so an owner writing
    // Value every step writes the from-state once and the to-state once.
    [Fact]
    public void ALinearRun_WalksTheTicksItWasStartedOn()
    {
        const int Ticks = 4;
        Tween tween = default;
        tween.Start(Ticks);

        Assert.Equal(0f, tween.Value);
        Assert.True(tween.IsRunning);

        for (int tick = 1; tick <= Ticks; tick++)
        {
            tween.Step();

            Assert.Equal(tick / (float)Ticks, tween.Value);
            Assert.Equal(tick == Ticks, tween.IsFinished);
            Assert.Equal(tick == Ticks, tween.JustFinished);
        }

        Assert.False(tween.IsRunning);
    }

    [Fact]
    public void AFinishedRun_IsUnmovedByFurtherSteps()
    {
        Tween tween = default;
        tween.Start(1);
        tween.Step();

        tween.Step();

        Assert.Equal(1, tween.TicksElapsed);
        Assert.Equal(1f, tween.Value);
        Assert.True(tween.IsFinished);

        // The edge is spent: an owner stepping past its finish runs the finish branch once.
        Assert.False(tween.JustFinished);
    }

    // The countdown case: a run cancelled mid-flight reports no finish on the step that would have
    // ended it, and keeps its duration for a restart.
    [Fact]
    public void AStoppedRun_ReportsNoFinishAndLeavesItsDurationToRestartOn()
    {
        Tween tween = default;
        tween.Start(2);
        tween.Step();

        tween.Stop();
        tween.Step();

        Assert.False(tween.JustFinished);
        Assert.False(tween.IsRunning);
        Assert.Equal(0, tween.TicksLeft);
        Assert.Equal(2, tween.Duration);

        tween.Start(3);

        Assert.Equal(3, tween.TicksLeft);
        Assert.True(tween.IsRunning);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(9)]
    public void SeekingToATick_LandsWhereThatManyStepsWould(int tick)
    {
        Tween stepped = default;
        stepped.Start(5, Ease.InOutBack);
        for (int step = 0; step < tick; step++)
        {
            stepped.Step();
        }

        Tween seeked = default;
        seeked.Start(5, Ease.InOutBack);
        seeked.Seek(tick);

        Assert.Equal(stepped.TicksElapsed, seeked.TicksElapsed);
        Assert.Equal(stepped.Value, seeked.Value);
        Assert.Equal(stepped.IsFinished, seeked.IsFinished);

        // Seeking onto the end is not the step that finished it, whatever stepping there would say.
        Assert.False(seeked.JustFinished);
    }

    [Fact]
    public void ATweenThatHasNeverStarted_HasNoRunToSeekAndNoValueToRead()
    {
        Tween tween = default;

        Assert.Equal(0f, tween.Value);
        Assert.False(tween.IsRunning);
        Assert.Throws<InvalidOperationException>(() => tween.Seek(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => tween.Start(0));
    }

    [Fact]
    public void EveryCurve_LandsExactlyOnItsEndpoints()
    {
        foreach (Ease ease in Enum.GetValues<Ease>())
        {
            Tween tween = default;
            tween.Start(3, ease);

            Assert.Equal(0f, tween.Value);

            tween.Seek(3);

            Assert.Equal(1f, tween.Value);
            Assert.Equal(0f, Easing.Apply(ease, 0f));
            Assert.Equal(1f, Easing.Apply(ease, 1f));

            // Out of range on either side is the endpoint it passed.
            Assert.Equal(0f, Easing.Apply(ease, -2f));
            Assert.Equal(1f, Easing.Apply(ease, 4f));
        }
    }

    // The families are authored in one direction; the other two are reflections of it, and a family
    // whose Out drifted from its In would read as a different curve depending on the direction.
    [Fact]
    public void EveryFamilysOutCurve_IsItsInCurveReflected()
    {
        foreach (Ease outward in Enum.GetValues<Ease>())
        {
            string name = outward.ToString();
            if (!name.StartsWith("Out", StringComparison.Ordinal))
            {
                continue;
            }

            Ease inward = Enum.Parse<Ease>("In" + name[3..]);
            for (float t = 0.05f; t < 1f; t += 0.05f)
            {
                float reflected = 1f - Easing.Apply(inward, 1f - t);

                Assert.True(
                    MathF.Abs(Easing.Apply(outward, t) - reflected) < Tolerance,
                    $"{outward} at {t} is {Easing.Apply(outward, t)}, not the reflected {reflected}.");
            }
        }
    }

    // A sawtooth: the tick that would read 1 is the 0 the next pass opens on, so a repeating fade
    // never flashes its end state between passes.
    [Fact]
    public void ARepeatingRun_WrapsToItsStartOnTheTickThatWouldEndIt()
    {
        Tween tween = default;
        tween.Start(4, Ease.Linear, TweenLoop.Repeat);

        List<float> values = [tween.Value];
        List<bool> edges = [];
        for (int step = 0; step < 5; step++)
        {
            tween.Step();
            values.Add(tween.Value);
            edges.Add(tween.JustFinished);
        }

        Assert.Equal([0f, 0.25f, 0.5f, 0.75f, 0f, 0.25f], values);
        Assert.Equal([false, false, false, true, false], edges);
        Assert.Equal(1, tween.Passes);
        Assert.False(tween.IsFinished);
        Assert.True(tween.IsRunning);
    }

    // A triangle: out over one duration and home over the next, ending a pass at each end.
    [Fact]
    public void APingPongRun_SwingsBackFromItsEndAndCountsBothEnds()
    {
        Tween tween = default;
        tween.Start(4, Ease.Linear, TweenLoop.PingPong);

        List<float> values = [tween.Value];
        List<bool> edges = [];
        List<int> passes = [];
        for (int step = 0; step < 8; step++)
        {
            tween.Step();
            values.Add(tween.Value);
            edges.Add(tween.JustFinished);
            passes.Add(tween.Passes);
        }

        Assert.Equal([0f, 0.25f, 0.5f, 0.75f, 1f, 0.75f, 0.5f, 0.25f, 0f], values);
        Assert.Equal([false, false, false, true, false, false, false, true], edges);
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, 2], passes);
        Assert.False(tween.IsFinished);
    }

    [Theory]
    [InlineData(TweenLoop.Repeat)]
    [InlineData(TweenLoop.PingPong)]
    public void SeekingALoopingRun_LandsWhereThatManyStepsWould(TweenLoop loop)
    {
        // Past one period on both modes, so the wrap is crossed rather than approached.
        for (int tick = 0; tick <= 11; tick++)
        {
            Tween stepped = default;
            stepped.Start(4, Ease.InOutSine, loop);
            for (int step = 0; step < tick; step++)
            {
                stepped.Step();
            }

            Tween seeked = default;
            seeked.Start(4, Ease.InOutSine, loop);
            seeked.Seek(tick);

            Assert.Equal(stepped.TicksElapsed, seeked.TicksElapsed);
            Assert.Equal(stepped.Value, seeked.Value);
            Assert.Equal(stepped.Passes, seeked.Passes);
            Assert.False(seeked.JustFinished);
        }
    }

    [Fact]
    public void ALoopModeTheEnumDoesNotDeclare_IsRefused()
    {
        Tween tween = default;

        Assert.Throws<ArgumentOutOfRangeException>(() => tween.Start(4, Ease.Linear, (TweenLoop)9));
    }

    // The reflections only say the three directions of a family agree; what they are is pinned here,
    // against the published formulas transcribed below through the platform's own transcendental
    // functions. A test is not simulation code, so this is also what holds Capsule's deterministic
    // sine and exponential to the real ones through every curve that calls them.
    [Theory]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(0.95f)]
    [InlineData(0.98f)]
    public void EveryCurve_IsThePublishedOneInsideItsRun(float t)
    {
        foreach (Ease ease in Enum.GetValues<Ease>())
        {
            float published = Published(ease, t);

            Assert.True(
                MathF.Abs(Easing.Apply(ease, t) - published) < 1e-4f,
                $"{ease} at {t} is {Easing.Apply(ease, t)}, not the published {published}.");
        }
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(float.NaN)]
    public void ACurveTheEnumDoesNotDeclare_IsRefusedAtEveryProgress(float t)
    {
        // The endpoints are literal, so a curve that is refused between them must be refused on them
        // too rather than answering 0 or 1 for something that does not exist.
        Assert.Throws<ArgumentOutOfRangeException>(() => Easing.Apply((Ease)999, t));
        Assert.Throws<ArgumentOutOfRangeException>(() => Easing.Apply((Ease)(-1), t));
    }

    // easings.net, transcribed: each curve written out in its own right rather than derived from
    // another, so a family whose directions agree with each other but not with the curve it claims
    // to be still fails.
    private static float Published(Ease ease, float x)
    {
        const float C1 = 1.70158f;
        const float C2 = C1 * 1.525f;
        const float C3 = C1 + 1f;
        const float C4 = 2f * MathF.PI / 3f;
        const float C5 = 2f * MathF.PI / 4.5f;

        return ease switch
        {
            Ease.Linear => x,

            Ease.InSine => 1f - MathF.Cos(x * MathF.PI / 2f),
            Ease.OutSine => MathF.Sin(x * MathF.PI / 2f),
            Ease.InOutSine => -(MathF.Cos(MathF.PI * x) - 1f) / 2f,

            Ease.InQuad => x * x,
            Ease.OutQuad => 1f - MathF.Pow(1f - x, 2f),
            Ease.InOutQuad => x < 0.5f ? 2f * x * x : 1f - (MathF.Pow((-2f * x) + 2f, 2f) / 2f),

            Ease.InCubic => MathF.Pow(x, 3f),
            Ease.OutCubic => 1f - MathF.Pow(1f - x, 3f),
            Ease.InOutCubic => x < 0.5f ? 4f * x * x * x : 1f - (MathF.Pow((-2f * x) + 2f, 3f) / 2f),

            Ease.InQuart => MathF.Pow(x, 4f),
            Ease.OutQuart => 1f - MathF.Pow(1f - x, 4f),
            Ease.InOutQuart => x < 0.5f ? 8f * x * x * x * x : 1f - (MathF.Pow((-2f * x) + 2f, 4f) / 2f),

            Ease.InQuint => MathF.Pow(x, 5f),
            Ease.OutQuint => 1f - MathF.Pow(1f - x, 5f),
            Ease.InOutQuint => x < 0.5f ? 16f * MathF.Pow(x, 5f) : 1f - (MathF.Pow((-2f * x) + 2f, 5f) / 2f),

            Ease.InExpo => MathF.Pow(2f, (10f * x) - 10f),
            Ease.OutExpo => 1f - MathF.Pow(2f, -10f * x),
            Ease.InOutExpo => x < 0.5f
                ? MathF.Pow(2f, (20f * x) - 10f) / 2f
                : (2f - MathF.Pow(2f, (-20f * x) + 10f)) / 2f,

            Ease.InCirc => 1f - MathF.Sqrt(1f - MathF.Pow(x, 2f)),
            Ease.OutCirc => MathF.Sqrt(1f - MathF.Pow(x - 1f, 2f)),
            Ease.InOutCirc => x < 0.5f
                ? (1f - MathF.Sqrt(1f - MathF.Pow(2f * x, 2f))) / 2f
                : (MathF.Sqrt(1f - MathF.Pow((-2f * x) + 2f, 2f)) + 1f) / 2f,

            Ease.InBack => (C3 * x * x * x) - (C1 * x * x),
            Ease.OutBack => 1f + (C3 * MathF.Pow(x - 1f, 3f)) + (C1 * MathF.Pow(x - 1f, 2f)),
            Ease.InOutBack => x < 0.5f
                ? MathF.Pow(2f * x, 2f) * (((C2 + 1f) * 2f * x) - C2) / 2f
                : ((MathF.Pow((2f * x) - 2f, 2f) * (((C2 + 1f) * ((x * 2f) - 2f)) + C2)) + 2f) / 2f,

            Ease.InElastic => -MathF.Pow(2f, (10f * x) - 10f) * MathF.Sin(((x * 10f) - 10.75f) * C4),
            Ease.OutElastic => (MathF.Pow(2f, -10f * x) * MathF.Sin(((x * 10f) - 0.75f) * C4)) + 1f,
            Ease.InOutElastic => x < 0.5f
                ? -(MathF.Pow(2f, (20f * x) - 10f) * MathF.Sin(((20f * x) - 11.125f) * C5)) / 2f
                : ((MathF.Pow(2f, (-20f * x) + 10f) * MathF.Sin(((20f * x) - 11.125f) * C5)) / 2f) + 1f,

            Ease.InBounce => 1f - PublishedOutBounce(1f - x),
            Ease.OutBounce => PublishedOutBounce(x),
            Ease.InOutBounce => x < 0.5f
                ? (1f - PublishedOutBounce(1f - (2f * x))) / 2f
                : (1f + PublishedOutBounce((2f * x) - 1f)) / 2f,

            _ => throw new ArgumentOutOfRangeException(nameof(ease), ease, "No published formula."),
        };
    }

    private static float PublishedOutBounce(float x)
    {
        const float N1 = 7.5625f;
        const float D1 = 2.75f;

        if (x < 1f / D1)
        {
            return N1 * x * x;
        }

        if (x < 2f / D1)
        {
            float second = x - (1.5f / D1);

            return (N1 * second * second) + 0.75f;
        }

        if (x < 2.5f / D1)
        {
            float third = x - (2.25f / D1);

            return (N1 * third * third) + 0.9375f;
        }

        float fourth = x - (2.625f / D1);

        return (N1 * fourth * fourth) + 0.984375f;
    }
}
