using Capsule.Tests.Scenes;
using Capsule.Tests.Tiles;
using Capsule.Tiles;

namespace Capsule.Tests.Allocation;

[Collection(StageAllocationCollection.Name)]
public sealed class TileRuleAllocationTests
{
    // A wall dug out and filled back a cell at a time, each paint repicking the walls around it, once every block of
    // the map has been emptied and refilled.
    [Fact]
    public void PaintingRuleTiles_AllocatesNothingOnceWarm()
    {
        const int Size = 32;
        TileMap map = new(new TileGrid(8, Size, Size, TileRuleTests.Palette, [.. Enumerable.Repeat(1, Size * Size)], SceneFixtures.Atlas, 8));

        long bytes = StepMeasurement.Bytes(
            step =>
            {
                int cell = step * 7 % (Size * Size);
                // Each pass digs out the whole map or fills it back. Every block empties and refills.
                map.SetTile(cell % Size, cell / Size, step / (Size * Size) % 2 == 0 ? TileGrid.EmptyTileName : "wall");
            },
            warmup: 2048,
            measured: 2048);

        Assert.Equal(0, bytes);
    }
}
