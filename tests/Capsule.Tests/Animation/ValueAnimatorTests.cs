using Capsule.Animation;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Animation.SpriteAnimatorFixtures;

namespace Capsule.Tests.Animation;

public sealed class ValueAnimatorTests
{
    /// <summary>Where a <c>Play</c> inside a step is made from, which must not change the values set.</summary>
    public enum PlaySite
    {
        EntityStep,
        ComponentAfterTheAnimator,
    }

    // A one-tick first key is the case a step that advanced too early would erase entirely. The setter
    // hears the first value at once, and the play tick shows it. Afterwards it hears each key entered.
    [Fact]
    public void Play_SetsTheFirstValueAtOnce_AndEachKeyHoldsForItsOwnTicks()
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();
        ValueClip<float> clip = new([(1f, 1), (2f, 3), (3f, 1)]);

        animator.Play(clip);

        Assert.Equal([1f], set);
        Assert.Equal([1f, 2f, 2f, 2f, 3f], ValuesOver(run, animator, 5));
        Assert.False(animator.IsFinished);
        Assert.Equal([3f], ValuesOver(run, animator, 1));
        Assert.True(animator.IsFinished);
        Assert.Equal([1f, 2f, 3f], set);
    }

    [Fact]
    public void ALoopingClip_WrapsFromItsLastKeyToItsFirstAndNeverFinishes()
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();

        animator.Play(new ValueClip<float>([(1f, 1), (2f, 2)], loop: true));

        Assert.Equal([1f, 2f, 2f, 1f, 2f, 2f, 1f], ValuesOver(run, animator, 7));
        Assert.False(animator.IsFinished);
        Assert.Equal([1f, 2f, 1f, 2f, 1f], set);
    }

    // A key's own value lands on its first tick and the blend moves toward the next across its ticks.
    // The last key holds, unless the clip loops and it blends back to the first. Keys at opposite finite
    // extremes blend without overflowing.
    [Theory]
    [InlineData(0f, 8f, false, new[] { 0f, 4f, 8f, 8f, 8f })]
    [InlineData(0f, 8f, true, new[] { 0f, 4f, 8f, 4f, 0f })]
    [InlineData(-float.MaxValue, float.MaxValue, false, new[] { -float.MaxValue, 0f, float.MaxValue, float.MaxValue, float.MaxValue })]
    public void ALinearClip_BlendsFromEachKeyTowardTheNext(float from, float to, bool loop, float[] values)
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();

        animator.Play(ValueClip.Linear([(from, 2), (to, 2)], loop));

        Assert.Equal(values, ValuesOver(run, animator, 5));
        Assert.Equal(!loop, animator.IsFinished);
    }

    // The setter hears a value only when it differs from the last it heard: never for an equal next key,
    // an equal key a loop wraps to, an equal key landed on across a crossed key, or a linear clip's hold.
    [Theory]
    [InlineData(new[] { 1f, 1f, 2f }, false, false, 1f, new[] { 1f, 2f })]
    [InlineData(new[] { 1f, 2f, 1f }, true, false, 1f, new[] { 1f, 2f, 1f, 2f, 1f })]
    [InlineData(new[] { 1f, 2f, 1f }, false, false, 2f, new[] { 1f })]
    [InlineData(new[] { 0f, 4f }, false, true, 1f, new[] { 0f, 4f })]
    public void TheSetter_RunsOnlyWhenTheValueChanges(float[] keyValues, bool loop, bool linear, float speed, float[] setValues)
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();
        (float, int)[] keys = [.. keyValues.Select(static value => (value, 1))];
        animator.Speed = speed;

        animator.Play(linear ? ValueClip.Linear(keys, loop) : new ValueClip<float>(keys, loop));
        run.Step(6);

        Assert.Equal(setValues, set);
    }

    // The held steps spend the step Play holds its first key for. Resuming advances at once.
    [Fact]
    public void AHeldClip_KeepsItsValueAndResumesWhereItWasHeld()
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();
        animator.Play(new ValueClip<float>([(1f, 1), (2f, 3)]));
        animator.Paused = true;

        run.Step(10);

        Assert.Equal(1f, animator.Value);
        Assert.False(animator.IsFinished);

        animator.Paused = false;

        Assert.Equal([2f], ValuesOver(run, animator, 1));
        Assert.Equal([1f, 2f], set);
    }

    // A fraction of a tick carries to the next step, and a key crossed inside one step never reaches
    // the setter.
    [Theory]
    [InlineData(0.5f, new[] { 1f, 1f, 2f, 2f, 3f }, new[] { 1f, 2f, 3f })]
    [InlineData(2f, new[] { 1f, 3f, 5f, 5f, 5f }, new[] { 1f, 3f, 5f })]
    public void Speed_CarriesFractionsAndSkipsTheKeysItCrosses(float speed, float[] values, float[] setValues)
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();
        animator.Speed = speed;

        animator.Play(new ValueClip<float>([(1f, 1), (2f, 1), (3f, 1), (4f, 1), (5f, 1)]));

        Assert.Equal(values, ValuesOver(run, animator, 5));
        Assert.Equal(setValues, set);
    }

    // The state that started a clip may keep asking for it. Re-triggering it is restart.
    [Fact]
    public void PlayingThePlayingClip_DoesNothingUnlessItRestarts()
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();
        ValueClip<float> clip = new([(1f, 1), (2f, 1)]);
        animator.Play(clip);
        run.Step(3);
        Assert.True(animator.IsFinished);

        animator.Play(clip);

        Assert.Equal([1f, 2f], set);
        Assert.True(animator.IsFinished);

        animator.Play(clip, restart: true);

        Assert.Equal([1f, 2f, 1f], set);
        Assert.Equal(1f, animator.Value);
        Assert.False(animator.IsFinished);
    }

    // A reused entity replays its clip as a new one would.
    [Fact]
    public void Removal_RewindsToTheFirstKeySetsItAndClearsThePause()
    {
        (ValueAnimator<float> animator, List<float> set, SimulationHost run) = Animating();
        ValueClip<float> clip = new([(1f, 1), (2f, 1)], loop: true);
        animator.Play(clip);
        run.Step(2);
        animator.Paused = true;

        run.Simulation.Scene.Remove(animator.Entity!);
        run.Step();

        Assert.Equal([1f, 2f, 1f], set);
        Assert.Equal(1f, animator.Value);
        Assert.False(animator.Paused);
        Assert.Same(clip, animator.Clip);
    }

    // Wherever in a step the Play came from, the first key holds for exactly its own ticks.
    [Theory]
    [InlineData(PlaySite.EntityStep)]
    [InlineData(PlaySite.ComponentAfterTheAnimator)]
    public void AClipPlayedInsideAStep_HoldsItsFirstKeyForItsOwnTicks(PlaySite site)
    {
        List<float> set = [];
        ValueAnimator<float> animator = new(set.Add);
        ValueClip<float> clip = new([(1f, 1), (2f, 3)]);
        Entity entity = site == PlaySite.EntityStep ? new SceneFixtures.Watcher(_ => animator.Play(clip)) : new SceneFixtures.Recorder("animated", []);
        entity.Add(animator);
        if (site == PlaySite.ComponentAfterTheAnimator)
        {
            entity.Add(new Driver(() => animator.Play(clip)));
        }

        using SimulationHost run = SceneFixtures.Simulate(entity);

        Assert.Equal([1f, 2f, 2f, 2f, 2f], ValuesOver(run, animator, 5));
        Assert.Equal([1f, 2f], set);
    }

    // Stepped through a scene, not by calling the component: the animator's promise is that values
    // advance on the fixed step, in the order a scene steps its components.
    private static (ValueAnimator<float> Animator, List<float> Set, SimulationHost Run) Animating()
    {
        List<float> set = [];
        ValueAnimator<float> animator = new(set.Add);
        SceneFixtures.Recorder entity = new("animated", []);
        entity.Add(animator);
        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return (animator, set, new SimulationHost(scene));
    }

    // The value the animator holds after each of the run's next steps.
    private static float[] ValuesOver(SimulationHost run, ValueAnimator<float> animator, int steps)
    {
        float[] values = new float[steps];
        for (int step = 0; step < steps; step++)
        {
            run.Step();
            values[step] = animator.Value;
        }

        return values;
    }
}
