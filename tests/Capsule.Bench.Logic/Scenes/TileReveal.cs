using Capsule.Bench.Logic.Cameras;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// Three tile layers of 16-unit tiles, empty at the top and filling with depth, under a 1920 by 1080 camera
/// that holds over the empty rows through the warm-up and then descends to the densest screen: what a frame
/// pays the first time the camera shows more cells than any frame before it.
/// </summary>
[Workload(WorkloadKind.Simulation)]
public sealed class TileReveal : Scene
{
    private const int TileSize = 16;
    private const int Wide = 256;
    private const int High = 160;
    private const int Layers = 3;
    private const int EmptyRows = 68;

    private static readonly TileType[] Palette =
    [
        new() { Name = TileGrid.EmptyTileName },
        new() { Name = "rock", Cell = 0 },
        new() { Name = "ledge", Cell = 1 },
    ];

    public TileReveal()
    {
        Vector2 half = World.HdViewportSize / 2f;
        Camera = new DescentCamera(half, (High * TileSize) - half.Y, holdSteps: 300, speed: 6f, World.HdViewportSize);

        for (int layer = 0; layer < Layers; layer++)
        {
            int[] tiles = new int[Wide * High];
            for (int y = EmptyRows; y < High; y++)
            {
                // A row is painted in proportion to its depth below the empty rows, full on the last row.
                int painted = 16 * (y - EmptyRows + 1) / (High - EmptyRows);
                for (int x = 0; x < Wide; x++)
                {
                    int mix = ((x * 31) + (y * 13) + (layer * 7)) % 16;
                    tiles[(y * Wide) + x] = mix < painted ? 1 + (mix % 2) : 0;
                }
            }

            TileMap map = new(new TileGrid(TileSize, Wide, High, Palette, tiles, CapsuleAssets.Textures.TerrainTexture, 2))
            {
                ZIndex = layer,
            };
            if (layer < Layers - 1)
            {
                map.ScrollFactor = new Vector2(0.5f + (layer * 0.25f));
            }

            Add(map);
        }
    }
}
