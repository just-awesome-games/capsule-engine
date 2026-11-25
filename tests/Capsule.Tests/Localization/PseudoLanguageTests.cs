using System.Numerics;
using Capsule.Input;
using Capsule.Localization;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Rendering;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Localization;

// What qps-ploc shows in a development build: the fallback's text accented and lengthened, with what no
// translation would change left as written.
public sealed class PseudoLanguageTests
{
    // A word gains 40% of its letters rounded up, spread over its vowels, else on its last letter. A lone
    // caption of ten letters or fewer doubles, and a longer one gains 40%. A [..] span is copied, "[[" is
    // one escaped bracket, and a span left open runs on into the next literal.
    [Theory]
    [InlineData("Options", false, false, "ÖÖptîîööñs", false)]
    [InlineData("Hmm", false, false, "Hmmmm", false)]
    [InlineData("Title", true, false, "Tîîîîtlééé", false)]
    [InlineData("Screenshake", true, false, "Sçréééééñshååkéé", false)]
    [InlineData("Go now", true, false, "Göö ñöööw", false)]
    [InlineData("Cafe\u0301s", true, false, "Çååååfééé\u0301s", false)]
    [InlineData("[color=#ffd060]Jump[/color] over", false, false, "[color=#ffd060]Jüüümp[/color] öövéér", false)]
    [InlineData("[[a]", false, false, "[[åå]", false)]
    [InlineData("[[Title]", true, false, "[[Tîîîîtlééé]", false)]
    [InlineData("=on]up", false, true, "=on]üüp", false)]
    [InlineData("a [b", false, false, "åå [b", true)]
    public void Write_LengthensAndAccentsWords_AndCopiesSpans(string text, bool lone, bool inTag, string shown, bool leftOpen)
    {
        char[] buffer = [];

        int length = PseudoLanguage.Write(text, lone, font: null, ref inTag, ref buffer, length: 0);

        Assert.Equal(shown, new string(buffer, 0, length));
        Assert.Equal(leftOpen, inTag);
    }

    // A key argument is rewritten as words, and a plain argument and a button's name show as written. Only
    // the pseudo-language's own tag selects it.
    [Fact]
    public void Arguments_AndButtonNames_ShowAsWritten()
    {
        Run run = new() { Language = "qps-ploc", Strings = StringTableFixtures.Buttons };

        Assert.Equal("Sçööréé: 12345", run.GetText(StringTableFixtures.Score, 12345));
        Assert.Equal("ÖÖñ ööf Avatar", run.GetText(StringTableFixtures.Pair, StringTableFixtures.On, "Avatar"));
        Assert.Equal("Préééss Shift Left", run.GetText(StringTableFixtures.Hint, Key.LeftShift));

        run.Language = "qps";

        Assert.Equal("Title", run.GetText(StringTableFixtures.Title));
    }

    // The fixture font carries no accented letter. Its label lengthens alone, and takes the accents once its
    // font carries them.
    [Fact]
    public void ALabel_AccentsOnlyWhatItsFontCarries()
    {
        Run run = new() { Language = "qps-ploc" };
        Label label = new(FontFixtures.Font(), StringTableFixtures.Title);
        Scene scene = new();
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, label));
        using SimulationHost host = new(scene, run: run);

        Assert.Equal("Tiiiitleee", label.Text);

        label.Font = new BitmapFont(FontFixtures.LineHeight, 8, [FontFixtures.Page], [Glyph('é'), Glyph('î')], []);

        Assert.Equal("Tîîîîtlééé", label.Text);
    }

    private static Glyph Glyph(char codepoint) => new(codepoint, 0, new TextureRegion(0, 0, 4, 6), 0, 2, 5);
}
