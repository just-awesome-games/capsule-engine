using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class RunTests
{
    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(1f, -1f)]
    [InlineData(1f, float.NaN)]
    [InlineData(float.PositiveInfinity, 1f)]
    [InlineData(1f, float.NegativeInfinity)]
    public void ACanvasWithANonPositiveOrNonFiniteComponent_IsRefused(float x, float y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Run { Canvas = new Vector2(x, y) });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ATimeScaleThatIsNotPositiveAndFinite_IsRefusedAndLeavesThePaceAsItWas(double scale)
    {
        Run run = new() { TimeScale = 2 };

        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(() => run.TimeScale = scale);
        Assert.Contains("set Scene.Paused", refused.Message, StringComparison.Ordinal);
        Assert.Equal(2, run.TimeScale);
    }

    // Inside a step the run's clock is the step's own. Outside one it counts the steps run.
    [Fact]
    public void TheRunsClock_ReadsTheStepInsideOneAndTheStepsRunOutsideOne()
    {
        List<(long Tick, double Seconds)> run = [];
        List<(long Tick, double Seconds)> step = [];
        using SimulationHost host = new(
            new SceneFixtures.HookScene(step: (Scene scene, in StepContext context) =>
            {
                run.Add((scene.Run.Tick, scene.Run.TotalSeconds));
                step.Add((context.Tick, context.TotalSeconds));
            }),
            stepHertz: 10);

        Assert.Equal((0L, 0d), (host.Run.Tick, host.Run.TotalSeconds));

        host.Step();
        host.Step();
        host.Step();

        Assert.Equal(step, run);
        Assert.Equal((3L, 3 * host.StepSeconds), (host.Run.Tick, host.Run.TotalSeconds));
    }

    [Fact]
    public void Exit_ReplacesAPendingTransitionAndRejectsLaterRequests()
    {
        Run run = new();
        run.RequestScene<Scene>();
        run.RequestExit();
        run.RequestExit();

        Assert.True(run.ExitRequested);
        Assert.True(run.TryTakeTransition(out SceneTransition transition));
        Assert.Equal(SceneTransitionKind.Exit, transition.Kind);
        Assert.False(run.TryTakeTransition(out _));
        Assert.Throws<InvalidOperationException>(() => run.RequestRestart());
        Assert.Throws<InvalidOperationException>(() => run.RequestScene<Scene>());
    }

    [Fact]
    public void TheFirstPendingTransitionWinsWithinAStep()
    {
        Run run = new();

        run.RequestScene<PendingSceneA>();
        run.RequestScene<PendingSceneB>();

        Assert.True(run.TryTakeTransition(out SceneTransition transition));
        Assert.Equal(SceneTransitionKind.Scene, transition.Kind);
        Assert.Equal(typeof(PendingSceneA), transition.SceneType);
        Assert.False(run.TryTakeTransition(out _));
    }

    // A default key names no document, and the request fails at the call that made it.
    [Fact]
    public void ADefaultSceneKey_IsRefusedAtTheRequest()
    {
        Run run = new();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => run.RequestScene(default));

        Assert.Contains("Pass a CapsuleAssets.Scenes member.", refused.Message, StringComparison.Ordinal);
        Assert.False(run.TryTakeTransition(out _));
    }

    [Fact]
    public void AFrameCaptureAfterExit_IsRejectedAndNotServed()
    {
        Run run = new();
        run.CaptureFrame("shot.png");
        run.RequestExit();

        Assert.Throws<InvalidOperationException>(() => run.CaptureFrame("later.png"));
        Assert.Null(run.FrameCaptureRequested);
        Assert.False(run.TryTakeFrameCapture(out _));
    }

    [Fact]
    public void ASceneDeclaringNoSampling_OpensAtTheRunsDefault()
    {
        using SceneSimulation simulation = new(
            new SceneFixtures.HookScene(),
            run: new Run { Sampling = TextureSampling.Point });

        Assert.Equal(TextureSampling.Point, simulation.View.Sampling);
    }

    [Fact]
    public void ASceneCannotReachItsRunInItsConstructor_ButCanOnStart()
    {
        RunProbeScene scene = new();

        Assert.NotNull(scene.ConstructorFailure);

        using SceneSimulation simulation = new(scene);

        Assert.Same(simulation.Run, scene.SeenOnStart);
    }

    [Fact]
    public void AnEntityAndComponentOffScene_CannotReachTheirRun()
    {
        RunProbeEntity entity = new();

        Assert.Throws<InvalidOperationException>(() => entity.Run);
        Assert.Throws<InvalidOperationException>(() => entity.Probe.Run);
    }

    private sealed class PendingSceneA : Scene;

    private sealed class PendingSceneB : Scene;

    private sealed class RunProbeScene : Scene
    {
        internal InvalidOperationException? ConstructorFailure { get; }

        internal Run? SeenOnStart { get; private set; }

        internal RunProbeScene()
        {
            try
            {
                _ = Run;
            }
            catch (InvalidOperationException failure)
            {
                ConstructorFailure = failure;
            }
        }

        protected override void OnStart() => SeenOnStart = Run;
    }

    private sealed class RunProbeEntity : Entity
    {
        internal RunProbeComponent Probe { get; }

        internal RunProbeEntity()
            : base(Vector2.Zero)
        {
            Probe = new RunProbeComponent();
            Add(Probe);
        }
    }

    private sealed class RunProbeComponent : Component;
}
