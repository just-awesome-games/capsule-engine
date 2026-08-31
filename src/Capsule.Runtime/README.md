# Capsule.Runtime

The host a game's shell boots. Everything that touches a device, a window or wall-clock time is
here, and it is the only module that links MonoGame — with the backend's compile assets withheld,
so a game cannot write a `Microsoft.Xna.Framework` using even transitively.

## Inside

- `CapsuleEngine`, `CapsuleGame`, `EngineBuilder`, `EngineOptions`, `SceneEngineBuilder`,
  `SimulationEngineBuilder` — configuring and starting a game.
- `FixedStepScheduler` — real elapsed time turned into fixed simulation steps, spikes clamped.
- `Input/` — keyboard and gamepad sampling, and deadzone filtering.
- `Rendering/` — `FrameRenderer` and `Letterbox`: render intent drawn at display rate, the camera
  viewport fitted uniformly into the output.
- `SceneHost`, `SceneComposer`, `SafeName`, `CrashLog` — scene hosting, transitions, and the
  unhandled-exception record.

## How it ships

As the `JAG.Capsule.Runtime` package, which depends on a pinned `JAG.Capsule`. Only a game's shell
project references it; the logic assembly must not, and Capsule's analyzer enforces that.

## Further reading

The render seam and the determinism contract: [`docs/architecture.md`](../../docs/architecture.md).
Project wiring for a consuming game: [`docs/consuming-capsule.md`](../../docs/consuming-capsule.md).
