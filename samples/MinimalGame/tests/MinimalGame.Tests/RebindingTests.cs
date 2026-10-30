using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.Scenes;
using MinimalGame.Game.UI;

namespace MinimalGame.Tests;

// Runtime rebinding proven at both boundaries a headless run touches: a rebinding already in the
// settings document is honoured at boot, and the options screen writes one into that same document.
public sealed class RebindingTests
{
    [Fact]
    public void ADocumentPresentAtBoot_RebindsJump()
    {
        Run run = new();

        GameSettings settings = run.Saves.Read(GameSaves.Settings);
        settings.Input.Jump = settings.Input.Jump.With(Key.F);
        run.Saves.Write(GameSaves.Settings, settings);

        run.StartGame(static run => new MinimalGameInstance(run));

        Assert.Contains((InputButton)Key.F, run.Input.ButtonsFor(GameInput.Jump).ToArray());
        Assert.DoesNotContain((InputButton)Key.Space, run.Input.ButtonsFor(GameInput.Jump).ToArray());
    }

    [Fact]
    public void TheOptionsScreenCaptures_APressIntoTheDocumentAndTheLiveBindings()
    {
        Run run = new();
        run.StartGame(static run => new MinimalGameInstance(run));

        using SimulationHost host = new(new Options(), run: run);

        host.Step(DeviceSnapshot.Of(Key.Enter));
        host.Step();
        host.Step(DeviceSnapshot.Of(Key.F));

        GameSettings settings = run.Saves.Read(GameSaves.Settings);
        Assert.Equal((InputButton)Key.F, settings.Input.Jump.Key);
        Assert.Contains((InputButton)Key.F, run.Input.ButtonsFor(GameInput.Jump).ToArray());
    }

    // The press that ends a capture is still down when the navigator steps after the menu. The
    // navigator does not read it as a fresh Confirm, and the capture stays closed.
    [Fact]
    public void ThePressThatEndsACapture_DoesNotReopenIt()
    {
        Run run = new();
        run.StartGame(static run => new MinimalGameInstance(run));

        using SimulationHost host = new(new Options(), run: run);

        host.Step(DeviceSnapshot.Of(Key.Space));
        host.Step();
        host.Step(DeviceSnapshot.Of(Key.Space));

        // The Jump row is the first item the menu adds. Its caption and prompt are the capture's state.
        MenuItem jump = host.Scene.FindFirst<MenuItem>()!;
        Assert.Equal((run.GetText(CapsuleStrings.Options.Jump), true), (jump.Caption, jump.ShowsPrompt));
    }
}
