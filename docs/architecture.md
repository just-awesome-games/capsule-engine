# Architecture

Capsule keeps gameplay deterministic and headless-testable by separating pure simulation from the host.

## Modules

| Module | Charter | May reference |
| --- | --- | --- |
| `Capsule.Core` | The contracts a simulation is written against, and nothing beneath them. Core references no project and no package at all — that absence is what keeps a graphics type out of game logic. | nothing |
| `Capsule.Collision` | Collision, and only collision: shapes, broadphase, queries, sweeps and a mover. No dynamics, no solver, no forces. Substrate-free — Capsule.Core only — so a world is built and asserted headlessly. | Core |
| `Capsule.Scenes` | The world a game plays in: a scene, the entities on it, their components, the camera, and the scene document a scene is composed from. Substrate-free — Capsule.Core and Capsule.Collision only — so a scene is constructible and assertable headlessly. | Core, Collision |
| `Capsule.Runtime` | The host: window, device, clock, sampling, renderer, crash log. | the pure modules |
| `Capsule.Generators` | Capsule's source generators and the compile-time enforcement of Capsule's game-logic boundary. Generators read a compilation and emit the registries a game would otherwise hand-maintain, so a game keeps no registration table and uses no reflection to boot. The analyzer enforces the logic boundary; determinism is a property the engine promises, and a logic assembly that reached a device, the filesystem, an ambient clock or ambient randomness would break it silently — so the compiler refuses instead. | unconstrained |
| `Capsule.Cli` | Capsule's dev-time command-line utilities. No game references it, which is why every line of Tiled parsing lives here rather than in Capsule.Scenes: a game runtime never links an authoring format. | unconstrained |
| `Capsule.Build` | Build-time tooling; ships in no game. | unconstrained |

`Capsule.Architecture.targets` enforces the reference column for the substrate-free modules, and that they take no package dependency at all.

Public types whose meaning is dimension-specific carry a `2D` suffix in the collision and physics domains; dimension-free vocabulary — layers, filters, handles, targets, cell faces — and namespaces and modules do not, and the render seam keeps its names.

## Logic boundary

The compiler enforces the game-logic boundary with these diagnostics:

| Id | Refuses |
| --- | --- |
| `CAP100` | a logic assembly referencing `Capsule.Runtime` |
| `CAP101` | a Capsule project referencing MonoGame directly |
| `CAP102` | external I/O |
| `CAP103` | ambient concurrency and asynchronous execution |
| `CAP104` | process or wall-clock time |
| `CAP105` | ambient randomness |

## Determinism contract

Given the same initial state, fixed-step duration, and sequence of `DeviceSnapshot` values, a simulation produces the same state transitions and render intents.

- Gameplay reads `StepContext`; it cannot access wall-clock time, external IO, ambient randomness, asynchronous execution, or the graphics backend.
- Input edges are differences between snapshots. The host preserves input edges when render and simulation rates differ.
- Simulation is single-threaded, and scene entities update and draw in insertion order.
- A step runs in one order: previous positions are retained, the scene's step runs, entities and their components step, contacts settle, the scene's late step runs, the scene's camera settles its own framing, the adds and removes requested during the step apply and everything newly attached then starts, and the frame view is rewritten. The camera is a scene-installed object rather than an entity for exactly that reason — settling after the entity pass is what lets it frame the step that just finished rather than the previous one.
- Lifecycle runs on two axes. Structural: `OnAddedToScene` and `OnRemovedFromScene` fire as an object is attached and detached, one at a time, so peers added alongside it may not exist yet — registration belongs here. Temporal: `OnStart` runs once, before the object's first step — its first late step, for a camera, which has no step of its own — and after everything added alongside it has attached, so the scene may be searched from there. A scene opens by starting its entities and their components, then its camera, then running its own `OnStart`; a camera installed after that starts as it is installed. Removal is teardown, so there is no separate stop hook below the scene.
- Collision is deterministic for a given sequence of operations: layers intern in registration order, casts report the nearest hit, and overlaps report tilemap cells before colliders — tilemaps in registration order and row-major within each, colliders by handle. `RaycastAll` fills its span with the nearest hits ordered by distance, ties broken by tiles before colliders and then by collider slot and cell, so the result never depends on the broadphase's current shape. Contacts settle after entities update and before the scene's late step, in the order colliders began reporting them, and a collider's own `Touching` set and contact events depart from that query order in one way — the contacts carried over from the previous step come before the newly entered ones, with query order holding within each group.
- Handles, layers and filters belong to the world that issued them and are rejected by any other, so two worlds' identities are never confused for one another. `CollisionFilter.None` and `CollisionFilter.Everything` name no layer table and are accepted anywhere.
- `TotalSeconds` is derived from the tick count rather than accumulated.
- The runtime clamps frame spikes before scheduling fixed steps.

The Capsule.Generators analyzer enforces the logic boundary. Tests hold the scheduling, input-latching, scene-ordering, and mutation contracts.

## Render seam

Simulation emits backend-free render intents. The host draws them at display rate, interpolating entities and camera from the previous settled step to the current one with a shared fraction. Rendering never feeds state back into simulation.

A frame is an ordered stream of render commands, drawn in submission order; each command names the kind of thing it draws and its place in that kind's typed pool. A sprite — a texel region of a texture, anchored at a pivot the interpolated world position lands on and mirrored about that pivot on either axis when flipped — is the first kind. Drawing needs a texture, so the host loads every handle the build registered into device textures once at boot — alpha premultiplied at decode — and a draw naming a handle it does not hold is a wiring fault, not a fallback.

The camera viewport is fitted uniformly into the output and letterboxed, so display shape does not change the visible world region. A scene sets its own span; there is no game-wide default.

## Package boundary

The same boundary is enforced for project-reference and package consumers; see [`PACKAGE.md`](../PACKAGE.md) for the package table.

## NativeAOT floor

Every shipping assembly stays ahead-of-time analyzable: no reflection-based discovery, no `Reflection.Emit` or `dynamic`, no AOT-unsafe package, and serialization through source generators. Consoles forbid runtime code generation, and the property is viral backward — cheap to hold from the first commit, a game-and-engine-wide hunt to regain at port time. The `platform-and-aot` job in [`ci.yml`](../.github/workflows/ci.yml) publishes the consumers under NativeAOT and runs the published binary on Windows and Linux. The generators and analyzers stand on their own reasons — compile-time duplicate and rename detection, cross-assembly aggregation, a shell that references no game type — and would remain without this floor.
