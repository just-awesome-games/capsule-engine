using System.Numerics;
using Capsule.Physics;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Documents;

public sealed class SceneCompositionTests
{
    [Fact]
    public void EachPlacementBecomesOneEntity_InTheDocumentsOwnOrder_CarryingItsPlacementData()
    {
        SceneDocument room = SceneFixtures.Room(
            new SceneDocumentEntry("chest", new EntitySpawn(new Vector2(48f, 16f))),
            new SceneDocumentEntry("player-spawn", new EntitySpawn(new Vector2(32f, 24f))));

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
            SceneFixtures.Room(new SceneDocumentEntry("chest", new EntitySpawn(new Vector2(48f, 16f)))),
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
                new SceneDocumentEntry("turned", new EntitySpawn(Vector2.Zero) { Rotation = float.DegreesToRadians(90f) }),
                new SceneDocumentEntry("upright", new EntitySpawn(Vector2.Zero) { Rotation = float.DegreesToRadians(90f) })),
            SceneFixtures.Registry(
                ("turned", static spawn => new Turned(spawn)),
                ("upright", static spawn => new Upright(spawn))));

        Turned turned = Assert.IsType<Turned>(scene.Entities[0]);

        Assert.Equal(float.DegreesToRadians(90f), turned.RotationInBody);
        Assert.Equal(float.DegreesToRadians(90f), turned.Rotation);
        Assert.Equal(0f, scene.Entities[1].Rotation);
    }

    // A collider cannot turn. The class that adds one to a turned spawn fails there, naming both
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

    // The document supplies the scroll factor ahead of the constructor's body. A class that sets none
    // takes it, and a class that sets its own keeps it.
    [Fact]
    public void ADocumentsScrollFactor_IsTheConstructorsToKeepOrOverride()
    {
        SceneDocument document = new(
            [
                new SceneDocumentEntry("placed", new EntitySpawn(new Vector2(4f, 4f)) { ScrollFactor = Vector2.Zero }),
                new SceneDocumentEntry("placed", new EntitySpawn(new Vector2(4f, 4f))),
                new SceneDocumentEntry("fixed", new EntitySpawn(new Vector2(4f, 4f)) { ScrollFactor = new Vector2(0.5f, 1f) }),
            ]);
        Scene scene = SceneFixtures.RoomScene(
            document,
            SceneFixtures.Registry(
                ("placed", spawn => new SceneFixtures.Placed(spawn)),
                ("fixed", spawn => new ScreenFixed(spawn))));

        Assert.Equal(Vector2.Zero, scene.Entities[0].ScrollFactor);
        Assert.Equal(Vector2.One, scene.Entities[1].ScrollFactor);
        Assert.Equal(Vector2.Zero, scene.Entities[2].ScrollFactor);
    }

    // The scroll factor is applied before the body runs. A body that then attaches a collider is refused
    // as it would be for any scrolled entity, and the placement fails at construction.
    [Fact]
    public void ASpawnsFactor_IsRefusedByTheColliderTheBodyAttaches()
    {
        Assert.Throws<InvalidOperationException>(
            () => new Solid(new EntitySpawn(Vector2.Zero) { ScrollFactor = new Vector2(0.5f, 1f) }));
    }

    private sealed class Turned : Entity
    {
        internal float RotationInBody { get; }

        public Turned(EntitySpawn spawn)
            : base(spawn) => RotationInBody = Rotation;
    }

    private sealed class Upright : Entity
    {
        public Upright(EntitySpawn spawn)
            : base(spawn) => Rotation = 0f;
    }

    private sealed class ScreenFixed : Entity
    {
        public ScreenFixed(EntitySpawn spawn)
            : base(spawn) => ScrollFactor = Vector2.Zero;
    }

    private sealed class Solid : Entity
    {
        public Solid(EntitySpawn spawn)
            : base(spawn) => Add(new BoxCollider2D(new Vector2(8f, 8f)));
    }
}
