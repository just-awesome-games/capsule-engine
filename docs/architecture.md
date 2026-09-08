# Architecture

Capsule keeps gameplay deterministic and headless-testable by separating pure simulation from the host.

## Modules

| Module | Charter | May reference |
| --- | --- | --- |
| `Capsule.Core` | Fixed-step, input, rendering, asset and diagnostic contracts. | nothing |
| `Capsule.Collision` | Shapes, broadphase, queries, sweeps and kinematic movement; no dynamics or solver. | Core |
| `Capsule.Scenes` | Scenes, entities, components, cameras, scene documents and their headless simulation. | Core, Collision |
| `Capsule.Runtime` | Window, device, clock, input sampling, rendering, scene hosting and crash reporting. | the pure modules |
| `Capsule.Generators` | Source generation and compile-time enforcement of the game-logic boundary. | unconstrained |
| `Capsule.Build` | Build-time validation and canonicalization of scene and sprite documents. | unconstrained |

`Capsule.Core`, `Capsule.Collision` and `Capsule.Scenes` are substrate-free. `Capsule.Architecture.targets` enforces their reference direction and absence of package dependencies. MonoGame belongs only to `Capsule.Runtime` and never appears in a game's logic API. The same boundary applies to project-reference and package consumers.

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
- A step retains previous positions, runs the scene, entities and components, settles contacts, runs the scene's late step, settles the camera, applies deferred structural changes, starts newly attached objects and rewrites the frame.
- Entities update in insertion order. Rendering is ordered by `ZIndex`, stable over insertion order. Collision queries and contact delivery have deterministic ordering documented on their public methods.
- `StepContext.TotalSeconds` is derived from its tick. Randomness comes from the run's seeded `RandomSource`, which persists across scene transitions.
- A frame runs at most the configured number of fixed steps. Reaching the limit drops the remaining accumulated wall-clock time; it does not alter the order or contents of steps that run.

Lifecycle details and callback contracts are documented on `Scene`, `Entity`, `Component`, `Camera`, `Collider2D` and `SceneSimulation` in the shipped XML API reference.

## Rendering and media

Simulation emits backend-free `FrameView` state. The host draws its ordered sprites at display rate, interpolating entities and camera with one shared fraction; rendering never feeds state back into simulation.

At a scene boundary the runtime synchronously preloads media collected from the composed scene, its entities and their components. A resource not collected there loads synchronously on first rendered use and is cached for the rest of that scene. The outgoing scene's resources are released at transition or exit except where the incoming preload also uses them. Headless simulation loads no media.

Camera fit, culling, texture sampling and pixel-grid behavior are documented on `CameraView`, `ViewportFit`, `FrameView` and `TextureSampling`.

## NativeAOT floor

Shipping assemblies remain ahead-of-time analyzable: no reflection-based discovery, runtime code generation, `dynamic`, AOT-unsafe package or reflection-based serialization. The `platform-and-aot` CI job publishes a package-consuming game and separately publishes and runs the independent, source-backed headless smoke on Windows and Linux.
