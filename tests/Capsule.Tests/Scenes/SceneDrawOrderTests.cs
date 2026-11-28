using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tiles;
using static Capsule.Tests.Scenes.EntityHierarchyFixtures;

namespace Capsule.Tests.Scenes;

public sealed class SceneDrawOrderTests
{
    [Fact]
    public void ARuntimeEntityInALowerBand_DrawsUnderADocumentPlacedOne()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.Room(new SceneDocumentEntry("prop", new EntitySpawn(Vector2.Zero) { ZIndex = 10 })),
            SceneFixtures.Registry(("prop", Spawns(2))));

        Node background = new(Vector2.Zero) { ZIndex = -5 };
        background.Add(Tag(1));
        scene.Add(background);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    [Fact]
    public void EntitiesSharingABand_KeepInsertionAndAttachmentOrder()
    {
        Node first = new(Vector2.Zero) { ZIndex = 3 };
        first.Add(Tag(11));
        first.Add(Tag(12));

        Node second = new(Vector2.Zero) { ZIndex = 3 };
        second.Add(Tag(21));
        second.Add(Tag(22));

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([11, 12, 21, 22], Order(simulation));
    }

    [Fact]
    public void ABandChangedAfterAttaching_ReordersTheFrame()
    {
        Node first = new(Vector2.Zero);
        first.Add(Tag(1));
        Node second = new(Vector2.Zero);
        SpriteRenderer offset = Tag(2);
        second.Add(offset);

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);

        using SimulationHost run = new(scene);
        run.Step();
        Assert.Equal([1, 2], Order(run.Simulation));

        first.ZIndex = 5;
        run.Step();
        Assert.Equal([2, 1], Order(run.Simulation));

        offset.ZIndex = 9;
        run.Step();
        Assert.Equal([1, 2], Order(run.Simulation));
    }

    [Fact]
    public void ARendererOffset_ComposesWithItsEntitysBand()
    {
        Node banded = new(Vector2.Zero) { ZIndex = 5 };
        banded.Add(Tag(2));

        Node split = new(Vector2.Zero);
        split.Add(Tag(1));
        split.Add(Tag(3, zIndex: 10));

        SceneFixtures.HookScene scene = new();
        scene.Add(banded);
        scene.Add(split);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2, 3], Order(simulation));
    }

    // The sum is widened, not wrapped. Wrapped, int.MaxValue + 1 would draw under everything.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ASumPastAnInt_StillDrawsOverTheBandBelowIt(bool beyondFirst)
    {
        Node ceiling = new(Vector2.Zero) { ZIndex = int.MaxValue };
        ceiling.Add(Tag(1));

        Node beyond = new(Vector2.Zero) { ZIndex = int.MaxValue };
        beyond.Add(Tag(2, zIndex: 1));

        SceneFixtures.HookScene scene = new();
        scene.Add(beyondFirst ? beyond : ceiling);
        scene.Add(beyondFirst ? ceiling : beyond);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    // The draw bookkeeping is the scene's, not the renderer's.
    [Fact]
    public void AnEntityMovedToAnotherSimulation_DrawsInThatSimulationsFirstFrame()
    {
        Node traveller = new(Vector2.Zero);
        traveller.Add(Tag(1));

        SceneFixtures.HookScene origin = new();
        origin.Add(traveller);
        using (SceneSimulation first = new(origin))
        {
            Assert.Equal([1], Order(first));
            origin.Remove(traveller);
        }

        SceneFixtures.HookScene destination = new();
        destination.Add(traveller);
        using SceneSimulation second = new(destination);

        Assert.Equal([1], Order(second));
    }

    // A constructor that sets no band takes the authored one, and one that sets its own keeps it.
    [Theory]
    [InlineData(null, 0, Banded.Band)]
    [InlineData(0, 0, Banded.Band)]
    [InlineData(-4, -4, Banded.Band)]
    public void AnAuthoredBand_IsTheConstructorsToKeepOrOverride(int? authored, int taken, int kept)
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.Room(
                new SceneDocumentEntry("placed", new EntitySpawn(Vector2.Zero) { ZIndex = authored }),
                new SceneDocumentEntry("banded", new EntitySpawn(Vector2.Zero) { ZIndex = authored })),
            SceneFixtures.Registry(
                ("placed", spawn => new SceneFixtures.Placed(spawn)),
                ("banded", spawn => new Banded(spawn))));

        Assert.Equal(taken, scene.Entities[0].ZIndex);
        Assert.Equal(kept, scene.Entities[1].ZIndex);
    }

    private static EntitySpawner Spawns(int tag) => spawn =>
    {
        SceneFixtures.Placed marker = new(spawn);
        marker.Add(Tag(tag));

        return marker;
    };

    /// <summary>An entity that bands itself over whatever its placement authored.</summary>
    private sealed class Banded : Entity
    {
        internal const int Band = 12;

        internal Banded(EntitySpawn spawn)
            : base(spawn) => ZIndex = Band;
    }
}
