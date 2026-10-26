# Testing a game

After this page you can test a game's scenes headless, with no window or graphics device, in an ordinary
test project. Each type's XML docs hold its rules.

A game's simulation runs the same in a test as in a window, under the
[determinism contract](architecture.md#determinism-contract). The game chooses its own test framework.

## Choosing the boundary

| Test subject | Entry point |
| --- | --- |
| Scene behaviour over time | `SimulationHost`, which owns ticks, input state and teardown. |
| Scene transitions, boot configuration or exit results | `EngineBuilder.RunHeadless`, from `JAG.Capsule.Runtime`. |

A headless test references the shell and begins with its generated `CapsuleBoot.Configure`, which hands
`CapsuleEngine.Configure` every generated registry, the input drivers and `IScenePayload` classes included.

## A scene over time

This test steps the [getting-started](getting-started.md) scene for one simulated second with the
move key held, then reads the entity back:

```csharp
using Capsule;
using Capsule.Input;
using Capsule.Scenes;
using MyGame;
using MyGame.Entities;
using MyGame.Scenes;

public sealed class PlayFieldTests
{
    [Fact]
    public void HoldingD_MovesTheBlobRight()
    {
        Run run = new();
        GameInput.Configure(run.Input);
        using SimulationHost host = new(new PlayField(), run: run);
        Blob blob = host.Scene.FindSingle<Blob>();
        float startX = blob.Position.X;

        host.Step(60, DeviceSnapshot.Of(Key.D));

        Assert.Equal(startX + 60f, blob.Position.X, precision: 3);
    }
}
```

A test composes a shipped scene document through the generated `CapsuleScenes.Registry` in
`Capsule.Generated`, as `Registry.Create(CapsuleAssets.Scenes.Halls.HallScene)`. One test catches every placement
a run would refuse ([Validating every document](scenes.md#validating-every-document)).

## What a test controls

Input is `DeviceSnapshot` values, an `InputScript`, or an `IInputDriver` that reads the scene
([`input.md`](input.md)). Audio mixing is simulation state, asserted through `Run.Audio` without playback
([`audio.md`](audio.md)). Saved state is `Run.Saves`, in memory at either boundary
([`persistence.md`](persistence.md)).

[`samples/MinimalGame/tests/MinimalGame.Tests/`](../samples/MinimalGame/tests/MinimalGame.Tests/) is the
worked example at both boundaries.
