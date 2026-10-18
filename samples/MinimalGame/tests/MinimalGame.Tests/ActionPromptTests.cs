using System.Numerics;
using Capsule;
using Capsule.Generated;
using Capsule.Input;
using Capsule.Rendering;
using Capsule.Scenes;
using MinimalGame.Game;
using MinimalGame.Game.UI;

namespace MinimalGame.Tests;

// The prompt reads the bindings and the active device each step. A device switch and a rebind show
// without anything telling it.
public sealed class ActionPromptTests
{
    private static readonly InputAction Action = new("prompt-test");

    [Fact]
    public void APrompt_FollowsTheActiveDeviceAndARebind()
    {
        Run run = new();
        run.Input.Bindings.Bind(Action, Key.Space, PadButton.South);
        using SimulationHost host = Host(run, out _);

        host.Step();
        Assert.Equal(Art(Key.Space), Drawn(host));

        host.Step(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal(Art(PadButton.South), Drawn(host));

        // Nothing about the device changed. Only the bindings did.
        run.Input.Bindings.Rebind(Action, PadButton.East);
        host.Step();
        Assert.Equal(Art(PadButton.East), Drawn(host));
    }

    [Fact]
    public void AButtonWithNoArt_ShowsItsNameAndAnActionUnboundOnTheDeviceShowsNothing()
    {
        Run run = new();
        run.Input.Bindings.Bind(Action, Key.F);
        using SimulationHost host = Host(run, out ActionPrompt prompt);

        host.Step();
        Assert.Equal(CapsuleAssets.Fonts.MenuFont.Measure("F").X, prompt.Width);

        host.Step(DeviceSnapshot.Empty.With(PadButton.South));
        Assert.Equal(0f, prompt.Width);
        Assert.Empty(host.Simulation.View.Sprites.ToArray());
    }

    private static SimulationHost Host(Run run, out ActionPrompt prompt)
    {
        Holder holder = new();
        prompt = new ActionPrompt(holder, Action);
        Scene scene = new();
        scene.Add(holder);

        return new SimulationHost(scene, run: run);
    }

    private static Sprite Drawn(SimulationHost host) => Assert.Single(host.Simulation.View.Sprites.ToArray()).Sprite;

    private static Sprite Art(InputButton button)
    {
        Assert.True(GamePrompts.TryGet(button, out Sprite art));

        return art;
    }

    private sealed class Holder() : Entity(Vector2.Zero);
}
