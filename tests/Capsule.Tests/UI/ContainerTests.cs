using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.UI;

namespace Capsule.Tests.UI;

// A container hands each child a slot and the child's own anchor places it there. The layout is cached,
// and a read after any change sees the new one.
public sealed class ContainerTests
{
    private static readonly Vector2 Canvas = new(100f, 50f);

    [Fact]
    public void ABox_SlotsVisibleChildrenInOrderAndEachChildsAnchorPlacesItInItsSlot()
    {
        BoxContainer box = new(Axis.Vertical, Anchor.TopLeft, new Vector2(5f, 5f)) { Spacing = 2f, Padding = new Insets(1f) };
        ColorRect frame = new();
        box.Add(frame);

        // A nudged child moves alone. Its slot, and so every sibling, stays where the layout put it.
        ColorRect nudged = Leaf(box, Anchor.TopLeft, new Vector2(10f, 4f));
        nudged.Entity!.Position = new Vector2(0f, 7f);
        Leaf(box, Anchor.TopLeft, new Vector2(30f, 6f)).Entity!.Visible = false;
        ColorRect centred = Leaf(box, Anchor.Center, new Vector2(6f, 4f));
        ColorRect wide = Leaf(box, Anchor.TopWide, new Vector2(0f, 3f));
        ColorRect widest = Leaf(box, Anchor.TopLeft, new Vector2(20f, 5f));

        using SimulationHost run = Run(box);

        // The inside starts at (6, 6) and is as wide as the widest child. The hidden child takes no
        // slot, so the rows are 4, 4, 3 and 5 tall with 2 between them.
        Assert.Equal(new Rect(new Vector2(6f, 13f), new Vector2(10f, 4f)), nudged.Bounds);
        Assert.Equal(new Rect(new Vector2(13f, 12f), new Vector2(6f, 4f)), centred.Bounds);
        Assert.Equal(new Rect(new Vector2(6f, 18f), new Vector2(20f, 3f)), wide.Bounds);
        Assert.Equal(new Rect(new Vector2(6f, 23f), new Vector2(20f, 5f)), widest.Bounds);
        Assert.Equal(new Rect(new Vector2(5f, 5f), new Vector2(22f, 24f)), frame.Bounds);
    }

    // The real risk of a cached layout: a change deep in the tree must reach every container above it
    // by the next read, with no step and no call from the game in between.
    [Fact]
    public void ANestedBoxsChildResized_RefitsTheOuterBoxOnTheNextRead()
    {
        BoxContainer outer = new(Axis.Vertical, Anchor.TopLeft, Vector2.Zero);
        BoxContainer inner = new(Axis.Horizontal, Anchor.TopLeft, Vector2.Zero) { Parent = outer };
        ColorRect leaf = Leaf(inner, Anchor.TopLeft, new Vector2(10f, 4f));
        ColorRect after = Leaf(outer, Anchor.TopWide, new Vector2(0f, 5f));

        using SimulationHost run = Run(outer);

        Assert.Equal(new Rect(new Vector2(0f, 4f), new Vector2(10f, 5f)), after.Bounds);

        ((ScreenEntity)leaf.Entity!).Size = new Vector2(30f, 9f);

        Assert.Equal(new Rect(new Vector2(0f, 9f), new Vector2(30f, 5f)), after.Bounds);
    }

    [Fact]
    public void AGrid_SizesEachColumnAndRowToItsLargestCell()
    {
        GridContainer grid = new(2, Anchor.TopLeft, Vector2.Zero) { Spacing = new Vector2(1f, 2f) };
        ColorRect frame = new();
        grid.Add(frame);

        Leaf(grid, Anchor.TopLeft, new Vector2(4f, 3f));
        ColorRect wide = Leaf(grid, Anchor.TopLeft, new Vector2(8f, 2f));
        ColorRect tall = Leaf(grid, Anchor.TopLeft, new Vector2(6f, 5f));
        ColorRect cornered = Leaf(grid, Anchor.BottomRight, new Vector2(2f, 1f));
        ColorRect last = Leaf(grid, Anchor.TopLeft, new Vector2(3f, 3f));

        using SimulationHost run = Run(grid);

        // Columns of 6 and 8, rows of 3, 5 and 3. A cell is its column's width and its row's height,
        // so a child anchored to its far corner sits there and not at its own size.
        Assert.Equal(new Vector2(7f, 0f), wide.Bounds.Position);
        Assert.Equal(new Vector2(0f, 5f), tall.Bounds.Position);
        Assert.Equal(new Vector2(13f, 9f), cornered.Bounds.Position);
        Assert.Equal(new Vector2(0f, 12f), last.Bounds.Position);
        Assert.Equal(new Rect(Vector2.Zero, new Vector2(15f, 15f)), frame.Bounds);
    }

    private static ColorRect Leaf(ScreenEntity parent, Anchor anchor, Vector2 size)
    {
        ColorRect rect = new();
        ScreenEntity leaf = new(anchor, Vector2.Zero) { Parent = parent, Size = size };
        leaf.Add(rect);

        return rect;
    }

    private static SimulationHost Run(Entity root)
    {
        Scene scene = new();
        scene.Add(root);

        return new SimulationHost(scene, run: new Run { Canvas = Canvas });
    }
}
