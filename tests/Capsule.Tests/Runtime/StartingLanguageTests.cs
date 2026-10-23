using Capsule.Input;
using Capsule.Localization;
using Capsule.Persistence;
using Capsule.Runtime;
using Capsule.Runtime.Scenes;
using Capsule.Scenes;
using Capsule.Tests.Localization;

namespace Capsule.Tests.Runtime;

// Which language a run starts in: the player's in a played run, the fallback in a headless or driven one,
// a run-start hook's choice over either, and --language over everything.
[Collection(LogSinkCollection.Name)]
public sealed class StartingLanguageTests
{
    private static readonly StringTable EnglishAndFrench = StringTableFixtures.Shipping("en", "fr-FR");

    private string? _seen = "never started";

    // fr-CA ships nowhere, and the closest shipped language is fr-FR, which the first scene sees as it
    // starts. A choice the run-start hook restores from a save wins over the player's preference.
    [Theory]
    [InlineData(null, "fr-FR")]
    [InlineData("en", "en")]
    public void APlayedRun_StartsInThePlayersLanguage_AndTheRunStartHookWins(string? saved, string expected)
    {
        Preferring platform = new("fr-CA", "en");
        EngineBuilder builder = Builder(platform);
        if (saved is not null)
        {
            builder.WithRunStart(run => run.Language = saved);
        }

        using SceneHost host = builder.CreateHost(Opening, null, 0, played: true);

        Assert.Equal(expected, _seen);
        Assert.Equal(1, platform.Asked);
    }

    [Fact]
    public void ADrivenRun_NeverAsksThePlatform_AndStartsInTheFallback()
    {
        Preferring platform = new("fr-CA");

        Builder(platform).RunHeadless<Recording>(new InputScript().Wait(1).Build());

        Assert.Null(_seen);
        Assert.Equal(0, platform.Asked);
    }

    // The flag names a shipped language by its closest tag and wins over the hook, played or not.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheLanguageFlag_WinsOverTheRunStartHook(bool played)
    {
        EngineBuilder builder = Builder(new Preferring("en"))
            .WithRunStart(static run => run.Language = "en")
            .WithCommandLine(["--language", "fr"]);

        using SceneHost host = builder.CreateHost(Opening, null, 0, played);

        Assert.Equal("fr-FR", _seen);
    }

    [Theory]
    [InlineData(true, "no shipped language matches 'de'. Shipped: en, fr-FR.")]
    [InlineData(false, "ships no string tables")]
    public void TheLanguageFlag_IsRefused_WhenNoShippedLanguageMatches(bool tables, string defect)
    {
        EngineBuilder builder = Builder(new Preferring(), tables ? EnglishAndFrench : null);

        CommandLineException refused = Assert.Throws<CommandLineException>(() => builder.WithCommandLine(["--language", "de"]));

        Assert.Contains(defect, refused.Message, StringComparison.Ordinal);
    }

    private static SceneTransition Opening => SceneTransition.ToScene(typeof(Recording), null);

    private EngineBuilder Builder(HostPlatform platform) => Builder(platform, EnglishAndFrench);

    private EngineBuilder Builder(HostPlatform platform, StringTable? strings) =>
        CapsuleEngine.Configure(
                "Language Game",
                platform,
                new SceneRegistry(new EntityRegistry([]), [SceneRegistration.Plain(typeof(Recording), _ => new Recording(language => _seen = language))]),
                strings: strings)
            .WithFixedStep(10)
            .WithoutCrashLog()
            .WithoutLogging();

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
