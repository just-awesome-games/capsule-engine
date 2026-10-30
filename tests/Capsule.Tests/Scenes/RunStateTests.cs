using Capsule.Scenes;
using static System.FormattableString;

namespace Capsule.Tests.Scenes;

// A run built by hand starts, steps and ends its game object as the engine's host does, and every scene
// reads the one object as Run.State.
public sealed class RunStateTests
{
    [Fact]
    public void AStartedGame_IsWhatEverySceneReads_StepsAheadOfTheScene_AndEndsAfterItStops()
    {
        List<string> events = [];
        Run run = new();
        Probe probe = run.StartGame(run => new Probe(run, events));

        using (SimulationHost host = new(new Reader(events), run: run))
        {
            host.Step(2);
            Assert.Same(probe, ((Reader)host.Scene).Seen);
            host.End();
            host.End();
        }

        Assert.Same(probe, run.State<GameInstance>());
        Assert.Equal(["game start", "Reader start", "game 0", "Reader 0", "game 1", "Reader 1", "Reader stop", "game end"], events);
    }

    [Fact]
    public void AHostDisposedWithoutEnding_DoesNotEndTheGame()
    {
        List<string> events = [];
        Run run = new();
        run.StartGame(run => new Probe(run, events));

        using (SimulationHost host = new(new Reader(events), run: run))
        {
            host.Step();
        }

        Assert.DoesNotContain("game end", events);
    }

    [Fact]
    public void ReadingTheGame_ThrowsWithNoneStarted_AndOnTheWrongType()
    {
        Run run = new();

        Assert.Throws<InvalidOperationException>(() => run.State<Probe>());

        run.StartGame(run => new Probe(run, []));

        InvalidOperationException wrong = Assert.Throws<InvalidOperationException>(() => run.State<Other>());
        Assert.Contains(nameof(Probe), wrong.Message, StringComparison.Ordinal);
    }

    // A second game, and a game started once the first scene has.
    [Fact]
    public void StartingTheGame_ThrowsWhenTheRunCannotTakeIt()
    {
        Run run = new();
        run.StartGame(run => new Probe(run, []));
        InvalidOperationException second = Assert.Throws<InvalidOperationException>(() => run.StartGame(run => new Probe(run, [])));
        Assert.Contains("hold every run-wide object as a member of it", second.Message, StringComparison.Ordinal);

        Run started = new();
        using SimulationHost host = new(new SceneFixtures.Idle(), run: started);
        Assert.Throws<InvalidOperationException>(() => started.StartGame(run => new Probe(run, [])));
    }

    private sealed class Probe(Run run, List<string> events) : GameInstance(run)
    {
        protected override void OnStart() => events.Add("game start");

        protected override void OnStep(in StepContext context) => events.Add(Invariant($"game {context.Tick}"));

        protected override void OnEnd() => events.Add("game end");
    }

    private sealed class Other(Run run) : GameInstance(run);

    private sealed class Reader(List<string> events) : Scene
    {
        internal Probe? Seen { get; private set; }

        protected override void OnStart()
        {
            Seen = Run.State<Probe>();
            events.Add("Reader start");
        }

        protected override void OnStep(in StepContext context) => events.Add(Invariant($"Reader {context.Tick}"));

        protected override void OnStop() => events.Add("Reader stop");
    }
}
