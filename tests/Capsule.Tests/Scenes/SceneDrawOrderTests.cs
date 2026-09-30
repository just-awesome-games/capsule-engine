using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tiles;

namespace Capsule.Tests.Scenes;

public sealed class SceneDrawOrderTests
{
    [Fact]
    public void ARuntimeEntityInALowerBand_DrawsUnderADocumentPlacedOne()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "prop", 0f, 0f, ZIndex: 10)),
            SceneFixtures.Registry(("prop", Spawns(2))));

        Layered background = new() { ZIndex = -5 };
        background.Add(Tag(1));
        scene.Add(background);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    [Fact]
    public void EntitiesSharingABand_KeepInsertionAndAttachmentOrder()
    {
        Layered first = new() { ZIndex = 3 };
        first.Add(Tag(11));
        first.Add(Tag(12));

        Layered second = new() { ZIndex = 3 };
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
        Layered first = new();
        first.Add(Tag(1));
        Layered second = new();
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
        Layered banded = new() { ZIndex = 5 };
        banded.Add(Tag(2));

        Layered split = new();
        split.Add(Tag(1));
        split.Add(Tag(3, zIndex: 10));

        SceneFixtures.HookScene scene = new();
        scene.Add(banded);
        scene.Add(split);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2, 3], Order(simulation));
    }

    // The sum is widened, not wrapped: an int would carry int.MaxValue + 1 round to int.MinValue
    // and draw the nearer renderer under everything.
    [Fact]
    public void ASumPastAnInt_StillDrawsOverTheBandBelowIt()
    {
        Layered ceiling = new() { ZIndex = int.MaxValue };
        ceiling.Add(Tag(1));

        Layered beyond = new() { ZIndex = int.MaxValue };
        beyond.Add(Tag(2, zIndex: 1));

        SceneFixtures.HookScene scene = new();
        scene.Add(ceiling);
        scene.Add(beyond);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    // The same pair the other way round: the larger key draws last whatever order they arrived in.
    [Fact]
    public void ASumPastAnInt_SortsAboveItsBandFromEitherInsertionOrder()
    {
        Layered beyond = new() { ZIndex = int.MaxValue };
        beyond.Add(Tag(2, zIndex: 1));

        Layered ceiling = new() { ZIndex = int.MaxValue };
        ceiling.Add(Tag(1));

        SceneFixtures.HookScene scene = new();
        scene.Add(beyond);
        scene.Add(ceiling);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    // The draw bookkeeping is the scene's, not the renderer's: an entity that drew under one
    // simulation must not be suppressed by the next one's first frame.
    [Fact]
    public void AnEntityMovedToAnotherSimulation_DrawsInThatSimulationsFirstFrame()
    {
        Layered traveller = new();
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

    // The engine-built tile map takes its band as composed; a game entity takes it through its
    // spawn.
    [Fact]
    public void AnAuthoredBand_LandsOnEveryComposedEntity()
    {
        Scene scene = SceneFixtures.RoomScene(
            new SceneDocument(
                [
                    new TileMapPlacement(SceneFixtures.TerrainId, SceneFixtures.RoomGrid(), ZIndex: -20),
                    new EntityPlacement(1, "prop", 0f, 0f, ZIndex: 7),
                ],
                SceneFixtures.TerrainId + 1),
            SceneFixtures.Registry(("prop", spawn => new SceneFixtures.Placed(spawn))));

        Assert.Equal(-20, Assert.IsType<TileMap>(scene.Entities[0]).ZIndex);
        Assert.Equal(7, scene.Entities[1].ZIndex);
    }

    // The document supplies a default and the class is the authority: a constructor that sets
    // nothing takes the authored band — 0 like any other — and one that sets its own keeps it,
    // whether or not the placement authors one.
    [Theory]
    [InlineData(null, 0, Banded.Band)]
    [InlineData(0, 0, Banded.Band)]
    [InlineData(-4, -4, Banded.Band)]
    public void AnAuthoredBand_IsTheConstructorsToKeepOrOverride(int? authored, int taken, int kept)
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(
                new EntityPlacement(1, "placed", 0f, 0f, ZIndex: authored),
                new EntityPlacement(2, "banded", 0f, 0f, ZIndex: authored)),
            SceneFixtures.Registry(
                ("placed", spawn => new SceneFixtures.Placed(spawn)),
                ("banded", spawn => new Banded(spawn))));

        Assert.Equal(taken, scene.Entities[0].ZIndex);
        Assert.Equal(kept, scene.Entities[1].ZIndex);
    }

    private static EntitySpawner Spawns(int tag) => spawn =>
    {
        Layered marker = new(spawn);
        marker.Add(Tag(tag));

        return marker;
    };

    // The offset is the renderer's identity in the frame: it draws its entity at the origin, so
    // the intent's X is the tag.
    private static SpriteRenderer Tag(int tag, int zIndex = 0) =>
        new(SceneFixtures.Frame(1, 1)) { Offset = new Vector2(tag, 0f), ZIndex = zIndex };

    private static int[] Order(SceneSimulation simulation)
    {
        ReadOnlySpan<SpriteIntent> sprites = simulation.View.Sprites;
        int[] tags = new int[sprites.Length];
        for (int index = 0; index < tags.Length; index++)
        {
            tags[index] = (int)sprites[index].Position.X;
        }

        return tags;
    }

    private sealed class Layered : Entity
    {
        internal Layered()
            : base(Vector2.Zero)
        {
        }

        internal Layered(EntitySpawn spawn)
            : base(spawn)
        {
        }
    }

    /// <summary>An entity that bands itself over whatever its placement authored.</summary>
    private sealed class Banded : Entity
    {
        internal const int Band = 12;

        internal Banded(EntitySpawn spawn)
            : base(spawn) => ZIndex = Band;
    }
}
