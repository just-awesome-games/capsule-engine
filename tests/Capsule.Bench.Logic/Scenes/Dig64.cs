using Capsule.Bench.Logic.Cameras;
using Capsule.Bench.Logic.Entities;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// A 128 by 128 map of a rule tile that joins its four sides, 64 of its cells dug out or filled back a step: what
/// repicking a paint's neighbours costs, with the view drawing the cells it changes. A pass over the map takes 256
/// steps.
/// </summary>
[Workload(WorkloadKind.Simulation)]
public sealed class Dig64 : Scene
{
    private const int TileSize = 16;
    private const int Side = 128;

    private static readonly TileType[] Palette =
    [
        new() { Name = TileGrid.EmptyTileName },
        new() { Name = "rock", Terrain = "rock", Rules = [.. Enumerable.Range(0, 16).Select(static joined => new TileRule(joined % 2, (TileSides)joined))] },
    ];

    public Dig64()
    {
        Camera = new ParkedCamera();
        TileMap map = new(new TileGrid(TileSize, Side, Side, Palette, [.. Enumerable.Repeat(1, Side * Side)], CapsuleAssets.Textures.TerrainTexture, 2));
        Add(map);
        Add(new Digger(map, "rock"));
    }
}
