using System.Numerics;
using Capsule.Maps;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The tilemap: a grid to query, quads baked once from it, and terrain that draws first because
/// the scene added it first — never because the engine treats it as special.
/// </summary>
public sealed class TileMapTests
{
    [Fact]
    public void TerrainIsBakedFromTheGrid_AndDrawsAheadOfWhatWasAddedAfterIt()
    {
        SceneFixtures.Drifter drifter = new(new Vector2(7, 9));
        drifter.Add(new QuadRenderer(new Vector2(4, 8), ColorRgba.White));

        MapScene scene = SceneFixtures.RoomScene(SceneFixtures.Room(), SceneFixtures.Registry());
        scene.Add(drifter);
        OpenOver(scene, new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize));
        SceneSimulation simulation = new(scene);

        simulation.Step(SceneFixtures.Step());

        Assert.Equal(2, simulation.View.Quads.Length);

        // The one solid tile is at (1, 0), and terrain never moves.
        QuadIntent terrain = simulation.View.Quads[0];
        Assert.Equal(new Vector2(SceneFixtures.TileSize, 0), terrain.Position);
        Assert.Equal(terrain.Position, terrain.PreviousPosition);
        Assert.Equal(new Vector2(SceneFixtures.TileSize, SceneFixtures.TileSize), terrain.Size);
        Assert.Equal(SceneFixtures.Solid, terrain.Color);

        Assert.Equal(new Vector2(8, 9), simulation.View.Quads[1].Position);
    }

    [Fact]
    public void ATilemapsPositionIsNotConsulted_ItsQuadsAreWorldCoordinates()
    {
        MapScene scene = SceneFixtures.RoomScene(SceneFixtures.Room(), SceneFixtures.Registry());
        OpenOver(scene, new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize));
        SceneSimulation simulation = new(scene);

        SceneFixtures.TerrainOf(scene).Position = new Vector2(1000, 1000);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(SceneFixtures.TileSize, 0), simulation.View.Quads[0].Position);
    }

    // A tilemap takes a grid, not a document: nothing here loads a file or builds a map, and a
    // procedurally generated grid reaches the renderer the same way an imported one does. The
    // colours come from the grid's own palette, so nothing outside it says what a tile looks like.
    [Fact]
    public void ATilemapIsBuiltFromAGridAlone_WithNoMapAnywhere()
    {
        ColorRgba amber = new(0xD6, 0x9E, 0x2E, 0x80);
        TileGrid grid = new(8, 2, 1, [TileGrid.EmptyTile, new TileDefinition("solid", amber)], [0, 1]);

        TileMap tiles = new(grid);
        Scene scene = new();
        scene.Add(tiles);
        OpenOver(scene, tiles.Size);
        SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(8, tiles.TileSize);
        Assert.Equal(new Vector2(16, 8), tiles.Size);
        Assert.Equal("solid", tiles.TileTypeAt(1, 0));
        Assert.Equal(amber, Assert.Single(simulation.View.Quads.ToArray()).Color);
    }

    [Fact]
    public void TheGridIsQueryable_AndItsExtentIsTheScenesSize()
    {
        MapScene scene = SceneFixtures.RoomScene(SceneFixtures.Room(), SceneFixtures.Registry());
        TileMap terrain = SceneFixtures.TerrainOf(scene);

        Assert.Equal(SceneFixtures.TileSize, terrain.TileSize);
        Assert.Equal(3, terrain.Width);
        Assert.Equal(2, terrain.Height);
        Assert.Equal("solid", terrain.TileTypeAt(1, 0));
        Assert.Equal(0, terrain.TileAt(0, 0));
        Assert.Equal(new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize), scene.Size);
    }

    [Fact]
    public void TerrainEmitsOnlyTilesCrossingTheCamera()
    {
        TileGrid grid = new(
            tileSize: 8,
            width: 4,
            height: 1,
            [TileGrid.EmptyTile, new TileDefinition("solid", SceneFixtures.Solid)],
            [1, 1, 1, 1]);
        Scene scene = new();
        scene.Camera.Center = new Vector2(12, 4);
        scene.Camera.ViewportSize = new Vector2(8, 8);
        scene.Add(new TileMap(grid));

        SceneSimulation simulation = new(scene);

        QuadIntent tile = Assert.Single(simulation.View.Quads.ToArray());
        Assert.Equal(new Vector2(8, 0), tile.Position);
    }

    [Fact]
    public void TerrainEmitsNothingBeforeTheCameraOpens()
    {
        Scene scene = new();
        scene.Add(new TileMap(SceneFixtures.RoomGrid()));

        SceneSimulation simulation = new(scene);

        Assert.Empty(simulation.View.Quads.ToArray());
    }

    [Fact]
    public void AColorlessSemanticTile_RemainsQueryableWithoutEmittingAQuad()
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
        Assert.Empty(simulation.View.Quads.ToArray());
    }

    private static void OpenOver(Scene scene, Vector2 size)
    {
        scene.Camera.Center = size / 2f;
        scene.Camera.ViewportSize = size;
    }
}
