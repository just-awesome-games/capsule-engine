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
            new SceneDocumentEntry("chest", 48f, 16f),
            new SceneDocumentEntry("player-spawn", 32f, 24f));

        Scene scene = SceneFixtures.RoomScene(
            room,
            SceneFixtures.Registry(
                ("chest", static spawn => new SceneFixtures.Placed(spawn)),
                ("player-spawn", static spawn => new SceneFixtures.Placed(spawn))));

        Entity[] entities = scene.Entities.ToArray();

        Assert.Equal(2, entities.Length);
        Assert.Equal(
            new EntitySpawn(new Vector2(48f, 16f)) { Type = "chest" },
            Assert.IsType<SceneFixtures.Placed>(entities[0]).Spawn);
        Assert.Equal(
            new EntitySpawn(new Vector2(32f, 24f)) { Type = "player-spawn" },
            Assert.IsType<SceneFixtures.Placed>(entities[1]).Spawn);

        // A spawn opens where it was placed rather than sliding in from the render origin.
        Assert.Equal(entities[0].Position, entities[0].PreviousTransform.Position);
    }

    // A scene of entities alone draws no terrain and spans nothing until it sets its own size.
    [Fact]
    public void ADocumentWithNoTerrain_ComposesWithNoTileMapAndNoSize()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.Room(new SceneDocumentEntry("chest", 48f, 16f)),
            SceneFixtures.Registry(("chest", static spawn => new SceneFixtures.Placed(spawn))));

        Assert.Null(scene.FindFirst<TileMap>());
        Assert.Equal(Vector2.Zero, scene.Size);
        Assert.IsType<SceneFixtures.Placed>(Assert.Single(scene.Entities.ToArray()));
    }

    // The document writes degrees, and the entity reads radians before its own body runs.
    [Fact]
    public void APlacementsRotation_LandsBeforeTheBody_AndABodyWriteWins()
    {
        Scene scene = SceneFixtures.RoomScene(
            SceneFixtures.Room(
                new SceneDocumentEntry("turned", 0f, 0f, RotationDegrees: 90f),
                new SceneDocumentEntry("upright", 0f, 0f, RotationDegrees: 90f)),
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
