using Capsule.Bench.Logic.Cameras;

namespace Capsule.Bench.Logic.Scenes;

/// <summary>
/// Four painted tile layers of 16-unit tiles under a looping 1920 by 1080 camera, two of them scrolled,
/// some 33 000 cells in view: what a frame pays per visible cell to build and to submit.
/// </summary>
/// <remarks>Row <c>TileLayersEdited</c> parks the camera and repaints one visible cell of the front layer every step.</remarks>
[Workload(WorkloadKind.Simulation)]
[Workload(WorkloadKind.Simulation, Row = Edited)]
[Workload(WorkloadKind.Rendering, Surface.Hd1080)]
public sealed class TileLayers : Scene
{
    private const string Edited = "TileLayersEdited";
    private const int TileSize = 16;
    private const int Wide = 512;
    private const int High = 256;
    private const int Layers = 4;

    private static readonly TileType[] Palette =
    [
        new() { Name = TileGrid.EmptyTileName },
        new() { Name = "rock", Cell = 0 },
        new() { Name = "ledge", Cell = 1 },
    ];

    private readonly TileMap? _edited;

    public TileLayers()
    {
        Camera = Workloads.Running == Edited
            ? new ParkedCamera(World.HdViewportSize / 2f, World.HdViewportSize)
            : new LoopCamera(World.HdViewportSize / 2f, new Vector2(4_000f, 2_000f)) { ViewportSize = World.HdViewportSize };

        TileMap? front = null;
        for (int layer = 0; layer < Layers; layer++)
        {
            int[] tiles = new int[Wide * High];
            TileTransform[] transforms = new TileTransform[tiles.Length];
            for (int cell = 0; cell < tiles.Length; cell++)
            {
                // One cell in sixteen is left empty, and the rest take either entry and one of four facings.
                int mix = (cell * 31) + (layer * 7) + (cell / Wide * 13);
                tiles[cell] = mix % 16 == 0 ? 0 : 1 + (mix % 2);
                transforms[cell] = (TileTransform)(mix / 2 % 4);
            }

            front = new TileMap(new TileGrid(TileSize, Wide, High, Palette, tiles, CapsuleAssets.Textures.TerrainTexture, 2, transforms))
            {
                ZIndex = layer,
            };
            if (layer < 2)
            {
                front.ScrollFactor = new Vector2(0.5f + (layer * 0.25f));
            }

            Add(front);
        }

        _edited = Workloads.Running == Edited ? front : null;
    }

    protected override void OnStep(in StepContext context)
    {
        if (_edited is not null)
        {
            (int x, int y) = _edited.CellAt(Camera.Center);
            _edited.SetTile(x, y, context.Tick % 2 == 0 ? "rock" : "ledge");
        }
    }
}
