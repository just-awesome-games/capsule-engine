using Capsule;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;
using MinimalGame.Game.UI;

namespace MinimalGame.Tests;

// The player's language choice is a setting like any other: kept in the settings document and honoured at boot.
public sealed class LanguageTests
{
    [Fact]
    public void AChoiceInTheDocumentAtBoot_ShowsTheOptionsScreenInThatLanguage()
    {
        Run run = new();

        GameSettings settings = run.Saves.Read(GameSaves.Settings);
        settings.Language = "de";
        run.Saves.Write(GameSaves.Settings, settings);

        run.StartGame(static run => new MinimalGameInstance(run));

        using SimulationHost host = new(new Options(), run: run);
        host.Step();

        Assert.Equal("Springen:", host.Scene.FindFirst<MenuItem>()!.Caption);
    }
}
