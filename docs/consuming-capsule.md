# Consuming Capsule

Everything a game repository does to build against this one, in the order it does it. Capsule
ships as source at a sibling path, so all of this lives in the client repo and none of it is a
packaging step.

## Bootstrap a new game

### 1. Clone the engine beside the game

```
Development/
  capsule-engine/
  my-game/
```

```
git clone https://github.com/just-awesome-games/capsule-engine.git
```

There is no feed, no package and no version number, so local development edits both repos inside
one build graph. Commit a `capsule-engine.pin` at the game repo root holding the git ref CI
clones the engine at: a branch name tracks the tip, a full commit SHA pins a build. It is bumped
in the same commit as the game change that needs the newer engine.

### 2. Lay out the repo

```
my-game/
  MyGame.sln
  MyGame.Shell/               # the host executable
    MyGame.Shell.csproj
    Program.cs
    Assets/                   # shipped content; Assets/Maps/ is derived, never committed
  MyGame.Game/                # game logic, substrate-free
    MyGame.Game.csproj
  MyGame.Tests/               # xUnit over MyGame.Game, run headless
  asset-sources/
    maps/                     # Tiled maps and their .tsj tilesets
  capsule-engine.pin
  Directory.Build.props
  Directory.Build.targets
  global.json
  .editorconfig
```

**The two projects are named for their roles.** `MyGame.Game` is the logic the purity rule below
holds substrate-free; `MyGame.Shell` is the host that owns the window, the device and file IO.
The shell sets `<AssemblyName>` to the bare game name so the shipped executable is `MyGame.exe`,
not `MyGame.Shell.exe`.

**`asset-sources/` is top-level, owned by no project.** Authoring sources are game content, not
one project's content: a second target shell feeds from the same Tiled maps, so putting them
under a shell would make one of them the odd owner of everything the others build from.

Capsule has no asset scanner to hide a file from, so what ships is exactly what the shell
`.csproj` copies. Authoring sources stay unshipped by living outside `Assets/`, never by a naming
convention.

**The skeleton is the ceremony bar.** A new Capsule game is a `Program.cs` shell plus its own
logic project, and an engine capability that would grow that list is misdesigned.

### 3. Take the build import

Build logic cannot travel through a `ProjectReference`, so the game imports it once. Copy
[`build/client/Directory.Build.targets`](../build/client/Directory.Build.targets) from the engine
clone to the game repo root, verbatim — it resolves the engine at the sibling path and fails with
a legible error when the clone is missing.

That import reaches every project in the repo, so every Capsule build hook, present and future,
arrives through it and a new hook is never new client wiring. Today it imports Tiled maps
([`Capsule.Maps/README.md`](../Capsule.Maps/README.md)).

### 4. Declare the roles

The import reaches every project, so each project with a Capsule role says which one it is. The
project that ships content and boots the game:

```xml
<!-- MyGame.Shell.csproj -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <AssemblyName>MyGame</AssemblyName>
  <CapsuleGameShell>true</CapsuleGameShell>
</PropertyGroup>
```

and the project holding the game's own classes:

```xml
<!-- MyGame.Game.csproj -->
<PropertyGroup>
  <CapsuleGameLogic>true</CapsuleGameLogic>
</PropertyGroup>
```

Every hook and every generator arrives through those two words, so gaining one is never new
wiring here. The roles decide what is generated where — the registries into the logic assembly,
the `GameBoot` entry point into the shell — rather than what a compilation happens to see, which
is why a shell reaching `Capsule.Scenes` through the logic assembly never grows a second, empty
pair of registries. Each role is taken by exactly one project — a second logic project would
generate a second pair, each holding only its own assembly's classes — and the test project takes
neither.

### 5. Wire the references

```xml
<!-- MyGame.Game.csproj -->
<ProjectReference Include="../../capsule-engine/Capsule.Core/Capsule.Core.csproj" />
<ProjectReference Include="../../capsule-engine/Capsule.Maps/Capsule.Maps.csproj" />
<ProjectReference Include="../../capsule-engine/Capsule.Scenes/Capsule.Scenes.csproj" />

<!-- MyGame.Shell.csproj -->
<ProjectReference Include="../../capsule-engine/Capsule.Runtime/Capsule.Runtime.csproj" />
<ProjectReference Include="../MyGame.Game/MyGame.Game.csproj" />
```

### 6. Lock the restore

`Directory.Build.props` sets `<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>`,
every package-consuming project commits its own `packages.lock.json`, and CI restores with
`--locked-mode` so a restore can never silently move a version. Capsule is never a package and
never appears in a lock file.

### 7. Configure the engine

`Program.cs` is the whole shell:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

GameBoot.Configure()
    .WithWindow("My Game", 1280, 720, resizable: true)
    .WithCrashLog("MyGame")
    .WithBindings(MyGameInput.Bind)
    .RunScene("room-01");
```

`Capsule.Runtime.Generated.GameBoot` is generated into the shell off `<CapsuleGameShell>`, already
holding the scene registry generated into the logic assembly the shell references — the registry
every boot verb resolves through. So a game's boot registers nothing, and every line in the chain
is a knob: set only what you disagree with, because every one ships a default and restating one
you agree with is noise.

`CapsuleEngine.Configure()` is the same builder unwired, taking a hand-built `SceneRegistry`
through `WithScenes`. That is the path a test or a bespoke host takes; a game boots through
`GameBoot`.

`RunScene(mapName)` resolves its argument against `Assets/Maps/<name>.map.json` beside the
executable, which is where the map hook ships it, so the name is the Tiled map's own. It runs the
class claiming that map, or a plain `MapScene` when no class claims it. `RunScene<TScene>()` boots
a scene by class instead, loading the map backing it first where one does. A map file that is not
there fails at load, naming the full path it looked for.

## Scenes

A scene is one screen of game. It lives in the logic project, so a headless test drives the same
type the shell ships.

A scene composed from a map subclasses `MapScene` and passes the context it is handed straight
through. The base class has already composed the screen — the grid as a `TileMap` added first, so
terrain draws behind everything, then one entity per placed object in the map's own order — and
leaves the subclass the behaviour:

```csharp
using System.Numerics;            // Vector2
using Capsule;                    // StepContext
using Capsule.Scenes;             // Scene, MapScene, MapSceneContext, Entity, Camera

public sealed class Room01(MapSceneContext context) : MapScene(context)
{
    protected override void OnStart() => Camera.ViewportSize = new Vector2(320f, 180f);

    protected override void OnStep(in StepContext step)
    {
        if (step.Input.WasPressed(MyGameInput.Quit))
        {
            RequestExit();
        }
    }
}
```

A scene no map backs subclasses `Scene` and takes nothing, composing itself:

```csharp
public sealed class MainMenu : Scene
{
    public MainMenu() => Add(new MenuCursor());
}
```

- `OnStart` runs once, before the first frame is built, which is where the camera opens.
- `OnStep` runs once per fixed step, after every position is retained and ahead of every entity
  update.
- `Size` is the scene's world extent, and a `MapScene` takes it from its tilemap.
- `Camera` is always there, carries a centre and a viewport size, and starts spanning nothing.
- `MapScene` keeps the `Map` it was composed from and its `Tiles` as protected members, so a
  subclass can query the terrain it is standing on.

A scene composing entities from something other than a map calls
`Spawn(ReadOnlySpan<EntitySpawn>, EntityRegistry)` itself, handing it
`Capsule.Scenes.Generated.GameEntities.Registry`. That is the same seam `MapScene` spawns
through; only where the spawns came from differs.

`TileMap` takes the grid alone: a tile's appearance is authored with the map and rides in its
palette, so nothing about terrain is registered at boot. One quad per non-empty tile is baked at
construction, after which drawing terrain allocates nothing.

Entities subclass `Entity`, override `Update(in StepContext)`, and carry a `Renderer` for what is
drawn — `QuadRenderer` today, a subclass of your own where a game needs one. The engine owns
`PreviousPosition`; game code sets `Position`. Renderers draw in entity order and, within an
entity, in attachment order, so a scene that adds its tilemap first draws terrain behind
everything. Adding or removing an entity during a step lands at the end of that step, and
removing one twice in a step removes it once.

### Registration is by constructor shape

Capsule generates two registries from the logic assembly — the scenes, and the entities they
spawn, which the scene registry already holds — and the shell's `GameBoot` hands the scene
registry over on the game's behalf, so a game names neither. A class enters one by the shape of
its public constructor rather than by declaring anything: a concrete `Scene` taking one
`MapSceneContext` is registered as composed from the map its name derives, and one taking nothing
as a scene no map backs. Two scenes deriving one map name is
`CAP005`.

A map's name is the kebab-cased class name — `Room01` composes from `room-01`, `CaveEntrance` from
`cave-entrance` — with a letter-to-digit boundary counting as a word boundary, so `Enemy2` gives
`enemy-2`. A backing class is optional: a map no class claims runs as a plain `MapScene`. Nothing
overrides the name a scene class derives, so a class is renamed together with its `.tmj`.

**A class of neither shape is passed over in silence, and that silence is deliberate.** A scene
your own code constructs — from a save file, from another scene's state, with a difficulty
argument — is an ordinary class, not a defect, and constructor discovery is only livable if it
says nothing about the classes it is not interested in. There is no diagnostic to suppress and no
opt-out to remember; the same rule governs entities below.

### Spawn types

A map object carries a `type` string, and the class claiming it is any concrete `Entity` with a
public constructor taking one `EntitySpawn`. That constructor *is* the opt-in:

```csharp
using Capsule.Scenes.Spawning;    // EntitySpawn, SpawnType

// claims "health-pickup"
public sealed class HealthPickup(EntitySpawn spawn) : Entity(spawn.Position);

[SpawnType("player-spawn")]
public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
```

- The spawn type is the kebab-cased class name. `[SpawnType("type")]` fixes the type in place
  instead — its argument is required — which is how a class is renamed without every map that
  places it having to change.
- `EntitySpawn` carries the object's id, its type and its `Position`, the raw authored coordinate.
  What that anchors is an authoring convention, so a class whose position means something else — a
  centre where the source marks feet — converts it in its own constructor.
- `[SpawnType]` is the one way to be wrong out loud: on a class that is not a concrete `Entity`
  it is `CAP001`, on one with no `EntitySpawn` constructor `CAP002`, and blank is `CAP004`. Two
  classes claiming one spawn type is `CAP003` whether either declared it or not.
- Nothing is reflected for: both registries are generated code, so the NativeAOT floor holds, and
  both are emitted whatever the assembly declares, so a call site naming either always compiles.
  A map object whose type no class claims fails at load with a `SpawnException` naming the type,
  the map's path and everything that is claimed. Validating a `.tmj`'s object Classes against the
  registry at build time is decided-not-built (`architecture.md`).

## The purity rule

`MyGame.Game` references **only Capsule modules that carry no substrate reference** —
`Capsule.Core`, `Capsule.Maps` and `Capsule.Scenes` today — and never `Capsule.Runtime`. Being
substrate-free is the admission test rather than membership in a fixed list, so the set grows
without the rule weakening.

**File IO is the host's.** A pure module supplies the parser and the model; the host layer —
`RunScene`, or a bespoke shell — reads the bytes and hands the parsed value in. Game logic
therefore takes its content already in hand and never touches a path, which is what keeps a
logic test a pure function of its inputs.

The project references hold that boundary mechanically, and `Capsule.Runtime` privatises its
backend's compile assets so no backend type reaches the shell either. The one gap is deliberate:
**a game repo never adds a backend `PackageReference` of its own.** Privatisation closes the
transitive path, not a direct one, and taking it is a defect rather than a compile error.
