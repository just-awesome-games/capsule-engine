# Build and publish

After this page you can wire a Capsule game from empty directories, run and test it from the command
line, build it against an engine clone or against the packages, and publish it. Each property's row and
each type's XML docs hold its rules.

## Repository shape

```text
my-game/
  src/
    MyGame.Build/
      MyGame.Build.csproj
      Program.cs
    MyGame.Game/
      MyGame.Game.csproj
      Assets/
    MyGame.Shell/
      MyGame.Shell.csproj
  tests/
    MyGame.Tests/
      MyGame.Tests.csproj
  hooks/
    pre-commit
  MyGame.slnx
```

The authoring tree lives inside the logic project, in whatever folders the game chooses
([`assets.md`](assets.md#named-assets)). The build derives the same tree as `assets/` beside the
executable, and the shell receives it through its project reference.

Inside the logic project, one folder vocabulary: `Scenes/`, `Entities/`, `Components/`, `Cameras/`, `UI/`,
`Drivers/`, with `Assets/` beside them holding no code. A concept gets its folder as soon as it has one
file. Folders map to namespaces, and a namespace is the key a document names a type by
([`scenes.md`](scenes.md#entries-and-composition)). The assembly root holds the game's declarations:
collision layers, input actions, audio buses, save keys, world units.

Create the [three projects and the tests](getting-started.md#three-projects) from the repository root,
then replace each generated project file's body with the wiring below:

```text
dotnet new sln --name MyGame --format slnx
dotnet new classlib -o src/MyGame.Game
dotnet new console -o src/MyGame.Shell
dotnet new console -o src/MyGame.Build
dotnet new xunit -o tests/MyGame.Tests
dotnet sln MyGame.slnx add src/MyGame.Game src/MyGame.Shell src/MyGame.Build tests/MyGame.Tests
```

## Consuming Capsule

Capsule is a set of ordinary NuGet packages ([`PACKAGE.md`](../PACKAGE.md)). Each project references the
one package named for its role:

| Project | References |
| --- | --- |
| Logic | `JAG.Capsule`, the API the game is written against and the build targets that run its asset build. |
| Shell | `JAG.Capsule.Runtime.Desktop`, which brings the neutral host and the graphics substrate. |
| Build | `JAG.Capsule.Build`, the build it runs, and any importer package, such as `JAG.Capsule.Tiled`. |

Build-time code reaches only the build project, and none of it ships with the game. The logic and shell
projects also state their role, which the build and the generators read:

```xml
<!-- src/MyGame.Game/MyGame.Game.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <CapsuleGameLogic>true</CapsuleGameLogic>
    <CapsuleBuildProject>../MyGame.Build/MyGame.Build.csproj</CapsuleBuildProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="JAG.Capsule" Version="[{YOUR_PINNED_VERSION}]" />
  </ItemGroup>
</Project>

<!-- src/MyGame.Shell/MyGame.Shell.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MyGame</AssemblyName>
    <CapsuleGameShell>true</CapsuleGameShell>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="JAG.Capsule.Runtime.Desktop" Version="[{YOUR_PINNED_VERSION}]" />
    <ProjectReference Include="../MyGame.Game/MyGame.Game.csproj" />
  </ItemGroup>
</Project>

<!-- src/MyGame.Build/MyGame.Build.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="JAG.Capsule.Build" Version="[{YOUR_PINNED_VERSION}]" />
  </ItemGroup>
</Project>
```

The build project's `Program.cs` runs the asset build:

```csharp
return Capsule.Build.CapsuleBuild.Configure(args).Run();
```

Build-wide settings and importers are [`packages.md`](packages.md#the-build-project). A shell is
a console app, and a Windows-subsystem app in Release, where a double-click opens no console. A test
driving `EngineBuilder.RunHeadless` also references `JAG.Capsule.Runtime.Desktop`.

To build against clones instead, one git-ignored `Directory.Build.local.props` names them and imports the
engine clone's `build/Capsule.Build.props`:

```xml
<!-- Directory.Build.local.props -->
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
    <CapsuleSourceOverrides>JAG.Capsule.Tiled=../capsule-engine-tiled</CapsuleSourceOverrides>
  </PropertyGroup>
  <Import Project="$(CapsuleSourcePath)/build/Capsule.Build.props" Condition="'$(CapsuleSourcePath)' != ''" />
</Project>
```

The committed `Directory.Build.props` imports it the conventional way, and a game that never builds from
clones needs neither file:

```xml
<Import Project="$(MSBuildThisFileDirectory)Directory.Build.local.props" Condition="Exists('$(MSBuildThisFileDirectory)Directory.Build.local.props')" />
```

Every reference to an engine package then becomes its project in the engine clone. An override's clone
supplies `build/<PackageId>.targets` in place of its package, and needs the engine clone too.
`-p:CapsuleSourcePath=` builds from the packages whatever the local file says.

Commit each project's `packages.lock.json` and restore CI with `--locked-mode`. A build from clones keeps its
own lock file under `obj/`.

Capsule's XML comments are its API reference. Every build stages them at `artifacts/capsule-api/` before
each project compiles, in either mode, with the JSON Schema of every authored format under `schemas/`. The
staged reference stays current when a build fails against a changed engine API.

## Commands

Capsule has no editor and no CLI. `dotnet` is the command surface, and any C# IDE runs and tests a game
through its ordinary affordances.

| Task | Command |
| --- | --- |
| Run | `dotnet run --project src/MyGame.Shell`. Restore a fresh clone first with `dotnet restore --locked-mode`. |
| Run a named scene | `dotnet run --project src/MyGame.Shell -- --scene <Name>`, a scene class name or a scene document key. |
| Run headless with a driver | `dotnet run --project src/MyGame.Shell -- --headless --driver <Name>` ([`input.md`](input.md#the-standard-command-line)). |
| Test | `dotnet test`. |
| Format check or fix | `dotnet format --verify-no-changes`, `dotnet format`. |
| The gate | `sh hooks/pre-commit`: restore, build, format check, tests. |
| Ship | The `dotnet publish` line under [Publishing](#publishing). |

The gate is also the commit hook, set once per clone with `git config core.hooksPath hooks`. Until it is
set, Git ignores `hooks/` and a commit passes with no report.

## Development builds

`CapsuleShipping` is the axis. A publish sets it to `true`, an ordinary build defaults it to `false`, and
setting it by hand verifies what a publish will hold. It moves three things together: the runtime feature switch
`Capsule.Development` to `false`, the compile symbol `CAPSULE_DEVELOPMENT` to undefined, and every
development-only directory out of the compile and the asset plane. A trimmed publish drops the
engine's development code, and an untrimmed one carries it disabled. `Capsule.Diagnostics.Development`
documents how a game's own code gates on the axis.

### Development-only directories

A directory holding a file named `.capsuleignore` is development-only: everything under it, recursively, is
part of every ordinary build and of no publish. Input drivers live in one.

```text
src/MyGame.Game/
  Drivers/
    .capsuleignore
    Walkthrough.cs
```

In a publish, sources under a marked directory leave the compile before the generators read them. Its
authoring sources and imported outputs leave the asset plane, and shipped code naming a development-only
asset fails to compile. The marker file's contents are not read.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`. Pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

The publish directory carries the host's native libraries beside the executable: on Windows, `SDL2.dll` for
the window and input, `openal.dll` for sound. Without the sound library or an output device the run plays
silently and logs that once.

On Windows the native link needs the MSVC Build Tools with the C++ workload, and a Build Tools-only install
also needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on `PATH`.

A publish holds no `.pdb`. A NativeAOT publish defaults `StackTraceSupport` to `false` and
`UseSystemResourceKeys` to `true`, which a project can set back. Its symbols go to
`CapsuleSymbolsDirectory`. A crash log's frames then read `MyGame!<BaseAddress>+0x296cd`. To decode one,
copy the shipped executable into the symbols directory and run
`llvm-symbolizer --relative-address --obj=<symbols>/MyGame.exe 0x296cd`. The MSVC Build Tools ship
`llvm-symbolizer` under `VC/Tools/MSVC/<version>/bin/Hostx64/x64`.

## Build properties

Ordinary MSBuild properties, each set in the narrowest project that owns it. Paths are absolute or relative
to the importing project unless a row says otherwise.

### Project roles

| Property | Value | Effect |
| --- | --- | --- |
| `CapsuleGameLogic` | `true` | Enables game-boundary analysis, generates the scene, entity and asset registries, and turns `CapsuleBuildAssets` on. Set it on the substrate-free logic library. |
| `CapsuleGameShell` | `true` | Generates `CapsuleBoot` and supplies default application icons. Reads no authoring sources. Set it on the executable shell. |

### Authoring sources and output

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleAssetSourcesDir` | `Assets` under the importing project | Locates the authoring tree. A named directory must exist. |
| `CapsuleBuildAssets` | `true` for the logic library, else `false` | Reads the authoring tree and ships the result under `assets/`. A role-free test or tool can opt in, and receives no `CapsuleAssets`. |
| `CapsuleBuildProject` | unset | The game's build project, which a project building assets must name. |
| `CapsuleShipping` | `true` for the duration of a publish | Switches the build to shipping shape. See [Development builds](#development-builds). |
| `CapsuleSymbolsDirectory` | the publish directory's path with `-symbols` appended | Receives a publish's symbols: the native pdb under NativeAOT, the managed pdbs otherwise. |

### Package and source properties

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleSourcePath` | unset | Points at an engine clone, relative to `Directory.Build.local.props`. |
| `CapsuleSourceOverrides` | unset | Replaces Capsule-family packages with clones, as `<PackageId>=<path>` separated by `;`, each path relative to the repository root. |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` | Where the build stages Capsule's XML documentation and schemas. A relative path resolves against the repository root, the directory holding `Directory.Build.targets`, else `Directory.Build.props`, else the project's own directory. |
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
whose alpha is all zero reads as fully opaque.
