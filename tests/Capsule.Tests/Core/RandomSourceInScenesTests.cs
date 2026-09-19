using System.Numerics;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Core;

public sealed class RandomSourceInScenesTests
{
    // The source is reached, never passed: an entity and its components draw from the one their
    // scene holds, and a game supplies the seed by supplying the source.
    [Fact]
    public void AnEntityAndItsComponentsDrawFromTheirScenesSource()
    {
        RandomSource run = new(0xBEEF, 3);
        Drawer entity = new();
        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene, run: new Run(run));

        Assert.Same(run, scene.Run.Random);
        Assert.Same(run, entity.Random);
        Assert.Same(run, entity.Probe.Random);
        Assert.Same(run, entity.SeenOnStart);
    }

    [Fact]
    public void ASceneSimulationWithNoSourceGetsTheDefaultStream()
    {
        Scene scene = new();

        using SceneSimulation simulation = new(scene);

        Assert.Equal(RandomSource.DefaultSeed, scene.Run.Random.Seed);
        Assert.Equal(0ul, scene.Run.Random.Stream);
    }

    // No throwaway source stands in before the run's: a scene that has not started has none, so a
    // draw that would silently ignore the configured seed fails instead.
    [Fact]
    public void ReadingTheSourceBeforeTheSceneStartsThrows()
    {
        Scene scene = new();
        Drawer entity = new();

        InvalidOperationException fromDetached = Assert.Throws<InvalidOperationException>(() => entity.Random);
        InvalidOperationException fromComponent = Assert.Throws<InvalidOperationException>(() => entity.Probe.Random);

        scene.Add(entity);

        InvalidOperationException fromScene = Assert.Throws<InvalidOperationException>(() => scene.Run);
        InvalidOperationException fromAttached = Assert.Throws<InvalidOperationException>(() => entity.Random);

        foreach (InvalidOperationException failure in new[] { fromDetached, fromComponent, fromScene, fromAttached })
        {
            Assert.Contains("OnStart", failure.Message, StringComparison.Ordinal);
        }
    }

    // The composing path the default instance hid: a document's entities are attached inside the
    // scene's constructor, so their OnAddedToScene runs before any source exists.
    [Fact]
    public void ADocumentComposedEntityDiscoversTheRunsSourceInOnStart()
    {
        RandomSource run = new(0x5EED);
        SceneFixtures.SpawnScene scene = new(
            SceneFixtures.Registry(("prober", static spawn => new SpawnedProber(spawn))),
            new EntitySpawn(1, "prober", Vector2.Zero));

        SpawnedProber prober = Assert.IsType<SpawnedProber>(scene.Entities[0]);

        Assert.NotNull(prober.AddedFailure);
        Assert.Contains("OnStart", prober.AddedFailure!.Message, StringComparison.Ordinal);

        using SceneSimulation simulation = new(scene, run: new Run(run));

        Assert.Same(run, prober.SeenOnStart);
        Assert.Equal(new RandomSource(0x5EED).NextFloat(), prober.FirstDraw);
    }

    private sealed class Drawer : Entity
    {
        internal Drawer()
            : base(Vector2.Zero)
        {
            Probe = new ProbeComponent();
            Add(Probe);
        }

        internal ProbeComponent Probe { get; }

        internal RandomSource? SeenOnStart { get; private set; }

        protected internal override void OnStart() => SeenOnStart = Random;
    }

    private sealed class ProbeComponent : Component;

    private sealed class SpawnedProber : Entity
    {
        internal SpawnedProber(EntitySpawn spawn)
            : base(spawn)
        {
        }

        internal InvalidOperationException? AddedFailure { get; private set; }

        internal RandomSource? SeenOnStart { get; private set; }

        internal float FirstDraw { get; private set; }

        protected internal override void OnAddedToScene()
        {
            try
            {
                _ = Random;
            }
            catch (InvalidOperationException failure)
            {
                AddedFailure = failure;
            }
        }

        protected internal override void OnStart()
        {
            SeenOnStart = Random;
            FirstDraw = Random.NextFloat();
        }
    }
}
