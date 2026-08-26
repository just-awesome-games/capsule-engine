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

Rendering sits outside the contract: it reads the same state at whatever rate the display runs,
and nothing about the window reaches the simulation. The camera's region is fitted into the
surface being drawn to — scaled uniformly, centred, the slack left as black bars — so two players
on differently shaped displays see the same world region, and a resize changes only how large it
is drawn. A game that declares a render resolution rasterises into a fixed-size surface, which is
then fitted into the back buffer the same way; the rule is applied at every stage and nothing
stretches at any of them. Alt+Enter toggles the window from the host, outside the step, and the
chord is withheld from the snapshot rather than routed through the action layer, so a window
gesture cannot read as game input.

## Designed, awaiting their game

Engine code lands only with a consuming call site (see [`AGENTS.md`](../AGENTS.md)), so each entry
below is settled in direction and waits for its game — deliberately absent, not overlooked.

| Not yet built | Decided direction |
| --- | --- |
| Spawn type validation at build time | The map build hook validates a `.tmj`'s object Classes against the generated registry, so a `.tmj` placing an object type no entity claims fails the build rather than the load. Tile size is the only thing the hook checks at build time today; the registry a Class would be checked against is built. |
| Camera follow and interpolation | A camera that tracks a target under a policy the scene sets, and a camera interpolated between steps the way entity positions are, so a moving viewport is as smooth as what it frames. `Camera` carries a centre and a viewport size; a scene moves it in its own step. |
| Expand-the-view aspect policy | A wider window showing more world rather than bars. Letterbox is the engine's policy because it is the only one that honours the camera's declared region exactly, so every player sees the same thing. A game wanting expand would set its own camera `ViewportSize` from the window's aspect, which needs the host to expose that aspect to the simulation — a member with no call site today. |
| Window-state read-back | Game code cannot observe what the player changed, so a shell booting from a preferences file cannot save `Fullscreen = true` after an Alt+Enter. Closing that loop needs the host to expose the current window state to game code; it lands with the first options surface, whose shape would otherwise be guessed at. |
| Update filtering | Skipping entities with nothing to update, or components switched inactive, behind a flag on each. Every entity updates today; this waits for a measured frame profile from the verify harness, or for the first population of non-updating entities large enough to show up in one. |
| Input action sets | Actions grouped into contexts a scene switches between, so a menu and a room read one device differently. One flat binding set until a second kind of scene exists. |
| Scene picker | A dev boot menu: the game boots into a list of the scenes its generated registry already holds and picks one. Development tooling, never a shipped surface. |
| Collision | Broad-phase plus swept AABB against a tile grid, inside the fixed step, allocation-free. Physics stays the game's; the scene owns the `CollisionWorld` and supplies queries to entity and game code. |
| Entity properties | The spawn contract hands an entity its `EntitySpawn` at construction, and the map format grows optional fields non-breakingly. A general per-entity property bag waits for the first game that needs one. |
| A map with no tile grid | The grid goes optional, so a map may be placements alone: a screen with nothing to paint — a menu, a cutscene, terrain a game builds for itself — stays an authored document rather than a hand-wired scene. Such a map composes into a scene that adds no `TileMap` and takes its `Size` from the game, and the format admits the omission non-breakingly, so every map written against it today keeps loading. It waits for the first document with nothing to paint. |
| Several tile layers | One tile layer per map. Background, foreground and collision as separate layers is a game's composition question, and answering it early would fix a layering vocabulary in the format. |
| Textures and sprites | Raw runtime load behind a Capsule-typed facade, same hiding contract as rendering, with content referenced by handle so `Capsule.Scenes` stays substrate-free. A `SpriteRenderer` component, sheets and animation, and a textured-quad intent beside the colour quad. Lands with the first game that ships image assets. |
| Audio | Raw runtime load behind a Capsule-typed facade, same hiding contract as rendering: no backend type in a public signature. |
| Debug text | A zero-asset pixel font, landing with the debug overlay and the verify harness that need it. |
| Game-facing text | Games bring their own font files, rendered through a future text facility. The engine never ships a game-facing font: a built-in glyph set is a visual decision, and that belongs to the game. |
| Verify harness | A headless-ish entry point that seeds deterministically, plays a scripted `DeviceSnapshot` sequence for N fixed steps, captures a screenshot plus a state dump, runs the allocation probe, and exits non-zero on any failure. `DeviceSnapshot` already exists for it. |
| Screen-space (HUD) intents | A coordinate-space attribute on render intent — world, or camera-relative — rather than a second view or a second renderer. It arrives with the first HUD element that needs it. |
| Mouse | New members on `DeviceSnapshot` and a new bindable input alongside `Key` and `PadButton`. The action layer above them does not change. |
| Several gamepads at once | The first connected pad only. Merging several into one snapshot, or routing each to its own player, is a policy choice with no single right answer; it waits for the game that has more than one player. |
| Axis thresholds, SOCD priority | Neither: no threshold turns an axis into a digital action, and an opposing digital pair bound to one axis cancels to 0 rather than obeying the last input. |
