using Capsule.Build.Atlases;

namespace Capsule.Tests.Documents;

// The packer's contract: one input packs the same way whatever order it arrives in, every cell
// keeps its gap from every other and stays on its page, and a page that is full hands the next
// cell to the next page.
public sealed class AtlasPackerTests
{
    [Fact]
    public void Pack_PlacesTheSameInputIdenticallyWhateverOrderItArrivesIn()
    {
        (string, int, int)[] items = Synthetic(40);

        (Placement[] placements, (int, int)[] pages) = AtlasPacker.Pack(items, 256);
        (Placement[] reversed, (int, int)[] reversedPages) = AtlasPacker.Pack([.. items.Reverse()], 256);

        Assert.Equal(pages, reversedPages);
        Assert.Equal(placements, reversed);
    }

    [Fact]
    public void Pack_KeepsEveryCellOnItsPageAndTheSpacingBetweenEveryPair()
    {
        (string Key, int Width, int Height)[] items = Synthetic(60);
        Dictionary<string, (string Key, int Width, int Height)> sizes = items.ToDictionary(static item => item.Key);

        (Placement[] placements, (int Width, int Height)[] pages) = AtlasPacker.Pack(items, 128);

        for (int i = 0; i < placements.Length; i++)
        {
            Placement a = placements[i];
            (_, int aWidth, int aHeight) = sizes[a.Key];
            Assert.True(a.X + aWidth <= pages[a.Page].Width && a.Y + aHeight <= pages[a.Page].Height, $"{a.Key} overhangs its page");

            for (int j = i + 1; j < placements.Length; j++)
            {
                Placement b = placements[j];
                if (b.Page != a.Page)
                {
                    continue;
                }

                (_, int bWidth, int bHeight) = sizes[b.Key];
                bool apartX = a.X + aWidth + AtlasPacker.Spacing <= b.X || b.X + bWidth + AtlasPacker.Spacing <= a.X;
                bool apartY = a.Y + aHeight + AtlasPacker.Spacing <= b.Y || b.Y + bHeight + AtlasPacker.Spacing <= a.Y;
                Assert.True(apartX || apartY, $"{a.Key} and {b.Key} are closer than {AtlasPacker.Spacing} texels");
            }
        }
    }

    // Four 30-texel cells and their gaps fill a 64-texel page exactly; the fifth opens the next.
    [Fact]
    public void Pack_OpensANewPageWhenTheCurrentOneIsFull()
    {
        (Placement[] placements, (int, int)[] pages) = AtlasPacker.Pack([.. Enumerable.Range(0, 5).Select(static i => ($"cell-{i}", 30, 30))], 64);

        Assert.Equal([(64, 64), (32, 32)], pages);
        Assert.Equal(4, placements.Count(static placement => placement.Page == 0));
        Assert.Equal(1, placements.Count(static placement => placement.Page == 1));
    }

    [Fact]
    public void Pack_RefusesACellNoPageHolds()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => AtlasPacker.Pack([("wide", 65, 4)], 64));

        Assert.Contains("'wide'", error.Message, StringComparison.Ordinal);
    }

    // A fixed pseudo-random spread of sizes, so the spec is the same on every run.
    private static (string Key, int Width, int Height)[] Synthetic(int count)
    {
        (string, int, int)[] items = new (string, int, int)[count];
        uint state = 12345;
        for (int i = 0; i < count; i++)
        {
            state = (state * 1664525u) + 1013904223u;
            int width = 4 + (int)(state >> 8) % 40;
            state = (state * 1664525u) + 1013904223u;
            items[i] = ($"item-{i}", width, 4 + (int)(state >> 8) % 40);
        }

        return items;
    }
}
