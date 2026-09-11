# Consuming Capsule

Capsule games use two projects: a substrate-free logic library and a small executable shell. This file contains the MSBuild wiring that cannot live in API comments.

> A complete minimal game — a logic project, a shell and an authoring tree — is at [`samples/MinimalGame/`](../samples/MinimalGame/). Copy it to start a game.

## Repository shape

```text
my-game/
  src/
    MyGame.Game/
      MyGame.Game.csproj
      Assets/
        Scenes/
        Sprites/
        Textures/
        Audio/
        Fonts/
    MyGame.Shell/
      MyGame.Shell.csproj
  tests/
    MyGame.Tests/
      MyGame.Tests.csproj
  Directory.Build.props
  Directory.Build.targets
  MyGame.slnx
```

The directory convention inside `src/MyGame.Game/` is in [`project-layout.md`](project-layout.md); this file stops at the project boundary.

The authoring tree lives inside the logic project: Capsule looks for authored sources at `<project>/Assets` by default, and the logic role is the one that reads them. The build derives `assets/` beside the executable, which the shell receives through its project reference.

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

The logic role activates source generation and purity analysis, owns the authoring tree under `Assets/`, compiles the game's sprite sheets, fonts and audio, and imports and ships the game's scene documents, textures, audio and font pages:

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

A shell is one host family: the desktop shell publishes for Windows, Linux and macOS from one project by runtime identifier, and a platform carrying its own host module takes its own shell project.

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

The shell role generates `CapsuleBoot` and supplies default application icons; it reads no authoring sources of its own, and the logic project's derived scene documents and shipped assets reach its output and its publish through the project reference. Its entry point is the whole of the shell's hand-written code:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

A role-free project that needs derived content — a test project, a headless smoke binary — can opt into `<CapsuleImportScenes>`, `<CapsuleShipAssets>` and `<CapsuleImportAudio>` independently.

## Package and source modes

Commit each package-consuming project's `packages.lock.json` and restore CI with `--locked-mode`; a source build resolves the engine through project references, so a source-mode restore runs without it.

An ignored `Directory.Build.local.props` sets a persistent source override, and the source-mode lock file lands under `obj/` so the committed one is untouched:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

A source build compiles the engine clone's own projects in `Release` whatever configuration the game builds in; `CapsuleSourceConfiguration` overrides that.

### The API reference

Capsule's XML comments are its API reference. A package consumer reads them beside the assemblies at `%USERPROFILE%\.nuget\packages\jag.capsule\<version>\lib\net10.0\`. A source build stages the same files at `artifacts/capsule-api/` under the repository root, written before each project compiles so the reference is current even on a build that fails against a changed engine API. `CapsuleApiReferenceDirectory` stages them elsewhere; the directory is derived and build-owned.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`; pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

The publish directory carries the host's native libraries beside the executable, one for the window and input and one for sound — `SDL2.dll` and `openal.dll` on Windows. The window's is required; sound is not, so a machine with no audio library or output device plays the run silently and says so once on the log. The rule the publish gates is in [`architecture.md`](architecture.md#nativeaot-floor).

## Development-only directories

A directory holding a file named `.capsuleignore` is development-only. Everything under it, recursively, is part of every ordinary build — Debug and Release alike — and part of no publish. Input drivers are the case it exists for: they live in the logic project because they read the scene, and they must not ship.

```text
src/MyGame.Game/
  Drivers/
    .capsuleignore
    Walkthrough.cs
```

The marker means the same thing in both planes. Sources under a marked directory leave the compile before the generators read it, so a shipped build's scene, entity and driver registries hold nothing declared there. Authoring sources under a marked directory leave the asset plane, so nothing under it reaches `assets/`, is loaded, or is declared in `CapsuleAssets` — shipped code naming a development-only asset therefore fails to compile in a publish. A directory is marked by where it is rather than by how a project spelled the path; the marker file's contents are not read.

`CapsuleShipping` is the switch, and a publish sets it. Set it on an ordinary build to see exactly what a publish will hold without running one.

## Named assets

Assets are authored under `Assets/<Domain>/` in the logic project and ship under `assets/<domain>/` at their key.

A key is the authored path below the domain root, forward slashes and no extension, with every directory segment and the file stem normalized to the kebab form of the identifier it names. Capsule dictates no spelling below a domain root: `Enemies/Bat.png`, `enemies/bat.png` and `enemies/Bat.png` are one asset with one identifier `CapsuleAssets.Textures.Enemies.Bat`, one key `enemies/bat`, and one shipped path `assets/textures/enemies/bat.png`. `Stage1`, `stage1` and `stage-1` are likewise one segment, `stage-1`. Every key a game or an authoring module hands the build is normalized this way — a document's key, a scene or sheet handle, an asset's path — so the runtime only ever sees keys. A segment that is no C# identifier fails the build naming the file, and two sources that key the same fail it naming both.

A scene or sheet document names an asset by its key and extension, `"enemies/bat.png"`, spelled however the author likes.

Each generated domain and directory class exposes an allocation-free `All` span over the handles beneath it, such as `CapsuleAssets.Textures.Enemies.All`. Sprite sheets generate typed frames and clips under `CapsuleAssets.Sprites`; [`sprite-animation.md`](sprite-animation.md) defines that format. C# identifier collisions fail the build.

`Audio/` takes `.wav` and `.ogg` and generates `CapsuleAssets.Audio` clips, each carrying the duration the build measured from its source. Author short, repeated sounds as `.wav` and long ones — music, ambience — as `.ogg`; Capsule reads no MP3. A source Capsule cannot measure fails the build naming the file and what is wrong with it.

A loop region is authored either inside the audio file, which the build reads — a WAV's first `smpl` sample loop, or an Ogg Vorbis file's `LOOPSTART` and `LOOPLENGTH` comments in samples — or in game code on `AudioClip.LoopRegion`. `LOOPSTART` with `LOOPEND` is read the same way, and `LOOPSTART` alone loops the rest of the file. A file tagging neither has no region. A region has to fit its clip: the build fails a file whose region does not, naming the file and the samples it claimed.

Which assets a scene preloads, and when they load and are released, is documented on `Scene.CollectAssets` and `AssetCollection`; the cross-cutting rule is in [`architecture.md`](architecture.md#rendering-and-media).

## Seeing your game's output

Game logic cannot reach `System.Console`, so it writes through `Capsule.Diagnostics.Log`. The shell's console sink writes every level to standard output in order, each line prefixed with the simulation tick:

```text
[   boot] info  main menu started
[     30] warn  no spawn point on this map
```

`WithLogSink(sink)` replaces the host sink and `WithoutLogging()` silences it; a headless test installs `CollectingLogSink` and asserts its entries.

## Controllers

All SDL controller backends enumerate at boot; on Windows, set `SDL_DIRECTINPUT_ENABLED=0` to isolate DirectInput's startup cost.

## Build configuration reference

Capsule is configured with ordinary MSBuild properties. Put a value in the narrowest project that owns it. Paths may be absolute or relative to the project whose build imports Capsule unless a row says otherwise.

### Project roles

| Property           | Value  | Effect                                                                                                                                        |
| ------------------ | ------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleGameLogic` | `true` | Enables game-boundary analysis, generates the game's scene, entity, and asset registries, compiles its sprite sheets, and defaults scene import and asset shipping on. Set it only on the substrate-free logic library. |
| `CapsuleGameShell` | `true` | Generates `CapsuleBoot` and supplies default application icons. Reads no authoring sources. Set it only on the executable shell.               |

### Authoring sources and output

| Property                 | Default                                       | Effect                                                                                                                                                                 |
| ------------------------ | --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleAssetSourcesDir` | `Assets` under the importing project          | Locates the authored `Scenes/`, `Sprites/`, `Textures/`, `Audio/`, and `Fonts/` trees. An explicitly named directory must exist. `Sprites/` is read by the compiler in the logic role and carries no property of its own. |
| `CapsuleImportScenes`    | `true` for the logic library; otherwise `false` | Validates and canonically re-emits `*.scene.json` sources, then ships them under `assets/scenes/`.          A role-free test or tool can opt in independently. |
| `CapsuleShipAssets`      | `true` for the logic library; otherwise `false` | Ships admitted textures, audio, and font pages under `assets/`. A role-free test or tool can opt in independently.                                                   |
| `CapsuleImportAudio`     | `true` for the logic library; otherwise `false` | Measures every `Audio/` source and compiles it into `CapsuleAssets.Audio`. Nothing ships from here; a role-free project that has to name a clip opts in independently.        |
| `CapsuleTileSize`        | unset                                         | Requires every imported tile map to use this positive pixel size. Set it on the logic project when the game has one global tile size.                                  |
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

Two traps in the alpha byte: a bitmap whose alpha is entirely zero reads as fully opaque rather than fully invisible, and `BI_RGB` formally declares the byte unused, so most viewers discard it and draw the file on black. A transparent `Icon.bmp` therefore looks black-backed in almost any viewer; flattening it to agree puts a real background back on the window.

### Package and source properties

| Property                       | Default                                           | Effect                                                                                                                                                                                        |
| ------------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleVersion`               | consumer-defined                                  | Pins `JAG.Capsule`, `JAG.Capsule.Runtime`, and `JAG.Capsule.Build` to one release.                                                                                                            |
| `CapsuleSourcePath`            | unset                                             | Points at an engine clone. The standard wiring resolves it relative to the `Directory.Build.props` that declares `CapsuleSourceRoot`, not the command's working directory.                    |
| `CapsuleUsePackages`           | `false`                                           | Set to `true` to ignore a source override and verify the pinned NuGet graph.                                                                                                                  |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where a source build stages Capsule's XML documentation. A relative path is resolved against the repository root. Read only in source mode; a package consumer reads the NuGet cache instead. |
| `CapsuleSourceConfiguration`   | `Release`                                         | The configuration the engine clone's own projects build in under a source consumer, whatever the game builds. Set it to `Debug` to step into engine source.                                   |
