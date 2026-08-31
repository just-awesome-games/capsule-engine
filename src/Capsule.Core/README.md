# Capsule.Core

The contracts a simulation is written against, and nothing beneath them. Core references no
project and no package at all — that absence is what keeps a graphics type out of game logic.

## Inside

- `ISimulation`, `StepContext` — the fixed-step seam, and the only clock gameplay can read.
- `Input/` — devices as named actions: `InputState`, `ActionBindings`, `DeviceSnapshot`,
  `SnapshotLatch`, and the key, button and axis enumerations behind them.
- `Rendering/` — backend-free render intent: `FrameView`, `QuadIntent`, `CameraView`,
  `ColorRgba`, `ViewBounds`, `RenderMetrics`.
- `Assets/` — the texture, audio and font handles a generated asset registry hands out.

## How it ships

Inside the `JAG.Capsule` package as its own assembly; see [`../Capsule/`](../Capsule/README.md).
Referenced by `Capsule.Scenes` and `Capsule.Runtime`.

## Further reading

The determinism contract and the module map: [`docs/architecture.md`](../../docs/architecture.md).
Per-member behaviour lives in the XML comments.
