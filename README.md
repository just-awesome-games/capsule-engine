<p align="center">
  <img src="docs/assets/capsule-hero.png" alt="Capsule — a hero stepping out of a glowing capsule as a game world materializes around it" width="720">
</p>

<h1 align="center">Capsule Engine</h1>

<p align="center">A code-first C# game engine — the whole game in one capsule, the machinery sealed inside.</p>

---

Capsule is JAG Studios' in-house engine: 2D, pixel-art, deterministic, code-first. It owns the
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

A complete Capsule game's entry point:

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
game code calls `RequestExit`.

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

Games consume the engine as a **sibling clone, by project reference** — no packaging, no feed, no
version dance — and reach its build hooks through one import at the game repo root. Capsule's
hooks import every `.tmj` under `asset-sources/maps/` and ship the result as content: create one,
build, and it is in the game. Nothing generated is committed.

**Setting a game up is one door: [`docs/consuming-capsule.md`](docs/consuming-capsule.md).** It
carries the bootstrap sequence end to end — layout, project skeleton, the build import, the role
declarations, locked restores — and the purity rule game logic is held to.

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
- **`Capsule.Runtime`** — the host: window, graphics device, clock, keyboard and gamepad, renderer,
  crash log. The only project that references MonoGame.
- **`Capsule.Tests`** — xUnit specs over Core, Maps and Scenes; hosts the generator over
  compilations it builds; adds builder validation and a reflection guard over Runtime's public
  surface.

## Building

```
dotnet build
dotnet test
dotnet format --verify-no-changes
```

With coverage, gated at a floor of 80% line coverage over `Capsule.Core`, `Capsule.Maps` and
`Capsule.Scenes`:

```
dotnet test -p:CollectCoverage=true "-p:Include=[Capsule.Core]*%2c[Capsule.Maps]*%2c[Capsule.Scenes]*" -p:CoverletOutputFormat=cobertura -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total
```

To restore exactly the committed dependency set — as CI does:

```
dotnet restore --locked-mode
```

The pre-commit hook mirrors the CI gates. Activate it once per clone:

```
git config core.hooksPath hooks
```

Above the device line, `Capsule.Core`'s and `Capsule.Scenes`'s contracts are asserted directly and
`Capsule.Tests` covers `Capsule.Runtime`'s builder validation, deadzone filtering and public
surface. Below it, the window-and-device paths need a real graphics device, so a consuming game's
verify run covers them.

## Further reading

[`AGENTS.md`](AGENTS.md) is the rules any contributor — human or agent — works under here.
[`docs/consuming-capsule.md`](docs/consuming-capsule.md) is the other side of the fence: what a
game repository sets up to build against this one.
[`docs/architecture.md`](docs/architecture.md) carries the determinism contract and the
capabilities designed but awaiting their game.
[`Capsule.Maps/README.md`](Capsule.Maps/README.md) covers the map format's invariants, the build
hook that imports Tiled maps, and the one thing outside this repository — installing and driving
Tiled itself. Everything else is in the code.
