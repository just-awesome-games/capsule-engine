using Capsule.Input;
using Capsule.Runtime;
using Capsule.Scenes;
using static System.FormattableString;

namespace Capsule.Tests.Runtime;

// The game object's lifecycle under the engine's host: it starts before the first scene, steps once
// ahead of each scene step whichever scene runs it, and ends after the last scene stops.
[Collection(LogSinkCollection.Name)]
public sealed class GameInstanceTests
{
    // The step of tick 1 asks for Second. The game object steps once on that tick, ahead of First, and
    // Second starts after the step and is the scene tick 2 runs. Both scenes read the one game object.
    [Fact]
    public void AHeadlessRun_StartsTheGameBeforeItsFirstScene_StepsItAheadOfEachSceneStep_AndEndsItLast()
    {
        List<string> events = [];
        List<Lifecycle> read = [];
        Lifecycle? game = null;

        Builder(events, run => read.Add(run.State<Lifecycle>()))
            .WithGame(run => game = new Lifecycle(run, events))
            .RunHeadless<First>(new InputScript().Wait(3).Build());

        Assert.Equal(
            [
                "game start", "First start",
                "game 0", "First 0",
                "game 1", "First 1", "Second start", "First stop",
                "game 2", "Second 2", "Second stop",
                "game end at 3",
            ],
            events);
        Assert.Equal(2, read.Count);
        Assert.All(read, seen => Assert.Same(game, seen));
    }

    [Fact]
    public void ARunWithNoGameObject_Runs_AndReadingItsGameObjectThrows()
    {
        List<string> events = [];
        Exception? read = null;

        Builder(events, run => read = Record.Exception(() => run.State<Lifecycle>())).RunHeadless<First>(new InputScript().Wait(1).Build());

        Assert.Equal(["First start", "First 0", "First stop"], events);
        Assert.Contains("WithGame", Assert.IsType<InvalidOperationException>(read).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondWithGame_Throws()
    {
        EngineBuilder builder = Builder([]).WithGame(run => new Lifecycle(run, []));

        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => builder.WithGame(run => new Lifecycle(run, [])));

        Assert.Contains("Call WithGame once", second.Message, StringComparison.Ordinal);
    }

    // sceneStarts runs as each scene starts.
    private static EngineBuilder Builder(List<string> events, Action<Run>? sceneStarts = null) =>
        CapsuleEngine.Configure(
                "Lifecycle Game",
                new SceneRegistry(
                    new EntityRegistry([]),
                    [
                        SceneRegistration.Plain(typeof(First), _ => new First(events, sceneStarts)),
                        SceneRegistration.Plain(typeof(Second), _ => new Second(events, sceneStarts)),
                    ]))
            .WithFixedStep(10)
            .WithoutCrashLog()
            .WithoutLogging();

    private sealed class Lifecycle(Run run, List<string> events) : GameInstance(run)
    {
        protected override void OnStart() => events.Add("game start");

        protected override void OnStep(in StepContext context) => events.Add(Invariant($"game {context.Tick}"));

        protected override void OnEnd() => events.Add(Invariant($"game end at {Run.Tick}"));
    }

    private abstract class Reporting(List<string> events, Action<Run>? starts) : Scene
    {
        protected override void OnStart()
        {
            events.Add($"{GetType().Name} start");
            starts?.Invoke(Run);
        }

        protected override void OnStep(in StepContext context) => events.Add(Invariant($"{GetType().Name} {context.Tick}"));

        protected override void OnStop() => events.Add($"{GetType().Name} stop");
    }

    private sealed class First(List<string> events, Action<Run>? starts) : Reporting(events, starts)
    {
        protected override void OnStep(in StepContext context)
        {
            base.OnStep(in context);
            if (context.Tick == 1)
            {
                Run.RequestScene<Second>();
            }
        }
    }

    private sealed class Second(List<string> events, Action<Run>? starts) : Reporting(events, starts);
}
