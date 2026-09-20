using Capsule;
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

        GameBoot.Start(run);

        Assert.Contains((InputButton)Key.F, run.Input.Bindings.ButtonsFor(GameInput.Jump).ToArray());
        Assert.DoesNotContain((InputButton)Key.Space, run.Input.Bindings.ButtonsFor(GameInput.Jump).ToArray());
    }

    [Fact]
    public void TheOptionsScreenCaptures_APressIntoTheDocumentAndTheLiveBindings()
    {
        Run run = new();
        GameBoot.Start(run);

        using SimulationHost host = new(new Options(), run: run);

        host.Step(DeviceSnapshot.Of(Key.Enter));
        host.Step();
        host.Step(DeviceSnapshot.Of(Key.F));

        GameSettings settings = run.Saves.Read(GameSaves.Settings);
        Assert.Equal((InputButton)Key.F, settings.Input.Jump.Key);
        Assert.Contains((InputButton)Key.F, run.Input.Bindings.ButtonsFor(GameInput.Jump).ToArray());
    }

    // The playtest bug: the press that ends a capture is still down when the navigator, stepping
    // after the menu, would otherwise read it as a fresh Confirm and reopen the capture it just closed.
    [Fact]
    public void ThePressThatEndsACapture_DoesNotReopenIt()
    {
        Run run = new();
        GameBoot.Start(run);

        using SimulationHost host = new(new Options(), run: run);

        host.Step(DeviceSnapshot.Of(Key.Space));
        host.Step();
        host.Step(DeviceSnapshot.Of(Key.Space));

        // The Jump row is the first item the menu adds. Its caption is the capture's state.
        Assert.Equal("Jump: Space", host.Scene.FindFirst<MenuItem>()!.Caption);
    }
}
