using System.Numerics;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.UI;

namespace MinimalGame.Tests;

// The prompt reads the bindings, the active device, the pad's family and the language each step. A
// device switch, a pad of another family, a rebind and a language change show without anything telling it.
public sealed class ActionPromptTests
{
    private static readonly InputAction Action = new("prompt-test");

    [Fact]
    public void APrompt_FollowsTheActiveDeviceAndARebind()
    {
        Run run = new();
        run.Input.Bind(Action, Key.Space, PadButton.South);
        using SimulationHost host = Host(run, out _);

        host.Step();
        Assert.Equal(Art(Key.Space), Drawn(host));

        host.Step(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal(Art(PadButton.South), Drawn(host));

        // Nothing about the device changed. Only the bindings did.
        run.Input.Rebind(Action, PadButton.East);
        host.Step();
        Assert.Equal(Art(PadButton.East), Drawn(host));
    }

    [Fact]
    public void AKeyWithNoArt_ShowsItsCharacterAndAnActionUnboundOnTheDeviceShowsNothing()
    {
        Run run = new();
        run.Input.Bind(Action, Key.F);
        using SimulationHost host = Host(run, out ActionPrompt prompt);

        host.Step();
        Assert.Equal("F", Shown(prompt));
        Assert.Equal(CapsuleAssets.Fonts.MenuFont.Measure("F").X, prompt.Width);

        host.Step(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal(0f, prompt.Width);
        Assert.Empty(host.Simulation.View.Sprites.ToArray());
    }

    // The sheet draws an Xbox pad. Another family's pad shows its button's name, and switching back
    // restores the art.
    [Fact]
    public void APadOfAnotherFamily_ShowsItsNameInsteadOfTheArt()
    {
        Run run = new();
        run.Input.Bind(Action, PadButton.South);
        using SimulationHost host = Host(run, out ActionPrompt prompt);

        DeviceSnapshot pressed = DeviceSnapshot.Empty.With(PadButton.South);
        host.Step(pressed.WithPadFamily(PadFamily.Xbox));
        Assert.Equal(Art(PadButton.South), Drawn(host));

        host.Step(pressed.WithPadFamily(PadFamily.PlayStation));
        Assert.DoesNotContain(host.Simulation.View.Sprites.ToArray(), drawn => Art(PadButton.South).Equals(drawn.Sprite));
        Assert.Equal("Cross", Shown(prompt));

        host.Step(pressed.WithPadFamily(PadFamily.Generic));
        Assert.Equal(Art(PadButton.South), Drawn(host));
    }

    // A key that prints no character and a button the sheet has no art for are named in the run's language.
    [Fact]
    public void AButtonWithNoArtOrCharacter_ShowsItsNameInTheRunsLanguage()
    {
        Run run = new() { Language = "de" };
        run.Input.Bind(Action, Key.LeftShift, PadButton.South);
        using SimulationHost host = Host(run, out ActionPrompt prompt);

        host.Step();
        Assert.Equal("Umschalt links", Shown(prompt));

        host.Step(DeviceSnapshot.Empty.With(PadButton.South).WithPadFamily(PadFamily.PlayStation));
        Assert.Equal("Kreuz", Shown(prompt));
    }

    private static SimulationHost Host(Run run, out ActionPrompt prompt)
    {
        Holder holder = new();
        prompt = new ActionPrompt(holder, Action);
        Scene scene = new();
        scene.Add(holder);

        return new SimulationHost(scene, run: run);
    }

    private static string Shown(ActionPrompt prompt) => prompt.Get<Label>().Text;

    private static Sprite Drawn(SimulationHost host) => Assert.Single(host.Simulation.View.Sprites.ToArray()).Sprite;

    private static Sprite Art(InputButton button)
    {
        Assert.True(GamePrompts.TryGet(button, out Sprite art));

        return art;
    }

    private sealed class Holder() : Entity(Vector2.Zero);
}
