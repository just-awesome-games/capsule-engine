# Architecture

After this page you know which module a type belongs in, what game logic may not do, and what the
determinism contract promises.

Capsule separates pure simulation from the host. Simulation modules and game logic touch no device, file,
clock or platform. The runtime hosts them and draws, plays and samples on their behalf.

## Modules

| Module | Charter | May reference |
| --- | --- | --- |
| `Capsule.Core` | Fixed step, input, rendering, audio mixing, persistence, asset and diagnostic contracts. | nothing |
| `Capsule.Physics` | Shapes, broadphase, queries, sweeps and kinematic movement. No dynamics, no solver. | Core |
| `Capsule.Scenes` | Scenes, entities, components, cameras, audio sources, scene documents and their headless simulation. | Core, Physics |
| `Capsule.Runtime` | The platform-neutral host: window, device, clock, input sampling, rendering, sound playback, scene hosting, and the `HostPlatform` contract. | the pure modules |
| `Capsule.Runtime.Desktop` | The desktop platform module: content beside the executable, the per-user local folder, window raising and focus, the default audio output. | Runtime |
| `Capsule.Generators` | Source generation from the game's C#: the entity, scene and input-driver registries and `CapsuleBoot`. Compile-time enforcement of the game-logic boundary. | unconstrained |
| `Capsule.Build` | The build tool, which reads every authored file: the key pass, scene document validation and canonicalization, sprite sheet and font compilation, atlas packing, audio measurement, shader compilation, and `CapsuleAssets`. | unconstrained |
| `Capsule` | No code. The pack root whose project-reference list is the `JAG.Capsule` package's admission list. | the pure modules |

The pure modules perform no external I/O. `SceneDocumentFile.Load` and `Save` are filesystem adapters for
tools and hosts, beside the pure `Parse` and `ToJson`. `Capsule.Architecture.targets` enforces the reference
direction and the absence of package dependencies. MonoGame belongs to `Capsule.Runtime`, for
project-reference and package consumers alike.

## Placement

Assemblies follow layers, and the compiler enforces their reference direction. Namespaces and folders
follow domains, and one `using` reaches a subsystem. A type's assembly follows the charters above. What it
depends on decides it, and a type that knows an operating system's locations or links a native library
belongs in a platform module.

Its namespace is its domain whichever assembly it lives in: `Capsule` for the step, the run, randomness and
deterministic math, then a namespace per subsystem, the runtime's `Capsule.Runtime.*` mirrors, and the
development overlay under `Capsule.Runtime.DevTools`. A new domain adds a namespace. A new assembly waits
until the compiler must enforce a reference direction.

## Logic boundary

The compiler refuses, in a logic assembly: a reference to `Capsule.Runtime` (`CAP100`), a direct MonoGame
reference in any Capsule project (`CAP101`), external I/O (`CAP102`), ambient concurrency or asynchronous
execution (`CAP103`), process or wall-clock time (`CAP104`), randomness outside the seeded
`RandomSource`, `System.Random` included (`CAP105`), a save document property declared `init` instead
of `set` (`CAP106`), and a platform transcendental that `DeterministicMath` replaces (`CAP107`).

## Argument validation

Argument validation follows .NET conventions. A null, non-finite or out-of-range argument throws from the
`ArgumentException` family, and no member documents that per parameter. An `<exception>` tag marks a state
rule a caller can violate, such as reaching a run before the scene has started or reconfiguring an object
from inside its own handler.

## Determinism contract

Given the same initial state, fixed-step duration and sequence of `DeviceSnapshot` values and output
extents, a simulation produces the same state transitions and render intents.

- Simulation is single-threaded. Work too large for one step is sliced across steps by its owner. Input
  edges are differences between snapshots, and the host preserves edges sampled between fixed steps.
- A step runs in this order: the mixer opens the step, the scene steps, each entity steps and then its
  components, contacts settle, every entity's late step runs in the same order, the scene's late step runs
  and the camera settles the visible region the frame will use, deferred structural changes are applied and
  newly attached objects started, the visible-screen notifiers settle once against that region, and the
  frame is rewritten. An entity held by a pause or a freeze is skipped by every pass but the drain and
  the frame.
- Entities update in tree order: each root in insertion order, then its subtree depth-first with children in
  parenting order. Rendering is ordered by `ZIndex` summed up the ancestry, stable over the same order.
  Collision queries and contact delivery order as their public methods document.
- A handler sees the new state. Reconfiguring the object whose handler is running throws, across colliders,
  focus navigators and screen notifiers.
- `StepContext.TotalSeconds` is derived from its tick. Randomness comes from `Run`'s seeded `RandomSource`,
  which persists across scene transitions.
- Simulation arithmetic is IEEE-exact. Transcendental functions differ between operating systems, and
  simulation code calls `DeterministicMath` in place of `MathF`.
- A frame runs at most the configured number of fixed steps. Reaching the limit drops the remaining
  accumulated wall-clock time and alters no step that runs.
- `Run.TimeScale` is host pace. It sets how many simulation seconds a wall second is worth. It alters no
  step that runs, and simulation code must not read it.

## Simulation and host

Simulation emits backend-free `FrameView` state and rewrites a step's audio commands the same way. The
host draws at display rate, interpolating entities and the camera with one shared fraction, and applies
audio commands after every step. Neither rendering nor audio feeds state back into simulation.

Every thread the engine runs is the host's, and each has one shape: a step emits an intent, the host queues
it, a worker fulfils it, and only the hand-off touches a device or the file system. A headless run runs no
worker.

How frames, layers, cameras and parallax are drawn is [`rendering.md`](rendering.md). Saved state is
`Run.Saves` ([`persistence.md`](persistence.md)), sound is `Run.Audio` ([`audio.md`](audio.md)), and what a
scene preloads and when it is released is [`assets.md`](assets.md#loading-and-residency). What the game
itself keeps for a run's length is one object it attaches at run start (`Run.Attach`) and reads anywhere
as `Run.State<T>()`.

## Platforms

A host family is one shell. The desktop shell publishes Windows, Linux and macOS from one project by runtime
identifier, and a console is another family with a shell of its own. A platform module is one subclass of
`HostPlatform`, handed to `CapsuleBoot.Configure` beside the game's name. It tells the host where shipped
content is read from, where saves and the crash log land, how the window is raised, focused and redrawn, and
how sound follows the default output.

`Capsule.Runtime` holds no implicit location and no native binding. Its banned-API list refuses one at
compile time (`src/Capsule.Runtime/BannedSymbols.txt`).

`Capsule.Runtime.Desktop` is the platform module the engine ships. It uses only the neutral host's public
surface, which proves a private module can be written against the same contract. Writing one is
[`build-and-publish.md`](build-and-publish.md#a-private-platform-module).

## NativeAOT floor

Shipping assemblies remain ahead-of-time analyzable: no reflection-based discovery, runtime code generation,
`dynamic`, AOT-unsafe package or reflection-based serialization. CI publishes the package-consuming sample
and the source-backed headless smoke with NativeAOT on Windows and Linux, and it runs the smoke. A console
platform module builds on the same floor.
