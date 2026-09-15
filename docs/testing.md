# Testing a game

Capsule's deterministic simulation runs in an ordinary test project without a window or graphics device; a game chooses its own test framework. Pick the boundary by what the test is about:

| Test subject | Entry point |
| --- | --- |
| Scene behaviour over time | `SimulationHost`, which owns ticks, input state and teardown. |
| A deliberately constructed step context | `SceneSimulation`. |
| Scene transitions, boot configuration or exit results | `CapsuleEngine.RunHeadless`, from `JAG.Capsule.Runtime`. |
| Geometry independent of scenes | `CollisionWorld2D`. |

Input is `DeviceSnapshot` values, an `InputScript`, or an `IInputDriver` that reads the scene ([`headless-play.md`](headless-play.md)). Audio mixing is simulation state, asserted through `Run.Audio` without playback; a seeded `RandomSource` makes a run repeatable under the [determinism contract](architecture.md#determinism-contract). [`samples/MinimalGame/tests/MinimalGame.Tests/`](../samples/MinimalGame/tests/MinimalGame.Tests/) is the worked example at both boundaries.
