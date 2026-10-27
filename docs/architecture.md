# Architecture

After this page you know which module a type belongs in, what game logic may not do, and what the
determinism contract promises. Each type's XML docs hold its rules.

Capsule separates pure simulation from the host. Simulation modules and game logic touch no device, file,
clock or platform. The runtime hosts them and draws, plays and samples on their behalf.

## Modules

| Module | Charter | May reference |
| --- | --- | --- |
| `Capsule.Core` | Fixed step, input, rendering, audio mixing, persistence, asset and diagnostic contracts. | nothing |
| `Capsule.Physics` | Shapes, broadphase, queries, sweeps and kinematic movement. No dynamics, no solver. | Core |
| `Capsule.Scenes` | Scenes, entities, components, cameras, audio sources, scene documents and their headless simulation. | Core, Physics |
| `Capsule.Runtime` | The host on SDL3: window, GPU device, clock, input sampling, rendering, the sound mix, scene hosting, and the `HostPlatform` a port overrides. | the pure modules |
| `Capsule.Generators` | Source generation from the game's C#: the entity, scene and input-driver registries and `CapsuleBoot`. Compile-time enforcement of the game-logic boundary. | unconstrained |
| `Capsule.Build` | The asset build a game's build project runs through `CapsuleBuild`: importers, keys, validation, compilation, atlas packing, `CapsuleAssets` and the derivation cache. | unconstrained |
| `Capsule` | No code. The pack root of `JAG.Capsule`, whose project references admit what the package holds. | the pure modules |

The pure modules perform no external I/O, except `SceneRegistry.Create(SceneKey)` and `ComposeAll`, which
read shipped documents for tests. `build/engine/Capsule.Architecture.targets` enforces the reference direction, and that the pure modules take no package dependency.

## Placement

Assemblies follow layers. A type's assembly is decided by what it depends on. A type that knows an
operating system's locations or links a native library belongs in `Capsule.Runtime`'s `Platform/` folder.
A new assembly waits until the compiler must enforce a reference direction.

A game-facing namespace is a feature, whichever assembly holds it. `Capsule` is the cross-cutting core of
the step, the run, transforms, randomness and deterministic math. The features are `Capsule.Scenes`,
`Capsule.Physics`, `Capsule.Rendering`, `Capsule.Input`, `Capsule.Audio`, `Capsule.Animation`,
`Capsule.Particles`, `Capsule.Tiles`, `Capsule.UI`, `Capsule.Assets`, `Capsule.Persistence`,
`Capsule.Localization` and `Capsule.Diagnostics`, and a new feature adds one. A logic project with `ImplicitUsings` on imports every
feature, `Capsule` and `System.Numerics`, and drops one with `<Using Remove="..." />`. In `Capsule.Core`,
`Capsule.Physics` and `Capsule.Scenes` a folder named for a feature carries that namespace, and a folder
that only groups files inside a feature carries its parent's. `Physics/Internal` keeps its own internal
namespace. A folder in `Capsule.Runtime` or `Capsule.Build` carries
`<Assembly>.<Folder>`. `Capsule.Generators` is one namespace, and the code it emits is
`Capsule.Generated`. A partial class file carries its class's namespace wherever it sits. A test file
carries `Capsule.Tests.<top folder>` in folders that mirror the source's. A subject's fixtures stay in its
folder even when another folder borrows them, and a helper with no subject that more than one folder uses
lives in `tests/Capsule.Tests/Support/` under `Capsule.Tests`.

## Logic boundary

The compiler refuses, in a logic assembly: a reference to `Capsule.Runtime` (`CAP100`), external I/O
(`CAP102`), ambient concurrency or asynchronous execution (`CAP103`), process or wall-clock time
(`CAP104`), randomness outside the seeded `RandomSource`, `System.Random` included (`CAP105`), a save
document property declared `init` instead of `set` (`CAP106`), a platform transcendental that
`DeterministicMath` replaces (`CAP107`), reflection (`CAP109`), and a dead method on an entity, component
or scene named like a lifecycle hook Capsule never calls, such as `Update` (`CAP110`).

## Argument validation

A null, non-finite or out-of-range argument throws from the `ArgumentException` family without an
`<exception>` tag, which the XML keeps for a state rule a caller can violate.

## Determinism contract

Given the same initial state, fixed-step duration, and sequences of `DeviceSnapshot` values, output
extents (the window's drawable size in pixels) and `Display.RefreshRate` values, a simulation produces
the same state transitions and render intents.

- Simulation is single-threaded. Work too large for one step is sliced across steps by its owner. Input
  edges are differences between snapshots, and the host preserves edges sampled between fixed steps.
- A step runs in this order: the mixer and the rumble open the step, the scene steps, each entity steps
  and then its components, contacts settle, every entity's late step runs in the same order, the scene's late step runs
  and the camera settles the visible region the frame will use, deferred structural changes are applied and
  newly attached objects started, the visible-screen notifiers settle once against that region, the mixer
  hears every positional voice from that region's centre, and the frame is rewritten. An entity held by a
  pause or a freeze is skipped by every pass but the drain and the frame.
- Entities update in tree order: each root in insertion order, then its subtree depth-first with children in
  parenting order. Rendering is ordered by `ZIndex` summed up the ancestry, stable over the same order.
  Collision queries and contact delivery order as their public methods document.
- A handler sees the new state. Reconfiguring the object whose handler is running throws, across colliders,
  focus navigators and screen notifiers.
- A duration typed `int` counts fixed steps, 60 a second unless the shell calls
  `EngineBuilder.WithFixedStep`, and a duration typed `float` counts seconds.
- `StepContext.TotalSeconds` is derived from its tick. Randomness comes from `Run`'s seeded `RandomSource`,
  which persists across scene transitions. A windowed run no driver plays draws a fresh seed unless the shell
  configures one, and it logs the seed. A headless or driven run with no configured seed starts from
  `RandomSource.DefaultSeed`.
- Simulation arithmetic is IEEE-exact. Transcendental functions differ between operating systems, and
  simulation code calls `DeterministicMath` in place of `MathF`.
- A frame runs at most the configured number of fixed steps, and starts no further step once its steps
  have taken one step length of real time. A frame cap, or vertical sync at the display's reported
  refresh rate, widens both to the steps and time a frame's period covers, and neither slows the
  simulation. Reaching either limit drops the remaining accumulated wall-clock time and alters no step
  that runs.
- `Run.TimeScale` is host pace. It sets how many simulation seconds a wall second is worth. It alters no
  step that runs. Simulation code may set it and must not read it.

## Simulation and host

Simulation emits host-free `FrameView` state and rewrites a step's audio commands the same way. The
host draws at display rate, interpolating entities and the camera with one shared fraction, and applies
audio commands after every step. Neither rendering nor audio feeds state back into simulation.

A step and a frame are separate clocks. Steps run at the fixed rate and hold all game logic. A frame
samples input, runs the steps its elapsed wall time owes, from none up to the bound, draws once and
waits for the display and the frame cap. A frame runs no game code but the renderers' `Draw`. How often
frames run never reaches the simulation, and a game plays the same at any frame rate. Motion is smooth
at any rate through the host's interpolation.

The frame is built on the first read of `SceneSimulation.View` after a step. A window builds at most one
per presented frame, and a headless run that reads none builds none. Each build calls every visible
renderer's `Draw`, which only writes the view it is handed. A `Draw` that changes the scene's structure
or draw order throws. Reading `View` during a step or from inside a `Draw` throws too.

Every thread the engine runs is the host's, and each has one shape: a step emits an intent, the host queues
it, a worker fulfils it, and only the hand-off touches a device or the file system. A headless run runs no
worker.

State that outlives a scene belongs to `Run`: saves (`Run.Saves`), sound (`Run.Audio`), and the one
object a game attaches at run start (`Run.Attach`) and reads anywhere as `Run.State<T>()`.

## Platforms

One shell publishes Windows, Linux and macOS by runtime identifier. `HostPlatform` tells the host where
shipped content is read from, where saves and the crash log land, how the window is raised, where the
finished sound mix plays, and which languages the player prefers. Its defaults are SDL3's, and a desktop
shell names no platform.

A console is a port: a private repository whose shell passes its own `HostPlatform` subclass to
`EngineBuilder.WithPlatform` ([`packages.md`](packages.md#porting-to-another-platform)).

SDL3 is the runtime's one native dependency. Its binding, and what the operating system tells the
defaults that SDL3 does not, live in the `Platform/` folder alone.

## NativeAOT floor

Shipping assemblies remain ahead-of-time analyzable: no reflection-based discovery, runtime code generation,
`dynamic`, AOT-unsafe package or reflection-based serialization.
