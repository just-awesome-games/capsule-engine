using System.Numerics;
using Capsule.Assets;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Allocation;
using static Capsule.Tests.Runtime.SceneHostFixtures;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Runtime;

// A prefetch builds its scene once and holds it. The boundary naming the same scene with an equal payload starts
// that instance, and anything else abandons it.
[Collection(StageAllocationCollection.Name)]
public sealed class PrefetchedSceneTests
{
    // The constructor and the collection run once. The scene opens with the request's payload instance, which
    // equals the one it collected with.
    [Fact]
    public void ARequestMatchingThePrefetch_StartsTheSceneThePrefetchBuilt()
    {
        Rig rig = new();
        Tag prefetched = new("north");
        Tag requested = new("north");
        rig.Script[0] = run => run.PrefetchScene<Target>(prefetched);
        rig.Script[2] = run => run.RequestScene<Target>(requested);

        rig.Steps(4);

        Target built = Assert.Single(rig.Built);
        Assert.Same(built, rig.Host.Scene);
        Assert.Equal(1, built.Collections);
        Assert.Same(requested, built.OpenedWith);
        Assert.Equal(0, rig.Pool.Available);
    }

    // A prefetch and a request match on the scene they resolve to, however each names it: by its class or by the
    // key of the document the class claims.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void APrefetchAndARequestNamingOneSceneDifferently_StartTheSceneThePrefetchBuilt(bool prefetchByClass)
    {
        Rig rig = new();
        rig.Script[0] = prefetchByClass
            ? static run => run.PrefetchScene<Target>(new Tag("north"))
            : static run => run.PrefetchScene(new SceneKey(Rig.TargetDocument), new Tag("north"));
        rig.Script[2] = prefetchByClass
            ? static run => run.RequestScene(new SceneKey(Rig.TargetDocument), new Tag("north"))
            : static run => run.RequestScene<Target>(new Tag("north"));

        rig.Steps(4);

        Assert.Same(Assert.Single(rig.Built), rig.Host.Scene);
    }

    // A class claiming a document resolves to one scene by either name. A plain class and an unclaimed document
    // each have one name, and no other name reaches them.
    [Fact]
    public void EveryNameOfAScene_ResolvesToOneIdentity()
    {
        SceneRegistry registry = new(
            Registry(),
            [
                SceneRegistration.FromDocument(typeof(Target), Rig.TargetDocument, static _ => new Scene()),
                SceneRegistration.Plain(typeof(Lobby), static _ => new Scene()),
                SceneRegistration.DocumentOnly("loose", static _ => new Scene()),
            ]);

        Assert.Equal((typeof(Target), Rig.TargetDocument), registry.IdentityOf(ToScene<Target>()));
        Assert.Equal((typeof(Target), Rig.TargetDocument), registry.IdentityOf(SceneTransition.ToName(Rig.TargetDocument, null)));
        Assert.Equal((typeof(Lobby), null), registry.IdentityOf(ToScene<Lobby>()));
        Assert.Equal((null, "loose"), registry.IdentityOf(SceneTransition.ToName("loose", null)));
    }

    // Every end but a matching request releases the held scene through the abandon path: its entities leave
    // it, a pooled one returns to its pool, and no hook of the scene or its entities runs.
    [Theory]
    [InlineData("another scene")]
    [InlineData("another payload")]
    [InlineData("restart")]
    [InlineData("second prefetch")]
    [InlineData("exit")]
    [InlineData("dispose")]
    public void EveryOtherEnd_AbandonsTheHeldScene(string end)
    {
        Rig rig = new();
        rig.Script[0] = run => run.PrefetchScene<Target>(new Tag("north"));
        Action<Run>? ending = end switch
        {
            "another scene" => static run => run.RequestScene<Scene>(),
            "another payload" => static run => run.RequestScene<Target>(new Tag("south")),
            "restart" => static run => run.RequestRestart(),
            "second prefetch" => static run => run.PrefetchScene<Scene>(),
            "exit" => static run => run.RequestExit(),
            _ => null,
        };
        if (ending is not null)
        {
            rig.Script[2] = ending;
        }

        rig.Steps(3);
        rig.Host.Dispose();

        // A request for another payload builds the scene again, which takes the pooled entity back until its
        // stop at disposal. Its join, start and stop are the whole log.
        Target held = rig.Built[0];
        Assert.Empty(held.Entities.ToArray());
        Assert.Null(held.Placed.SceneOrNull);
        Assert.Null(held.OpenedWith);
        Assert.Equal(end == "another payload" ? ["pooled+", "target start", "pooled-"] : [], rig.Log);
        Assert.Equal(1, rig.Pool.Available);
        Assert.Equal(end == "another payload" ? 2 : 1, rig.Built.Count);
    }

    // A held scene is built at the prefetch. What its start reads is read at the boundary.
    [Fact]
    public void StateChangedBetweenThePrefetchAndTheRequest_IsWhatTheSceneReadsAsItStarts()
    {
        Rig rig = new(static run => run.StartGame(static run => new Tally(run)));
        rig.ReadsOnStart = static run => " " + run.State<Tally>().Value;
        rig.Script[0] = run => run.PrefetchScene<Target>();
        rig.Script[1] = static run => run.State<Tally>().Value = 7;
        rig.Script[2] = run => run.RequestScene<Target>();

        rig.Steps(3);

        Target built = Assert.Single(rig.Built);
        Assert.Same(built, rig.Host.Scene);
        Assert.Contains("target start 7", rig.Log);
    }

    // Nothing composed at the prefetch can reach the run, whose state would be the prefetch step's.
    [Theory]
    [InlineData(Reach.Constructor)]
    [InlineData(Reach.Collection)]
    [InlineData(Reach.ComposedEntity)]
    public void ARunReachedBeforeTheSceneStarts_Throws(Reach reach)
    {
        using SceneHost host = new(
            ToScene<Lobby>(),
            (in SceneTransition target) => target.SceneType == typeof(Reaching) ? new Reaching(reach) : new Lobby([]),
            new Run());
        host.Run.PrefetchScene<Reaching>();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => host.Step(Step(0)));

        Assert.Contains(Scene.NoRunYet, failure.Message, StringComparison.Ordinal);
    }

    // Starting the held instance instead of a second build moves no seeded draw, entity or position. A run that
    // prefetched replays as one that did not.
    [Fact]
    public void ARunThatStartsTheHeldScene_MatchesOneThatBuiltItAtTheBoundary()
    {
        Rig prefetching = new();
        prefetching.Script[0] = run => run.PrefetchScene<Target>();
        prefetching.Script[2] = run => run.RequestScene<Target>();
        Rig cold = new();
        cold.Script[2] = run => run.RequestScene<Target>();

        prefetching.Steps(8);
        cold.Steps(8);

        Assert.Equal(cold.Checksum(8), prefetching.Checksum(8));
        Assert.Single(prefetching.Built);
    }

    public enum Reach
    {
        Constructor,
        Collection,
        ComposedEntity,
    }

    // A host over a Lobby that runs Script's act at each tick, and builds each Target from one pool.
    private sealed class Rig
    {
        // The document Target claims.
        internal const string TargetDocument = "target";

        internal Dictionary<long, Action<Run>> Script { get; } = [];

        internal List<string> Log { get; } = [];

        // What each Target logs from the run as it starts.
        internal Func<Run, string>? ReadsOnStart { get; set; }

        internal List<Target> Built { get; } = [];

        internal EntityPool<Recorder> Pool { get; }

        internal SceneHost Host { get; }

        internal Rig(Action<Run>? onRunStart = null)
        {
            Pool = new(() => new Recorder("pooled", Log), capacity: 1);
            Host = new(
                ToScene<Lobby>(),
                Resolve,
                new Run(),
                onRunStart: onRunStart,
                registry: new SceneRegistry(
                    Registry(),
                    [
                        SceneRegistration.FromDocument(typeof(Target), TargetDocument, static _ => throw new InvalidOperationException("The rig builds its scenes.")),
                        SceneRegistration.Plain(typeof(Lobby), static _ => throw new InvalidOperationException("The rig builds its scenes.")),
                    ]));
        }

        internal void Steps(int count)
        {
            for (long tick = 0; tick < count && !Host.ExitRequested; tick++)
            {
                Host.Step(Step(tick));
            }
        }

        internal ulong Checksum(long tick) => SimulationChecksum.Compute(tick, Host.Scene, Host.Run.Random, entities: true);

        private Scene Resolve(in SceneTransition target)
        {
            if (target.SceneType == typeof(Target) || target.DocumentName == TargetDocument)
            {
                Target built = new(Log, Pool, ReadsOnStart);
                Built.Add(built);
                return built;
            }

            return target.SceneType == typeof(Lobby) ? new Lobby(Script) : new Scene();
        }
    }

    private sealed class Lobby(Dictionary<long, Action<Run>> script) : Scene
    {
        protected override void OnStep(in StepContext context)
        {
            if (script.TryGetValue(context.Tick, out Action<Run>? act))
            {
                act(Run);
            }
        }
    }

    // Places an entity that walks a seeded step once started, and takes a pooled recorder.
    private sealed class Target : Scene
    {
        private readonly List<string> _log;
        private readonly Func<Run, string>? _reads;

        internal Entity Placed { get; }

        internal int Collections { get; private set; }

        internal IScenePayload? OpenedWith { get; private set; }

        public Target(List<string> log, EntityPool<Recorder> pool, Func<Run, string>? reads)
        {
            _log = log;
            _reads = reads;
            Placed = Add(new Wanderer());
            Add(pool.Take());
        }

        protected internal override void CollectAssets(AssetCollection assets) => Collections++;

        protected override void OnStart()
        {
            OpenedWith = EntryPayload;
            _log.Add("target start" + _reads?.Invoke(Run));
        }
    }

    private sealed class Wanderer() : Entity(Vector2.Zero)
    {
        protected internal override void OnStep(in StepContext context) =>
            Teleport(Position + new Vector2(Random.Range(-1f, 1f), 0f));
    }

    private sealed class Tally(Run run) : GameInstance(run)
    {
        internal int Value { get; set; }
    }

    private sealed class Reaching : Scene
    {
        private readonly Reach _reach;

        public Reaching(Reach reach)
        {
            _reach = reach;
            if (reach == Reach.Constructor)
            {
                _ = Run;
            }
            else if (reach == Reach.ComposedEntity)
            {
                _ = Add(new Wanderer()).Run;
            }
        }

        protected internal override void CollectAssets(AssetCollection assets)
        {
            if (_reach == Reach.Collection)
            {
                _ = Run;
            }
        }
    }
}
