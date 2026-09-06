using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Spawning;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes;

public sealed class SceneDrawOrderTests
{
    [Fact]
    public void ARuntimeEntityInALowerBand_DrawsUnderADocumentPlacedOne()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "prop", 0f, 0f, ZIndex: 10)),
            SceneFixtures.Registry(("prop", Spawns(2))));

        Marker background = new() { ZIndex = -5 };
        background.Add(Tag(1));
        scene.Add(background);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2], Order(simulation));
    }

    [Fact]
    public void EntitiesSharingABand_KeepInsertionAndAttachmentOrder()
    {
        Marker first = new() { ZIndex = 3 };
        first.Add(Tag(11));
        first.Add(Tag(12));

        Marker second = new() { ZIndex = 3 };
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
        Marker first = new();
        first.Add(Tag(1));
        Marker second = new();
        SpriteRenderer offset = Tag(2);
        second.Add(offset);

        SceneFixtures.HookScene scene = new();
        scene.Add(first);
        scene.Add(second);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        Assert.Equal([1, 2], Order(simulation));

        first.ZIndex = 5;
        simulation.Step(SceneFixtures.Step());
        Assert.Equal([2, 1], Order(simulation));

        offset.ZIndex = 9;
        simulation.Step(SceneFixtures.Step());
        Assert.Equal([1, 2], Order(simulation));
    }

    [Fact]
    public void ARendererOffset_ComposesWithItsEntitysBand()
    {
        Marker banded = new() { ZIndex = 5 };
        banded.Add(Tag(2));

        Marker split = new();
        split.Add(Tag(1));
        split.Add(Tag(3, zIndex: 10));

        SceneFixtures.HookScene scene = new();
        scene.Add(banded);
        scene.Add(split);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal([1, 2, 3], Order(simulation));
    }

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
            SceneFixtures.Registry(("prop", Spawns(1))));

        Assert.Equal(-20, Assert.IsType<TileMap>(scene.Entities[0]).ZIndex);
        Assert.Equal(7, scene.Entities[1].ZIndex);
    }

    private static EntitySpawner Spawns(int tag) => _ =>
    {
        Marker marker = new();
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

    private sealed class Marker() : Entity(Vector2.Zero);
}
