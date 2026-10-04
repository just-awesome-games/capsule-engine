using Capsule.Animation;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using static Capsule.Tests.Animation.SpriteAnimatorFixtures;

namespace Capsule.Tests.Animation;

// Events a clip's entries raise, read by polling, and the speed that decides which entries a step enters.
public sealed class SpriteAnimatorEventTests
{
    private const string Hit = "hit";

    // Frame 1 raises nothing and frame 2 raises two names. Each frame reads its own run of the list.
    private static readonly SpriteClip Cycle = new(
        [Frame(0), Frame(1), Frame(2)],
        [1, 1, 1],
        loop: true,
        [["a"], [], ["b", "c"]]);

    private static readonly SpriteClip Strike = new([Frame(3), Frame(4)], [2, 2], frameEvents: [["x"], ["y"]]);

    [Fact]
    public void AFastStep_ReportsEveryFrameItCrossesAndFrameZeroOnEveryWrap()
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Cycle);
        run.Step();

        animator.Speed = 2f;
        run.Step();

        Assert.Equal(2, animator.FrameIndex);
        Assert.Equal([false, true, true], Read(animator));

        run.Step();

        Assert.Equal(1, animator.FrameIndex);
        Assert.Equal([true, false, false], Read(animator));

        // Two whole passes and one tick more, which crosses every frame and lands one frame on.
        animator.Speed = 7f;
        run.Step();

        Assert.Equal(2, animator.FrameIndex);
        Assert.Equal([true, true, true], Read(animator));
    }

    // Each Play form is made outside a step on a clip that has finished and reported nothing since. The
    // report is read after the animator's next step.
    [Theory]
    [InlineData("play", "x")]
    [InlineData("restart", "x")]
    [InlineData("at a frame's first tick", "y")]
    [InlineData("at a tick inside a frame", null)]
    [InlineData("at a frame", "y")]
    [InlineData("at a frame part-way", null)]
    [InlineData("the clip already playing", null)]
    public void APlay_ReportsTheFrameItLandsOnOnlyAtThatFramesFirstTick(string form, string? reported)
    {
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Play(Strike);
        run.Step(6);
        Assert.True(animator.IsFinished);
        Assert.False(animator.Reached("x") || animator.Reached("y"));

        switch (form)
        {
            case "play":
                animator.Play(Land);
                animator.Play(Strike);
                break;
            case "restart":
                animator.Play(Strike, restart: true);
                break;
            case "at a frame's first tick":
                animator.Play(Strike, atTick: 2);
                break;
            case "at a tick inside a frame":
                animator.Play(Strike, atTick: 1);
                break;
            case "at a frame":
                animator.PlayAtFrame(Strike, 1, 0);
                break;
            case "at a frame part-way":
                animator.PlayAtFrame(Strike, 0, 1);
                break;
            default:
                animator.Play(Strike);
                break;
        }

        run.Step();

        Assert.Equal(reported == "x", animator.Reached("x"));
        Assert.Equal(reported == "y", animator.Reached("y"));
    }

    // A pooled entity rewound by its removal replays as a new one does, frame 0's events included.
    [Fact]
    public void AnEntityRemovedAndAddedAgain_ReportsFrameZeroOnItsFirstStep()
    {
        Animated entity = new();
        SceneFixtures.HookScene scene = new();
        scene.Add(entity);
        SimulationHost run = new(scene);
        entity.Animator.Play(Strike);
        run.Step(4);
        Assert.False(entity.Animator.Reached("x"));

        scene.Remove(entity);
        scene.Add(entity);
        run.Step();

        Assert.True(entity.Animator.Reached("x"));
    }

    // The entity steps before its animator and a component attached after it steps after. Each
    // sees the event exactly once, the later reader on the step it happens.
    [Fact]
    public void AReaderBeforeTheAnimatorSeesAnEventOneStepLaterAndAReaderAfterSeesItOnItsStep()
    {
        Listener listener = new();
        SimulationHost run = Simulate(listener);
        listener.Animator.Play(new SpriteClip([Frame(0), Frame(1)], [2, 2], frameEvents: [[], [Hit]]));

        run.Step(6);

        long late = Assert.Single(listener.Late);
        Assert.Equal(late + 1, Assert.Single(listener.Early));
    }

    // A late step plays after the animator has stepped. Its report waits for the animator's next step,
    // and neither reader misses it or sees it twice.
    [Fact]
    public void APlayAfterTheAnimatorStepped_IsSeenOnceByEachReaderFromTheAnimatorsNextStep()
    {
        Listener listener = new() { PlayLate = new SpriteClip([Frame(0)], [10], frameEvents: [[Hit]]) };
        SimulationHost run = Simulate(listener);

        run.Step(6);

        long played = Assert.NotNull(listener.PlayedOn);
        Assert.Equal(played + 1, Assert.Single(listener.Late));
        Assert.Equal(played + 2, Assert.Single(listener.Early));
    }

    // A late step replays the event the animator just reported. The first report stays readable for the
    // rest of this step, and the replay is readable through the next.
    [Fact]
    public void APlayRaisingAnEventAlreadyReadable_KeepsItReadableAndReportsItAgainOnTheNextStep()
    {
        (SpriteRenderer renderer, SpriteAnimator animator, SimulationHost run) = Animating();
        renderer.Entity!.Add(new Replayer(animator, new SpriteClip([Frame(0)], [10], frameEvents: [[Hit]])));
        animator.Play(new SpriteClip([Frame(1), Frame(2)], [1, 1], frameEvents: [[], [Hit]]));

        run.Step(2);

        Assert.True(animator.Reached(Hit));

        run.Step();

        Assert.True(animator.Reached(Hit));

        run.Step();

        Assert.False(animator.Reached(Hit));
    }

    [Fact]
    public void AFractionalSpeedHoldsEachFrameForItsShareOfStepsAndAPlayRestartsTheCarry()
    {
        SpriteClip clip = new([Frame(0), Frame(1), Frame(2), Frame(3)], [1, 1, 1, 1]);
        (_, SpriteAnimator animator, SimulationHost run) = Animating();
        animator.Speed = 0.5f;
        animator.Play(clip);

        // The first step is the one Play holds its frame for. Each frame then lasts two steps.
        Assert.Equal([0, 0, 1, 1], Frames(animator, run, 4));

        // Half a tick is carried into the Play. A carry kept across it would advance a step early.
        animator.Play(clip, restart: true);

        Assert.Equal([0, 0, 1], Frames(animator, run, 3));
    }

    private static bool[] Read(SpriteAnimator animator) =>
        [animator.Reached("a"), animator.Reached("b"), animator.Reached("c")];

    private static int[] Frames(SpriteAnimator animator, SimulationHost run, int steps)
    {
        int[] frames = new int[steps];
        for (int step = 0; step < steps; step++)
        {
            run.Step();
            frames[step] = animator.FrameIndex;
        }

        return frames;
    }

    private static SimulationHost Simulate(Entity entity)
    {
        SceneFixtures.HookScene scene = new();
        scene.Add(entity);

        return new SimulationHost(scene);
    }

    private sealed class Listener : Entity
    {
        internal SpriteAnimator Animator { get; }

        internal List<long> Early { get; } = [];

        internal List<long> Late { get; } = [];

        // A clip the late step plays once, on tick 2, and the tick it played on.
        internal SpriteClip? PlayLate { get; init; }

        internal long? PlayedOn { get; private set; }

        internal Listener()
            : base(System.Numerics.Vector2.Zero)
        {
            SpriteRenderer renderer = new(Frame(9));
            Animator = new SpriteAnimator(renderer);
            Add(renderer);
            Add(Animator);
            Add(new LateReader(this));
        }

        protected internal override void OnStep(in StepContext context)
        {
            if (Animator.Reached(Hit))
            {
                Early.Add(context.Tick);
            }
        }

        protected internal override void OnLateStep(in StepContext context)
        {
            if (PlayLate is { } clip && PlayedOn is null && context.Tick >= 2)
            {
                Animator.Play(clip);
                PlayedOn = context.Tick;
            }
        }
    }

    // Plays its clip once, from the late step of the first step the animator reports the event on.
    private sealed class Replayer(SpriteAnimator animator, SpriteClip clip) : Component
    {
        private bool _played;

        protected internal override void OnLateStep(in StepContext context)
        {
            if (!_played && animator.Reached(Hit))
            {
                animator.Play(clip);
                _played = true;
            }
        }
    }

    private sealed class LateReader(Listener listener) : Component
    {
        protected internal override void OnStep(in StepContext context)
        {
            if (listener.Animator.Reached(Hit))
            {
                listener.Late.Add(context.Tick);
            }
        }
    }
}
