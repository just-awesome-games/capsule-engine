using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class LabelTests
{
    [Fact]
    public void ALabel_PreloadsEveryPageItsFontIsCutFrom()
    {
        Scene scene = new();
        scene.Add(Prompt("A"));

        Assert.Equal([FontFixtures.Page], scene.CollectAssetPreloads().Textures);
    }

    [Fact]
    public void ALabel_DrawsItsRunFromTheEntityPositionPlusItsOffset()
    {
        LabelEntity entity = Prompt("AB");
        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        ReadOnlySpan<SpriteIntent> drawn = simulation.View.Sprites;

        Assert.Equal(2, drawn.Length);

        // The entity sits at (4, 5) and the label offsets by (1, 1). 'A' then carries its own
        // one-pixel bearing.
        Assert.Equal(new Vector2(6f, 8f), drawn[0].Position);
        Assert.Equal(new Vector2(8f, 8f), drawn[1].Position);
    }

    [Fact]
    public void ALabelWithNoPivot_LandsItsBoxCornerOnThatPoint()
    {
        LabelEntity entity = Prompt("AB");
        Scene scene = new();
        scene.Add(entity);

        // The entity at (4, 5) plus the label's (1, 1) offset, and the measured run from there.
        Assert.Equal(Rect.FromEdges(5f, 6f, 14f, 6f + FontFixtures.LineHeight), entity.Text.Bounds);
    }

    [Fact]
    public void ALabelBox_IsWhatTheLabelWasSized()
    {
        LabelEntity entity = Prompt("AB");
        entity.Text.Size = new Vector2(40f, 30f);
        entity.Text.HorizontalAlignment = HorizontalAlignment.Center;
        entity.Text.VerticalAlignment = VerticalAlignment.Middle;

        Scene scene = new();
        scene.Add(entity);

        // The entity at (4, 5) plus the label's (1, 1) offset, which the default pivot hangs the box
        // from. Centring the pivot instead centres that box on the same point.
        Assert.Equal(Rect.FromEdges(5f, 6f, 45f, 36f), entity.Text.Bounds);

        entity.Text.Pivot = Pivot.Center;

        Assert.Equal(Rect.FromEdges(-15f, -9f, 25f, 21f), entity.Text.Bounds);
    }

    [Fact]
    public void AWrappedLabel_BreaksInsideItsBoxAndDrawsEveryLine()
    {
        LabelEntity entity = Prompt("AB AB");
        entity.Text.Size = new Vector2(15f, 0f);
        entity.Text.Wrap = TextWrap.Word;

        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        ReadOnlySpan<SpriteIntent> drawn = simulation.View.Sprites;

        // Four glyphs over two lines, where the space the wrap broke at draws nothing. 'A' sits two font
        // pixels below its line's top edge, which is the box's top edge on the first line.
        Assert.Equal(4, drawn.Length);
        Assert.Equal(8f, drawn[0].Position.Y);
        Assert.Equal(8f + FontFixtures.LineHeight, drawn[2].Position.Y);
    }

    [Fact]
    public void AVisibleCount_LimitsWhatALabelDraws()
    {
        LabelEntity entity = Prompt("AB");
        entity.Text.VisibleCharacters = 1;

        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(FontFixtures.A.Region, Assert.Single(simulation.View.Sprites.ToArray()).Sprite.Region);
    }

    // The label caches its layout and draws from the cache. It is held to every sprite FrameView.Add(TextIntent)
    // lays out, with the interpolation across a step.
    [Theory]
    [InlineData("AB AB A", TextWrap.None, HorizontalAlignment.Left, 0f, 0f, null)]
    [InlineData("AB AB A", TextWrap.Word, HorizontalAlignment.Left, 15f, 0f, null)]
    [InlineData("AB AB\nA", TextWrap.Word, HorizontalAlignment.Center, 22f, 40f, null)]
    [InlineData("AB AB\nA", TextWrap.None, HorizontalAlignment.Right, 0f, 40f, null)]
    [InlineData("AB\nA AB", TextWrap.Word, HorizontalAlignment.Center, 0f, 0f, null)]
    [InlineData("AB AB A", TextWrap.Word, HorizontalAlignment.Right, 17f, 0f, 4)]
    [InlineData("AB\nA \uD83D\uDE00B", TextWrap.None, HorizontalAlignment.Left, 30f, 12f, 3)]
    public void APlainLabel_DrawsExactlyWhatTheTextIntentDraws(
        string text,
        TextWrap wrap,
        HorizontalAlignment alignment,
        float width,
        float height,
        int? visible)
    {
        LabelEntity entity = Prompt(text);
        entity.Scale = new Vector2(2f, 3f);
        entity.Drift = new Vector2(5f, -8f);
        Label label = entity.Text;
        label.Size = new Vector2(width, height);
        label.Wrap = wrap;
        label.HorizontalAlignment = alignment;
        label.VerticalAlignment = VerticalAlignment.Middle;
        label.Pivot = Pivot.Center;
        label.VisibleCharacters = visible;
        label.Color = new ColorRgba(200, 100, 50, 180);

        Scene scene = new();
        scene.Add(entity);
        using SceneSimulation simulation = new(scene);
        Vector2 before = entity.Position;
        simulation.Step(SceneFixtures.Step());

        // The label's offset turns with the entity's scale, and the box is sized in the entity's units.
        FrameView expected = new();
        expected.Add(new TextIntent(
            FontFixtures.Font(),
            text,
            before + (label.Offset * entity.Scale),
            entity.Position + (label.Offset * entity.Scale),
            entity.Scale,
            label.Color)
        {
            Size = label.Size * entity.Scale,
            Pivot = label.Pivot,
            Wrap = wrap,
            HorizontalAlignment = alignment,
            VerticalAlignment = label.VerticalAlignment,
            VisibleCharacters = visible,
        });

        Assert.NotEmpty(expected.Sprites.ToArray());
        Assert.Equal(expected.Sprites.ToArray(), simulation.View.Sprites.ToArray());
    }

    // A negative scale axis mirrors the placed run about the entity's position, box, wrap and alignment
    // included. Each glyph folds the sign into its flip and keeps a positive size, which is the only size a
    // sprite draws at. A flipped glyph's position is its mirrored left edge.
    [Fact]
    public void AMirroredLabel_DrawsTheMirrorImageOfItsRun()
    {
        SpriteIntent[] upright = DrawnBoxed(Vector2.One, out Rect uprightBounds);
        SpriteIntent[] mirrored = DrawnBoxed(new Vector2(-1f, 1f), out Rect mirroredBounds);

        // The run wraps. Its last glyph, an A, sits a line below the first.
        Assert.True(upright[^1].Position.Y > upright[0].Position.Y);
        Assert.Equal(upright.Length, mirrored.Length);
        for (int index = 0; index < upright.Length; index++)
        {
            Assert.False(upright[index].FlipX);
            Assert.True(mirrored[index].FlipX);
            Assert.False(mirrored[index].FlipY);
            Assert.Equal(upright[index].Size, mirrored[index].Size);
            Assert.Equal(new Vector2(8f - upright[index].Position.X, upright[index].Position.Y), mirrored[index].Position);
            Assert.True(mirrored[index].TryGetSweptBounds(out _));
        }

        Assert.Equal(Rect.FromEdges(8f - uprightBounds.Right, uprightBounds.Top, 8f - uprightBounds.Left, uprightBounds.Bottom), mirroredBounds);
    }

    [Fact]
    public void ALabelOnNoEntity_OccupiesNoBox()
    {
        Assert.True(new Label(FontFixtures.Font(), "AB").Bounds.IsEmpty);
    }

    [Fact]
    public void ASpanWrittenLabel_LaysOutAsTheStringWouldAndReadsBackAsOne()
    {
        LabelEntity byString = Prompt("AB\nA");
        LabelEntity bySpan = Prompt("");
        char[] buffer = ['A', 'B', '\n', 'A', 'X'];
        bySpan.Text.SetText(buffer.AsSpan(0, 4));
        buffer[0] = 'X';

        Scene scene = new();
        scene.Add(byString);
        scene.Add(bySpan);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal("AB\nA", bySpan.Text.Text);
        Assert.Same(bySpan.Text.Text, bySpan.Text.Text);
        Assert.Equal(byString.Text.Bounds, bySpan.Text.Bounds);

        SpriteIntent[] drawn = simulation.View.Sprites.ToArray();
        Assert.Equal(6, drawn.Length);
        for (int index = 0; index < 3; index++)
        {
            Assert.Equal(drawn[index].Sprite, drawn[index + 3].Sprite);
            Assert.Equal(drawn[index].Position, drawn[index + 3].Position);
        }
    }

    // A centred label wrapping inside a box, on an entity at x = 4 under the given scale.
    private static SpriteIntent[] DrawnBoxed(Vector2 scale, out Rect bounds)
    {
        LabelEntity entity = Prompt("AB AB A");
        entity.Text.Size = new Vector2(22f, 0f);
        entity.Text.Wrap = TextWrap.Word;
        entity.Text.HorizontalAlignment = HorizontalAlignment.Center;
        entity.Scale = scale;

        Scene scene = new();
        scene.Add(entity);
        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());
        bounds = entity.Text.Bounds;

        return simulation.View.Sprites.ToArray();
    }

    private static LabelEntity Prompt(string text) =>
        new(new Label(FontFixtures.Font(), text) { Offset = Vector2.One });

    private sealed class LabelEntity : Entity
    {
        internal Label Text { get; }

        // Moves the entity each step, which gives the label somewhere to interpolate from.
        internal Vector2 Drift { get; set; }

        internal LabelEntity(Label label)
            : base(new Vector2(4f, 5f))
        {
            Text = label;
            Add(label);
        }

        protected internal override void OnStep(in StepContext context) => Position += Drift;
    }
}
