# Architecture

Capsule keeps gameplay deterministic and headless-testable by separating pure simulation from the host: game logic and the engine's simulation modules touch no device, file, clock or platform; the runtime hosts them and draws, plays and samples on their behalf.

## Modules

| Module               | Charter                                                                                                                             | May reference    |
| -------------------- | ----------------------------------------------------------------------------------------------------------------------------------- | ---------------- |
| `Capsule.Core`       | Fixed-step, input, rendering, audio mixing, persistence, asset and diagnostic contracts.                                            | nothing          |
| `Capsule.Physics`    | Shapes, broadphase, queries, sweeps and kinematic movement; no dynamics or solver.                                                  | Core             |
| `Capsule.Scenes`     | Scenes, entities, components, cameras, audio sources, scene documents and their headless simulation.                                | Core, Physics    |
| `Capsule.Runtime`    | Window, device, clock, input sampling, rendering, sound playback, scene hosting, crash reporting and save storage.                  | the pure modules |
| `Capsule.Generators` | Source generation and compile-time enforcement of the game-logic boundary.                                                          | unconstrained    |
| `Capsule.Build`      | Build-time validation and canonicalization of scene documents, the asset key pass, atlas packing, and measurement of audio sources. | unconstrained    |

The pure modules perform no external I/O; `SceneDocumentFile.Load` and `Save` are explicit filesystem adapters for tools and hosts beside the pure `Parse` and `ToJson`. `Capsule.Architecture.targets` enforces the reference direction and the absence of package dependencies. MonoGame belongs to `Capsule.Runtime` alone, for project-reference and package consumers alike.

Inside `Capsule.Runtime`, platform-neutral hosting carries no operating-system, file-system, window or MonoGame-platform assumption; those live in the desktop files [`AGENTS.md`](../AGENTS.md#boundaries) names.

## Placement

Assemblies follow layers, so the compiler enforces reference direction; namespaces and folders follow domains, so a subsystem is reached with one `using`. A type's assembly is decided by what it depends on — a contract or data plane with no scene dependency in `Capsule.Core`, the collision server in `Capsule.Physics`, anything referencing `Scene`, `Entity` or `Component` in `Capsule.Scenes`, anything touching a device, window, file or MonoGame in `Capsule.Runtime` — and its namespace is its domain's whichever assembly it lives in: `Capsule` (step, run, randomness, timing, deterministic math), `Capsule.Scenes`, `Capsule.Physics`, `Capsule.Rendering`, `Capsule.Audio`, `Capsule.Animation`, `Capsule.Input`, `Capsule.UI`, `Capsule.Tiles`, `Capsule.Assets`, `Capsule.Persistence`, `Capsule.Diagnostics`, and the runtime's own `Capsule.Runtime.*` mirrors, with the development overlay under `Capsule.Runtime.DevTools`. A new domain adds a namespace; a new assembly is created only when the compiler must enforce a reference direction.

## Logic boundary

The compiler refuses, in a logic assembly: a reference to `Capsule.Runtime` (`CAP100`), a direct MonoGame reference in any Capsule project (`CAP101`), external I/O (`CAP102`), ambient concurrency or asynchronous execution (`CAP103`), process or wall-clock time (`CAP104`), and randomness outside the seeded `RandomSource`, `System.Random` included (`CAP105`).

## Determinism contract

Given the same initial state, fixed-step duration and sequence of `DeviceSnapshot` values, a simulation produces the same state transitions and render intents.

- Simulation is single-threaded, and work too large for one step is sliced across steps by its owner, never threaded. Input edges are differences between snapshots, and the host preserves edges sampled between fixed steps.
- A step retains previous transforms, runs the scene, entities and components, settles contacts, runs each entity's late step and its components' in that same order, runs the scene's late step, settles the camera and the visible region its notifiers answer against, applies deferred structural changes, starts newly attached objects, settles the notifiers those changes brought in against that same region, and rewrites the frame.
- Entities update in tree order: each root in insertion order, followed by its subtree depth-first with children in the order they were parented, so a child steps after its parent and reads where the parent moved to this step. Rendering is ordered by `ZIndex` summed up the ancestry, stable over that same order. Collision queries and contact delivery order as their public methods document.
- `StepContext.TotalSeconds` is derived from its tick. Randomness comes from `Run`'s seeded `RandomSource`, which persists across scene transitions.
- Simulation arithmetic is IEEE-exact throughout. A platform's sine, cosine and exponential are correctly rounded by no standard and differ between operating systems, so simulation code evaluates them through `DeterministicMath` instead of calling `MathF`.
- A frame runs at most the configured number of fixed steps; reaching the limit drops the remaining accumulated wall-clock time and alters no step that runs.
- `Run.TimeScale` is host pace: it moves how much wall time a frame is worth in simulation seconds, and alters no step that runs, so a simulation never reads it.

## Rendering and media

Simulation emits backend-free `FrameView` state and rewrites a step's `AudioCommand` list the same way; the host draws at display rate, interpolating entities and camera with one shared fraction, and applies audio commands after every step. Neither rendering nor audio feeds state back into simulation.

Every thread the engine runs is the host's and has one shape: a step emits an intent — an audio command, a frame capture, a scene request — the host queues it, a worker fulfils it, and only the hand-off touches the device or the file system. The simulation never observes a thread or a completion, and a headless run runs no worker.

A frame carries two ordered lists: the world, placed by the camera and culled against it, and a screen layer in canvas pixels, culled against `Run.Canvas`. Which layer an entity lives in is its type — a `ScreenEntity` is on the screen layer, anything else in the world — and every renderer it holds follows, so each layer bands on its own and the whole screen layer draws over the whole world.

An entity may be placed by a parent. Its position, rotation and scale are then local, and its world transform is the parent's applied to them — position through the parent's scale, rotation and offset, rotation summed, scale multiplied per axis, never sheared — cached on the entity and recomposed lazily after a write anywhere above. Only presentation follows a turn or a scale: every renderer under the entity is placed, turned and sized by the composed transform, interpolated from the transform the step began with, while anything that collides follows world position alone and refuses a turned or scaled ancestry. A subtree shares its root's scene, draw layer and scroll factor, and enters and leaves the scene with it.

The world list stays in authored positions and may carry per-entity scroll factors (`Entity.ScrollFactor`) in runs beside it; the host draws each run by a virtual camera — the frame's top-left corner moved by the factor about the camera's `ScrollOrigin` — and snaps it from that camera's corner as it snaps the world from the frame's. Inside such an entity's `Draw`, `FrameView.Camera` is that virtual camera, so a renderer culls against it with no knowledge of the factor, and its intent is culled against a region that covers every rect the frame can draw the run at. Draw order is the one `ZIndex` sum whatever the factor.

On a declared render surface the world is drawn at exactly the declared pixels per unit under either sampling; `ViewportFit.Expand` and `FixedHeight` quantise the axis they grow to whole surface pixels, never past the span the fit resolved; and point sampling alone snaps each sprite to the surface's pixel grid from the camera's corner and presents the surface at a whole scale whenever the output can hold it at least once (a smaller output falls back to a fractional fit).

A sound is an `AudioSource` component playing an `AudioClip` on a named `AudioBus`; `Run.Audio` is the one mixer every source mixes into, so a voice survives a scene transition and a bus is levelled, paused or resumed once at boot rather than per scene.

The screen layer is `ScreenEntity` placed by an `Anchor` — a fraction of the canvas on each axis — so an interface element keeps its distance from the edge it was anchored to whatever the canvas is. A menu is `Focusable` components under one `FocusNavigator`, which owns which item has focus and moves it from the game's own focus actions, pointer included.

Saved state is `Run.Saves`, a store of named JSON documents the host restores before the first scene and persists after every step ([`persistence.md`](persistence.md)).

At a scene boundary the runtime synchronously preloads the media the composed scene, its entities and their components collect; a resource not collected there loads on first rendered or audible use and is cached for the rest of that scene, and the outgoing scene's resources are released at transition or exit except where the incoming preload also uses them. A texture packed onto an atlas page is resident as that page ([`atlases.md`](atlases.md)). `Run` owns one mixer, so a voice survives a scene transition. Headless simulation loads no media.

## NativeAOT floor

Shipping assemblies remain ahead-of-time analyzable: no reflection-based discovery, runtime code generation, `dynamic`, AOT-unsafe package or reflection-based serialization. CI publishes a package-consuming game and the source-backed headless smoke with NativeAOT on Windows and Linux and runs the result.
