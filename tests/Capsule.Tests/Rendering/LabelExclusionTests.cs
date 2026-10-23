using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Rendering;

// A label flowing around one rect. The box is 30 wide and lines are 10 apart. Row 0 is the only row a
// 10-tall rect at the top crosses. "AB" measures 9 and "AB AB" 22, and a narrowed 20-wide row holds only "AB".
public sealed class LabelExclusionTests
{
    private const string Words = "AB AB AB AB";

    [Fact]
    public void LinesBesideTheRect_EndAtItsEdgeAndLinesBelowSpanTheBox()
    {
        Label label = Flowing(Words, new Rect(20f, 0f, 10f, 10f));
        using SimulationHost host = RichLabelTests.Host(label);

        // "AB" beside the rect, then "AB AB" and "AB" across the box.
        Assert.Equal(3, label.LineCount);
        Assert.Equal([0, 3, 9, 11], Enumerable.Range(0, 4).Select(label.LineStart));
        Assert.Equal(new Vector2(FontFixtures.TwoWordsWidth, 3 * FontFixtures.LineHeight), label.ContentSize);

        // The reveal crosses into the line below the rect by one glyph.
        label.VisibleCharacters = label.LineStart(1) + 1;
        host.Step();

        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(3, drawn.Length);
        Assert.Equal(new Vector2(1f, FontFixtures.LineHeight + 2f), drawn[2].Position);
    }

    // Two-line pages: line 2 opens the second page in row 0 beside the rect again, and the layout is the
    // same whichever page is shown.
    [Fact]
    public void TheRect_StandsInEveryPageAndTurningAPageReflowsNothing()
    {
        Label label = Flowing("AB AB AB AB AB AB", new Rect(20f, 0f, 10f, 10f));
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

    // Line 0 aligns inside the side the rect leaves, and line 1 inside the whole box. A rect on the left
    // edge leaves the right side, 10 to 30. A rect from 10.1 to 19.1 leaves the right side wider, 20 to 30
    // once rounded inward, though both sides round to 10.
    [Theory]
    [InlineData(20f, 10f, HorizontalAlignment.Right, 12f, 9f)]
    [InlineData(0f, 10f, HorizontalAlignment.Left, 11f, 1f)]
    [InlineData(0f, 10f, HorizontalAlignment.Center, 16f, 5f)]
    [InlineData(10.1f, 9f, HorizontalAlignment.Right, 22f, 9f)]
    public void ANarrowedLine_AlignsInsideTheSpaceTheRectLeaves(float rectLeft, float rectWidth, HorizontalAlignment alignment, float first, float second)
    {
        Label label = Flowing(Words, new Rect(rectLeft, 0f, rectWidth, 10f));
        label.HorizontalAlignment = alignment;
        using SimulationHost host = RichLabelTests.Host(label);
        host.Step();

        // The first glyph of each line is an 'A', which carries a one-pixel bearing.
        SpriteIntent[] drawn = host.Simulation.View.Sprites.ToArray();
        Assert.Equal(first, drawn[0].Position.X);
        Assert.Equal(second, drawn[2].Position.X);
    }

    private static Label Flowing(string text, Rect exclusion) =>
        new(FontFixtures.Font(), text)
        {
            Size = new Vector2(30f, 0f),
            Wrap = TextWrap.Word,
            Exclusion = exclusion,
        };

    private static Vector2[] Positions(SimulationHost host) =>
        [.. host.Simulation.View.Sprites.ToArray().Select(static sprite => sprite.Position)];
}
