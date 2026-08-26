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
project wizard: scenes are C#, levels are data.**

- **Everything is C# and text on disk** — the whole surface reachable by a person and an agent alike.
- **Gameplay is pure by construction.** A scene advances one fixed step at a time, reads input as
  named actions, never touches a graphics device, and so is assertable headlessly.
- **MonoGame is an implementation detail.** `Capsule.Runtime` marks its compile assets private, so a
  `Microsoft.Xna.Framework` using in a consuming game does not compile, while MonoGame's managed and
  native libraries still reach that game's output. Swapping the backend is engine-side only.

## Quickstart

A complete Capsule game's entry point:

```csharp
using Capsule.Runtime;
using MyGame.Game;

CapsuleEngine.Configure()
    .WithWindow("My Game", 1280, 720, resizable: true)
    .WithFixedStep(60)
    .WithCrashLog("MyGame")
    .WithBindings(MyGameInput.Bind)
    .RunScene("room-01", level => new MyGameScene(level));
```

That factory is the game's whole boot contract. `MyGameScene` derives from `Scene` and composes
what the screen is made of — a `TileMap` for terrain, then the level's entities through the
registry generated from the game's `[LevelType]` classes — overriding `OnStart` to open the camera
and `OnStep` for scene-wide input. It runs until game code calls `RequestExit`.

Below the scene sits the seam it is built on. A game that wants its own container hands the host
an `ISimulation` instead — `Run(simulation)` — which advances one fixed step at a time, reads
input as named actions, sets `ExitRequested` when it wants to stop, and exposes what to draw as a
`FrameView`. Neither ever touches a graphics device.

Games consume the engine as a **sibling clone, by project reference** — no packaging, no feed, no
version dance — and reach its build hooks through one import at the game repo root. Capsule's
hooks derive a level from every Tiled map under `asset-sources/levels/` and ship it as content:
create a `.tmj`, build, and it is in the game. Nothing generated is committed.

**Setting a game up is one door: [`docs/consuming-capsule.md`](docs/consuming-capsule.md).** It
carries the bootstrap sequence end to end — layout, project skeleton, the build import, the role
declarations, locked restores — and the purity rule game logic is held to.

## Architecture

Dependencies point one way and each direction is held mechanically, not by review: Core and Levels
reference nothing, Scenes references Core and Levels, Runtime references all three, and a game's
logic references `Capsule.Core`, `Capsule.Levels` and `Capsule.Scenes` while only its one-file
shell references `Capsule.Runtime`. That split is the compiler-enforced guarantee that gameplay
stays pure and headless-testable, and it is why no game ever links a line of Tiled-parsing code.

- **`Capsule.Core`** — the pure contracts a game codes against: `ISimulation`, the fixed step,
  input as named actions, render intent. No package references at all, so it cannot reach a device
  even by accident.
- **`Capsule.Levels`** — the level format and its loader. BCL only: a level is data, and no
  authoring format is ever a runtime dependency.
- **`Capsule.Levels.Cli`** — the dev-time tool a build hook runs to derive levels from Tiled maps
  ([`Capsule.Levels/README.md`](Capsule.Levels/README.md)).
- **`Capsule.Scenes`** — the world: `Scene`, `Entity`, `Component`, `Renderer`, `Camera`, and
  `SceneSimulation`, which owns the step choreography behind Core's seam.
- **`Capsule.Scenes.Generator`** — the compile-time generator that turns a game's `[LevelType]`
  classes into its spawn registry. Generated code, never reflection.
- **`Capsule.Runtime`** — the host: window, graphics device, clock, keyboard and gamepad, renderer,
  crash log. The only project that references MonoGame.
- **`Capsule.Tests`** — xUnit specs over Core, Levels and Scenes; hosts the generator over
  compilations it builds; adds builder validation and a reflection guard over Runtime's public
  surface.

## Building

```
dotnet build
dotnet test
dotnet format --verify-no-changes
```

With coverage, gated at the studio floor of 80% line coverage over `Capsule.Core`,
`Capsule.Levels` and `Capsule.Scenes`:

```
dotnet test -p:CollectCoverage=true "-p:Include=[Capsule.Core]*%2c[Capsule.Levels]*%2c[Capsule.Scenes]*" -p:CoverletOutputFormat=cobertura -p:Threshold=80 -p:ThresholdType=line -p:ThresholdStat=total
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
[`Capsule.Levels/README.md`](Capsule.Levels/README.md) covers the one thing outside this
repository — installing and driving Tiled — plus the level build hook and the level conventions.
Everything else is in the code.
