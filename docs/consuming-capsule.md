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
        Atlases/
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

The convention inside `src/MyGame.Game/` is [`project-layout.md`](project-layout.md). The authoring tree lives inside the logic project, which is the role that reads it; the build derives `assets/` beside the executable, which the shell receives through its project reference.

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

return CapsuleBoot.Configure("My Game")
    .WithCommandLine(args)
    .WithInput(GameInput.Configure)
    .RunScene<MainMenu>();
```

`WithInput` installs the game's action bindings; a game that omits it reads every action unbound.

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

Each generated domain and directory class exposes an allocation-free `All` span over the handles beneath it. Sprite sheets generate typed frames and clips under `CapsuleAssets.Sprites` ([`sprite-animation.md`](sprite-animation.md)); `Atlases/` packs textures onto shared pages without changing any handle or region ([`atlases.md`](atlases.md)); fonts compile from `Fonts/` ([`text.md`](text.md)); `Audio/` takes `.wav` and `.ogg` — no MP3 — into `CapsuleAssets.Audio` clips carrying the duration the build measured and the loop region it read (`AudioClip` documents both, and how the host plays each format), and a source it cannot measure or whose region does not fit fails the build naming the file. What a scene preloads and when it is released is on `Scene.CollectAssets` and `AssetCollection`.

## Seeing your game's output

Game logic cannot reach `System.Console`, so it writes through `Capsule.Diagnostics.Log`; the shell's console sink, `EngineBuilder.WithLogSink` and `WithoutLogging` document where it lands, and a headless test installs `CollectingLogSink`. On Windows, `SDL_DIRECTINPUT_ENABLED=0` in the environment isolates DirectInput's controller-enumeration cost at boot.

## Commands

Capsule has no editor and no CLI of its own: `dotnet` is the whole command surface, and any C# IDE runs and tests a game through its ordinary affordances. Every command below is the editor-free path from a game's repository root.

| Task | Command |
| --- | --- |
| Clone and first run | `dotnet restore --locked-mode`, then `dotnet run --project src/MyGame.Shell`. |
| Run | `dotnet run --project src/MyGame.Shell`; `--no-build` starts the last build as it stands. |
| Run a named scene | `dotnet run --project src/MyGame.Shell -- --scene <Name>`. |
| Run headless with a driver | `dotnet run --project src/MyGame.Shell -- --headless --driver <Name>` ([`headless-play.md`](headless-play.md)). |
| Test | `dotnet test`. |
| Format check / fix | `dotnet format --verify-no-changes` / `dotnet format`. |
| The gate | `sh hooks/pre-commit`: restore, build, format check, tests. |
| Source or package mode | Set or remove `CapsuleSourcePath`; `CapsuleUsePackages=true` forces the package lane ([Package and source modes](#package-and-source-modes)). |
| Ship | The `dotnet publish` line under [Publishing](#publishing). |
| Debug | The development overlay in the window, and the console the game was launched from ([`debugging.md`](debugging.md)). |

The gate is also the commit hook, once per clone: `git config core.hooksPath hooks`. Git ignores `hooks/` until it is set, and an unconfigured clone commits straight past it without reporting anything.

Designer-owned tunables are plain C# — the sample's `src/MinimalGame.Game/Entities/PlayerTuning.cs` is the pattern — and the engine version is pinned once, in `Directory.Build.props`.

## Build properties

Every `Capsule*` MSBuild property, with its default and effect, is [`build-properties.md`](build-properties.md).
