# Build and publish

After this page you can wire a Capsule game from empty directories, run and test it from the command
line, build it against an engine clone or against the packages, and publish it.

## Packages

A game references `JAG.Capsule` from its logic project, `JAG.Capsule.Runtime.Desktop` from its shell, and
`JAG.Capsule.Build` from every project. The desktop package brings the neutral host and the graphics
substrate. [`PACKAGE.md`](../PACKAGE.md) lists what ships in each.

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

The authoring tree lives inside the logic project, the role that reads it. The build derives `assets/`
beside the executable, and the shell receives it through its project reference.

Inside the logic project, one folder vocabulary: `Scenes/`, `Entities/`, `Components/`, `Cameras/`, `UI/`,
`Drivers/`, with `Assets/` beside them holding no code. A concept gets its folder as soon as it has one
file. Folders map to namespaces, and a namespace is the registry key a document names a type by
([`scenes.md`](scenes.md#entries-and-composition)). The assembly root holds the game's declarations:
collision layers, input actions, audio buses, save keys, world units.

Create the solution and three projects from the repository root, then replace each generated project
file's body with the wiring below:

```text
dotnet new sln --name MyGame --format slnx
dotnet new classlib -o src/MyGame.Game
dotnet new console -o src/MyGame.Shell
dotnet new xunit -o tests/MyGame.Tests
dotnet sln MyGame.slnx add src/MyGame.Game src/MyGame.Shell tests/MyGame.Tests
```

## Shared configuration

Pin one Capsule version and give every project the build package:

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

The logic role turns on source generation, purity analysis, the authoring tree, and asset shipping:

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

Tests reference the logic project and take no role. A test driving `CapsuleEngine.RunHeadless` also
references the platform module, switched between package and source like the shell's:

```xml
<ItemGroup>
  <ProjectReference Include="../../src/MyGame.Game/MyGame.Game.csproj" />
</ItemGroup>
<ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
  <PackageReference Include="JAG.Capsule.Runtime.Desktop" Version="[$(CapsuleVersion)]" />
</ItemGroup>
<ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
  <ProjectReference Include="$(CapsuleSourceRoot)/src/Capsule.Runtime.Desktop/Capsule.Runtime.Desktop.csproj" />
</ItemGroup>
```

## Shell project

A shell is one host family and references that family's platform module:

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
    <PackageReference Include="JAG.Capsule.Runtime.Desktop" Version="[$(CapsuleVersion)]" />
  </ItemGroup>
  <ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
    <ProjectReference Include="$(CapsuleSourceRoot)/src/Capsule.Runtime.Desktop/Capsule.Runtime.Desktop.csproj" />
  </ItemGroup>
</Project>
```

The shell's entry point is all of its hand-written code
([`getting-started.md`](getting-started.md#the-shell)).

## Commands

Capsule has no editor and no CLI. `dotnet` is the command surface, and any C# IDE runs and tests a game
through its ordinary affordances.

| Task | Command |
| --- | --- |
| Clone and first run | `dotnet restore --locked-mode`, then `dotnet run --project src/MyGame.Shell`. |
| Run | `dotnet run --project src/MyGame.Shell`. Add `--no-build` to start the last build as it stands. |
| Run a named scene | `dotnet run --project src/MyGame.Shell -- --scene <Name>`. |
| Run headless with a driver | `dotnet run --project src/MyGame.Shell -- --headless --driver <Name>` ([`input.md`](input.md)). |
| Test | `dotnet test`. |
| Format check or fix | `dotnet format --verify-no-changes`, `dotnet format`. |
| The gate | `sh hooks/pre-commit`: restore, build, format check, tests. |
| Source or package mode | Set or remove `CapsuleSourcePath`. `CapsuleUsePackages=true` forces the package lane. |
| Ship | The `dotnet publish` line under [Publishing](#publishing). |
| Debug | The development overlay in the window, and the console the game was launched from ([`debugging.md`](debugging.md)). |

The gate is also the commit hook, set once per clone with `git config core.hooksPath hooks`. Until it is
set, Git ignores `hooks/` and a commit passes with no report.

Game logic cannot reach `System.Console` and writes through `Capsule.Diagnostics.Log`
([`debugging.md`](debugging.md)). A headless test installs `CollectingLogSink`. On Windows,
`SDL_DIRECTINPUT_ENABLED=0` in the environment isolates DirectInput's controller-enumeration cost at
boot.

## Package and source modes

Commit each package-consuming project's `packages.lock.json` and restore CI with `--locked-mode`. A source
build resolves the engine through project references, and its lock file lands under `obj/`. An ignored
`Directory.Build.local.props` sets a persistent source override:

```xml
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

Capsule's XML comments are its API reference. A package consumer reads them in the NuGet cache
(`jag.capsule/<version>/lib/net10.0/`). A source build stages them at `artifacts/capsule-api/` before each
project compiles, so the reference stays current when a build fails against a changed engine API.

## Development builds

`CapsuleShipping` is the axis. A publish sets it to `true`, an ordinary build defaults it to `false`, and
setting it by hand verifies what a publish will hold. It moves three things together: the runtime switch
`Capsule.Development` to `false`, the compile symbol `CAPSULE_DEVELOPMENT` to undefined, and every
development-only directory out of the compile and the asset plane. A trimmed publish drops the engine's
development code and an untrimmed one carries it disabled. `Capsule.Diagnostics.Development` is the contract
between them, and [`debugging.md`](debugging.md) covers what the development code is for.

### Development-only directories

A directory holding a file named `.capsuleignore` is development-only: everything under it, recursively, is
part of every ordinary build and of no publish. Input drivers live in one.

```text
src/MyGame.Game/
  Drivers/
    .capsuleignore
    Walkthrough.cs
```

Sources under a marked directory leave the compile before the generators read it, so a shipped build's
registries hold nothing declared there. Authoring sources leave the asset plane, and shipped code naming a
development-only asset fails to compile in a publish. Marking follows the directory, not the path a project
spelled, and the marker file's contents are not read.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`. Pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

The publish directory carries the host's native libraries beside the executable: on Windows, `SDL2.dll` for
the window and input, `openal.dll` for sound. The window's library is required. Without the sound library or
an output device the run plays silently and logs that once. Sound follows the operating system's default
output as it moves. The publish gates the [NativeAOT floor](architecture.md#nativeaot-floor).

## Build properties

Ordinary MSBuild properties, each set in the narrowest project that owns it. Paths are absolute or relative
to the importing project unless a row says otherwise.

### Project roles

| Property | Value | Effect |
| --- | --- | --- |
| `CapsuleGameLogic` | `true` | Enables game-boundary analysis, generates the scene, entity and asset registries, compiles sprite sheets, and defaults scene import and asset shipping on. Set it on the substrate-free logic library. |
| `CapsuleGameShell` | `true` | Generates `CapsuleBoot` and supplies default application icons. Reads no authoring sources. Set it on the executable shell. |

### Authoring sources and output

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleAssetSourcesDir` | `Assets` under the importing project | Locates the authored `Scenes/`, `Sprites/`, `Textures/`, `Atlases/`, `Audio/` and `Fonts/` trees. A named directory must exist. |
| `CapsuleImportScenes` | `true` for the logic library, else `false` | Validates and canonically re-emits `*.scene.json` sources, then ships them under `assets/scenes/`. A role-free test or tool can opt in. |
| `CapsuleShipAssets` | `true` for the logic library, else `false` | Ships admitted textures, audio and font pages under `assets/`. |
| `CapsuleImportAudio` | `true` for the logic library, else `false` | Measures every `Audio/` source and compiles it into `CapsuleAssets.Audio`. Nothing ships from here. |
| `CapsuleTileSize` | unset | Requires every imported tile map to use this positive pixel size. Set it on the logic project when the game has one global tile size. |
| `CapsuleShipping` | `true` for the duration of a publish | Switches the build to shipping shape. See [Development builds](#development-builds). |

### Package and source properties

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleVersion` | consumer-defined | Pins `JAG.Capsule`, `JAG.Capsule.Runtime.Desktop` and `JAG.Capsule.Build` to one release. |
| `CapsuleSourcePath` | unset | Points at an engine clone. The standard wiring resolves it against the `Directory.Build.props` that declares `CapsuleSourceRoot`, not the working directory. |
| `CapsuleUsePackages` | `false` | `true` ignores a source override and verifies the pinned NuGet graph. |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where a source build stages Capsule's XML documentation. A relative path resolves against the repository root. Source mode only. |
| `CapsuleSubstratePackage` | `MonoGame.Framework.DesktopGL` | The substrate package `Capsule.Runtime` compiles against, with `CapsuleSubstrateVersion` (default `3.8.5.1`). Source mode only, for a private platform module retargeting the host. |
| `CapsuleSourceConfiguration` | `Release` | The configuration an engine clone's own projects build in, whatever the game builds. Set `Debug` to step into engine source. |

### Application icons

A shell with no icon configuration receives Capsule's executable and window icons. Override either or both
beside the shell project:

| Input | Effect |
| --- | --- |
| `Icon.ico` | Becomes the executable icon through the .NET `ApplicationIcon` property. |
| `Icon.bmp` | Becomes the window and taskbar icon: a 128x128, 32-bit uncompressed BMP, alpha honoured. |
| `ApplicationIcon` | Overrides the executable icon with any path the .NET SDK accepts. |
| `EmbeddedResource` with `LogicalName="Icon.bmp"` | Overrides the window icon when the bitmap sits elsewhere. |

```xml
<PropertyGroup>
  <ApplicationIcon>branding/MyGame.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <EmbeddedResource Include="branding/MyGame.bmp" LogicalName="Icon.bmp" />
</ItemGroup>
```

Defining one half is allowed, and the build warns that the other half keeps Capsule branding. An `Icon.bmp`
whose alpha is all zero reads as fully opaque. Most viewers draw a `BI_RGB` alpha bitmap on black, so a
transparent icon looks black-backed outside the window.

## A private platform module

A console runtime is a private repository, one per platform, holding whatever its NDA covers, and not a
branch of the engine:

1. Subclass `HostPlatform` there. `OpenContent` and `OpenSaveStorage` are its abstract members, and every
   window and audio member has a no-window default to override where the platform has a policy.
2. Put the game's shell for that host family in the same repository, referencing the module and passing it
   to `CapsuleBoot.Configure`. The logic project is untouched.
3. Consume Capsule in source mode at a pinned tag (`CapsuleSourcePath`) and swap the substrate with
   `CapsuleSubstratePackage` and `CapsuleSubstrateVersion`. An unmodified checkout retargets.

Isolation at publish is the reference graph. A shell references one platform module, so a desktop assembly
is absent from a console publish and not trimmed out of it. The engine's CI publishes and runs the desktop
path, and a private module is tested by its own repository
([`architecture.md`](architecture.md#platforms)).
