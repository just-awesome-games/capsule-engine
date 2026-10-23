using System.Numerics;
using Capsule.Physics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Rendering;

// A label flowing around shapes. The box is 30 wide and rows are 10 tall and 10 apart. "AB" measures 9,
// "AB AB" 22 and "AB AB AB" 35. The first glyph of each line is an 'A', drawn one pixel right of its pen and
// two below its line's top.
public sealed class LabelExclusionTests
{
    private const string Words = "AB AB AB AB";

    [Fact]
    public void LinesBesideACornerBox_EndAtItsEdgeAndLinesBelowSpanTheBox()
    {
        Label label = Flowing(Words, Shape2D.Box(new Vector2(20f, 0f), new Vector2(10f, 10f)));
        using SimulationHost host = RichLabelTests.Host(label);

        // "AB" beside the box, then "AB AB" and "AB" across the box.
        Assert.Equal(3, label.LineCount);
        Assert.Equal([0, 3, 9, 11], Enumerable.Range(0, 4).Select(label.LineStart));
        Assert.Equal(new Vector2(FontFixtures.TwoWordsWidth, 3 * FontFixtures.LineHeight), label.ContentSize);

        // The reveal crosses into the line below the box by one glyph.
        label.VisibleCharacters = label.LineStart(1) + 1;
        host.Step();

        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(3, drawn.Length);
        Assert.Equal(new Vector2(1f, FontFixtures.LineHeight + 2f), drawn[2].Position);
    }

    // A circle centred on the box's top-right corner with radius 20 leaves row 0 the span 0 to 10 and row 1
    // 0 to 12.68, its chord at y = 10. It touches row 2 only at its bottom point and leaves it whole. Its
    // bounds alone would leave row 1 10 wide.
    [Fact]
    public void ACircle_NarrowsEachRowByItsChord()
    {
        Label label = Flowing(Words, Shape2D.Circle(new Vector2(30f, 0f), 20f));
        label.HorizontalAlignment = HorizontalAlignment.Right;
        using SimulationHost host = RichLabelTests.Host(label);
        host.Step();

        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal([0, 3, 6, 11], Enumerable.Range(0, 4).Select(label.LineStart));
        Assert.Equal(2f, drawn[0].Position.X);
        Assert.Equal(4f, drawn[2].Position.X);
        Assert.Equal(9f, drawn[4].Position.X);
    }

    // A box on the left edge and a circle on the right leave row 0 the span 5 to 25. The 20 between them holds
    // one word, where either shape alone would leave room for two.
    [Fact]
    public void ShapesOnOppositeEdges_NarrowTheSameRowFromBothSides()
    {
        Label label = Flowing(Words, Shape2D.Box(Vector2.Zero, new Vector2(5f, 10f)), Shape2D.Circle(new Vector2(30f, 5f), 5f));
        using SimulationHost host = RichLabelTests.Host(label);
        host.Step();

        Assert.Equal(3, label.LineStart(1));
        Assert.Equal(6f, host.Simulation.View.Sprites.ToArray()[0].Position.X);
    }

    // A circle from 9 to 15 leaves row 0 a 9-wide span on the left, room for "AB", and a 15-wide one on the
    // right. The line takes the right span alone and leaves the left one empty.
    [Fact]
    public void AShapeInTheMiddle_LeavesTheLineOnTheWiderSideAlone()
    {
        Label label = Flowing(Words, Shape2D.Circle(new Vector2(12f, 5f), 3f));
        using SimulationHost host = RichLabelTests.Host(label);
        host.Step();

        Assert.Equal(3, label.LineStart(1));
        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(new Vector2(16f, 2f), drawn[0].Position);
        Assert.Equal(new Vector2(1f, FontFixtures.LineHeight + 2f), drawn[2].Position);
    }

    // Two overlapping boxes, 13 to 19 passed before 9 to 15, block 9 to 19 together, and a third box blocks from
    // its left edge to 30. With the third at 28 both gaps are 9 wide and the left one wins. At 29 the right gap
    // is 10 wide and wins. Either way the gap holds "AB" alone.
    [Theory]
    [InlineData(28f, 1f)]
    [InlineData(29f, 20f)]
    public void OverlappingShapesInAnyOrder_LeaveTheRowTheirWidestGap(float thirdLeft, float x)
    {
        Label label = Flowing(
            Words,
            Shape2D.Box(new Vector2(13f, 0f), new Vector2(6f, 10f)),
            Shape2D.Box(new Vector2(9f, 0f), new Vector2(6f, 10f)),
            Shape2D.Box(new Vector2(thirdLeft, 0f), new Vector2(30f - thirdLeft, 10f)));
        using SimulationHost host = RichLabelTests.Host(label);
        host.Step();

        Assert.Equal(3, label.LineStart(1));
        Assert.Equal(x, host.Simulation.View.Sprites.ToArray()[0].Position.X);
    }

    // Two-line pages: line 2 opens the second page in row 0 beside the box again, and the layout is the same
    // whichever page is shown.
    [Fact]
    public void TheShapes_StandInEveryPageAndTurningAPageReflowsNothing()
    {
        Label label = Flowing("AB AB AB AB AB AB", Shape2D.Box(new Vector2(20f, 0f), new Vector2(10f, 10f)));
        label.MaxLines = 2;
        using SimulationHost host = RichLabelTests.Host(label);

        int[] starts = [.. Enumerable.Range(0, 5).Select(label.LineStart)];
        Assert.Equal([0, 3, 9, 12, 17], starts);

        host.Step();
        Vector2[] first = Positions(host);

        label.FirstLine = 2;
        host.Step();

        Assert.Equal(starts, Enumerable.Range(0, 5).Select(label.LineStart));
        Assert.Equal(first, Positions(host));
    }

    // Line 0 aligns inside the span the box leaves, and line 1 inside the whole box. A box on the left edge
    // leaves the right span, 10 to 30. A box from 10.1 to 19.1 leaves the right span wider, 20 to 30 once
    // rounded inward, though both spans round to 10.
    [Theory]
    [InlineData(20f, 10f, HorizontalAlignment.Right, 12f, 9f)]
    [InlineData(0f, 10f, HorizontalAlignment.Left, 11f, 1f)]
    [InlineData(0f, 10f, HorizontalAlignment.Center, 16f, 5f)]
    [InlineData(10.1f, 9f, HorizontalAlignment.Right, 22f, 9f)]
    public void ANarrowedLine_AlignsInsideTheSpanTheShapeLeaves(float boxLeft, float boxWidth, HorizontalAlignment alignment, float first, float second)
    {
        Label label = Flowing(Words, Shape2D.Box(new Vector2(boxLeft, 0f), new Vector2(boxWidth, 10f)));
        label.HorizontalAlignment = alignment;
        using SimulationHost host = RichLabelTests.Host(label);
        host.Step();

        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(first, drawn[0].Position.X);
        Assert.Equal(second, drawn[2].Position.X);
    }

    [Fact]
    public void Exclusions_RefuseADefaultArrayAndADefaultShape()
    {
        Label label = new(FontFixtures.Font());

        Assert.Throws<ArgumentException>(() => label.Exclusions = default);
        Assert.Throws<ArgumentException>(() => label.Exclusions = [default]);
    }

    private static Label Flowing(string text, params Shape2D[] exclusions) =>
        new(FontFixtures.Font(), text)
        {
            Size = new Vector2(30f, 0f),
            Wrap = TextWrap.Word,
            Exclusions = [.. exclusions],
        };

    private static Vector2[] Positions(SimulationHost host) =>
        [.. host.Simulation.View.Sprites.ToArray().Select(static sprite => sprite.Position)];
}
