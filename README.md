<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.</p>

---

Capsule is JAG Studios' open-source engine: 2D, deterministic, code-first. It owns the
frame — loop, clock, window, input, the sim/render seam, the determinism contract — and the world
inside it: a scene, the entities on it, and the order they update and draw in. A game brings its
own `Program.cs`, its scenes, and its entities. **No editor, no serialized scene format, no
project wizard: scenes are C#, maps are data.**

- **Everything is C# and text on disk** — the whole surface reachable by a person and an agent alike.
- **Gameplay is pure by construction.** A scene advances one fixed step at a time, reads input as
  named actions, never touches a graphics device, and so is assertable headlessly.
- **MonoGame is an implementation detail.** `Capsule.Runtime` marks its compile assets private, so a
  `Microsoft.Xna.Framework` using in a consuming game does not compile, while MonoGame's managed and
  native libraries still reach that game's output. Swapping the backend is engine-side only.

## Quickstart

Install the .NET SDK selected by [`global.json`](global.json). Capsule needs no editor, engine SDK,
or MonoGame installation: a game restores the exact `JAG.Capsule.*` packages it pins from
NuGet.org. The complete two-project bootstrap is in
[`docs/consuming-capsule.md`](docs/consuming-capsule.md); once wired, the entire host is ordinary
C#:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

GameBoot.Configure()
    .WithWindow("My Game", 1280, 720, resizable: true)
    .WithFixedStep(60)
    .WithCrashLog("MyGame")
    .WithBindings(MyGameInput.Bind)
    .RunScene("room-01");
```

`GameBoot` is generated into the shell already carrying the registry the compiler built from the
game's own classes, so a boot registers nothing and every line in the chain is a knob with a
default.
`RunScene` takes a map name and boots the class claiming that map — or a plain `MapScene` when no
class claims it; `RunScene<TScene>()` boots a scene by class instead. A scene composed from a map
derives from `MapScene`, which has already added the terrain and spawned the map's objects,
leaving the subclass `OnStart` to open the camera and `OnStep` for scene-wide input. It runs until
game code exits or replaces it.

Below the scene sits the seam it is built on. A game that wants its own container hands the host
an `ISimulation` instead — `Run(simulation)` — which advances one fixed step at a time, reads
input as named actions, sets `ExitRequested` when it wants to stop, and exposes what to draw as a
`FrameView`. Neither ever touches a graphics device.

**Capsule never stretches what it draws.** The camera's world region is fitted into the window
scaled uniformly and centred, with black bars over whatever slack the window's shape leaves, so a
resize changes how large a frame is drawn and never what it contains. By default the world
rasterises straight into the window at its live size, imposing no resolution ceiling.
`WithRenderResolution(width, height)` opts into the other lane: the world rasterises into a
fixed-size surface, which is then fitted into the window the same way — a canvas declared once,
after which the window stops mattering. Those pixels are independent of the camera's world units;
they coincide only where a game wants one unit to be one pixel. `WithFullscreen()` boots
borderless at the desktop's resolution, Alt+Enter toggles either way, and `WithWindow`'s size is
the windowed one, returned to on the way back.

Games consume exact SemVer releases from NuGet.org. Public package IDs use `JAG.Capsule.*` while
assemblies and namespaces remain `Capsule.*`. `JAG.Capsule.Build` carries every build hook,
generator, analyzer and authoring tool transitively, while the runtime modules stay
separate so game logic never references `Capsule.Runtime`. For engine work, one
`CapsuleSourcePath` build property swaps all package references for project references to a local
clone without editing a project file. Capsule's hooks import every `.tmj` under
`asset-sources/maps/` and ship the result as content: create one, build, and it is in the game.
Nothing generated is committed.

### Develop a game and Capsule together

Clone Capsule beside the game, ignore `Directory.Build.local.props` in the game repository, and
create that file locally:

```xml
<!-- Directory.Build.local.props -->
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

Ordinary restore, build, test, run, IDE navigation, and debugging now use project references into
the live engine source. The committed package version and lock files remain unchanged. Delete the
file to return to packages, or set `CapsuleUsePackages=true` for one command when the consumer
supports the standard override contract.

**Setting a game up is one door: [`docs/consuming-capsule.md`](docs/consuming-capsule.md).** It
carries the bootstrap sequence end to end — package source, project skeleton, the local-source
switch, role declarations, locked restores — and the purity rule game logic is held to.

## Architecture

Dependencies point one way and each direction is held mechanically, not by review: Core references
nothing, Maps references Core, Scenes references Core and Maps, Runtime references all three, and
a game's logic references `Capsule.Core`, `Capsule.Maps` and `Capsule.Scenes` while only its
one-file shell references `Capsule.Runtime`. That split is the compiler-enforced guarantee that
gameplay stays pure and headless-testable, and it is why no game ever links a line of
Tiled-parsing code.

- **`Capsule.Core`** — the pure contracts a game codes against: `ISimulation`, the fixed step,
  input as named actions, render intent. No package references at all, so it cannot reach a device
  even by accident.
- **`Capsule.Maps`** — the map format and its loader: a tile grid, what its tiles draw as, and the
  typed objects placed over it. It takes no package references and reaches only `Capsule.Core`,
  for the colour a palette entry carries, so no authoring format is ever a runtime dependency
  ([`Capsule.Maps/README.md`](Capsule.Maps/README.md)).
- **`Capsule.Maps.Cli`** — the dev-time tool a build hook runs to import Tiled maps.
- **`Capsule.Scenes`** — the world: `Scene`, `MapScene`, `Entity`, `Component`, `Renderer`,
  `Camera`, and `SceneSimulation`, which owns the step choreography behind Core's seam.
- **`Capsule.Scenes.Generator`** — the compile-time generator that turns a game's entity and scene
  classes into the two registries it boots through, and emits the shell's entry point already
  holding them. Generated code, never reflection.
- **`Capsule.Analyzers`** — compile-time enforcement for logic purity, deterministic services,
  role legality, and the hidden backend boundary.
- **`Capsule.Build`** — the tooling-only package that carries build hooks, generators, analyzers,
  and the map importer to a game; none of it ships in the executable.
- **`Capsule.Runtime`** — the host: window, graphics device, clock, keyboard and gamepad, renderer,
  crash log. The only project that references MonoGame.
- **`Capsule.Verify`** — the device-free scripted verification runner and allocation probe a game
  drives with its own simulation and artifact sink. It references Core only.
- **`Capsule.Tests`** — xUnit specs over Core, Maps and Scenes; hosts the generator over
  compilations it builds; adds builder validation and a reflection guard over Runtime's public
  surface.

## Building

```
dotnet build
dotnet test
dotnet format --verify-no-changes
dotnet pack --configuration Release --output artifacts/packages
```

With coverage, gated at a floor of 80% line coverage over `Capsule.Core`, `Capsule.Maps`,
`Capsule.Scenes` and `Capsule.Verify`:

```
dotnet test -p:CollectCoverage=true "-p:Include=[Capsule.Core]*%2c[Capsule.Maps]*%2c[Capsule.Scenes]*%2c[Capsule.Verify]*" -p:CoverletOutputFormat=cobertura -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total
```

To restore exactly the committed dependency set — as CI does:

```
dotnet restore --locked-mode
```

The pre-commit hook mirrors the CI gates. Activate it once per clone:

```
git config core.hooksPath hooks
```

`Capsule.Verify` replays per-tick input, gates steady-state allocations and records timing/render
metrics; games supply state and image artifact writers. A deterministic render-intent image is not
a real-device framebuffer test, which remains a game/runtime integration concern.

## Further reading

[`AGENTS.md`](AGENTS.md) is the rules any contributor — human or agent — works under here.
[`docs/consuming-capsule.md`](docs/consuming-capsule.md) is the other side of the fence: what a
game repository sets up to build against this one.
[`docs/architecture.md`](docs/architecture.md) carries the determinism contract and the
boundaries that enforce it.
[`Capsule.Maps/README.md`](Capsule.Maps/README.md) covers the map format's invariants, the build
hook that imports Tiled maps, and the one thing outside this repository — installing and driving
Tiled itself. Everything else is in the code.

Capsule is licensed under the [MIT License](LICENSE).
