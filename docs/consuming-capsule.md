# Consuming Capsule

Everything a game repository does to build against this one, in the order it does it. Released
games restore exact SemVer versions from NuGet.org. Engine development can
replace those packages with a local source clone for one build, without editing project files.

## Bootstrap a new game

### 1. Use NuGet.org

Capsule packages use NuGet.org's standard public source and require no Capsule-specific credentials.

All six packages in a release carry one version: `JAG.Capsule.Core`, `JAG.Capsule.Maps`,
`JAG.Capsule.Scenes`, `JAG.Capsule.Runtime`, `JAG.Capsule.Verify`, and the tooling-only
`JAG.Capsule.Build`. Assemblies and namespaces remain `Capsule.*`. A game pins one exact
`CapsuleVersion`, commits the resulting lock files, and upgrades it deliberately.

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

### 3. Declare package and local-source resolution

The game root's `Directory.Build.props` owns the one version and the optional source override:

```xml
<Import Project="$(MSBuildThisFileDirectory)Directory.Build.local.props"
        Condition="Exists('$(MSBuildThisFileDirectory)Directory.Build.local.props')" />

<PropertyGroup>
  <CapsuleVersion>0.1.0</CapsuleVersion>
  <CapsuleSourceRoot Condition="'$(CapsuleUsePackages)' != 'true' and '$(CapsuleSourcePath)' != ''">$([MSBuild]::NormalizePath('$(MSBuildThisFileDirectory)', '$(CapsuleSourcePath)'))</CapsuleSourceRoot>
  <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  <NuGetLockFilePath Condition="'$(CapsuleSourceRoot)' != ''">$(MSBuildProjectDirectory)/obj/packages.source.lock.json</NuGetLockFilePath>
</PropertyGroup>

<ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
  <PackageReference Include="JAG.Capsule.Build"
                    Version="[$(CapsuleVersion)]"
                    PrivateAssets="all" />
</ItemGroup>
```

`JAG.Capsule.Build` reaches every project, but its hooks remain inert until a project declares a
Capsule role. It supplies map import, source generation, and compile-time architectural checks;
the tooling package contributes nothing to a game's output or publish set.

The root `Directory.Build.targets` supplies the source-development lane:

```xml
<Import Project="$(CapsuleSourceRoot)/build/Capsule.Build.targets"
        Condition="'$(CapsuleSourceRoot)' != '' and Exists('$(CapsuleSourceRoot)/build/Capsule.Build.targets')" />

<Target Name="CapsuleRequireSourceRoot" BeforeTargets="Restore;Build"
        Condition="'$(CapsuleSourceRoot)' != ''">
  <Error Condition="!Exists('$(CapsuleSourceRoot)/build/Capsule.Build.targets')"
         Text="Capsule source was not found at '$(CapsuleSourceRoot)'." />
</Target>
```

This file changes only when the consumption contract changes; ordinary engine releases change
`CapsuleVersion` alone.

For the normal engine-and-game development loop, add `Directory.Build.local.props` to the game's
`.gitignore`, then create the ignored file in the game root:

```xml
<!-- Directory.Build.local.props -->
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

The local file makes every ordinary IDE and `dotnet` invocation use Capsule source without
changing committed configuration. `CapsuleUsePackages=true` forces package mode for a command
without moving that file, which is how release tooling regenerates and validates the canonical
lock files.

### 4. Declare the roles

`JAG.Capsule.Build` reaches every project, so each project with a Capsule role says which one it is. The
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
wiring here. The roles decide what is generated where rather than what a compilation happens to
see. Each logic project publishes a generated registry provider for the classes in that assembly;
the one shell project aggregates every referenced provider, diagnoses duplicate claims across
assemblies, and emits `GameBoot` already holding the combined registry. A game may therefore split
logic into several modules without client registration or reflection. Exactly one project takes
the shell role; tests and ordinary libraries take neither role.

### 5. Wire the references

```xml
<!-- MyGame.Game.csproj -->
<ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
  <PackageReference Include="JAG.Capsule.Core" Version="[$(CapsuleVersion)]" />
  <PackageReference Include="JAG.Capsule.Maps" Version="[$(CapsuleVersion)]" />
  <PackageReference Include="JAG.Capsule.Scenes" Version="[$(CapsuleVersion)]" />
</ItemGroup>

<ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
  <ProjectReference Include="$(CapsuleSourceRoot)/Capsule.Core/Capsule.Core.csproj" />
  <ProjectReference Include="$(CapsuleSourceRoot)/Capsule.Maps/Capsule.Maps.csproj" />
  <ProjectReference Include="$(CapsuleSourceRoot)/Capsule.Scenes/Capsule.Scenes.csproj" />
</ItemGroup>

<!-- MyGame.Shell.csproj -->
<ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
  <PackageReference Include="JAG.Capsule.Runtime" Version="[$(CapsuleVersion)]" />
</ItemGroup>

<ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
  <ProjectReference Include="$(CapsuleSourceRoot)/Capsule.Runtime/Capsule.Runtime.csproj" />
</ItemGroup>

<ProjectReference Include="../MyGame.Game/MyGame.Game.csproj" />
```

The package and source branches expose the same assembly graph. The explicit logic references
make the purity boundary visible at the call site; the shell alone takes Runtime. A verify project
uses the same conditional pair for `JAG.Capsule.Verify`.

For a one-off local engine build without the persistent file above, clone Capsule beside the game
and set the switch on the command:

```
git clone https://github.com/just-awesome-games/capsule-engine.git ../capsule-engine
dotnet build -p:CapsuleSourcePath=../capsule-engine
```

The resulting build graph contains project references into that clone, so breakpoints and source
navigation work normally. Omit the property to return to the pinned packages; no generated file
or local path is committed.

### 6. Lock the restore

`Directory.Build.props` enables package lock files in package mode, every package-consuming
project commits its own `packages.lock.json`, and CI restores with
`--locked-mode` so a restore can never silently move a version. Package mode records the exact
Capsule modules and their runtime dependencies in those files. Source mode is for development;
it bypasses the game lock files so replacing packages with project references does not dirty the
committed graph. The release build omits `CapsuleSourcePath` and proves the locked package graph.

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
- `OnStop` runs once before deterministic reverse-order entity detachment when a scene is replaced,
  restarted, or its host ends.
- `OnStep` runs once per fixed step, after every position is retained and ahead of every entity
  update.
- `Size` is the scene's world extent, and a `MapScene` takes it from its tilemap.
- `Camera` is always there, carries a centre and a viewport size, and starts spanning nothing.
- `MapScene` keeps the `Map` it was composed from and its `Tiles` as protected members, so a
  subclass can query the terrain it is standing on.

Scene restart, replacement and exit requests are deferred until the current step completes; the
host remains alive across them.

A scene composing entities from something other than a map calls
`Spawn(ReadOnlySpan<EntitySpawn>, EntityRegistry)` itself, handing it
`Capsule.Scenes.Generated.GameEntities.Registry`. That is the same seam `MapScene` spawns
through; only where the spawns came from differs.

`TileMap` takes the grid alone. A tile's colour is optional presentation; a colourless tile remains
semantic grid data and draws nothing. Rendering visits only camera-intersecting coordinates.

Entities subclass `Entity`, override `Update(in StepContext)`, and carry a `Renderer` for what is
drawn — `QuadRenderer` today, a subclass of your own where a game needs one. The engine owns
`PreviousPosition`; game code sets `Position`. Renderers draw in entity order and, within an
entity, in attachment order, so a scene that adds its tilemap first draws terrain behind
everything. Adding or removing an entity during a step lands at the end of that step, and
removing one twice in a step removes it once.

### Registration is by constructor shape

Capsule generates entity and scene registrations in every logic assembly, and the shell's
`GameBoot` aggregates their providers and hands the combined scene registry over on the game's
behalf, so a game names none of them. A class enters one by the shape of its public constructor
rather than by declaring anything: a concrete `Scene` taking one
`MapSceneContext` is registered as composed from the map its name derives, and one taking nothing
as a scene no map backs. Two scenes deriving one map name is
`CAP005`.

A map's default name is the kebab-cased class name — `Room01` composes from `room-01`,
`CaveEntrance` from `cave-entrance` — with a letter-to-digit boundary counting as a word boundary,
so `Enemy2` gives `enemy-2`. `[MapName("room-01")]` fixes an existing authored identity across a
class rename; its value must be a portable ASCII file stem. A backing class is optional: a map no
class claims runs as a plain `MapScene`.

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
  the map's path and everything that is claimed.

## The purity rule

`MyGame.Game` references **only Capsule modules that carry no substrate reference** —
`Capsule.Core`, `Capsule.Maps` and `Capsule.Scenes` today — and never `Capsule.Runtime`. Being
substrate-free is the admission test rather than membership in a fixed list, so the set grows
without the rule weakening.

**File IO is the host's.** A pure module supplies the parser and the model; the host layer —
`RunScene`, or a bespoke shell — reads the bytes and hands the parsed value in. Game logic
therefore takes its content already in hand and never touches a path, which is what keeps a
logic test a pure function of its inputs.

The module references hold that boundary mechanically, and Capsule's analyzer rejects a logic
project that reaches Runtime, MonoGame, nondeterministic host services, or asynchronous execution.
`Capsule.Runtime` privatises its backend's compile assets so no backend type reaches the shell
either. A game repository never adds a backend `PackageReference` of its own.

## Verification

A game-owned verify executable references `Capsule.Verify` and its logic assembly, but not
`Capsule.Runtime`. It gives `VerifyRunner` the real simulation, bindings, one `DeviceSnapshot` per
tick, warm-up count and allocation budgets. Game-owned artifact writers run after measurement.

The runner is device-free. A render-intent image is deterministic but is not a MonoGame framebuffer
test; games needing that claim add a separately labelled real-device integration.
