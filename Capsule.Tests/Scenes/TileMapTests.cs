using System.Numerics;
using Capsule.Levels;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Components;
using Capsule.Scenes.Entities;
using Capsule.Scenes.Spawning;

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

        SceneFixtures.LevelScene scene = new(SceneFixtures.Room(), SceneFixtures.Registry());
        scene.Add(drifter);
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
        SceneFixtures.LevelScene scene = new(SceneFixtures.Room(), SceneFixtures.Registry());
        SceneSimulation simulation = new(scene);

        scene.Tiles.Position = new Vector2(1000, 1000);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(new Vector2(SceneFixtures.TileSize, 0), simulation.View.Quads[0].Position);
    }

    [Fact]
    public void TheGridIsQueryable_AndItsExtentIsTheScenesSize()
    {
        SceneFixtures.LevelScene scene = new(SceneFixtures.Room(), SceneFixtures.Registry());

        Assert.Equal(SceneFixtures.TileSize, scene.Tiles.TileSize);
        Assert.Equal(3, scene.Tiles.Width);
        Assert.Equal(2, scene.Tiles.Height);
        Assert.Equal("solid", scene.Tiles.TileTypeAt(1, 0));
        Assert.Equal(0, scene.Tiles.TileAt(0, 0));
        Assert.Equal(new Vector2(3 * SceneFixtures.TileSize, 2 * SceneFixtures.TileSize), scene.Size);
    }

    [Fact]
    public void TheWholePaletteIsResolvedAtConstruction_NotOnTheFirstPaintedTile()
    {
        List<string> asked = [];
        ColorRgba Resolve(string tileType)
        {
            asked.Add(tileType);

            return tileType == "spike"
                ? throw new ArgumentException($"tile type '{tileType}' has no colour.", nameof(tileType))
                : SceneFixtures.Solid;
        }

        // "spike" is in the palette and painted nowhere, so only an eager resolve reaches it.
        Level room = SceneFixtures.Room(["empty", "solid", "spike"]);

        Assert.Throws<ArgumentException>(() => new TileMap(room, Resolve));

        string[] expected = ["solid", "spike"];
        Assert.Equal(expected, asked);
    }
}
