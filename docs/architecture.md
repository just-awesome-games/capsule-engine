# Capsule — Architecture

The two things the code cannot state for itself: the determinism contract the whole engine is
built to hold, and the capabilities that are settled in direction but not yet built.

## The determinism contract

Given the same starting state, the same sequence of `DeviceSnapshot`s and the same fixed step,
a simulation produces the same run. Concretely:

1. **The step is fixed, and time is the engine's.** Gameplay reads `StepContext.DeltaSeconds`,
   `Tick` and `TotalSeconds`, never wall-clock time. The runtime owns the accumulator and the
   tick counter; MonoGame's own fixed step is off. `TotalSeconds` is `Tick * DeltaSeconds`
   rather than an accumulation, so two runs over the same tick sequence agree exactly.
2. **Input edges are diffs, never events.** No callback, queue or timestamp reaches a
   simulation — only the difference between two snapshots.
3. **Sampling is decoupled from stepping.** `SnapshotLatch` reconciles the two rates in both
   directions — many frames per step and many steps per frame — so a frame-rate change cannot
   change how many times an edge fires, nor whether it fires at all.
4. **The simulation is single-threaded.** No worker threads, no `async` in the step path.
5. **Frame time is clamped at 0.25 s**, so a stall bounds the number of steps it can queue.

Rendering sits outside the contract: it reads the same state at whatever rate the display runs.

## Designed, awaiting their game

Engine code lands only with a consuming game call site in the same change-set (see
[`AGENTS.md`](../AGENTS.md)), so each entry below is settled in direction and waits for the game
that calls it. The direction is recorded so the eventual implementation is not re-litigated; the
absence is recorded so nobody reads a gap as an oversight.

| Not yet built | Decided direction |
| --- | --- |
| Scenes | Data, not a type hierarchy: a scene is loaded content plus the simulation state a game constructs from it. No scene graph. |
| Entities | Bespoke per game first. No third-party ECS and no engine-imposed entity base class; if a shared shape emerges across two games, it is promoted then. |
| Collision | Broad-phase plus swept AABB against a tile grid, inside the fixed step, allocation-free. Physics stays the game's; the engine supplies queries. |
| Tiled | `.tmj` loaded directly through source-generated `System.Text.Json` — no export step, no generated map artifact, per the studio standard. |
| Audio | Raw runtime load behind a Capsule-typed facade, same hiding contract as rendering: no backend type in a public signature. |
| Debug text | A zero-asset pixel font, returning with the debug overlay and the verify harness that need it. An implementation existed for the bootstrap screen and was deleted with it; it is recoverable from git history rather than re-derived. |
| Game-facing text | Games bring their own font files, rendered through a future text facility. The engine never ships a game-facing font: a built-in glyph set is a visual decision, and that belongs to the game. |
| Verify harness | A headless-ish entry point that seeds deterministically, plays a scripted `DeviceSnapshot` sequence for N fixed steps, captures a screenshot plus a state dump, runs the allocation probe, and exits non-zero on any failure. `DeviceSnapshot` already exists for it. |
| Screen-space (HUD) intents | A coordinate-space attribute on render intent — world, or camera-relative — rather than a second view or a second renderer. It arrives with the first HUD element that needs it. |
| Gamepad, mouse | New members on `DeviceSnapshot` and new bindable inputs alongside `Key`. The action layer above them does not change. |
