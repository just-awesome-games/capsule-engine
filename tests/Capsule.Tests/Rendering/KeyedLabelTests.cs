using System.Numerics;
using Capsule.Localization;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Tests.Localization;
using Capsule.Tests.Scenes;

namespace Capsule.Tests.Rendering;

public sealed class KeyedLabelTests
{
    // A request before the first step shows from the start. Once steps run, the getter reads a request at
    // once and the label shows it from the next step's start. A request for the tag already held changes nothing.
    [Fact]
    public void ALanguageRequest_ShowsAtOnceBeforeTheFirstStep_AndFromTheNextStepAfterIt()
    {
        Label label = new(FontFixtures.Font(), StringTableFixtures.Title);
        Run run = new() { Language = "de" };
        int applied = run.LanguageVersion;
        run.Language = "de";
        using SceneSimulation simulation = Started(label, run);

        Assert.Equal(applied, run.LanguageVersion);
        Assert.Equal("Titel", label.Text);

        simulation.Step(SceneFixtures.Step());
        run.Language = "fr-CA";

        Assert.Equal("fr-CA", run.Language);
        Assert.Equal("Titel", label.Text);

        simulation.Step(SceneFixtures.Step(1));

        Assert.Equal("Titre", label.Text);

        applied = run.LanguageVersion;
        run.Language = "fr-CA";
        simulation.Step(SceneFixtures.Step(2));

        Assert.Equal(applied, run.LanguageVersion);
    }

    // The language's rules pick the category, an exact branch beats it, and # shows the count's text.
    [Theory]
    [InlineData("pl", 1, "1 śruba")]
    [InlineData("pl", 2, "2 śruby")]
    [InlineData("pl", 5, "5 śrub")]
    [InlineData("pl", 12, "12 śrub")]
    [InlineData("pl", 22, "22 śruby")]
    [InlineData("pl", 25, "25 śrub")]
    [InlineData("en", 0, "No bolts")]
    [InlineData("en", 1, "1 bolt")]
    public void APluralLabel_ShowsTheFormItsCountSelects(string language, int count, string shown)
    {
        Label label = new(FontFixtures.Font());
        using SceneSimulation simulation = Started(label, new Run { Language = language });

        label.SetText(StringTableFixtures.Bolts, count);

        Assert.Equal(shown, label.Text);
    }

    [Fact]
    public void ALanguageSwitch_SelectsThePluralFormAgain()
    {
        Label label = new(FontFixtures.Font());
        Run run = new() { Language = "pl" };
        using SceneSimulation simulation = Started(label, run);
        label.SetText(StringTableFixtures.Bolts, 3UL);

        run.Language = "fr";
        simulation.Step(SceneFixtures.Step());

        Assert.Equal("3 boulons", label.Text);
    }

    // Each argument is formatted once with the invariant culture and placed by its index, wherever the
    // language's text puts it.
    [Fact]
    public void AFormattedLabel_KeepsItsArgumentsAcrossALanguageSwitch()
    {
        Label label = new(FontFixtures.Font());
        Run run = new();
        using SceneSimulation simulation = Started(label, run);

        label.SetText(StringTableFixtures.Pair, 1.5f, "Avatar");
        Assert.Equal("1.5 of Avatar", label.Text);

        run.Language = "de";
        simulation.Step(SceneFixtures.Step());

        Assert.Equal("Avatar hat 1.5", label.Text);
    }

    // A key argument is kept as a key, and resolves in the template's language each time the template does.
    // A nullable from the table's lookup and an object holding a key are kept the same way.
    [Theory]
    [InlineData("key")]
    [InlineData("nullable")]
    [InlineData("object")]
    public void AKeyArgument_ShowsItsTextInTheActiveLanguage_AndFollowsALanguageSwitch(string shape)
    {
        Label label = new(FontFixtures.Font());
        Run run = new() { Language = "de" };
        using SceneSimulation simulation = Started(label, run);

        switch (shape)
        {
            case "key":
                label.SetText(StringTableFixtures.Score, StringTableFixtures.On);
                break;
            case "nullable":
                label.SetText(StringTableFixtures.Score, StringTableFixtures.Table.Find("menu.on"));
                break;
            default:
                label.SetText(StringTableFixtures.Score, (object)StringTableFixtures.On);
                break;
        }

        Assert.Equal("Punkte: An", label.Text);

        run.Language = "pl";
        simulation.Step(SceneFixtures.Step());

        Assert.Equal("Wynik: Wł.", label.Text);
    }

    [Fact]
    public void AKeyArgumentATranslationLacks_ShowsTheSourceText()
    {
        Label label = new(FontFixtures.Font());
        using SceneSimulation simulation = Started(label, new Run { Language = "fr" });

        label.SetText(StringTableFixtures.Pair, StringTableFixtures.On, StringTableFixtures.Title);

        Assert.Equal("On sur Titre", label.Text);
    }

    [Fact]
    public void WritingLiteralText_DropsTheKey()
    {
        Label label = new(FontFixtures.Font(), StringTableFixtures.Title);
        Run run = new();
        using SceneSimulation simulation = Started(label, run);

        label.Text = "AB";
        run.Language = "de";
        simulation.Step(SceneFixtures.Step());

        Assert.Equal("AB", label.Text);
    }

    [Fact]
    public void AKeyedLabelsSize_IsRefusedBeforeItsEntityHasARun()
    {
        Label label = new(FontFixtures.Font(), StringTableFixtures.Title);
        _ = new SceneFixtures.Holder(Vector2.Zero, label);

        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => label.ContentSize);

        Assert.Contains("menu.title", refused.Message, StringComparison.Ordinal);
        Assert.Contains("OnStart", refused.Message, StringComparison.Ordinal);

        // A label on no entity at all is refused the same way, for every measure.
        Label unattached = new(FontFixtures.Font(), StringTableFixtures.Title);
        _ = Assert.Throws<InvalidOperationException>(() => unattached.Bounds);
    }

    // Two runs can hold the same language version. A label moved between them shows its new run's language.
    [Fact]
    public void AKeyedLabelMovedToAnotherRun_ShowsThatRunsLanguage()
    {
        Label label = new(FontFixtures.Font(), StringTableFixtures.Title);
        using SceneSimulation german = Started(label, new Run { Language = "de" });
        Assert.Equal("Titel", label.Text);

        label.Entity!.Remove(label);
        using SceneSimulation french = Started(label, new Run { Language = "fr" });

        Assert.Equal("Titre", label.Text);
    }

    // A key passed with the wrong number of arguments, a count that is no integer, the default key, or a key
    // argument that is null, default or has placeholders of its own, names the defect, however it is held.
    [Theory]
    [InlineData(0, "'hud.score' takes 1 argument(s), and 0 were passed")]
    [InlineData(2, "'hud.score' takes 1 argument(s), and 2 were passed")]
    [InlineData(1, "'hud.bolts' selects a plural form on argument 0, which was passed no integer")]
    [InlineData(-1, "is default and names no text")]
    [InlineData(3, "'hud.score' takes 1 argument(s) and cannot fill a placeholder")]
    [InlineData(4, "is default and names no text")]
    [InlineData(5, "is null and names no text. Pass a key that names one.")]
    [InlineData(6, "is default and names no text")]
    [InlineData(7, "'hud.score' takes 1 argument(s) and cannot fill a placeholder")]
    public void AKeyTheLabelCannotFill_IsRefused(int arguments, string defect)
    {
        Label label = new(FontFixtures.Font());
        TextKey score = StringTableFixtures.Score;

        ArgumentException refused = Assert.Throws<ArgumentException>(() =>
        {
            switch (arguments)
            {
                case 0:
                    label.SetText(score);
                    break;
                case 2:
                    label.SetText(score, 1, 2);
                    break;
                case 1:
                    label.SetText(StringTableFixtures.Bolts, 1.5f);
                    break;
                case 3:
                    label.SetText(score, score);
                    break;
                case 4:
                    label.SetText(score, default(TextKey));
                    break;
                case 5:
                    label.SetText(score, StringTableFixtures.Table.Find("menu.absent"));
                    break;
                case 6:
                    label.SetText(score, (object)default(TextKey));
                    break;
                case 7:
                    label.SetText(score, (TextKey?)score);
                    break;
                default:
                    label.SetText(default(TextKey));
                    break;
            }
        });

        Assert.Contains(defect, refused.Message, StringComparison.Ordinal);
    }

    private static SceneSimulation Started(Label label, Run run)
    {
        Scene scene = new();
        scene.Add(new SceneFixtures.Holder(Vector2.Zero, label));

        return new SceneSimulation(scene, run: run);
    }
}
