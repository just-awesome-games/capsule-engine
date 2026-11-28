using Capsule.Input;
using Capsule.Localization;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.DevTools.Recordings;
using Capsule.Scenes;
using Capsule.Tests.Localization;

namespace Capsule.Tests.Runtime;

// Which language a run starts in: the player's in a played run, the fallback in a headless or driven one,
// the game object's choice over either, and --language over everything.
[Collection(LogSinkCollection.Name)]
public sealed class StartingLanguageTests
{
    private static readonly StringTable EnglishAndFrench = StringTableFixtures.Shipping("en", "fr-FR");

    private string? _seen = "never started";

    // fr-CA ships nowhere, and the closest shipped language is fr-FR, which the first scene sees as it
    // starts. A choice the game object restores from a save wins over the player's preference.
    [Theory]
    [InlineData(null, "fr-FR")]
    [InlineData("en", "en")]
    public void APlayedRun_StartsInThePlayersLanguage_AndTheGameObjectsChoiceWins(string? saved, string expected)
    {
        Preferring platform = new("fr-CA", "en");
        EngineBuilder builder = Builder(platform);
        if (saved is not null)
        {
            builder.WithGame(CallbackGame.Starting(run => run.Language = saved));
        }

        Start(builder, played: true);

        Assert.Equal(expected, _seen);
        Assert.Equal(1, platform.Asked);
    }

    [Fact]
    public void ADrivenRun_NeverAsksThePlatform_AndStartsInTheFallback()
    {
        Preferring platform = new("fr-CA");

        Start(Builder(platform), played: false);

        Assert.Null(_seen);
        Assert.Equal(0, platform.Asked);
    }

    // The flag names a shipped language by its closest tag, or the pseudo-language, and wins over the game
    // object's choice, played or not.
    [Theory]
    [InlineData(true, "fr", "fr-FR")]
    [InlineData(false, "fr", "fr-FR")]
    [InlineData(false, "QPS_ploc", "qps-ploc")]
    public void TheLanguageFlag_WinsOverTheGameObjectsChoice(bool played, string flag, string expected)
    {
        EngineBuilder builder = Builder(new Preferring("en"))
            .WithGame(CallbackGame.Starting(static run => run.Language = "en"))
            .WithCommandLine(["--language", flag]);

        Start(builder, played);

        Assert.Equal(expected, _seen);
    }

    [Theory]
    [InlineData(true, "no shipped language matches 'de'. Shipped: en, fr-FR, and qps-ploc.")]
    [InlineData(false, "ships no string tables")]
    public void TheLanguageFlag_IsRefused_WhenNoShippedLanguageMatches(bool tables, string defect)
    {
        EngineBuilder builder = Builder(new Preferring(), tables ? EnglishAndFrench : null);

        CommandLineException refused = Assert.Throws<CommandLineException>(() => builder.WithCommandLine(["--language", "de"]));

        Assert.Contains(defect, refused.Message, StringComparison.Ordinal);
    }

    // A played run is windowed with no driver, here with no window, a medium in memory and no recording. Any
    // other is headless or driven. Either ends at its first step.
    private static void Start(EngineBuilder builder, bool played)
    {
        if (played)
        {
            builder.WithSaveStorage(new RecordedSaveStorage([])).WithoutRecording()
                .RunRecorded(SceneTransition.ToScene(typeof(Recording), null), replayed: null, static (_, _, _, _) => { });
        }
        else
        {
            builder.RunHeadless<Recording>(new InputScript().Wait(1).Build());
        }
    }

    private EngineBuilder Builder(HostPlatform platform) => Builder(platform, EnglishAndFrench);

    private EngineBuilder Builder(HostPlatform platform, StringTable? strings) =>
        QuietEngine.Configure([SceneRegistration.Plain(typeof(Recording), _ => new Recording(language => _seen = language))], strings: strings)
            .WithPlatform(platform);

    // Reports the run's language as the scene starts.
    private sealed class Recording(Action<string?> started) : Scene
    {
        protected override void OnStart() => started(Run.Language);

        protected override void OnStep(in StepContext context) => Run.RequestExit();
    }

    // Reports these preferences and counts how often it was asked.
    private sealed class Preferring(params string[] preferred) : HostPlatform
    {
        internal int Asked { get; private set; }

        protected internal override Stream OpenContent(string relativePath) => throw new NotSupportedException();

        protected internal override ISaveStorage OpenSaveStorage(string localFolderName) => throw new NotSupportedException();

        protected internal override IReadOnlyList<string> PreferredLanguages()
        {
            Asked++;
            return preferred;
        }
    }
}
