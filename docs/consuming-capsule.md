# Consuming Capsule

Capsule games use two projects: a substrate-free logic library and a small executable shell. This file is the MSBuild wiring and the build's contracts; [`samples/MinimalGame/`](../samples/MinimalGame/) is a complete game in this shape, to copy.

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
  hooks/
    pre-commit
  Directory.Build.props
  Directory.Build.targets
  MyGame.slnx
```

The convention inside `src/MyGame.Game/` is [`project-layout.md`](project-layout.md); the commands over this shape are [`workflow.md`](workflow.md). The authoring tree lives inside the logic project, which is the role that reads it; the build derives `assets/` beside the executable, which the shell receives through its project reference.

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

The logic role — source generation, purity analysis, the authoring tree, and shipping its assets:

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

Tests reference the logic project and take no role; one that drives `CapsuleEngine.RunHeadless` also references the runtime, switched between package and source as the shell's is:

```xml
<ItemGroup>
  <ProjectReference Include="../../src/MyGame.Game/MyGame.Game.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
  <PackageReference Include="JAG.Capsule.Runtime" Version="[$(CapsuleVersion)]" />
</ItemGroup>
<ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
  <ProjectReference Include="$(CapsuleSourceRoot)/src/Capsule.Runtime/Capsule.Runtime.csproj" />
</ItemGroup>
```

## Shell project

A shell is one host family: the desktop shell publishes for Windows, Linux and macOS from one project by runtime identifier.

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <!-- Release ships as a Windows-subsystem app, so a double-click opens no console; Debug
         keeps the console for the log sink. -->
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

The shell role generates `CapsuleBoot` and supplies default application icons; its entry point is the whole of its hand-written code:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

return CapsuleBoot.Configure("My Game").WithCommandLine(args).RunScene<MainMenu>();
```

A role-free project that needs derived content opts into `<CapsuleImportScenes>`, `<CapsuleShipAssets>` and `<CapsuleImportAudio>` independently.

## Package and source modes

Commit each package-consuming project's `packages.lock.json` and restore CI with `--locked-mode`; a source build resolves the engine through project references and its lock file lands under `obj/`, so the committed one is untouched. An ignored `Directory.Build.local.props` sets a persistent source override:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

### The API reference

Capsule's XML comments are its API reference. A package consumer reads them beside the assemblies in the NuGet cache (`jag.capsule/<version>/lib/net10.0/`); a source build stages them at `artifacts/capsule-api/` under the repository root before each project compiles, so the reference is current even on a build that fails against a changed engine API.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`; pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

The publish directory carries the host's native libraries beside the executable — `SDL2.dll` for the window and input, `openal.dll` for sound, on Windows. The window's is required; without the sound library or an output device the run plays silently and says so once on the log, and sound follows the operating system's default output as it moves. What the publish gates is the [NativeAOT floor](architecture.md#nativeaot-floor).

## Development builds

`CapsuleShipping` is the one axis: a publish sets it to `true`, an ordinary build — Debug or Release — defaults it to `false`, and setting it on an ordinary build verifies what a publish will hold. It moves three things together — the runtime switch `Capsule.Development` to `false`, so a trimmed publish removes the engine's development code and an untrimmed one carries it disabled; the compile symbol `CAPSULE_DEVELOPMENT` to undefined, where every other build defines it; and every development-only directory out of the compile and the asset plane. `Capsule.Diagnostics.Development` is the contract between them; [`debugging.md`](debugging.md) is what the plane is for.

### Development-only directories

A directory holding a file named `.capsuleignore` is development-only: everything under it, recursively, is part of every ordinary build and of no publish. Input drivers live in one.

```text
src/MyGame.Game/
  Drivers/
    .capsuleignore
    Walkthrough.cs
```

Sources under a marked directory leave the compile before the generators read it, so a shipped build's registries hold nothing declared there; authoring sources under one leave the asset plane, so nothing there reaches `assets/` or `CapsuleAssets`, and shipped code naming a development-only asset fails to compile in a publish. A directory is marked by where it is, not by how a project spelled the path; the marker's contents are not read.

## Named assets

Assets are authored under `Assets/<Domain>/` in the logic project and ship under `assets/<domain>/` at their key.

A key is the authored path below the domain root, forward slashes and no extension, every directory segment and the file stem normalized to the kebab form of the identifier it names: `Enemies/Bat.png`, `enemies/bat.png` and `enemies/Bat.png` are one asset with one identifier `CapsuleAssets.Textures.Enemies.Bat`, one key `enemies/bat`, and one shipped path `assets/textures/enemies/bat.png`; `Stage1`, `stage1` and `stage-1` are one segment, `stage-1`. Every key a game or an authoring module hands the build is normalized this way, so the runtime only ever sees keys; a document names an asset by key and extension, `"enemies/bat.png"`, spelt however the author likes. A segment that is no C# identifier, two sources keying the same, and C# identifier collisions fail the build naming the files.

Each generated domain and directory class exposes an allocation-free `All` span over the handles beneath it. Sprite sheets generate typed frames and clips under `CapsuleAssets.Sprites` ([`sprite-animation.md`](sprite-animation.md)); fonts compile from `Fonts/` ([`text.md`](text.md)); `Audio/` takes `.wav` and `.ogg` — no MP3 — into `CapsuleAssets.Audio` clips carrying the duration the build measured and the loop region it read (`AudioClip` documents both, and how the host plays each format), and a source it cannot measure or whose region does not fit fails the build naming the file. What a scene preloads and when it is released is on `Scene.CollectAssets` and `AssetCollection`.

## Seeing your game's output

Game logic cannot reach `System.Console`, so it writes through `Capsule.Diagnostics.Log`; the shell's console sink, `EngineBuilder.WithLogSink` and `WithoutLogging` document where it lands, and a headless test installs `CollectingLogSink`. On Windows, `SDL_DIRECTINPUT_ENABLED=0` in the environment isolates DirectInput's controller-enumeration cost at boot.

## Build configuration reference

Ordinary MSBuild properties, each in the narrowest project that owns it; paths are absolute or relative to the importing project unless a row says otherwise.

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
| `CapsuleShipping`        | `true` for the duration of a publish          | Excludes every `.capsuleignore` directory from the compile and from the asset plane, leaves `CAPSULE_DEVELOPMENT` undefined, and sets `Capsule.Development` to `false`; see [Development builds](#development-builds). |

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

Defining only one half is allowed; the build warns because the other half keeps Capsule branding. An `Icon.bmp` whose alpha is entirely zero reads as fully opaque, and most viewers draw a `BI_RGB` alpha bitmap on black, so a transparent one looks black-backed outside the window.

### Package and source properties

| Property                       | Default                                           | Effect                                                                                                                                                                                        |
| ------------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleVersion`               | consumer-defined                                  | Pins `JAG.Capsule`, `JAG.Capsule.Runtime`, and `JAG.Capsule.Build` to one release.                                                                                                            |
| `CapsuleSourcePath`            | unset                                             | Points at an engine clone. The standard wiring resolves it relative to the `Directory.Build.props` that declares `CapsuleSourceRoot`, not the command's working directory.                    |
| `CapsuleUsePackages`           | `false`                                           | Set to `true` to ignore a source override and verify the pinned NuGet graph.                                                                                                                  |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where a source build stages Capsule's XML documentation. A relative path is resolved against the repository root. Read only in source mode; a package consumer reads the NuGet cache instead. |
| `CapsuleSourceConfiguration`   | `Release`                                         | The configuration the engine clone's own projects build in under a source consumer, whatever the game builds. Set it to `Debug` to step into engine source.                                   |
