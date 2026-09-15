using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Scenes;

public sealed class RunTests
{
    [Fact]
    public void TheDefaultCanvas_IsTheStandardCanvas()
    {
        Run run = new();

        Assert.Equal(Run.StandardCanvas, run.Canvas);
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(-1f, 1f)]
    [InlineData(float.NaN, 1f)]
    [InlineData(1f, 0f)]
    [InlineData(1f, -1f)]
    [InlineData(1f, float.NaN)]
    public void ACanvasWithANonPositiveOrNaNComponent_IsRefused(float x, float y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Run { Canvas = new Vector2(x, y) });
    }

    [Fact]
    public void TheDefaultTimeScale_IsOne()
    {
        Run run = new();

        Assert.Equal(1, run.TimeScale);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ATimeScaleThatIsNotPositiveAndFinite_IsRefusedAndLeavesThePaceAsItWas(double scale)
    {
        Run run = new() { TimeScale = 2 };

        Assert.Throws<ArgumentOutOfRangeException>(() => run.TimeScale = scale);
        Assert.Equal(2, run.TimeScale);
    }

    [Fact]
    public void Exit_ReplacesAPendingTransitionAndRejectsLaterRequests()
    {
        Run run = new();
        run.RequestScene<Scene>();
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

    [Fact]
    public void RequestExit_TwiceProducesOneExit()
    {
        Run run = new();

        run.RequestExit();
        run.RequestExit();

        Assert.True(run.TryTakeTransition(out SceneTransition transition));
        Assert.Equal(SceneTransitionKind.Exit, transition.Kind);
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
    public void ASceneDeclaringItsOwnSampling_KeepsItOverTheRunsDefault()
    {
        using SceneSimulation simulation = new(
            new ComposedScene(),
            run: new Run { Sampling = TextureSampling.Point });

        Assert.Equal(TextureSampling.Linear, simulation.View.Sampling);
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

    private sealed class ComposedScene : Scene
    {
        internal ComposedScene() => Sampling = TextureSampling.Linear;
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
        internal RunProbeEntity()
            : base(Vector2.Zero)
        {
            Probe = new RunProbeComponent();
            Add(Probe);
        }

        internal RunProbeComponent Probe { get; }
    }

    private sealed class RunProbeComponent : Component;
}
