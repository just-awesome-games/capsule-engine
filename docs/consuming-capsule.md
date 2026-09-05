# Consuming Capsule

Capsule games use two projects: a substrate-free logic library and a small executable shell. This file contains the MSBuild wiring that cannot live in API comments.

> A complete minimal game is available at [`samples/MinimalGame/`](../samples/MinimalGame/): a logic project, a shell, an authoring tree, and a headless smoke binary CI publishes under NativeAOT. Copy it to start a game. The scene document format, including a tile palette with a `solid` layer and a `player` entry, is in [`scenes.md`](scenes.md).

## Repository shape

```text
my-game/
  src/
    MyGame.Game/
      MyGame.Game.csproj
    MyGame.Shell/
      MyGame.Shell.csproj
    asset-sources/
      scenes/
      sprites/
      textures/
      audio/
      fonts/
  tests/
    MyGame.Tests/
      MyGame.Tests.csproj
  Directory.Build.props
  Directory.Build.targets
  MyGame.slnx
```

The directory convention inside `src/MyGame.Game/` is in [`project-layout.md`](project-layout.md); this file stops at the project boundary.

Keep `src/asset-sources/` as a sibling of the logic and shell projects: Capsule looks for authored sources at `<project>/../asset-sources` by default, so both role projects find one tree without a `CapsuleAssetSourcesDir` override. The build derives `assets/` beside the executable.

From the repository root, create the modern solution and add the three projects after writing the project files below:

```text
dotnet new sln --name MyGame --format slnx
dotnet new classlib -o src/MyGame.Game
dotnet new console -o src/MyGame.Shell
dotnet new xunit -o tests/MyGame.Tests
dotnet sln MyGame.slnx add src/MyGame.Game src/MyGame.Shell tests/MyGame.Tests
```

Replace each generated project file's body with the wiring below.

## Shared configuration

Pin one exact Capsule version and give every project the build package:

```xml
<!-- Directory.Build.props -->
<Project>
  <Import Project="$(MSBuildThisFileDirectory)Directory.Build.local.props" Condition="Exists('$(MSBuildThisFileDirectory)Directory.Build.local.props')" />

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <JsonSerializerIsReflectionEnabledByDefault>false</JsonSerializerIsReflectionEnabledByDefault>
    <CapsuleVersion>{YOUR_PINNED_VERSION}</CapsuleVersion>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <CapsuleSourceRoot Condition="'$(CapsuleUsePackages)' != 'true' and '$(CapsuleSourcePath)' != ''">$([MSBuild]::NormalizePath('$(MSBuildThisFileDirectory)', '$(CapsuleSourcePath)'))</CapsuleSourceRoot>
    <NuGetLockFilePath Condition="'$(CapsuleSourceRoot)' != ''">$(MSBuildProjectDirectory)/obj/packages.source.lock.json</NuGetLockFilePath>
  </PropertyGroup>

  <ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
    <PackageReference Include="JAG.Capsule.Build"
                      Version="[$(CapsuleVersion)]"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

The matching source-development import is:

```xml
<!-- Directory.Build.targets -->
<Project>
  <Import Project="$(CapsuleSourceRoot)/build/Capsule.Build.targets"
          Condition="'$(CapsuleSourceRoot)' != '' and Exists('$(CapsuleSourceRoot)/build/Capsule.Build.targets')" />

  <Target Name="CapsuleRequireSourceRoot" BeforeTargets="Restore;Build"
          Condition="'$(CapsuleSourceRoot)' != ''">
    <Error Condition="!Exists('$(CapsuleSourceRoot)/build/Capsule.Build.targets')"
           Text="Capsule source was not found at '$(CapsuleSourceRoot)'." />
  </Target>
</Project>
```

## Logic project

The logic role activates source generation and purity analysis, and compiles the game's sprite sheets into typed frames and clips:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <CapsuleGameLogic>true</CapsuleGameLogic>
  </PropertyGroup>

  <ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
    <PackageReference Include="JAG.Capsule" Version="[$(CapsuleVersion)]" />
  </ItemGroup>
  <ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
    <ProjectReference Include="$(CapsuleSourceRoot)/src/Capsule/Capsule.csproj" />
  </ItemGroup>
</Project>
```

Tests reference the logic project and `JAG.Capsule`; they take no Capsule role. From `tests/MyGame.Tests/MyGame.Tests.csproj`, the logic reference is:

```xml
<ItemGroup>
  <ProjectReference Include="../../src/MyGame.Game/MyGame.Game.csproj" />
</ItemGroup>
```

## Shell project

Exactly one project takes the shell role:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>MyGame</AssemblyName>
    <CapsuleGameShell>true</CapsuleGameShell>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="../MyGame.Game/MyGame.Game.csproj" />
  </ItemGroup>
  <ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
    <PackageReference Include="JAG.Capsule.Runtime" Version="[$(CapsuleVersion)]" />
  </ItemGroup>
  <ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
    <ProjectReference Include="$(CapsuleSourceRoot)/src/Capsule.Runtime/Capsule.Runtime.csproj" />
  </ItemGroup>
</Project>
```

The shell role generates `CapsuleBoot`, imports scene documents, ships assets, and supplies default application icons.

A role-free project that needs derived content — a test project, a headless smoke binary — can opt into `<CapsuleImportScenes>`, `<CapsuleShipAssets>` and `<CapsuleImportSprites>` independently.

## Package and source modes

Commit each package-consuming project's `packages.lock.json` and restore CI with `--locked-mode`. That pairing is package mode only: a source build resolves the engine through project references, so a source-mode restore runs without `--locked-mode`. The in-repo sample commits no lock file — its feed is repacked from source on every run, and a repacked `.nupkg` never reproduces its content hash.

> For persistent local development, create an ignored `Directory.Build.local.props`:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

It is git-ignored, and the source-mode lock file lands under `obj/`, so the committed lock file is untouched.

### The API reference

Capsule's XML comments are its API reference. A package consumer reads them where NuGet unpacks them, beside the assemblies at `%USERPROFILE%\.nuget\packages\jag.capsule\<version>\lib\net10.0\`.

A source consumer has no such directory, so the source build stages the same files at `artifacts/capsule-api/` under the repository root — one directory holding the documentation of every Capsule assembly the repository references, written before each project compiles so the reference is current even on a build that fails against a changed engine API. `CapsuleApiReferenceDirectory` stages them somewhere else; a relative path resolves against the repository root. The directory is derived and build-owned: ignore it.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`; pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

Keep game code AOT-safe: the NativeAOT publish is the whole-graph gate, and running the published binary proves it boots. The rule is in [`architecture.md`](architecture.md#nativeaot-floor).

## Model and rendering

Rendering draws sprites: a `SpriteRenderer` — in `Capsule.Scenes.Rendering`, with the `Renderer` it derives from — holds a `Sprite` — a `TextureHandle`, a `TextureRegion` of it, and the `Pivot` the entity's position anchors — and sets `Offset`, `FlipX`, `FlipY` and `Color` on top of it; a tile map draws cells of one texture. A handle whose file is missing fails the game where its scene is loaded, naming the handle and the path it looked in.

## Texture residency

The host keeps one scene's textures on the device at a time. Entering a scene loads what its set adds and releases what the scene being left wanted alone, in one synchronous exchange before that scene is torn down; a texture in both sets is never touched. Drawing a handle the current scene's set does not hold throws, naming the scene.

A set is a union of *residency groups*, and a group is one asset directory's generated `All` — `GameAssets.Textures.All`, `GameAssets.Textures.Enemies.All`. The build derives each scene's groups and you write nothing: they are the textures the scene's document names, plus, for the scene's class and for every entity its document places, the `GameAssets.Textures` members that code reaches — closed over the types those types reference, so a `Player` that spawns a `Buster` drawing `GameAssets.Textures.Fx.Shot` keeps `Fx` resident. A handle built from literals, as a sprite sheet's generated frames build theirs, counts the same. A reference from one scene class to another does not: each scene's set is its own, loaded when it is entered.

Two things the derivation cannot see are a handle whose name is computed at run time and a texture reached through a `using static` or an alias of the generated tree. A scene that needs either declares its whole set instead, which replaces the derivation:

```csharp
public sealed class BossArena : Scene
{
    protected internal override IReadOnlyList<TextureHandle>? ResidentTextures =>
        [.. GameAssets.Textures.Bosses.All, .. GameAssets.Textures.Fx.All];
}
```

It is read once, before the scene starts, so it cannot depend on state the scene builds in `OnStart`. Returning `null`, which is the default, takes the derivation.

## Named assets

Shipped assets are authored under `src/asset-sources/<domain>/` — `textures/`, `audio/`, `fonts/` — and a source's path under its domain root is its name everywhere. Directories nest freely and are kept: `asset-sources/textures/enemies/bat.png` ships at `assets/textures/enemies/bat.png`, hands out the handle named `enemies/bat`, and is declared as `GameAssets.Textures.Enemies.Bat`.

`asset-sources/sprites/` follows the same rule: `sprites/enemies/bat.sheet.json` is compiled into `GameSprites.Enemies.Bat`, whose `Frames` and `Clips` are the sheet's own.

A document names a texture by that same path, extension included — `"enemies/bat.png"`, or `"tiles.png"` for a file at the root. Forward slashes only, and no empty, `.` or `..` segment.

Every generated class, each domain root and each nested class, exposes a read-only `All` holding every handle beneath it, its subdirectories included: `GameAssets.Textures.All` is the whole domain, `GameAssets.Textures.Enemies.All` is that directory. It is a `ReadOnlySpan<T>` over generated constant data, so enumerating it allocates nothing.

Two sources may share a stem in different directories. Names collide only within one directory, where two spellings that become one C# identifier — `a-b.png` beside `a_b.png`, or a `bat/` directory beside `bat.png` — fail the build naming both. So does a name that would shadow the class it is declared on: `textures/enemies/enemies.png`, a `textures/textures/` directory, or anything named `all`.

## Testing headlessly

A scene and everything on it are substrate-free, so a test builds one, steps it, and asserts simulation state with no window, no graphics device and no assets on disk. `SceneSimulation` needs a scene and nothing else: the render defaults and the random source are optional, and omitting them takes the default sampling and the default seed's stream 0.

```csharp
using Capsule;
using Capsule.Input;
using Capsule.Scenes;

Scene scene = new();
Player player = new(Vector2.Zero);
scene.Add(player);

using SceneSimulation simulation = new(scene);

InputState input = new(bindings);
for (long tick = 0; tick < 30; tick++)
{
    input.Advance(snapshot);
    simulation.Step(new StepContext(1.0 / 60.0, input, tick));
}

Assert.Same(GameSprites.Player.Clips.Idle, player.Animator.Clip);
```

`StepContext` is the whole of what one fixed step is given: its duration in seconds, the `InputState` to read, and the tick index. Hold one `InputState` across the run and `Advance` it with the `DeviceSnapshot` a device would have reported, since input edges are differences between consecutive snapshots and a fresh state each step has none. `DeviceSnapshot.Empty` is nothing held.

What the step would draw is `simulation.View`, rewritten once per step, so a test asserts the frame a player would have seen without a renderer or a window. A test whose subject draws from `Scene.Random` takes its own seed — `new SceneSimulation(scene, random: new RandomSource(seed))` — and replays exactly.

## Seeing your game's output

Game logic cannot reach `System.Console` — the analyzer stops it — so it says things out loud through `Capsule.Diagnostics.Log`:

```csharp
using Capsule.Diagnostics;

Log.Info($"picked up {tile}");
Log.Warning("no spawn point on this map");
```

The shell installs a console sink at boot; every level goes to standard output in order, prefixed with the simulation tick:

```text
[   boot] info  main menu started
[     30] warn  no spawn point on this map
```

Run the shell with `dotnet run --project src/MyGame.Shell` and the lines appear in that terminal. A shell launched by double-clicking its executable has no terminal attached and shows nothing.

Nothing is installed until the host runs, so a headless test harness supplies its own. `CollectingLogSink` keeps what it is given:

```csharp
CollectingLogSink log = new();
Log.UseSink(log);

// ... step the simulation ...

Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning);
```

`WithLogSink(sink)` on the engine builder sends the game's output somewhere else instead, and `WithoutLogging()` silences it.

## Measuring the host

`WithFrameDiagnostics(path)` on the engine builder writes a CSV of what the host spent its time on. It opens with a commented boot trace — the milliseconds from process start to builder entry, host construction, device readiness, texture residency, the first update and the first submitted frame — and then holds one row per frame: the interval since the previous frame began, the time spent in the update, and the time spent submitting the draw, all in milliseconds. Present is excluded, because the wait for the display happens after the host's draw returns. A second argument exits the run that many seconds after the first frame, for an unattended capture. It is off unless the call is made; the shell decides where the path comes from.

## Build configuration reference

Capsule is configured with ordinary MSBuild properties. Put a value in the narrowest project that owns it. Paths may be absolute or relative to the project whose build imports Capsule unless a row says otherwise.

### Project roles

| Property           | Value  | Effect                                                                                                                                        |
| ------------------ | ------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleGameLogic` | `true` | Enables game-boundary analysis, generates the game's scene, entity, and asset registries, and compiles its sprite sheets. Set it only on the substrate-free logic library. |
| `CapsuleGameShell` | `true` | Generates `CapsuleBoot` and defaults scene import and asset shipping on. Set it only on the executable shell.                                 |

### Authoring sources and output

| Property                 | Default                                       | Effect                                                                                                                                                                 |
| ------------------------ | --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleAssetSourcesDir` | `../asset-sources` from the importing project | Locates the authored `scenes/`, `sprites/`, `textures/`, `audio/`, and `fonts/` trees. An explicitly named directory must exist.                                       |
| `CapsuleImportScenes`    | `true` for the shell; otherwise `false`       | Validates and canonically re-emits `*.scene.json` sources, then ships them under `assets/scenes/`.            A role-free test or tool can opt in independently. |
| `CapsuleShipAssets`      | `true` for the shell; otherwise `false`       | Ships admitted textures, audio, and fonts under `assets/`. A role-free test or tool can opt in independently.                                                          |
| `CapsuleImportSprites`   | `true` for the logic library; otherwise `false` | Validates `*.sheet.json` sources and compiles them into `GameSprites`. Nothing ships; a role-free project that has to name a frame or clip opts in independently.    |
| `CapsuleTileSize`        | unset                                         | Requires every imported tile map to use this positive pixel size. Set it on each project that imports scenes when the game has one global tile size.                   |

### Application icons

A shell with no icon configuration receives Capsule's executable and window icons. Override either or both beside the shell project:

| Input                                            | Effect                                                                                   |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| `Icon.ico`                                       | Becomes the executable icon through the standard .NET `ApplicationIcon` property.        |
| `Icon.bmp`                                       | Becomes the window and taskbar icon: a 128x128, 32-bit uncompressed BMP, alpha honoured. |
| `ApplicationIcon`                                | Overrides the executable icon with any path accepted by the .NET SDK.                    |
| `EmbeddedResource` with `LogicalName="Icon.bmp"` | Overrides the window icon when the bitmap is not beside the shell project.               |

```xml
<PropertyGroup>
  <ApplicationIcon>branding/MyGame.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <EmbeddedResource Include="branding/MyGame.bmp" LogicalName="Icon.bmp" />
</ItemGroup>
```

Defining only one half is allowed, but the build warns because the other half retains Capsule branding.

The window icon is transparent where its alpha says so, and Capsule's own default is. Two traps in that byte: a bitmap whose alpha is entirely zero is read as fully opaque rather than fully invisible, so zeroing the byte a tool calls padding still ships a visible icon; and `BI_RGB` formally declares the byte unused, so most image viewers discard it and draw the file on black. A transparent `Icon.bmp` therefore looks black-backed in almost any viewer — that is the viewer, and flattening the bitmap to make it agree puts a real background back on the window.

### Package and source properties

| Property                       | Default                                           | Effect                                                                                                                                                                                        |
| ------------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleVersion`               | consumer-defined                                  | Pins `JAG.Capsule`, `JAG.Capsule.Runtime`, and `JAG.Capsule.Build` to one release.                                                                                                            |
| `CapsuleSourcePath`            | unset                                             | Points at an engine clone. The standard wiring resolves it relative to the `Directory.Build.props` that declares `CapsuleSourceRoot`, not the command's working directory.                    |
| `CapsuleUsePackages`           | `false`                                           | Set to `true` to ignore a source override and verify the pinned NuGet graph.                                                                                                                  |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where a source build stages Capsule's XML documentation. A relative path is resolved against the repository root. Read only in source mode; a package consumer reads the NuGet cache instead. |
