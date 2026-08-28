# Architecture

Capsule keeps gameplay deterministic and headless-testable by separating pure simulation from the host.

## Determinism contract

Given the same initial state, fixed-step duration, and sequence of `DeviceSnapshot` values, a simulation produces the same state transitions and render intents.

- Gameplay reads `StepContext`; it cannot access wall-clock time, external IO, ambient randomness, asynchronous execution, or the graphics backend.
- Input edges are differences between snapshots. `SnapshotLatch` preserves them when render and simulation rates differ.
- Simulation is single-threaded. Scene entities update and draw in insertion order, and mutations requested during a step apply after iteration.
- `TotalSeconds` is derived from the tick count rather than accumulated.
- The runtime clamps frame spikes before scheduling fixed steps.

The analyzer enforces the logic boundary. Tests hold the scheduling, input-latching, scene-ordering, and mutation contracts.

## Render seam

Simulation emits backend-free render intents. The host draws them at display rate, interpolating entities and camera from the previous settled step to the current one with a shared fraction. Rendering never feeds state back into simulation.

The camera viewport is fitted uniformly into the output and letterboxed, so display shape does not change the visible world region.

## Package boundary

`JAG.Capsule` contains every substrate-free module. `JAG.Capsule.Runtime` contains the host and is the only package linked to MonoGame. `JAG.Capsule.Build` contains build-time tooling. The same boundary is enforced for project-reference and package consumers.
