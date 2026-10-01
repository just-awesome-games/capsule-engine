using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Documents;

public sealed class SceneCompositionTests
{
    [Fact]
    public void EachPlacementBecomesOneEntity_InTheDocumentsOwnOrder_CarryingItsPlacementData()
    {
        SceneDocument room = SceneFixtures.Room(
            new EntityPlacement(2, "chest", 48f, 16f),
            new EntityPlacement(1, "player-spawn", 32f, 24f));

        Scene scene = SceneFixtures.RoomScene(
            room,
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player-spawn", static spawn => new SceneFixtures.Placed(spawn))));

        Entity[] entities = scene.Entities.ToArray();

        Assert.Equal(3, entities.Length);
        Assert.IsType<TileMap>(entities[0]);
        Assert.Equal(
            new EntitySpawn(new Vector2(48f, 16f)) { Id = 2, Type = "chest" },
            Assert.IsType<SceneFixtures.Placed>(entities[1]).Spawn);
        Assert.Equal(
            new EntitySpawn(new Vector2(32f, 24f)) { Id = 1, Type = "player-spawn" },
            Assert.IsType<SceneFixtures.Placed>(entities[2]).Spawn);

        // A spawn opens where it was placed rather than sliding in from the render origin.
        Assert.Equal(entities[1].Position, entities[1].PreviousTransform.Position);
    }

    [Fact]
    public void ASubclassPassesItsContentThrough_AndReachesTheTerrainItComposed()
    {
        SceneDocument room = SceneFixtures.Room(new EntityPlacement(1, "chest", 48f, 16f));

        SceneFixtures.Room01 scene = new(SceneFixtures.Content(
            room,
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn)))));

        Assert.Same(scene.Terrain, scene.Entities[0]);
        Assert.Equal(new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize), scene.Size);
        Assert.IsType<SceneFixtures.Placed>(scene.Entities[1]);
    }

    // A scene of entities alone draws no terrain and spans nothing until it sets its own size.
    [Fact]
    public void ADocumentWithNoTerrain_ComposesWithNoTileMapAndNoSize()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(new EntityPlacement(1, "chest", 48f, 16f)),
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn))));

        Assert.Null(scene.FindFirst<TileMap>());
        Assert.Equal(Vector2.Zero, scene.Size);
        Assert.IsType<SceneFixtures.Placed>(Assert.Single(scene.Entities.ToArray()));
    }

    [Fact]
    public void TileMapsAndEntities_ComposeInDocumentOrder()
    {
        SceneDocument document = new(
            [
                new TileMapPlacement(3, SceneFixtures.RoomGrid()),
                new EntityPlacement(1, "chest", 32f, 24f),
                new TileMapPlacement(4, SceneFixtures.RoomGrid()),
                new EntityPlacement(2, "chest", 48f, 16f),
            ],
            5);

        Scene scene = SceneFixtures.RoomScene(
            document,
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn))));

        Assert.Equal(
            [typeof(TileMap), typeof(SceneFixtures.Placed), typeof(TileMap), typeof(SceneFixtures.Placed)],
            scene.Entities.ToArray().Select(static entity => entity.GetType()));
    }

    // The document writes degrees, and the entity reads radians before its own body runs.
    [Fact]
    public void APlacementsRotation_LandsBeforeTheBody_AndABodyWriteWins()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.RoomWithoutTerrain(
                new EntityPlacement(1, "turned", 0f, 0f, RotationDegrees: 90f),
                new EntityPlacement(2, "upright", 0f, 0f, RotationDegrees: 90f)),
            SceneFixtures.Registry(
                ("turned", static spawn => new Turned(spawn)),
                ("upright", static spawn => new Upright(spawn))));

        Turned turned = Assert.IsType<Turned>(scene.Entities[0]);

        Assert.Equal(float.DegreesToRadians(90f), turned.RotationInBody);
        Assert.Equal(float.DegreesToRadians(90f), turned.Rotation);
        Assert.Equal(0f, scene.Entities[1].Rotation);
    }

    // A collider cannot turn, so the class that adds one to a turned spawn fails there, naming both
    // ways out.
    [Fact]
    public void ATurnedSpawn_OnAClassAddingACollider_FailsNamingBothFixes()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new Solid(new EntitySpawn(Vector2.Zero) { Rotation = 1f }));

        Assert.Contains("Clear that rotation, whether placed or set in code", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "set Rotation = 0 in the Solid constructor before adding the BoxCollider2D and read spawn.Rotation",
            error.Message,
            StringComparison.Ordinal);
    }

    private sealed class Turned : Entity
    {
        public Turned(EntitySpawn spawn)
            : base(spawn) => RotationInBody = Rotation;

        internal float RotationInBody { get; }
    }

    private sealed class Upright : Entity
    {
        public Upright(EntitySpawn spawn)
            : base(spawn) => Rotation = 0f;
    }

    private sealed class Solid : Entity
    {
        public Solid(EntitySpawn spawn)
            : base(spawn) => Add(new BoxCollider2D(new Vector2(8f, 8f)));
    }
}
