# Capsule — Architecture

Cross-cutting view: the project map, the two seams that define the engine, and the capabilities
awaiting their game. Per-module contracts live in
[`Capsule.Core/README.md`](../Capsule.Core/README.md) and
[`Capsule.Runtime/README.md`](../Capsule.Runtime/README.md).

## Project map

```
                    game logic  ─────────────►  Capsule.Core
                   (MyGame.Systems)                  ▲
                                                     │
                    game shell  ─────────────►  Capsule.Runtime  ──►  MonoGame
                    (MyGame)                                          (compile assets
                                                                       stop here)
```

| Project | References | Purpose |
| --- | --- | --- |
| `Capsule.Core` | **nothing** | Contracts and pure logic: `ISimulation`, input, render intent |
| `Capsule.Runtime` | `Capsule.Core`, MonoGame | The host: window, device, clock, keyboard, renderer, crash log |
| `Capsule.Tests` | both | Specs over Core; builder validation and the public-surface guard over Runtime |

Dependencies point one way, and each direction is held by something mechanical rather than by
review:

- **Core does not reference Runtime**, because Core has no project references. Pure logic
  cannot reach a device even by accident, which is what makes it testable in milliseconds.
- **Core does not reference MonoGame**, because Core has no package references at all. There is
  no setting to misconfigure.
- **A game does not reference MonoGame**, because `Capsule.Runtime` marks MonoGame's compile
  assets private. See the Runtime README's *MonoGame-hiding contract*.
- **A game's logic does not reference Runtime**, by the same split the studio MonoGame standard
  mandates: game logic references `Capsule.Core`, and only the one-file shell references
  `Capsule.Runtime`.

Games consume the engine as a **sibling clone, by project reference**. There is no package, no
feed and no version negotiation at one consumer; a game's CI reproduces the layout by checking
the engine out beside it at a pinned ref.

## The simulation/render seam

Entities and simulations never draw themselves. A simulation exposes a `FrameView` — a value
describing what it wants on screen — and the runtime turns that into draw calls.

```
ISimulation.View  ──►  FrameView  ──►  FrameRenderer  ──►  device
   (pure)              (pure)          (internal)          (backend)
```

Three things fall out of that:

- Game logic stays free of a graphics device, so a spec can assert what *would* be drawn.
- The renderer is replaceable without touching a game.
- The view is immutable and built once per distinct visual state, so the render path allocates
  nothing per frame. A view rebuilt every frame would allocate every frame — the one thing the
  fixed step must not do.

`FrameView` has no members today and the renderer clears the frame; render intent lands one
member at a time, each carried in by the game call site that needs it.

## The input pipeline, end to end

```
OS keyboard
    │  once per frame
    ▼
KeyboardSampler.Sample()          Runtime — the only place hardware enters
    │
    ▼
DeviceSnapshot                    Core — a pure value: the set of keys held at one instant
    │  every frame, drained step or not
    ▼
SnapshotLatch.Observe()           Core — unions the frames seen since the last step
    │  once per fixed step: ConsumeStepSnapshot()
    ▼
InputState.Advance(snapshot)      Core — diffs previous against current
    │
    ▼
IsHeld / WasPressed / WasReleased Core — resolved through ActionBindings
    │
    ▼
ISimulation.Step(in StepContext)  the game
```

`DeviceSnapshot` is the seam. Above it is hardware; below it is arithmetic. A harness that
fabricates snapshots drives the identical code path the shipping game does, which is what makes
a scripted-input run mean something — and because the latch sits below the seam and is pure, a
harness may drive it or bypass it and hand `InputState` one snapshot per tick.

`SnapshotLatch` exists because the two rates in the pipeline are not the same one. Sampling runs
at the render rate and consumption at the step rate, so a frame may drain no step — at 240 Hz
against a 60 Hz step, three frames in four. Discarding those samples would lose any key pressed
and released between two consumed steps entirely. The latch makes "down in any frame since the
last step" the step's truth, which restores the edge without letting a frame-rate change alter
how many times it fires.

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
| Interpolated rendering | The alpha is already computed and passed to the renderer; the remaining half is previous-state retention in the view, which arrives with the first thing that moves. |
| Gamepad, mouse | New members on `DeviceSnapshot` and new bindable inputs alongside `Key`. The action layer above them does not change. |
