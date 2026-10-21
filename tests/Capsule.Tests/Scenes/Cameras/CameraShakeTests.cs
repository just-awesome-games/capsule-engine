using System.Numerics;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

// The shake, read back through the settled region the frame draws. A pinned camera frames OneScreen. A
// region's position is the shake's offset.
public sealed class CameraShakeTests
{
    private static readonly Rect OneScreen = SceneFixtures.OneScreen;

    // A quarter-second shake spans 15 steps. The step after them draws unshaken.
    [Fact]
    public void AShake_OffsetsAViewPinnedToItsBounds_AndEndsExactlyUnshakenAfterItsSeconds()
    {
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
        ulong draws = simulation.Run.Random.DrawCount;
        camera.Shake(1f, 0.25f);

        Rect[] regions = Steps(simulation, camera, 16);

        Assert.NotEqual(OneScreen, regions[0]);
        Assert.NotEqual(OneScreen, regions[14]);
        Assert.Equal(OneScreen, regions[15]);
        Assert.Equal(draws, simulation.Run.Random.DrawCount);
    }

    [Fact]
    public void TwoIdenticalShakes_SettleIdenticalRegions()
    {
        static Rect[] Shaken()
        {
            (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
            camera.Shake(0.8f);

            return Steps(simulation, camera, 40);
        }

        Assert.Equal(Shaken(), Shaken());
    }

    // The noise phase runs whether or not the camera shakes. Runs that differ only in their calls
    // compare step for step.
    [Fact]
    public void AWeakShake_ChangesNothingUnderAStrongOne_AndTakesOverAtItsOwnIntensityOnceTheStrongOneFades()
    {
        static Rect Shaken(float? strong, int after, float? weak)
        {
            (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
            if (strong is { } first)
            {
                camera.Shake(first);
            }

            Steps(simulation, camera, after);
            if (weak is { } second)
            {
                camera.Shake(second);
            }

            return Steps(simulation, camera, 1)[0];
        }

        Assert.Equal(Shaken(1f, 2, null), Shaken(1f, 2, 0.2f));
        Assert.Equal(Shaken(null, 25, 0.2f), Shaken(1f, 25, 0.2f));
    }

    // The offsets the shorthand drew before shake profiles existed, bit for bit. The second run changes
    // the camera-wide amplitude mid-shake, which the shorthand reads live.
    [Fact]
    public void TheShorthand_DrawsTheOffsetsItDrewBeforeProfiles()
    {
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
        camera.Shake(1f, 0.25f);
        Rect[] regions = Steps(simulation, camera, 15);

        Assert.Equal(new Vector2(-6.4704437f, 6.8266983f), regions[0].Position);
        Assert.Equal(new Vector2(1.4763184f, 0.49692535f), regions[7].Position);
        Assert.Equal(new Vector2(-0.0011291504f, -0.00920105f), regions[14].Position);

        (simulation, camera) = SceneFixtures.Pinned();
        camera.Shake(0.9f, 0.4f);
        Steps(simulation, camera, 5);
        camera.ShakeAmplitude = new Vector2(3f, 12f);

        Assert.Equal(new Vector2(0.7381439f, 2.9030533f), Steps(simulation, camera, 1)[0].Position);
    }

    // A cosine at half the step rate lands on its peak every step. Nine steps leave the envelope a
    // rounding remainder that would otherwise hold a tenth.
    [Fact]
    public void ASineAtHalfTheStepRate_AlternatesItsAmplitudeEveryStep_AndEndsExactlyUnshakenAfterItsSeconds()
    {
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
        camera.Shake(new CameraShake(new Vector2(0f, 4f), 9f / 60f) { Wave = ShakeWave.Sine, Frequency = 30f, Sustain = 1f });

        Rect[] regions = Steps(simulation, camera, 10);

        for (int step = 0; step < 9; step++)
        {
            Assert.Equal(new Vector2(0f, step % 2 == 0 ? 4f : -4f), regions[step].Position);
        }

        Assert.Equal(OneScreen, regions[9]);
    }

    // A shake held to its end draws every step that starts inside its seconds, at full strength, and
    // nothing after. 0.14 seconds is 8.4 steps, and its ninth step starts inside it. A minute holds
    // 3600 steps, where a subtracted envelope would drift by several.
    [Theory]
    [InlineData(60, 9f / 60f, 9)]
    [InlineData(60, 0.14f, 9)]
    [InlineData(60, 60f, 3600)]
    [InlineData(144, 0.5f, 72)]
    public void AHeldShake_DrawsEachStepStartingInsideItsSeconds_AndThenEndsExactlyUnshaken(int hertz, float seconds, int samples)
    {
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
        camera.Shake(new CameraShake(new Vector2(0f, 4f), seconds) { Wave = ShakeWave.Sine, Frequency = 0f, Sustain = 1f });

        Rect[] regions = Steps(simulation, camera, samples + 1, 1.0 / hertz);

        for (int step = 0; step < samples; step++)
        {
            Assert.Equal(4f, regions[step].Top);
        }

        Assert.Equal(OneScreen, regions[samples]);
    }

    // A still sine at zero hertz reads the strength straight off the offset: 1 through the first five of
    // ten steps, then ((10 - step) / 5) squared.
    [Fact]
    public void AHalfSustain_HoldsFullStrengthThroughTheFirstHalf_ThenFallsSquaredToExactlyZero()
    {
        (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
        ulong draws = simulation.Run.Random.DrawCount;
        camera.Shake(new CameraShake(new Vector2(0f, 10f), 10f / 60f) { Wave = ShakeWave.Sine, Frequency = 0f, Sustain = 0.5f });

        Rect[] regions = Steps(simulation, camera, 11);

        for (int step = 0; step < 5; step++)
        {
            Assert.Equal(10f, regions[step].Top);
        }

        for (int step = 5; step < 10; step++)
        {
            float fall = (10 - step) / 5f;
            Assert.Equal(10f * fall * fall, regions[step].Top, 1e-4f);
        }

        Assert.Equal(OneScreen, regions[10]);
        Assert.Equal(draws, simulation.Run.Random.DrawCount);
    }

    // A still sine holds a known offset. A call that changes nothing leaves it in place. A profile leaves
    // the camera-wide phase be. A shorthand call that takes over draws what it draws alone.
    [Fact]
    public void AProfileAndTheShorthand_EachChangeNothingUnderAStrongerShake_AndReplaceAWeakerOne()
    {
        static CameraShake Still(float amplitude) =>
            new(new Vector2(0f, amplitude), 1f) { Wave = ShakeWave.Sine, Frequency = 0f, Sustain = 1f };

        static Rect Shaken(Action<Camera>? first, Action<Camera> second)
        {
            (SceneSimulation simulation, Camera camera) = SceneFixtures.Pinned();
            first?.Invoke(camera);
            Steps(simulation, camera, 2);
            second(camera);

            return Steps(simulation, camera, 1)[0];
        }

        // A full shorthand shake reaches the default amplitude's length, about 11.3, and about 9.9 two
        // steps into its half second.
        Assert.Equal(Shaken(c => c.Shake(1f), _ => { }), Shaken(c => c.Shake(1f), c => c.Shake(Still(9.5f))));
        Assert.Equal(new Vector2(0f, 12f), Shaken(c => c.Shake(1f), c => c.Shake(Still(12f))).Position);

        Assert.Equal(new Vector2(0f, 12f), Shaken(c => c.Shake(Still(12f)), c => c.Shake(1f)).Position);
        Assert.Equal(Shaken(null, c => c.Shake(1f)), Shaken(c => c.Shake(Still(11f)), c => c.Shake(1f)));
    }

    [Theory]
    [InlineData(-1f, 1f, 15f, 0f, ShakeWave.Noise)]
    [InlineData(1f, 0f, 15f, 0f, ShakeWave.Noise)]
    [InlineData(1f, 1f, -1f, 0f, ShakeWave.Noise)]
    [InlineData(1f, 1f, 15f, -0.1f, ShakeWave.Noise)]
    [InlineData(1f, 1f, 15f, 1.1f, ShakeWave.Noise)]
    [InlineData(1f, 1f, 15f, 0f, (ShakeWave)2)]
    public void AProfileOutOfRange_IsRefused(float amplitude, float seconds, float frequency, float sustain, ShakeWave wave)
    {
        (_, Camera camera) = SceneFixtures.Pinned();
        CameraShake shake = new(new Vector2(0f, amplitude), seconds) { Frequency = frequency, Sustain = sustain, Wave = wave };

        Assert.Throws<ArgumentOutOfRangeException>(() => camera.Shake(shake));
    }

    private static Rect[] Steps(SceneSimulation simulation, Camera camera, int count, double? stepSeconds = null)
    {
        Rect[] regions = new Rect[count];
        for (int step = 0; step < count; step++)
        {
            simulation.Step(stepSeconds is { } length ? new StepContext(length, new InputState(new ActionBindings()), step) : SceneFixtures.Step(step));
            regions[step] = camera.VisibleRegion;
        }

        return regions;
    }
}
