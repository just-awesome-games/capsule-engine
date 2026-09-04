using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;
using Capsule.Scenes.Tiles;

namespace Capsule.Tests.Scenes;

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
        Assert.Equal(new Vector2(SceneFixtures.TileSize, 0), terrain.Position);
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
        Assert.Equal("solid", tiles.TileTypeAt(1, 0));
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
        Assert.Equal(new Vector2(8, 0), tile.Position);
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

        Assert.Equal("hazard", tiles.TileTypeAt(0, 0));
        Assert.Empty(simulation.View.Sprites.ToArray());
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
