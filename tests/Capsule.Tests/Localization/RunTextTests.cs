namespace Capsule.Tests.Localization;

public sealed class RunTextTests
{
    // A request before the first step applies at once. Each read sees the language just requested, and the
    // source text for a key that language lacks.
    [Fact]
    public void GetText_ReturnsTheActiveLanguage_AndTheNewOneAfterASwitch()
    {
        Run run = new() { Language = "de" };

        Assert.Equal("Titel", run.GetText(StringTableFixtures.Title));

        run.Language = "fr";

        Assert.Equal("Titre", run.GetText(StringTableFixtures.Title));
        Assert.Equal("On", run.GetText(StringTableFixtures.On));
    }

    // An argument, a key argument and a plural count fill as a label fills them.
    [Fact]
    public void GetText_FillsItsArgumentsAsALabelDoes()
    {
        Run run = new() { Language = "pl" };

        Assert.Equal("Wynik: 12", run.GetText(StringTableFixtures.Score, 12));
        Assert.Equal("Wł. z 1.5", run.GetText(StringTableFixtures.Pair, StringTableFixtures.On, 1.5f));
        Assert.Equal("22 śruby", run.GetText(StringTableFixtures.Bolts, 22));
    }

    // The label's refusals guard it too. The label's specs cover each one.
    [Fact]
    public void GetText_RefusesAKeyTheLabelWouldRefuse()
    {
        Run run = new();

        ArgumentException refused = Assert.Throws<ArgumentException>(() => run.GetText(StringTableFixtures.Score));

        Assert.Contains("'hud.score' takes 1 argument(s), and 0 were passed", refused.Message, StringComparison.Ordinal);
    }

    // An argument's formatting may call GetText on the same run, with arguments of its own. The inner call
    // fills its own arguments, and the outer one keeps its key and every argument around it.
    [Fact]
    public void GetText_CalledWhileFormattingAnArgument_LeavesTheOuterCallIntact()
    {
        Run run = new();

        Assert.Equal("Score: Title", run.GetText(StringTableFixtures.Score, new Formatted(() => run.GetText(StringTableFixtures.Title))));
        Assert.Equal(
            "Score: 7 of Avatar",
            run.GetText(StringTableFixtures.Pair, new Formatted(() => run.GetText(StringTableFixtures.Score, 7)), "Avatar"));
    }

    private sealed class Formatted(Func<string> text)
    {
        public override string ToString() => text();
    }
}
