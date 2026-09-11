# Architecture

Capsule keeps gameplay deterministic and headless-testable by separating pure simulation from the host.

## Modules

| Module | Charter | May reference |
| --- | --- | --- |
| `Capsule.Core` | Fixed-step, input, rendering, audio mixing, asset and diagnostic contracts. | nothing |
| `Capsule.Collision` | Shapes, broadphase, queries, sweeps and kinematic movement; no dynamics or solver. | Core |
| `Capsule.Scenes` | Scenes, entities, components, cameras, audio sources, scene documents and their headless simulation. | Core, Collision |
| `Capsule.Runtime` | Window, device, clock, input sampling, rendering, sound playback, scene hosting and crash reporting. | the pure modules |
| `Capsule.Generators` | Source generation and compile-time enforcement of the game-logic boundary. | unconstrained |
| `Capsule.Build` | Build-time validation and canonicalization of scene documents, the asset key pass, and measurement of audio sources. | unconstrained |

`Capsule.Core`, `Capsule.Collision` and `Capsule.Scenes` are substrate-free. `Capsule.Architecture.targets` enforces their reference direction and absence of package dependencies. MonoGame belongs only to `Capsule.Runtime` and never appears in a game's logic API. The same boundary applies to project-reference and package consumers.

`Capsule.Runtime` is itself split: platform-neutral hosting carries no operating-system, file-system, window or MonoGame-platform assumption of its own, and those assumptions live in the desktop files. The boot surface a game's shell is generated against — `CapsuleBoot`, `CapsuleEngine` and `EngineBuilder` — exposes no desktop concept beyond an application title and texture sampling.

## Logic boundary

The compiler enforces the game-logic boundary:

| Id | Refuses |
| --- | --- |
| `CAP100` | a logic assembly referencing `Capsule.Runtime` |
| `CAP101` | a Capsule project referencing MonoGame directly |
| `CAP102` | external I/O |
| `CAP103` | ambient concurrency and asynchronous execution |
| `CAP104` | process or wall-clock time |
| `CAP105` | randomness outside the seeded `RandomSource`, including `System.Random` |

## Determinism contract

Given the same initial state, fixed-step duration and sequence of `DeviceSnapshot` values, a simulation produces the same state transitions and render intents.

- Simulation is single-threaded. Input edges are differences between snapshots, and the host preserves edges sampled between fixed steps.
- A step retains previous positions, runs the scene, entities and components, settles contacts, runs each entity's late step and its components' in that same order, runs the scene's late step, settles the camera and the visible region its notifiers answer against, applies deferred structural changes, starts newly attached objects, settles the notifiers those changes brought in against that same region, and rewrites the frame.
- Entities update in insertion order. Rendering is ordered by `ZIndex`, stable over insertion order. Collision queries and contact delivery have deterministic ordering documented on their public methods.
- `StepContext.TotalSeconds` is derived from its tick. Randomness comes from the run's seeded `RandomSource`, which persists across scene transitions.
- A frame runs at most the configured number of fixed steps. Reaching the limit drops the remaining accumulated wall-clock time; it does not alter the order or contents of steps that run.

## Rendering and media

Simulation emits backend-free `FrameView` state. The host draws its ordered sprites at display rate, interpolating entities and camera with one shared fraction; rendering never feeds state back into simulation.

A frame carries two ordered lists: the world, placed by the camera and culled against it, and a screen layer in canvas pixels, culled against the canvas. The canvas is a run constant the simulation knows — the declared render resolution, or the configured window size where there is none — so anchoring and hit-testing never read the window the player drags. Which layer an entity lives in is its type — a `ScreenEntity` is on the screen layer, anything else in the world — and every renderer it holds follows, so banding orders each layer on its own and the whole screen layer draws over the whole world.

At a scene boundary the runtime synchronously preloads media collected from the composed scene, its entities and their components. A resource not collected there loads synchronously on first rendered or audible use and is cached for the rest of that scene; the outgoing scene's resources are released at transition or exit except where the incoming preload also uses them. Headless simulation loads no media.

Sound is mixed purely and played by the host: simulation rewrites a step's `AudioCommand` list the way it rewrites `FrameView`, and the host applies those commands after every step rather than once a frame, since a frame may run several steps. `.wav` clips are resident for the scene that uses them and `.ogg` clips are never resident, decoding on one background worker; a looping voice whose clip carries a loop region always streams on that worker whatever the clip's format. The run owns one mixer, so a voice survives a scene transition, and a resident clip it is playing is retained past the transition that released it. Audio never feeds state back into simulation.

Lifecycle, callback and playback contracts are documented on the public types in the shipped XML API reference.

## NativeAOT floor

Shipping assemblies remain ahead-of-time analyzable: no reflection-based discovery, runtime code generation, `dynamic`, AOT-unsafe package or reflection-based serialization. The `platform-and-aot` CI job publishes a package-consuming game and separately publishes and runs the independent, source-backed headless smoke on Windows and Linux.
