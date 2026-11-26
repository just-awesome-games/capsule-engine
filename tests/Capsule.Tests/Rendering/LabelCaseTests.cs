using System.Globalization;
using System.Numerics;
using System.Text;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class LabelCaseTests
{
    // Every character the tests draw has a glyph of its own width. A measure tells the cases apart.
    private const string Alphabet = "aAbBiIİıeEfFlLtTsShH []/=#cor0";

    [Theory]
    [InlineData(TextCase.AsWritten, "aB")]
    [InlineData(TextCase.Upper, "AB")]
    [InlineData(TextCase.Lower, "ab")]
    public void Case_ChangesWhatDraws_AndLeavesTheTextAsWritten(TextCase textCase, string drawn)
    {
        Label label = new(Font(), "aB") { Case = textCase };

        using SimulationHost host = Host(label);
        host.Step();

        Assert.Equal(drawn, Describe(host));
        Assert.Equal("aB", label.Text);
    }

    [Fact]
    public void ACasedLabel_MeasuresAsItsCasedText()
    {
        Label cased = new(Font(), "ab") { Case = TextCase.Upper };
        Label written = new(Font(), "AB");

        using SimulationHost host = Host(cased);
        using SimulationHost other = Host(written);

        Assert.Equal(written.ContentSize, cased.ContentSize);
    }

    [Fact]
    public void ARichTextsTags_KeepTheirOwnCase()
    {
        Label label = new(Font(), "[color=#ff0000]a[/color]b") { RichText = true, Case = TextCase.Upper };

        using SimulationHost host = Host(label);
        host.Step();

        Assert.Equal("AB", Describe(host));
        Assert.Equal(new ColorRgba(255, 0, 0), host.Simulation.View.Sprites[0].Color);
    }

    [Theory]
    [InlineData(null, "I")]
    [InlineData("tr", "İ")]
    public void EachCharacter_TakesTheActiveLanguagesRules(string? language, string drawn)
    {
        Label label = new(Font(), "i") { Case = TextCase.Upper };

        using SimulationHost host = Host(label, new Run { Language = language });
        host.Step();

        Assert.Equal(drawn, Describe(host));
    }

    [Fact]
    public void AButtonsName_TakesTheCase()
    {
        Label label = new(Font()) { Case = TextCase.Upper };
        label.SetText(Key.LeftShift);

        using SimulationHost host = Host(label);
        host.Step();

        Assert.Equal("LEFT SHIFT", Describe(host));
    }

    [Fact]
    public void ACaseSetAfterTheFirstDraw_DrawsOnTheNext()
    {
        Label label = new(Font(), "aB");

        using SimulationHost host = Host(label);
        host.Step();
        label.Case = TextCase.Lower;
        host.Step();

        Assert.Equal("ab", Describe(host));
    }

    // "AB AB" is 53 pixels wide and "ab ab" 49. The 50-pixel box wraps only the capitals. A width measured in
    // another case between the layout and its line count shares the layout's buffers, and the count must still
    // read the laid-out case.
    [Fact]
    public void AMeasureInAnotherCase_LeavesTheLinesTheLaidOutCasesOwn()
    {
        TextInfo casing = CultureInfo.InvariantCulture.TextInfo;
        BitmapFont font = Font();
        LabelLayout layout = new();
        LabelLayoutKey upper = new(1, font, 50, TextWrap.Word, HorizontalAlignment.Left, false, TextCase.Upper, casing, 0, null, 0, default);

        layout.Update("ab ab", in upper);
        _ = layout.Natural("ab ab", font, false, TextCase.Lower, casing, 0, null);
        layout.Update("ab ab", in upper);

        Assert.Equal(2, layout.LineCount("ab ab"));
    }

    private static BitmapFont Font()
    {
        Glyph[] glyphs = [.. Alphabet.Select((c, index) => new Glyph(c, 0, new TextureRegion(index * 8, 0, 4 + (index % 3), 6), 0, 2, 5 + index))];

        return new BitmapFont(FontFixtures.LineHeight, 8, [FontFixtures.Page], glyphs, []);
    }

    private static SimulationHost Host(Label label, Run? run = null)
    {
        Entity entity = new();
        entity.Add(label);
        Scene scene = new();
        scene.Add(entity);

        return new SimulationHost(scene, run: run);
    }

    // The characters drawn, read back from each glyph's region.
    private static string Describe(SimulationHost host)
    {
        BitmapFont font = Font();
        StringBuilder text = new();
        foreach (SpriteIntent drawn in host.Simulation.View.Sprites.ToArray())
        {
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
