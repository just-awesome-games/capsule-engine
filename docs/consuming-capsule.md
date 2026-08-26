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
    Assets/                   # shipped content; Assets/Levels/ is derived, never committed
  MyGame.Game/                # game logic, substrate-free
    MyGame.Game.csproj
  MyGame.Tests/               # xUnit over MyGame.Game, run headless
  asset-sources/
    levels/                   # .tmj maps and their .tsj tilesets
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

**`asset-sources/` is top-level, owned by no project.** Level sources are game content, not one
project's content: a second target shell feeds from the same maps, so putting them under a shell
would make one of them the odd owner of everything the others build from.

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
arrives through it and a new hook is never new client wiring. Today it derives levels
([`Capsule.Levels/README.md`](../Capsule.Levels/README.md)).

### 4. Declare the roles

The import reaches every project, so each project with a Capsule role says which one it is. The
project that ships content:

```xml
<!-- MyGame.Shell.csproj -->
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <AssemblyName>MyGame</AssemblyName>
  <CapsuleGameShell>true</CapsuleGameShell>
</PropertyGroup>
```

and the project Capsule's source generators run over:

```xml
<!-- MyGame.Game.csproj -->
<PropertyGroup>
  <CapsuleGameLogic>true</CapsuleGameLogic>
</PropertyGroup>
```

Every hook and every generator arrives through those two words, so gaining one is never new
wiring here. Each role is taken by exactly one project — a second logic project would generate a
second registry holding only its own assembly's level types — and the test project takes neither.

### 5. Wire the references

```xml
<!-- MyGame.Game.csproj -->
<ProjectReference Include="../../capsule-engine/Capsule.Core/Capsule.Core.csproj" />
<ProjectReference Include="../../capsule-engine/Capsule.Levels/Capsule.Levels.csproj" />
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
using Capsule.Runtime;
using MyGame.Game;

CapsuleEngine.Configure()
    .WithWindow("My Game", 1280, 720, resizable: true)
    .WithCrashLog("MyGame")
    .WithBindings(MyGameInput.Bind)
    .RunScene("room-01", level => new MyGameScene(level));
```

Set only what you disagree with — every knob ships a default, and restating one you agree with is
noise. `RunScene` resolves its argument against `Assets/Levels/<name>.level.json` beside the
executable, which is where the level hook ships it, so the name is the Tiled map's own. The
factory is the whole boot contract: the engine holds no registry and no tile colours.

## Scenes

A scene is one screen of game. Subclass `Scene` and compose what that screen is made of. It lives
in the logic project, so a headless test drives the same type the shell ships:

```csharp
using System.Numerics;            // Vector2
using Capsule;                    // StepContext
using Capsule.Levels;             // Level
using Capsule.Scenes;             // Scene, Entity, Component, Renderer, Camera
using Capsule.Scenes.Components;  // QuadRenderer
using Capsule.Scenes.Entities;    // TileMap, TileColorResolver
using Capsule.Scenes.Spawning;    // LevelType, EntitySpawn, LevelTypeRegistry
using Capsule.Scenes.Generated;   // LevelTypes, the generated registry

public sealed class MyGameScene : Scene
{
    public MyGameScene(Level level)
    {
        TileMap tiles = new(level, TilePalette.ColorOf);
        Add(tiles);
        Size = tiles.Size;

        Spawn(level, LevelTypes.Registry);
    }

    protected override void OnStart() => Camera.ViewportSize = new Vector2(320f, 180f);

    protected override void OnStep(in StepContext context)
    {
        if (context.Input.WasPressed(MyGameInput.Quit))
        {
            RequestExit();
        }
    }
}
```

- `OnStart` runs once, before the first frame is built, which is where the camera opens.
- `OnStep` runs once per fixed step, after every position is retained and ahead of every entity
  update.
- `Spawn` adds one entity per level entity, in the level's own order, through the registry.
- `Size` is the scene's world extent, and a scene built from a level takes it from its tilemap.
- `Camera` is always there, carries a centre and a viewport size, and starts spanning nothing.

A scene with no level builds no tilemap and spawns nothing: a menu is a `Scene` with its own
entities and its own step.

`TileMap` asks its `TileColorResolver` for every tile type in the level's palette at
construction, so a type the game cannot draw fails at load rather than on the first painted tile.
That resolver is transient: it goes when a tile's appearance moves into the level format.

Entities subclass `Entity`, override `Update(in StepContext)`, and carry a `Renderer` for what is
drawn — `QuadRenderer` today, a subclass of your own where a game needs one. The engine owns
`PreviousPosition`; game code sets `Position`. Renderers draw in entity order and, within an
entity, in attachment order, so a scene that adds its tilemap first draws terrain behind
everything. Adding or removing an entity during a step lands at the end of that step, and
removing one twice in a step removes it once.

### Level types

A level entity record carries a `type` string. `[LevelType]` declares the class that claims it,
and the registry Capsule generates from the logic assembly maps one to the other:

```csharp
[LevelType]                              // claims "health-pickup"
public sealed class HealthPickup(EntitySpawn spawn) : Entity(spawn.Position);

[LevelType("player-spawn")]              // claims "player-spawn"
public sealed class Player : Entity { public Player(EntitySpawn spawn) : base(...) { } }
```

- The level type is the kebab-cased class name unless the attribute gives one.
- The class is non-abstract, derives from `Entity`, and takes one `EntitySpawn` through a public
  constructor. Each is a compile error otherwise — `CAP001` and `CAP002` — as is two classes
  claiming one level type (`CAP003`) or declaring a blank one (`CAP004`).
- `EntitySpawn.Position` is the raw level coordinate. What it anchors is an authoring convention,
  so a class whose position means something else — a corner where the map marks feet — converts
  it in its own constructor.
- A scene passes `Capsule.Scenes.Generated.LevelTypes.Registry` to `Spawn`. Nothing is reflected
  for: the registry is generated code, so the NativeAOT floor holds. The compile errors above
  cover the declarations only; a level entity whose Class matches no `[LevelType]` class fails at
  load with a `SpawnException` naming it. Validating level Classes against the registry at build
  time is decided-not-built (`architecture.md`).

## The purity rule

`MyGame.Game` references **only Capsule modules that carry no substrate reference** —
`Capsule.Core`, `Capsule.Levels` and `Capsule.Scenes` today — and never `Capsule.Runtime`. Being
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
