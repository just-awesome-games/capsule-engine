using System.Numerics;
using System.Text;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class RichLabelTests
{
    // Every character the markup tests write has a glyph. A literal tag shows as its own characters.
    private const string Alphabet = "[]/=#colrspiteanABf0x ";

    [Theory]
    [InlineData("A[color=#ff0000]B[/color]A", "A<800000ff>B<808080ff>A")]
    [InlineData("[color=#ff0000]A[color=#00ff00]B[/color]A[/color]B", "<800000ff>A<008000ff>B<800000ff>A<808080ff>B")]
    [InlineData("[color=#ffffff40]A[/color]", "<80808040>A")]
    [InlineData("[color=#ff0000]AB", "<800000ff>AB")]
    [InlineData("A[[B", "A[B")]
    [InlineData("[[color=#ff0000]B", "[color=#ff0000]B")]
    [InlineData("A[AB]B", "A[AB]B")]
    [InlineData("[color=#f0]AB", "[color=#f0]AB")]
    [InlineData("A[/color]B", "A[/color]B")]
    [InlineData("[color]A", "[color]A")]
    [InlineData("[color=#ff0000]A[/color=#ff0000]B", "<800000ff>A[/color=#ff0000]B")]
    public void Markup_IsStrippedAndEveryColourIsTheSpanTimesTheLabelColour(string markup, string drawn)
    {
        Label label = RichLabel(markup);
        label.Color = new ColorRgba(128, 128, 128);

        using SimulationHost host = Host(label);
        host.Step();

        Assert.Equal(drawn, Describe(host, new ColorRgba(128, 128, 128)));
    }

    [Fact]
    public void VisibleCharacters_CountsNoTag()
    {
        Label label = RichLabel("[color=#ff0000]A[/color]B");
        label.VisibleCharacters = 1;

        using SimulationHost host = Host(label);
        host.Step();

        Assert.Equal("<ff0000ff>A", Describe(host, ColorRgba.White));
    }

    // Each change alters what the base label draws. A layout cached across it would fail.
    [Theory]
    [InlineData("text")]
    [InlineData("font")]
    [InlineData("box")]
    [InlineData("wrap")]
    [InlineData("alignment")]
    [InlineData("rich")]
    public void AWarmLayout_DrawsAsAFreshLabelAfterAnythingItDependsOnChanges(string change)
    {
        Label warm = Wrapped();
        using SimulationHost host = Host(warm);
        host.Step();

        // The frame is drawn on its first read, which lays the run out.
        _ = host.Simulation.View;
        Change(warm, change);
        host.Step();

        Label fresh = Wrapped();
        Change(fresh, change);
        using SimulationHost freshHost = Host(fresh);
        freshHost.Step();

        Assert.Equal(freshHost.Simulation.View.Sprites.ToArray(), host.Simulation.View.Sprites.ToArray());

        // Each word fills most of the 15 pixel box. Every one takes a line.
        static Label Wrapped()
        {
            Label label = RichLabel("AB [color=#ff0000]BA[/color] AB");
            label.Size = new Vector2(15f, 0f);
            label.Wrap = TextWrap.Word;

            return label;
        }

        static void Change(Label label, string change)
        {
            switch (change)
            {
                case "text":
                    label.Text = "BA AB";
                    break;
                case "font":
                    label.Font = Font(FontFixtures.LineHeight + 4);
                    break;
                case "box":
                    label.Size = new Vector2(60f, 0f);
                    break;
                case "wrap":
                    label.Wrap = TextWrap.None;
                    break;
                case "alignment":
                    label.HorizontalAlignment = HorizontalAlignment.Right;
                    break;
                default:
                    label.RichText = false;
                    break;
            }
        }
    }

    [Fact]
    public void ContentSize_IsTheTextsExtentWhateverTheBox()
    {
        Label label = RichLabel("AB");
        label.Size = new Vector2(200f, 50f);

        using SimulationHost host = Host(label);
        host.Step();

        Assert.Equal(label.Font.Measure("AB"), label.ContentSize);
    }

    private static Label RichLabel(string markup) =>
        new(Font(), markup) { RichText = true };

    private static BitmapFont Font(int lineHeight = FontFixtures.LineHeight)
    {
        Glyph[] glyphs = [FontFixtures.A, FontFixtures.B, FontFixtures.Space, .. Alphabet.Where(c => c is not (' ' or 'A' or 'B'))
            .Select((c, index) => new Glyph(c, 0, new TextureRegion(100 + (index * 4), 0, 4, 6), 0, 2, 5))];

        return new BitmapFont(lineHeight, 8, [FontFixtures.Page], glyphs, [FontFixtures.AgainstB]);
    }

    private static SimulationHost Host(Label label)
    {
        Entity entity = new();
        entity.Add(label);
        Scene scene = new();
        scene.Add(entity);

        return new SimulationHost(scene);
    }

    // The glyphs drawn, with a colour in angle brackets wherever it differs from the one before.
    private static string Describe(SimulationHost host, ColorRgba baseline)
    {
        BitmapFont font = Font();
        StringBuilder text = new();
        ColorRgba last = baseline;

        foreach (SpriteIntent drawn in host.Simulation.View.Sprites.ToArray())
        {
            if (drawn.Color != last)
            {
                last = drawn.Color;
                text.Append($"<{last.R:x2}{last.G:x2}{last.B:x2}{last.A:x2}>");
            }

            foreach (char c in Alphabet)
            {
                if (font.TryGetGlyph(c, out Glyph glyph) && glyph.Region == drawn.Sprite.Region)
                {
                    text.Append(c);
                }
            }
        }

        return text.ToString();
    }
}
