using System.Numerics;
using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Runtime;

[Collection(LogSinkCollection.Name)]
public sealed class StateTraceRunTests : IDisposable
{
    private readonly string _directory =
        Directory.CreateTempSubdirectory(nameof(StateTraceRunTests)).FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void OneTrace_SpansASceneTransitionWithItsTicksRunningOn()
    {
        StateTrace trace = new();

        static Scene Resolve(in SceneTransition target) => target.SceneType == typeof(Leaving)
            ? new Leaving()
            : new Arriving();

        using SceneHost host = new(
            SceneTransition.ToScene(typeof(Leaving), null),
            Resolve,
            random: null,
            trace: trace);

        host.Step(SceneFixtures.Step(0));
        host.Step(SceneFixtures.Step(1));

        // Tick 0 belongs to the scene that requested the transition; tick 1 to the one that
        // replaced it, on the same trace and with the tick counter carrying on.
        Assert.Contains("0,e0,type,Alpha\n", trace.ToString(), StringComparison.Ordinal);
        Assert.Contains("1,e1,type,Beta\n", trace.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WithStateTrace_OverAHeadlessRun_WritesTheRunsCsv()
    {
        string path = Path.Combine(_directory, "run.csv");
        InputTape tape = new InputScript().Wait(3).Build();

        CapsuleEngine.Configure(
                "Traced Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [SceneRegistration.Plain(typeof(Arriving), static () => new Arriving())]))
            .WithoutCrashLog()
            .WithoutLogging()
            .WithStateTrace(path)
            .RunHeadless<Arriving>(tape);

        string[] lines = File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal("tick,subject,column,value", lines[0]);
        Assert.Contains("0,e0,type,Beta", lines);
        Assert.Contains("2,e0,type,Beta", lines);
    }

    [Fact]
    public void WithStateTrace_RefusesABlankPath()
    {
        Assert.Throws<ArgumentException>(() => Builder().WithStateTrace(" "));
    }

    [Fact]
    public void WithFrameCapture_RefusesABlankDirectoryNoTicksOrANegativeTick()
    {
        Assert.Throws<ArgumentException>(() => Builder().WithFrameCapture(" ", 0));
        Assert.Throws<ArgumentException>(() => Builder().WithFrameCapture(_directory));
        Assert.Throws<ArgumentNullException>(() => Builder().WithFrameCapture(_directory, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder().WithFrameCapture(_directory, 4, -1));
    }

    private static SceneEngineBuilder Builder() =>
        CapsuleEngine.Configure("Traced Game", new SceneRegistry(new EntityRegistry([]), []));

    private sealed class Leaving : Scene
    {
        protected override void OnStart() => Add(new Alpha());

        protected override void OnStep(in StepContext context) => RequestScene<Arriving>();
    }

    private sealed class Arriving : Scene
    {
        protected override void OnStart() => Add(new Beta());
    }

    private sealed class Alpha() : Entity(Vector2.Zero);

    private sealed class Beta() : Entity(Vector2.Zero);
}
