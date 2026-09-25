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

        Scene scene = SceneFixtures.RoomScene(SceneFixtures.Room(), SceneFixtures.Registry());
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

    // The sprites are world coordinates, so a position write would move nothing and mean nothing.
    [Fact]
    public void ATilemapRefusesAPositionWrite()
    {
        Scene scene = SceneFixtures.RoomScene(SceneFixtures.Room(), SceneFixtures.Registry());
        TileMap terrain = SceneFixtures.TerrainOf(scene);

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
            [TileGrid.EmptyTile, new TileDefinition("solid", 3)],
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
        Assert.Equal("solid", tiles.TileAt(1, 0));
        Assert.Equal(
            new TextureRegion(8, 8, 8, 8),
            Assert.Single(simulation.View.Sprites.ToArray()).Sprite.Region);
    }

    [Fact]
    public void TerrainEmitsOnlyTilesCrossingTheCamera()
    {
        Scene scene = new();
        scene.Camera.Center = new Vector2(12, 4);
        scene.Camera.ViewportSize = new Vector2(8, 8);
        scene.Add(new TileMap(Run()));

        SceneSimulation simulation = new(scene);

        SpriteIntent tile = Assert.Single(simulation.View.Sprites.ToArray());
        Assert.Equal(new Vector2(12, 4), tile.Position);
    }

    [Fact]
    public void TerrainEmitsTheTilesTheCameraSweepsAcross()
    {
        static void Sweep(Scene scene, in StepContext context) => scene.Camera.Center = new Vector2(28, 4);

        SceneFixtures.HookScene scene = new(step: Sweep);
        scene.Camera.Center = new Vector2(4, 4);
        scene.Camera.ViewportSize = new Vector2(8, 8);
        scene.Add(new TileMap(Run()));

        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(4, simulation.View.Sprites.Length);
    }

    // The camera's centre stays the raw framing target and the bounds confine only what is drawn,
    // so culling has to confine the same way: the tile at X 8 is on screen and reachable only
    // because the view was pushed right off the room's left edge.
    [Fact]
    public void TerrainEmitsTheTilesConfinementBringsIntoView()
    {
        Scene scene = new();
        scene.Camera.Center = new Vector2(0, 4);
        scene.Camera.ViewportSize = new Vector2(16, 8);
        scene.Camera.Bounds = new Rect(0f, 0f, 32f, 8f);
        scene.Add(new TileMap(Run()));

        SceneSimulation simulation = new(scene);

        Assert.Equal(
            [new Vector2(4, 4), new Vector2(12, 4)],
            simulation.View.Sprites.ToArray().Select(tile => tile.Position));
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
            [TileGrid.EmptyTile, new TileDefinition("hazard", null)],
            [1]);
        TileMap tiles = new(grid);
        Scene scene = new();
        scene.Add(tiles);

        SceneSimulation simulation = new(scene);

        Assert.Equal("hazard", tiles.TileAt(0, 0));
        Assert.Empty(simulation.View.Sprites.ToArray());
    }

    // Clearing a solid cell changes what the map collides as and nothing else: the body standing on it
    // falls on its next move, the reporting collider exits at the next settle, the face the cell hid
    // on its neighbour stops a sweep, and the grid the map was built from still reads as authored.
    [Fact]
    public void SetTile_ChangesTheMapsCollision_AndLeavesTheGridItWasBuiltFromAlone()
    {
        TileGrid grid = SceneFixtures.TerrainGrid("....", "....", ".##.");
        TileMap map = new(grid);
        SceneFixtures.Body body = new(new Vector2(18f, 0f), blocksOn: "solid");
        body.Collider.SetFilter("solid");
        body.Collider.ReportsContacts = true;
        int exits = 0;
        body.Collider.ContactExited += _ => exits++;

        Scene scene = new();
        scene.Add(map);
        scene.Add(body);
        using SceneSimulation simulation = new(scene);
        body.Mover.Move(new Vector2(0f, 40f));
        simulation.Step(SceneFixtures.Step(0));
        Assert.True(body.Mover.IsOnFloor);
        Assert.Single(body.Collider.Touching.ToArray());

        map.RemoveTile(1, 2);

        Assert.Equal(TileGrid.EmptyTileType, map.TileAt(1, 2));
        simulation.Step(SceneFixtures.Step(1));
        Assert.Equal(1, exits);

        body.Mover.Move(new Vector2(0f, 4f));
        Assert.False(body.Mover.IsOnFloor);

        MoveResult2D swept = scene.Collision.MoveBox(
            Aabb2D.FromCorner(new Vector2(16f, 36f), new Vector2(8f, 8f)),
            new Vector2(20f, 0f),
            scene.Collision.CreateFilter("solid"),
            default);
        Assert.True(swept.Blocked);
        Assert.Equal(8f, swept.Translation.X, 0.01f);

        Assert.Equal("solid", new TileMap(grid).TileAt(1, 2));

        ArgumentException unknown = Assert.Throws<ArgumentException>(() => map.SetTile(0, 0, "lava"));
        Assert.Contains("lava", unknown.Message, StringComparison.Ordinal);
        Assert.Contains("empty, solid", unknown.Message, StringComparison.Ordinal);
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

    private static TileGrid Run() =>
        new(
            tileSize: 8,
            width: 4,
            height: 1,
            [TileGrid.EmptyTile, new TileDefinition("solid", 0)],
            [1, 1, 1, 1],
            SceneFixtures.Atlas,
            1);

}
