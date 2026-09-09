# Consuming Capsule

Capsule games use two projects: a substrate-free logic library and a small executable shell. This file contains the MSBuild wiring that cannot live in API comments.

> A complete minimal game is available at [`samples/MinimalGame/`](../samples/MinimalGame/): a logic project, a shell and an authoring tree. Copy it to start a game. The independent NativeAOT smoke fixture lives under [`tests/Capsule.AotSmoke.Logic/`](../tests/Capsule.AotSmoke.Logic/); it does not depend on sample gameplay or assets. The scene document format is in [`scenes.md`](scenes.md).

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
    <!-- A console-subsystem executable double-clicked from Explorer opens a console window, and
         the console host's startup (~230 ms) lands inside the game's boot. Release ships as a
         Windows-subsystem app; Debug keeps the console for the log sink. -->
    <OutputType Condition="'$(Configuration)' == 'Release'">WinExe</OutputType>
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

The shell role generates `CapsuleBoot`, imports scene documents, ships assets, and supplies default application icons. Its entry point is the whole of the shell's hand-written code:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

`WithCommandLine` applies Capsule's standard flags, tabulated in [`headless-play.md`](headless-play.md); `RunScene` returns the process's exit code.

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

A source build compiles the engine clone's own projects optimised — `Release`, into the clone's `bin/Release` — whatever configuration the game builds in, so a `Debug` game runs at the speed of the engine it will ship against while its own code stays debuggable. `CapsuleSourceConfiguration` overrides that; set it to `Debug` to step into engine source.

### Release schedule

A game develops in source mode against the engine clone and its CI builds the same way, at the engine commit a pin file in the game names; an engine change lands on the engine's `main` first, then the game bumps its pin in the change that needs it. Capsule and its modules publish to NuGet only on a deliberate release, so a package pin is expected to lag `main` between releases.

### The API reference

Capsule's XML comments are its API reference. A package consumer reads them where NuGet unpacks them, beside the assemblies at `%USERPROFILE%\.nuget\packages\jag.capsule\<version>\lib\net10.0\`. Every shipped file lists only the public surface, so an entry in it is an API a game can call.

A source consumer has no such directory, so the source build stages the same files at `artifacts/capsule-api/` under the repository root — one directory holding the documentation of every Capsule assembly the repository references, written before each project compiles so the reference is current even on a build that fails against a changed engine API. `CapsuleApiReferenceDirectory` stages them somewhere else; a relative path resolves against the repository root. The directory is derived and build-owned: ignore it.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`; pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

Keep game code AOT-safe: the NativeAOT publish is the whole-graph gate, and running the published binary proves it boots. The rule is in [`architecture.md`](architecture.md#nativeaot-floor).

## Development-only directories

A directory holding a file named `.capsuleignore` is development-only. Everything under it, recursively, is part of every ordinary build — Debug and Release alike, so a Release measurement can still run one — and part of no publish.

Input drivers are the case it exists for. They live in the logic project because they read the scene, and they must not ship:

```text
src/MyGame.Game/
  Drivers/
    .capsuleignore
    Walkthrough.cs
```

The marker means the same thing in both planes. Sources under a marked directory leave the compile before the generators read it, so a shipped build's scene, entity, and driver registries hold nothing declared there and `--driver` answers to no name from it. Authoring sources under a marked directory leave the asset plane, so nothing under it reaches `assets/`, is loaded, or is declared in `GameAssets`. Shipped code naming a development-only asset therefore fails to compile in a publish, which is the failure it is owed.

The marker file's contents are not read; a line saying what the directory is helps whoever finds it.

A directory is marked by where it is rather than by how a project spelled the path, so a relative glob and an absolute include of the same file are both covered. Whether a sibling differing only in case is that same directory is the filesystem's answer, as it is everywhere else in the build.

`CapsuleShipping` is the switch, and a publish sets it. Set it on an ordinary build to see exactly what a publish will hold without running one.

## Rendering and scene assets

`SpriteRenderer`, `SpriteAnimator`, `TileMap` and the scene camera are the presentation entry points; their behavior is documented in the shipped API reference. Rendering submits visible sprites in order, while asset collection is independent of visibility.

Initial boot collection sees the already-started scene; transition and restart collection sees the composed incoming graph before `OnStart`. Sprite renderers, the clips currently held by sprite animators and tile maps contribute automatically. An asset initialized later — by a spawn, a changed sprite or a changed animation — loads synchronously on first rendered use and is then cached for the current scene. Explicit collection avoids that first-use hitch. Headless simulation never loads media.

Scene, entity and component `CollectAssets(AssetCollection)` overrides add to the engine-owned collection; they never replace assets contributed elsewhere. Collection for transitions and restarts happens before `OnStart`, so an override must use construction and composition state only:

```csharp
using Capsule.Assets;
using Capsule.Scenes;

public sealed class BossArena : Scene
{
    protected override void CollectAssets(AssetCollection assets)
    {
        assets.Add(GameAssets.Textures.Bosses.All);
        assets.Add(GameAssets.Textures.Fx.Telegraph);
    }
}
```

The runtime owns collected and first-used resources for one scene. A transition keeps resources the incoming preload also uses and releases the rest; exit releases all of them. Loading is synchronous, cached and host-only. Textures are the only media decoded today; `AudioHandle` and `FontHandle` remain names, not playback or rendering APIs.

## Named assets

Assets are authored under `src/asset-sources/<domain>/` and ship at the same relative path under `assets/<domain>/`. For example, `asset-sources/textures/enemies/bat.png` becomes `GameAssets.Textures.Enemies.Bat` and ships at `assets/textures/enemies/bat.png`. A scene or sheet document names it as `"enemies/bat.png"`.

Each generated domain and directory class exposes an allocation-free `All` span over the handles beneath it, such as `GameAssets.Textures.Enemies.All`. Sprite sheets generate typed frames and clips under `GameSprites`; [`sprite-animation.md`](sprite-animation.md) defines that format. Invalid paths and C# identifier collisions fail the build.

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

Game logic cannot reach `System.Console`, so it writes through `Capsule.Diagnostics.Log`:

```csharp
using Capsule.Diagnostics;

Log.Info($"picked up {tile}");
Log.Warning("no spawn point on this map");
```

The shell writes every level to standard output in order, prefixed with the simulation tick:

```text
[   boot] info  main menu started
[     30] warn  no spawn point on this map
```

Run the shell from a terminal to see the lines. A headless test can install `CollectingLogSink` and assert its entries:

```csharp
CollectingLogSink log = new();
Log.UseSink(log);

// ... step the simulation ...

Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning);
```

`WithLogSink(sink)` replaces the host sink, and `WithoutLogging()` silences it. Ownership and failure behavior are documented on `Log`, `ILogSink` and `CollectingLogSink` in the API reference.

## Controllers

All SDL controller backends enumerate at boot; on Windows, set `SDL_DIRECTINPUT_ENABLED=0` to isolate DirectInput's startup cost.

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
| `CapsuleShipping`        | `true` for the duration of a publish          | Excludes every [development-only directory](#development-only-directories) from the compile and from the asset plane. Set it on an ordinary build to verify a publish. |

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
| `CapsuleSourceConfiguration`   | `Release`                                         | The configuration the engine clone's own projects build in under a source consumer, whatever the game builds. Set it to `Debug` to step into engine source.                                   |
