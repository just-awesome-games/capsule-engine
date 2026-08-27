# Capsule — Architecture

The determinism contract the engine's types and analyzers enforce together.

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
7. **The compiler guards the boundary.** A logic-role project fails its build if it reaches the
   runtime/backend, external I/O, ambient clocks or randomness, or asynchronous execution. Seeded
   randomness remains explicit state; role-free tools are outside this policy.

Rendering sits outside the contract: it reads the same state at whatever rate the display runs,
and nothing about the window reaches the simulation. A frame between two steps interpolates every
quad from where it was to where it is, and the camera along with them — one fraction applied to
the whole scene, so a moving camera does not drag the world back and let it catch up. A deliberate
cut sets the camera's previous centre to its current one and so renders as a cut. Culling tests
the union of the camera's previous and current regions, the way a quad is tested over its own
swept corner, so what is visible at any point mid-step reaches the frame. The camera's region is
fitted into the surface being drawn to — scaled uniformly, centred, the slack left as black bars —
so two players on differently shaped displays see the same world region, and a resize changes only
how large it is drawn. A game that declares a render resolution rasterises into a fixed-size surface, which is
then fitted into the back buffer the same way; the rule is applied at every stage and nothing
stretches at any of them. Alt+Enter toggles the window from the host, outside the step, and the
chord is withheld from the snapshot rather than routed through the action layer, so a window
gesture cannot read as game input.
