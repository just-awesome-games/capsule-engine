using Capsule;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;

namespace MinimalGame.Tests;

// The profile is written at quit: each run's end banks its length on top of what earlier runs banked.
public sealed class ProfileTests
{
    [Fact]
    public void ARunsEnd_AddsItsLengthToThePlayedTime()
    {
        Run run = new();
        run.Saves.Write(GameSaves.Profile, new GameProfile { PlayedSeconds = 10 });
        GameBoot.Start(run);

        using SimulationHost host = new(new Options(), run: run);
        host.Step(60);
        GameBoot.End(run);

        Assert.Equal(10 + (60 * host.StepSeconds), run.Saves.Read(GameSaves.Profile).PlayedSeconds);
    }
}
