using System.Numerics;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Rendering;
using Capsule.Tests.Rendering;

namespace Capsule.Tests.Scenes;

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
        Holder entity = Prompt("AB");
        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        ReadOnlySpan<SpriteIntent> drawn = simulation.View.Sprites;

        Assert.Equal(2, drawn.Length);

        // The entity sits at (4, 5) and the label offsets by (1, 1); 'A' then carries its own
        // one-pixel bearing.
        Assert.Equal(new Vector2(6f, 8f), drawn[0].Position);
        Assert.Equal(new Vector2(8f, 8f), drawn[1].Position);
    }

    [Fact]
    public void ALabelWithNoPivot_LandsItsBoxCornerOnThatPoint()
    {
        Holder entity = Prompt("AB");
        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);

        // The entity at (4, 5) plus the label's (1, 1) offset, and the measured run from there.
        Assert.Equal(new Rect(5f, 6f, 14f, 6f + FontFixtures.LineHeight), entity.Text.Bounds);
    }

    [Fact]
    public void ALabelBox_IsWhatTheLabelWasSized()
    {
        Holder entity = Prompt("AB");
        entity.Text.Size = new Vector2(40f, 30f);
        entity.Text.HorizontalAlignment = HorizontalAlignment.Center;
        entity.Text.VerticalAlignment = VerticalAlignment.Middle;

        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);

        // The entity at (4, 5) plus the label's (1, 1) offset, which the default pivot hangs the box
        // from; centring the pivot instead centres that box on the same point.
        Assert.Equal(new Rect(5f, 6f, 45f, 36f), entity.Text.Bounds);

        entity.Text.Pivot = Pivot.Center;

        Assert.Equal(new Rect(-15f, -9f, 25f, 21f), entity.Text.Bounds);
    }

    [Fact]
    public void AWrappedLabel_BreaksInsideItsBoxAndDrawsEveryLine()
    {
        Holder entity = Prompt("AB AB");
        entity.Text.Size = new Vector2(15f, 0f);
        entity.Text.Wrap = TextWrap.Word;

        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        ReadOnlySpan<SpriteIntent> drawn = simulation.View.Sprites;

        // Four glyphs over two lines: the space the wrap broke at draws nothing. 'A' sits two font
        // pixels below its line's top edge, which is the box's top edge on the first line.
        Assert.Equal(4, drawn.Length);
        Assert.Equal(8f, drawn[0].Position.Y);
        Assert.Equal(8f + FontFixtures.LineHeight, drawn[2].Position.Y);
    }

    [Fact]
    public void AVisibleCount_LimitsWhatALabelDraws()
    {
        Holder entity = Prompt("AB");
        entity.Text.VisibleCharacters = 1;

        Scene scene = new();
        scene.Add(entity);

        using SceneSimulation simulation = new(scene);
        simulation.Step(SceneFixtures.Step());

        Assert.Equal(FontFixtures.A.Region, Assert.Single(simulation.View.Sprites.ToArray()).Sprite.Region);
    }

    [Fact]
    public void ALabelOnNoEntity_OccupiesNoBox()
    {
        Assert.True(new Label(FontFixtures.Font(), "AB").Bounds.IsEmpty);
    }

    private static Holder Prompt(string text) =>
        new(new Label(FontFixtures.Font(), text) { Offset = Vector2.One });

    private sealed class Holder : Entity
    {
        internal Holder(Label label)
            : base(new Vector2(4f, 5f))
        {
            Text = label;
            Add(label);
        }

        internal Label Text { get; }
    }
}
