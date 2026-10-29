using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;
using Capsule.Tiles;

namespace Capsule.Tests.Tiles;

public sealed class TileMapTests
{
    [Fact]
    public void TerrainIsBakedFromTheGrid_AndDrawsAheadOfWhatWasAddedAfterIt()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(7, 9));
        drifter.Add(new SpriteRenderer(SceneFixtures.Frame(4, 8)));

        Scene scene = new();
        scene.Add(new TileMap(SceneFixtures.RoomGrid()));
        scene.Add(drifter);
        Vector2 room = new(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize);
        SceneFixtures.Open(scene, room / 2f, room);
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Sprites.Length);

        SpriteIntent terrain = simulation.View.Sprites[0];
        Assert.Equal(new Vector2(SceneFixtures.TileSize * 1.5f, SceneFixtures.TileSize / 2f), terrain.Position);
        Assert.Equal(terrain.Position, terrain.PreviousPosition);
        Assert.Equal(new Vector2(SceneFixtures.TileSize, SceneFixtures.TileSize), terrain.Size);
        Assert.Equal(SceneFixtures.Atlas, terrain.Sprite.Texture);
        Assert.Equal(ColorRgba.White, terrain.Color);
        Assert.False(terrain.FlipX);
        Assert.False(terrain.FlipY);

        Assert.Equal(new Vector2(8, 9), simulation.View.Sprites[1].Position);
    }

    // The sprites are world coordinates. A position write would move nothing and mean nothing.
    [Fact]
    public void ATilemapRefusesAPositionWrite()
    {
        TileMap terrain = new(SceneFixtures.RoomGrid());

        Assert.Throws<InvalidOperationException>(() => terrain.Position = new Vector2(1000, 1000));
        Assert.Throws<InvalidOperationException>(() => terrain.Teleport(new Vector2(1000, 1000)));
        Assert.Equal(Vector2.Zero, terrain.Position);
    }

    [Fact]
    public void ATilemapIsBuiltFromAGridAlone_WithNoDocumentAnywhere()
    {
        TileGrid grid = new(
            8,
            2,
            1,
            [TileGrid.EmptyTile, new TileType { Name = "solid", Cell = 3 }],
            [0, 1],
            SceneFixtures.Atlas,
            2);

        TileMap tiles = new(grid);
        Scene scene = new();
        scene.Add(tiles);
        SceneFixtures.Open(scene, tiles.Size / 2f, tiles.Size);
        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(8, tiles.TileSize);
        Assert.Equal(new Vector2(16, 8), tiles.Size);
        Assert.Equal("solid", tiles.TileAt(1, 0).Name);
        Assert.Equal(
            new TextureRegion(8, 8, 8, 8),
            Assert.Single(simulation.View.Sprites.ToArray()).Sprite.Region);
    }

    [Fact]
    public void ATilemapsMaterial_DrawsEveryTile_AndLoadsWithTheScene()
    {
        Material stone = new(new Shader("effects/stone"));
        TileGrid grid = new(
            8,
            2,
            1,
            [TileGrid.EmptyTile, new TileType { Name = "solid", Cell = 3 }],
            [1, 1],
            SceneFixtures.Atlas,
            2);

        TileMap tiles = new(grid) { Material = stone };
        Scene scene = new();
        scene.Add(tiles);
        SceneFixtures.Open(scene, tiles.Size / 2f, tiles.Size);
        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Sprites.Length);
        Assert.Equal([new MaterialRun(0, stone)], simulation.View.MaterialRuns.ToArray());
        Assert.Equal([stone.Shader], scene.CollectAssetPreloads(entryPayload: null).Shaders);
    }

    [Fact]
    public void TerrainEmitsOnlyTilesCrossingTheCamera()
    {
        Scene scene = new();
        scene.Camera.Center = new Vector2(12, 4);
        scene.Camera.ViewportSize = new Vector2(8, 8);
        scene.Add(new TileMap(Strip()));

        SceneSimulation simulation = new(scene);

        SpriteIntent tile = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(new Vector2(12, 4), tile.Position);
    }

    [Fact]
    public void TerrainEmitsNothingBeforeTheCameraOpens()
    {
        Scene scene = new();
        scene.Add(new TileMap(SceneFixtures.RoomGrid()));

        SceneSimulation simulation = new(scene);

        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    [Fact]
    public void ACelllessSemanticTile_RemainsQueryableWithoutEmittingASprite()
    {
        TileGrid grid = new(
            tileSize: 8,
            width: 1,
            height: 1,
            [TileGrid.EmptyTile, new TileType { Name = "hazard" }],
            [1]);
        TileMap tiles = new(grid);
        Scene scene = new();
        scene.Add(tiles);

        SceneSimulation simulation = new(scene);

        Assert.Equal("hazard", tiles.TileAt(0, 0).Name);
        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    // Clearing a solid cell changes what the map collides as and nothing else. The body standing on it
    // falls on its next move, the reporting collider exits at the next settle, the face the cell hid
    // on its neighbour stops a sweep, and the grid the map was built from still reads as authored.
    [Fact]
    public void SetTile_ChangesTheMapsCollision_AndLeavesTheGridItWasBuiltFromAlone()
    {
        TileGrid grid = SceneFixtures.TerrainGrid("....", "....", ".##.");
        TileMap map = SceneFixtures.Colliding(grid);
        (SceneSimulation simulation, SceneFixtures.Body body) = FloorBody(map, out List<ColliderContact2D> exits);
        using SceneSimulation run = simulation;
        Assert.Single(body.Collider.Touching.ToArray());

        map.RemoveTile(1, 2);

        Assert.Equal(TileGrid.EmptyTileName, map.TileAt(1, 2).Name);
        simulation.Step(SceneFixtures.Step(1));
        Assert.Single(exits);

        body.Mover.Move(new Vector2(0f, 4f));
        Assert.False(body.Mover.IsOnFloor);
        AssertSweepStopsAtWall(simulation.Scene);

        Assert.Equal("solid", new TileMap(grid).TileAt(1, 2).Name);

        ArgumentException unknown = Assert.Throws<ArgumentException>(() => map.SetTile(0, 0, "lava"));
        Assert.Contains("lava", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("empty, solid", unknown.Message, StringComparison.Ordinal);
    }

    // A map without a collider is decoration, whatever its palette's layers. A collider answers at the
    // authored cells, and it and a scroll factor refuse each other in either order.
    [Fact]
    public void ADecorativeMapScrolls_AndAColliderThenRefusesIt()
    {
        TileMap map = new(SceneFixtures.TerrainGrid("#")) { ScrollFactor = new Vector2(0.5f, 1f) };
        Scene scene = new Scene().Started();
        scene.Add(map);
        TileMapCollider2D collider = new();

        Assert.Empty(scene.Collision.Grids.ToArray());
        Assert.Equal("solid", map.TileAt(0, 0).Layer);
        InvalidOperationException added = Assert.Throws<InvalidOperationException>(() => map.Add(collider));
        Assert.Contains("Remove the TileMapCollider2D, or set the scroll factor to one", added.Message, StringComparison.Ordinal);
        Assert.Null(collider.Entity);

        map.ScrollFactor = Vector2.One;
        map.Add(collider);

        Assert.Throws<InvalidOperationException>(() => map.ScrollFactor = new Vector2(0.5f, 1f));
        Assert.Equal(Vector2.One, map.ScrollFactor);
        Assert.Single(scene.Collision.Grids.ToArray());
    }

    // Disabling removes the grid at once and the body exits it on its next settle. Re-enabling builds a
    // new grid from the cells as painted while it was off.
    [Fact]
    public void ADisabledCollider_GetsItsEditsWhenItIsEnabledAgain()
    {
        TileMap map = SceneFixtures.Colliding(SceneFixtures.TerrainGrid("....", "....", ".##."));
        TileMapCollider2D collider = map.Get<TileMapCollider2D>();
        (SceneSimulation simulation, SceneFixtures.Body body) = FloorBody(map, out List<ColliderContact2D> exits);
        using SceneSimulation run = simulation;

        collider.Enabled = false;

        Assert.Null(collider.Grid);
        simulation.Step(SceneFixtures.Step(1));
        Assert.Single(exits);
        body.Mover.Move(new Vector2(0f, 4f));
        Assert.False(body.Mover.IsOnFloor);

        map.RemoveTile(1, 2);
        collider.Enabled = true;

        Assert.NotNull(collider.Grid);
        AssertSweepStopsAtWall(simulation.Scene);
    }

    // A refused collider is left unattached and can still be added where it belongs.
    [Theory]
    [InlineData("entity", "Add it to a TileMap")]
    [InlineData("second", "Keep one per map")]
    [InlineData("layerless", "Give a tile type a layer")]
    public void AColliderRefusesAnythingButOneLayeredMap(string target, string fix)
    {
        Entity entity = target switch
        {
            "entity" => new SceneFixtures.Drifter(Vector2.Zero),
            "second" => SceneFixtures.Colliding(SceneFixtures.TerrainGrid("#")),
            _ => new TileMap(SceneFixtures.RoomGrid()),
        };
        TileMapCollider2D collider = new();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => entity.Add(collider));

        Assert.Contains(fix, error.Message, StringComparison.Ordinal);
        Assert.Null(collider.Entity);
        Assert.DoesNotContain(collider, entity.Components.ToArray());
    }

    // Floor, not truncation: a position a fraction left of the origin is in cell -1, and one exactly
    // on a cell edge is in the cell that edge opens. A quotient past the int range pins to its end.
    [Theory]
    [InlineData(8, -0.5f, 0f, -1, 0)]
    [InlineData(8, 8f, 16f, 1, 2)]
    [InlineData(8, 7.99f, 0f, 0, 0)]
    [InlineData(1, 2147483648f, 0f, int.MaxValue, 0)]
    public void CellAt_FloorsAWorldPositionToTheCellItFallsIn(int tileSize, float x, float y, int cellX, int cellY)
    {
        TileMap map = new(new TileGrid(tileSize, 1, 1, [TileGrid.EmptyTile], [0]));

        Assert.Equal((cellX, cellY), map.CellAt(new Vector2(x, y)));
    }

    // The strip's first cell, its last, then a fraction past each end and beneath it.
    [Theory]
    [InlineData(0f, 0f, "solid")]
    [InlineData(31.9f, 7.9f, "solid")]
    [InlineData(-0.1f, 4f, null)]
    [InlineData(32f, 4f, null)]
    [InlineData(4f, 8f, null)]
    public void TileAtAWorldPosition_ReadsTheCellItFallsIn_AndNullOutsideTheMap(float x, float y, string? name)
    {
        TileMap map = new(Strip());

        Assert.Equal(name, map.TileAt(new Vector2(x, y))?.Name);
    }

    // A cell beside the map has a centre too, and the centre falls back in its own cell.
    [Theory]
    [InlineData(1, 0, 12f, 4f)]
    [InlineData(-1, 2, -4f, 20f)]
    public void CellCenter_IsTheMiddleOfTheCell_InsideTheMapOrNot(int x, int y, float centreX, float centreY)
    {
        TileMap map = new(Strip());

        Assert.Equal(new Vector2(centreX, centreY), map.CellCenter(x, y));
        Assert.Equal((x, y), map.CellAt(map.CellCenter(x, y)));
    }

    // A row of four solid 8-pixel tiles.
    private static TileGrid Strip() =>
        new(
            tileSize: 8,
            width: 4,
            height: 1,
            [TileGrid.EmptyTile, new TileType { Name = "solid", Cell = 0 }],
            [1, 1, 1, 1],
            SceneFixtures.Atlas,
            1);

    // A body landed on the middle cells of a map painted "....", "....", ".##.", reporting its contact exits.
    private static (SceneSimulation Simulation, SceneFixtures.Body Body) FloorBody(TileMap map, out List<ColliderContact2D> exits)
    {
        SceneFixtures.Body body = new(new Vector2(18f, 0f), blocksOn: "solid");
        body.Collider.Detects = new("solid");
        body.Collider.ReportsContacts = true;
        List<ColliderContact2D> exited = [];
        body.Collider.ContactExited += exited.Add;
        exits = exited;

        Scene scene = new();
        scene.Add(map);
        scene.Add(body);
        SceneSimulation simulation = new(scene);
        body.Mover.Move(new Vector2(0f, 40f));
        simulation.Step(SceneFixtures.Step(0));
        Assert.True(body.Mover.IsOnFloor);

        return (simulation, body);
    }

    // A box sweeping right along the bottom row from the cleared cell meets the face of the solid one beside it.
    private static void AssertSweepStopsAtWall(Scene scene)
    {
        MoveResult2D swept = scene.Collision.MoveBox(
            Aabb2D.FromCorner(new Vector2(16f, 36f), new Vector2(8f, 8f)),
            new Vector2(20f, 0f),
            scene.Collision.CreateFilter("solid"),
            default);

        Assert.True(swept.Blocked);
        Assert.Equal(8f, swept.Translation.X, 0.01f);
    }
}
