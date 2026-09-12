using System.Numerics;
using Capsule.Rendering;

namespace Capsule.Tests.Rendering;

// A run of text laid out in a box: what wraps, where a line sits between the box's edges, what a
// visible count reveals, and the box the whole thing occupies.
public sealed class TextBoxTests
{
    // 'A' advances 5, 'B' advances 6 and kerns -2 behind 'A', and the space advances 4, so "AB AB"
    // measures 22 font pixels and breaks into two lines of 9 at any box between 9 and 21.
    private const string TwoWords = "AB AB";

    [Theory]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(15)]
    [InlineData(21)]
    public void AWrappedRun_BreaksAtTheLastSpaceThatFitsAndDropsIt(int boxWidth)
    {
        // 9 is the narrowest box a word fits in, and up to 12 the space behind the second word is
        // itself what overflows: the break falls on it rather than on the word after it.
        Assert.Equal(new Vector2(9f, 2 * FontFixtures.LineHeight), FontFixtures.Font().Measure(TwoWords, boxWidth));
    }

    [Fact]
    public void ABoxTooNarrowForAWord_BreaksInsideIt()
    {
        // One pixel under the 9 a word needs, so every glyph takes a line of its own.
        Assert.Equal(new Vector2(6f, 4 * FontFixtures.LineHeight), FontFixtures.Font().Measure(TwoWords, 8));
    }

    [Fact]
    public void AWordWiderThanTheBox_BreaksAtTheCharacter()
    {
        // No space to break at, and 'B' alone needs 6 of the 6 the box has.
        Assert.Equal(new Vector2(6f, 2 * FontFixtures.LineHeight), FontFixtures.Font().Measure("AB", 6));
    }

    [Fact]
    public void AGlyphWiderThanTheWholeBox_StillTakesALineOfItsOwn()
    {
        Assert.Equal(new Vector2(6f, 2 * FontFixtures.LineHeight), FontFixtures.Font().Measure("AB", 1));
    }

    [Fact]
    public void AnUnwrappedRun_MeasuresTheSameWhateverWidthIsOffered()
    {
        BitmapFont font = FontFixtures.Font();

        Assert.Equal(font.Measure(TwoWords), font.Measure(TwoWords, 0));
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left, 1f, 1f)]
    [InlineData(HorizontalAlignment.Center, 6f, 8f)]
    [InlineData(HorizontalAlignment.Right, 12f, 16f)]
    public void EachLine_ShiftsToItsAlignmentInsideTheBox(HorizontalAlignment alignment, float first, float second)
    {
        // Two lines of 9 and 5 font pixels in a box of 20, each measured from the box's left edge;
        // 'A' carries a one-pixel bearing on top of wherever its pen lands.
        TextIntent intent = Text("AB\nA") with { Size = new Vector2(20f, 0f), HorizontalAlignment = alignment };

        FrameView view = new();
        view.Add(intent);

        ReadOnlySpan<SpriteIntent> drawn = view.Sprites;
        float left = intent.Bounds.Left;

        Assert.Equal(3, drawn.Length);
        Assert.Equal(first, drawn[0].Position.X - left);
        Assert.Equal(second, drawn[2].Position.X - left);
    }

    [Theory]
    [InlineData(VerticalAlignment.Top, 2f)]
    [InlineData(VerticalAlignment.Middle, 12f)]
    [InlineData(VerticalAlignment.Bottom, 22f)]
    public void TheRun_SitsAtItsVerticalAlignmentInsideTheBox(VerticalAlignment alignment, float top)
    {
        // One line of 10 in a box of 30, measured from the box's top edge; 'A' sits two font pixels
        // below its own line's top edge.
        TextIntent intent = Text("A") with { Size = new Vector2(0f, 30f), VerticalAlignment = alignment };

        FrameView view = new();
        view.Add(intent);

        Assert.Equal(top, view.Sprites[0].Position.Y - intent.Bounds.Top);
    }

    [Fact]
    public void APivot_IsWhereThePositionLands()
    {
        TextIntent centred = Text("A") with
        {
            Size = new Vector2(40f, 30f),
            Pivot = Pivot.Center,
            Position = new Vector2(100f, 50f),
            PreviousPosition = new Vector2(100f, 50f),
        };

        Assert.Equal(new Rect(80f, 35f, 120f, 65f), centred.Bounds);
    }

    [Theory]
    [InlineData(HorizontalAlignment.Left, VerticalAlignment.Top, 1f, 2f)]
    [InlineData(HorizontalAlignment.Center, VerticalAlignment.Middle, 18f, 12f)]
    [InlineData(HorizontalAlignment.Right, VerticalAlignment.Bottom, 36f, 22f)]
    public void AnAlignment_MovesTheTextAndNeverTheBox(
        HorizontalAlignment horizontal,
        VerticalAlignment vertical,
        float x,
        float y)
    {
        // One line of 5 by 10 in a box of 40 by 30, the pivot left at its top-left corner; 'A' carries
        // a one-pixel bearing on X and sits two font pixels below its line's top edge. The line's own
        // shift is whole font pixels, so the centred half-pixel floors.
        TextIntent aligned = Text("A") with
        {
            Size = new Vector2(40f, 30f),
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
        };

        FrameView view = new();
        view.Add(aligned);

        Assert.Equal(new Rect(0f, 0f, 40f, 30f), aligned.Bounds);
        Assert.Equal(new Vector2(x, y), view.Sprites[0].Position);
    }

    [Fact]
    public void ASizelessRun_RunsRightFromItsPositionUntilThePivotSaysOtherwise()
    {
        // The box is the text itself, so there is nothing for the alignment to move the run inside of:
        // right-aligned text still starts at the position, and only the pivot puts its end there.
        TextIntent rightAligned = Text("AB") with { HorizontalAlignment = HorizontalAlignment.Right };

        Assert.Equal(new Rect(0f, 0f, 9f, FontFixtures.LineHeight), rightAligned.Bounds);
        Assert.Equal(
            new Rect(-9f, 0f, 0f, FontFixtures.LineHeight),
            (rightAligned with { Pivot = Pivot.TopRight }).Bounds);
    }

    [Fact]
    public void ABoxOfNoSize_IsTheMeasuredRun()
    {
        Assert.Equal(new Rect(0f, 0f, 9f, FontFixtures.LineHeight), Text("AB").Bounds);
    }

    [Fact]
    public void AVisibleCount_DrawsThatManyCodepointsOfTheFullLayout()
    {
        FrameView view = new();
        view.Add(Text("AB\nA") with { VisibleCharacters = 2 });

        // The newline spends the third codepoint, so three reveals no more than two.
        FrameView andTheBreak = new();
        andTheBreak.Add(Text("AB\nA") with { VisibleCharacters = 3 });

        Assert.Equal(2, view.Sprites.Length);
        Assert.Equal(2, andTheBreak.Sprites.Length);
    }

    [Fact]
    public void AVisibleCountOfNone_DrawsNothingAndSubmitsNothing()
    {
        FrameView view = new();
        view.Add(Text("AB") with { VisibleCharacters = 0 });

        Assert.Equal(new RenderMetrics(Submitted: 0, Visible: 0), view.Metrics);
    }

    [Fact]
    public void ARevealedRun_NeverReflowsWhatIsAlreadyShown()
    {
        TextIntent whole = Text(TwoWords) with
        {
            Size = new Vector2(15f, 0f),
            Wrap = TextWrap.Word,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        FrameView all = new();
        all.Add(whole);

        FrameView partial = new();
        partial.Add(whole with { VisibleCharacters = 2 });

        Assert.Equal(all.Sprites[0].Position, partial.Sprites[0].Position);
        Assert.Equal(all.Sprites[1].Position, partial.Sprites[1].Position);
    }

    [Fact]
    public void ARunThatDrawsNothing_OccupiesNoBox()
    {
        Assert.True(Text("").Bounds.IsEmpty);
        Assert.True((Text("A") with { Font = null }).Bounds.IsEmpty);
    }

    private static TextIntent Text(string text) =>
        new(FontFixtures.Font(), text, Vector2.Zero, Vector2.Zero, Vector2.One, ColorRgba.White);
}
