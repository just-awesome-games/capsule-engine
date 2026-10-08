using System.Numerics;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using static Capsule.Tests.Scenes.SceneFixtures;

namespace Capsule.Tests.Scenes;

// A scene that has not started composes what it is given and joins it as it starts.
public sealed class SceneJoinTests
{
    private static readonly AudioClip Chime = new("chime", ".wav", 0.5);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AComposedEntity_ReadsTheRunAndPlaysItsSourceFromItsJoinHook(bool fromDocument)
    {
        Chimer composed = new();
        Scene scene = fromDocument
            ? RoomScene(Room(new SceneDocumentEntry("chimer")), Registry(("chimer", spawn => composed = new Chimer(spawn))))
            : new Scene();
        if (!fromDocument)
        {
            scene.Add(composed);
        }

        Assert.False(composed.Source.IsPlaying);
        Assert.True(scene.CollectPreloads().Contains(Chime));

        using SimulationHost host = new(scene);

        Assert.True(composed.Source.IsPlaying);
        Assert.Equal(AudioCommandKind.Play, host.Run.Audio.Commands[0].Kind);
    }

    [Fact]
    public void AnEntityAddedThenRemovedBeforeTheSceneStarts_FiresNoHookAndNeverSteps()
    {
        List<string> log = [];
        EntityPool<Recorder> pool = new(() => new Recorder("pooled", log), capacity: 1);
        Scene scene = new();
        Recorder root = new("root", log, logsStart: true);
        Recorder child = new("child", log, logsStart: true) { Parent = root };
        Recorder pooled = pool.Take();
        scene.Add(root);
        scene.Add(pooled);

        Assert.Equal([root, child, pooled], scene.Entities.ToArray());
        Assert.Same(scene, child.SceneOrNull);

        scene.Remove(root);
        scene.Remove(pooled);

        Assert.Empty(scene.Entities.ToArray());
        Assert.Null(root.SceneOrNull);
        Assert.Null(child.SceneOrNull);
        Assert.Equal(1, pool.Available);

        using SceneSimulation simulation = new(scene);
        simulation.Step(Step());

        Assert.Empty(log);
    }

    [Fact]
    public void ASceneReleasedBeforeItStarts_FiresNoEntityOrComponentHook()
    {
        List<string> log = [];
        EntityPool<Recorder> pool = new(() => new Recorder("pooled", log), capacity: 1);
        Hooked hooked = new(log);
        Scene scene = RoomScene(
            Room(new SceneDocumentEntry("hooked")),
            Registry(("hooked", spawn => new Hooked(spawn, log))));
        scene.Add(hooked);
        scene.Add(pool.Take());

        scene.Abandon();

        Assert.Empty(scene.Entities.ToArray());
        Assert.Null(hooked.SceneOrNull);
        Assert.Equal(1, pool.Available);
        Assert.Empty(log);
    }

    [Fact]
    public void EveryComposedEntity_JoinsAfterThePreloadAndBeforeAnyStart()
    {
        List<string> log = [];
        PoolScene scene = new(log);
        scene.Add(new Joiner("a", log, spawnsWhenJoined: true));
        scene.Add(new Joiner("b", log, spawnsWhenJoined: false));
        scene.Add(new Recorder("c", log, logsStart: true));

        Assert.Empty(log);

        using SceneSimulation simulation = new(scene);

        // The joiners saw the declared pool and a run. A join hook that adds attaches at once, and the
        // batch starts together with it.
        Assert.Equal(
            ["a:pool 2", "a:run", "taken+", "b:pool 1", "b:run", "c+", "a!", "b!", "c!", "taken!", "scene!"],
            log);
    }

    [Fact]
    public void ASceneConstructor_FindsTheEntitiesItComposed()
    {
        EntityRegistry registry = Registry(("placed", static spawn => new Placed(spawn)));

        Searching scene = new(Content(
            Room(new SceneDocumentEntry("placed"), new SceneDocumentEntry("placed")),
            registry));

        Assert.Equal(2, scene.Placements);
        Assert.Same(scene, scene.Added.SceneOrNull);
        Assert.Same(scene.Added, scene.Found);
        Assert.Equal(3, scene.Entities.Length);
    }

    [Fact]
    public void ASceneThatHasNotStarted_RefusesItsCollisionWorld()
    {
        Scene scene = new();
        scene.Add(new Wall());

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => scene.Collision);
        Assert.Contains("OnStart", refused.Message);

        using SceneSimulation simulation = new(scene);

        CollisionFilter all = scene.Collision.CreateFilter(CollisionWorld2D.DefaultLayerName);
        Assert.True(scene.Collision.Raycast(new Vector2(0f, -32f), Vector2.UnitY, 64f, all, out _));
    }

    [Fact]
    public void AComponentAddedToAReusedEntityBeforeTheSceneStarts_StartsAsTheEntityJoins()
    {
        List<string> log = [];
        Recorder reused = new("reused", log);
        Scene earlier = new();
        using (new SceneSimulation(earlier))
        {
            earlier.Add(reused);
            earlier.Remove(reused);
        }

        log.Clear();
        Scene scene = new();
        scene.Add(reused);
        reused.Add(new Starting(log));

        Assert.Empty(log);

        using SceneSimulation simulation = new(scene);

        Assert.Equal(["reused+", "component!"], log);
    }

    [Fact]
    public void AJoinHookThatRemovesItsEntity_EndsTheJoinOfItsSubtree()
    {
        List<string> log = [];
        Quitter root = new();
        Recorder child = new("child", log) { Parent = root };
        Scene scene = new();
        scene.Add(root);

        using SceneSimulation simulation = new(scene);

        Assert.Null(root.SceneOrNull);
        Assert.Null(child.SceneOrNull);
        Assert.Same(root, child.Parent);
        Assert.Empty(scene.Entities.ToArray());
        Assert.Empty(log);
    }

    [Fact]
    public void ABodysMasksWrittenBeforeTheSceneStarts_ResolveAsItJoins()
    {
        Scene scene = SceneFixtures.Terrain("....", "....", "####");
        SceneFixtures.Body body = new(new Vector2(8f, 8f));
        scene.Add(body);
        body.Mover.BlockedBy = new("solid");
        body.Mover.MovedBy = new("solid");

        using SceneSimulation simulation = new(scene);
        MoveResult2D result = body.Mover.Move(new Vector2(0f, 60f));

        Assert.True(result.Blocked);
        Assert.Equal(24f, body.Position.Y, Tests.Physics.CollisionFixtures.Tolerance);
    }

    private sealed class Wall : Entity
    {
        internal Wall()
            : base(Vector2.Zero) => Add(new BoxCollider2D(new Vector2(16f, 16f)));
    }

    private sealed class Chimer : Entity
    {
        internal AudioSource Source { get; }

        internal Chimer()
            : base(Vector2.Zero) => Add(Source = new AudioSource(Chime));

        internal Chimer(EntitySpawn spawn)
            : base(spawn) => Add(Source = new AudioSource(Chime));

        protected internal override void OnAddedToScene() => Source.Play();
    }

    private sealed class Starting(List<string> log) : Component
    {
        protected internal override void OnStart() => log.Add("component!");
    }

    // Leaves the scene from its own join hook, before its children join.
    private sealed class Quitter() : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene() => Scene.Remove(this);
    }

    private sealed class Hooked : Entity
    {
        private readonly List<string> _log;

        internal Hooked(List<string> log)
            : base(Vector2.Zero) => Setup(_log = log);

        internal Hooked(EntitySpawn spawn, List<string> log)
            : base(spawn) => Setup(_log = log);

        protected internal override void OnAddedToScene() => _log.Add("hooked+");

        protected internal override void OnRemovedFromScene() => _log.Add("hooked-");

        private void Setup(List<string> log) => Add(new Joined(log));
    }

    private sealed class Joined(List<string> log) : Component
    {
        protected internal override void OnAddedToScene() => log.Add("component+");

        protected internal override void OnRemovedFromScene() => log.Add("component-");
    }

    private sealed class PoolScene(List<string> log) : Scene
    {
        protected internal override void CollectAssets(AssetCollection assets) => assets.Pool<Taken>(capacity: 2);

        protected override void OnStart() => log.Add("scene!");
    }

    private sealed class Taken(List<string> log) : Entity(Vector2.Zero)
    {
        internal List<string> Log { get; set; } = log;

        public Taken()
            : this([])
        {
        }

        protected internal override void OnStart() => Log.Add("taken!");

        protected internal override void OnAddedToScene() => Log.Add("taken+");

        protected internal override void OnRemovedFromScene() => Log.Add("taken-");
    }

    private sealed class Joiner(string name, List<string> log, bool spawnsWhenJoined) : Entity(Vector2.Zero)
    {
        protected internal override void OnAddedToScene()
        {
            log.Add($"{name}:pool {Scene.Pool<Taken>().Available}");
            log.Add($"{name}:run");
            _ = Run;

            if (spawnsWhenJoined)
            {
                Taken taken = Scene.Pool<Taken>().Take();
                taken.Log = log;
                Scene.Add(taken);
            }
        }

        protected internal override void OnStart() => log.Add($"{name}!");
    }

    private sealed class Searching : Scene
    {
        internal int Placements { get; private set; }

        internal Drifter Added { get; }

        internal Drifter Found { get; }

        internal Searching(SceneContent content)
            : base(content)
        {
            Added = new Drifter();
            Add(Added);
            foreach (Placed _ in FindAll<Placed>())
            {
                Placements++;
            }

            Found = FindSingle<Drifter>();
        }
    }
}
