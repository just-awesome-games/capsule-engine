# Architecture

Capsule keeps gameplay deterministic and headless-testable by separating pure simulation from the host.

## Modules

| Module | Charter | May reference |
| --- | --- | --- |
| `Capsule.Core` | Simulation seam, fixed step, input, render intent, asset handles. | nothing |
| `Capsule.Collision` | Collision only: the shape union, tag filtering, the tilemap grid, the dynamic AABB tree, and the query seam — raycast, shape cast, overlap, mover. | Core |
| `Capsule.Scenes` | The world: scenes, entities, components, camera, spawning, transitions, tile grids, and the [scene document](scenes.md) — format, loader, composition. It adapts collision onto entities in `Capsule.Scenes.Physics`. | Core, Collision |
| `Capsule.Runtime` | The host: window, device, clock, sampling, renderer, crash log. | the pure modules |
| `Capsule.Build`, `Capsule.Analyzers`, `Capsule.Generators`, `Capsule.Cli` | Build-time tooling; ships in no game. | unconstrained |

`Capsule.Architecture.targets` enforces the reference column for the substrate-free modules, and that they take no package dependency at all.

Public types whose meaning is dimension-specific carry a `2D` suffix in the collision and physics domains; dimension-free vocabulary — tags, filters, handles, targets, cell kinds — and namespaces and modules do not, and the render seam keeps its names.

## Determinism contract

Given the same initial state, fixed-step duration, and sequence of `DeviceSnapshot` values, a simulation produces the same state transitions and render intents.

- Gameplay reads `StepContext`; it cannot access wall-clock time, external IO, ambient randomness, asynchronous execution, or the graphics backend.
- Input edges are differences between snapshots. `SnapshotLatch` preserves them when render and simulation rates differ.
- Simulation is single-threaded. Scene entities update and draw in insertion order, and mutations requested during a step apply after iteration.
- Collision is deterministic for a given sequence of operations: tags intern in registration order, casts report the nearest hit, and overlaps report tilemap cells before colliders — tilemaps in registration order and row-major within each, colliders by handle. `RaycastAll` fills its span with the nearest hits ordered by distance, ties broken by tiles before colliders and then by collider slot and cell, so the result never depends on the broadphase's current shape. Contacts settle after entities update and before the scene's late step, in the order colliders began reporting them.
- Handles, tags and filters belong to the world that issued them and are rejected by any other, so two worlds' identities are never confused for one another. `CollisionFilter.None` and `CollisionFilter.Everything` name no tag table and are accepted anywhere.
- `TotalSeconds` is derived from the tick count rather than accumulated.
- The runtime clamps frame spikes before scheduling fixed steps.

The analyzer enforces the logic boundary. Tests hold the scheduling, input-latching, scene-ordering, and mutation contracts.

## Render seam

Simulation emits backend-free render intents. The host draws them at display rate, interpolating entities and camera from the previous settled step to the current one with a shared fraction. Rendering never feeds state back into simulation.

The camera viewport is fitted uniformly into the output and letterboxed, so display shape does not change the visible world region.

## Package boundary

`JAG.Capsule` contains every substrate-free module. `JAG.Capsule.Runtime` contains the host and is the only package linked to MonoGame. `JAG.Capsule.Build` contains build-time tooling. The same boundary is enforced for project-reference and package consumers.

## NativeAOT floor

Every shipping assembly stays ahead-of-time analyzable: no reflection-based discovery, no `Reflection.Emit` or `dynamic`, no AOT-unsafe package, and serialization through source generators. Consoles forbid runtime code generation, and the property is viral backward — cheap to hold from the first commit, a game-and-engine-wide hunt to regain at port time. The `platform-and-aot` job in [`ci.yml`](../.github/workflows/ci.yml) publishes the consumers under NativeAOT and runs the published binary on Windows and Linux. The generators and analyzers stand on their own reasons — compile-time duplicate and rename detection, cross-assembly aggregation, a shell that references no game type — and would remain without this floor.
