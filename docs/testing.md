# Testing a game

Capsule's deterministic simulation runs in an ordinary test project without a window or graphics device,
and a game chooses its own test framework. Pick the boundary by what the test is about:

| Test subject | Entry point |
| --- | --- |
| Scene behaviour over time | `SimulationHost`, which owns ticks, input state and teardown. |
| Scene transitions, boot configuration or exit results | `CapsuleEngine.RunHeadless`, from `JAG.Capsule.Runtime`, with a platform module. |

Input is `DeviceSnapshot` values, an `InputScript`, or an `IInputDriver` that reads the scene
([`input.md`](input.md)). Audio mixing is simulation state, asserted through `Run.Audio` without playback
([`audio.md`](audio.md)). Saved state is `Run.Saves`, in memory at either boundary
([`persistence.md`](persistence.md)). A seeded `RandomSource` makes a run repeatable under the
[determinism contract](architecture.md#determinism-contract).
[`samples/MinimalGame/tests/MinimalGame.Tests/`](../samples/MinimalGame/tests/MinimalGame.Tests/) is the
worked example at both boundaries.
