# Testing a game

Capsule's deterministic simulation runs in an ordinary test project without a window or graphics
device. Games choose their own test framework, assertions and fixtures. XML comments define each
API's behavior; this page helps choose the appropriate boundary.

| Test subject | Entry point |
| --- | --- |
| Scene behavior over time | `SceneRun`, which owns ticks, input state and simulation teardown. |
| A deliberately constructed step context | `SceneSimulation`. |
| Scene transitions, boot configuration or exit results | `CapsuleEngine.RunHeadless`, from `JAG.Capsule.Runtime`. |
| Geometry independent of scenes | `CollisionWorld2D`. |

Supply input as `DeviceSnapshot` values, script a sequence with `InputScript`, or implement an
`IInputDriver` that observes the scene. See [headless-play.md](headless-play.md) for driver discovery
and command-line execution.

Audio mixing is pure simulation state and can be asserted through `Scene.Audio` without playback.
Use a seeded `RandomSource` for repeatable runs; the cross-cutting guarantees are in
[architecture.md](architecture.md#determinism-contract).
