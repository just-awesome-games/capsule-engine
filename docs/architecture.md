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
5. **Frame time is clamped** at the configured spike clamp, 0.25 s by default, so a stall
   bounds the number of steps it can queue.
6. **A scene's order is insertion order.** Entities update and draw in the order they were
   added, never in hash order, and an add or remove during a step lands at the end of it — so a
   step never iterates a list changing under it, and two runs over the same inputs produce the
   same update order and the same quads in the same order.

Rendering sits outside the contract: it reads the same state at whatever rate the display runs.

## Designed, awaiting their game

Engine code lands only with a consuming game call site in the same change-set (see
[`AGENTS.md`](../AGENTS.md)), so each entry below is settled in direction and waits for the game
that calls it. The direction is recorded so the eventual implementation is not re-litigated; the
absence is recorded so nobody reads a gap as an oversight.

| Not yet built | Decided direction |
| --- | --- |
| Level type validation at build time | The level build hook validates a `.tmj`'s object Classes against the generated registry, so a map painted with a type no `[LevelType]` class claims fails the build rather than the room. The registry itself is built (D-capsule-004). |
| Camera follow and interpolation | A camera that tracks a target under a policy the scene sets, and a camera interpolated between steps the way entity positions are, so a moving viewport is as smooth as what it frames. `Camera` carries a centre and a viewport size; a scene moves it in its own step. |
| Update filtering | Skipping entities with nothing to update, or components switched inactive, behind a flag on each. Every entity updates today; this waits for a measured frame profile from the verify harness, or for the first population of non-updating entities large enough to show up in one. |
| Input action sets | Actions grouped into contexts a scene switches between, so a menu and a room read one device differently. One flat binding set until a second kind of scene exists. |
| Scene picker | A dev boot menu: a client registers its scenes and boots into a list to choose one. Development tooling, never a shipped surface. |
| Collision | Broad-phase plus swept AABB against a tile grid, inside the fixed step, allocation-free. Physics stays the game's; the scene owns the `CollisionWorld` and supplies queries to entity and game code (D-capsule-004 re-homes it; the algorithm is unchanged). |
| Entity properties | The spawn contract hands an entity its level-entity data at construction, and the level format grows optional fields non-breakingly. A general per-entity property bag still waits for the first game that needs one (D-capsule-004). |
| Tile definitions | A tile's behavioural class and its appearance — a colour now, a sprite region later — are authored as Tiled tileset tile properties, carried into the level format's palette non-breakingly by the importer, and rendered by the engine `TileMap`. A tile with no appearance fails at import. Until then a `TileMap` takes a game-supplied `TileColorResolver`, asked for the whole palette at construction so an unpainted type still fails at load; that seam goes when this lands (D-capsule-004). |
| Several tile layers | One tile layer per level. Background, foreground and collision as separate layers is a game's composition question, and answering it early would fix a layering vocabulary in the format. |
| Textures and sprites | Raw runtime load behind a Capsule-typed facade, same hiding contract as rendering, with content referenced by handle so `Capsule.Scenes` stays substrate-free. A `SpriteRenderer` component, sheets and animation, and a textured-quad intent beside the colour quad. Lands with the first game that ships image assets (D-capsule-004). |
| Audio | Raw runtime load behind a Capsule-typed facade, same hiding contract as rendering: no backend type in a public signature. |
| Debug text | A zero-asset pixel font, returning with the debug overlay and the verify harness that need it. An implementation existed for the bootstrap screen and was deleted with it; it is recoverable from git history rather than re-derived. |
| Game-facing text | Games bring their own font files, rendered through a future text facility. The engine never ships a game-facing font: a built-in glyph set is a visual decision, and that belongs to the game. |
| Verify harness | A headless-ish entry point that seeds deterministically, plays a scripted `DeviceSnapshot` sequence for N fixed steps, captures a screenshot plus a state dump, runs the allocation probe, and exits non-zero on any failure. `DeviceSnapshot` already exists for it. |
| Screen-space (HUD) intents | A coordinate-space attribute on render intent — world, or camera-relative — rather than a second view or a second renderer. It arrives with the first HUD element that needs it. |
| Mouse | New members on `DeviceSnapshot` and a new bindable input alongside `Key` and `PadButton`. The action layer above them does not change. |
| Several gamepads at once | The first connected pad only. Merging several into one snapshot, or routing each to its own player, is a policy choice with no single right answer; it waits for the game that has more than one player. |
| Axis thresholds, SOCD priority | Neither: no threshold turns an axis into a digital action, and an opposing digital pair bound to one axis cancels to 0 rather than obeying the last input. |
