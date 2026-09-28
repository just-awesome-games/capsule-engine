# Build and publish

After this page you can wire a Capsule game from empty directories, run and test it from the command
line, build it against an engine clone or against the packages, and publish it.

## Repository shape

```text
my-game/
  src/
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

The authoring tree lives inside the logic project, the role that reads it, in whatever folders the game
chooses ([`assets.md`](assets.md#named-assets)). The build derives the same tree as `assets/` beside the
executable, and the shell receives it through its project reference.

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

## Consuming Capsule

Capsule is a set of ordinary NuGet packages. The logic project references `JAG.Capsule`, and the shell
references `JAG.Capsule.Runtime.Desktop`, which brings the neutral host and the graphics substrate
([`PACKAGE.md`](../PACKAGE.md)). `JAG.Capsule` brings the build with it. Each project also states its role,
which the build and the generators read:

```xml
<!-- src/MyGame.Game/MyGame.Game.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <CapsuleGameLogic>true</CapsuleGameLogic>
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
```

A shell is a console app, and a Windows-subsystem app in Release, where a double-click opens no console.
Tests reference the logic project and take no role. A test driving `CapsuleEngine.RunHeadless` also
references `JAG.Capsule.Runtime.Desktop`.

To build against clones instead, one git-ignored `Directory.Build.local.props` names them and imports the
engine clone's `build/Capsule.Build.props`. `CapsuleSourceOverrides` replaces other Capsule packages, as
`<PackageId>=<path>` separated by `;`. Both paths resolve against the repository root:

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

Every reference to an engine package then becomes its project in the engine clone, and an override's clone
supplies `build/<PackageId>.targets` in place of the package, before restore. An override needs the engine
clone. A path that holds no clone fails the build naming the file it expected. `-p:CapsuleSourcePath=`
builds from the packages whatever the local file says.

Commit each project's `packages.lock.json` and restore CI with `--locked-mode`. A build from clones keeps its
own lock file under `obj/`.

Capsule's XML comments are its API reference. Every build stages them at `artifacts/capsule-api/` before
each project compiles, from the engine clone or from the restored packages. The staged reference stays
current when a build fails against a changed engine API. The JSON Schema of every authored format is staged
the same way, at `artifacts/capsule-api/schemas/`.

## Commands

Capsule has no editor and no CLI. `dotnet` is the command surface, and any C# IDE runs and tests a game
through its ordinary affordances.

| Task | Command |
| --- | --- |
| Clone and first run | `dotnet restore --locked-mode`, then `dotnet run --project src/MyGame.Shell`. |
| Run | `dotnet run --project src/MyGame.Shell`. Add `--no-build` to start the last build as it stands. |
| Run a named scene | `dotnet run --project src/MyGame.Shell -- --scene <Name>`, a scene class name or a scene document key. |
| Run headless with a driver | `dotnet run --project src/MyGame.Shell -- --headless --driver <Name>` ([`input.md`](input.md)). |
| Test | `dotnet test`. |
| Format check or fix | `dotnet format --verify-no-changes`, `dotnet format`. |
| The gate | `sh hooks/pre-commit`: restore, build, format check, tests. |
| Source or package mode | Set or remove `CapsuleSourcePath`. `-p:CapsuleSourcePath=` forces the packages. |
| Ship | The `dotnet publish` line under [Publishing](#publishing). |
| Debug | The development overlay in the window, and the console the game was launched from ([`debugging.md`](debugging.md)). |

The gate is also the commit hook, set once per clone with `git config core.hooksPath hooks`. Until it is
set, Git ignores `hooks/` and a commit passes with no report.

Game logic cannot reach `System.Console` and writes through `Capsule.Diagnostics.Log`
([`debugging.md`](debugging.md)). On Windows,
`SDL_DIRECTINPUT_ENABLED=0` in the environment isolates DirectInput's controller-enumeration cost at
boot.

## Development builds

`CapsuleShipping` is the axis. A publish sets it to `true`, an ordinary build defaults it to `false`, and
setting it by hand verifies what a publish will hold. It moves three things together: the runtime switch
`Capsule.Development` to `false`, the compile symbol `CAPSULE_DEVELOPMENT` to undefined, and every
development-only directory out of the compile and the asset plane. A trimmed publish drops the engine's
development code, and an untrimmed one carries it disabled. `Capsule.Diagnostics.Development` documents how
a game's own code gates on the axis. The development code itself is [`debugging.md`](debugging.md).

### Development-only directories

A directory holding a file named `.capsuleignore` is development-only: everything under it, recursively, is
part of every ordinary build and of no publish. Input drivers live in one.

```text
src/MyGame.Game/
  Drivers/
    .capsuleignore
    Walkthrough.cs
```

Sources under a marked directory leave the compile before the generators read it. A shipped build's
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

On Windows the native link needs the MSVC Build Tools with the C++ workload. A Build Tools-only install
also needs `C:\Program Files (x86)\Microsoft Visual Studio\Installer` on `PATH`, or the link fails with
`'vswhere.exe' is not recognized`.

Scene documents ship compact and gzipped, and the texture map ships compact. A publish holds no `.pdb`. A NativeAOT publish defaults
`StackTraceSupport` to `false` and `UseSystemResourceKeys` to `true`, which a project can set back. Its
symbols go to `CapsuleSymbolsDirectory`, beside the publish directory. A crash log's frames then read
`MyGame!<BaseAddress>+0x296cd`. To decode one, copy the shipped executable into the symbols directory and
run `llvm-symbolizer --relative-address --obj=<symbols>/MyGame.exe 0x296cd`. The MSVC Build Tools ship
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
| `CapsuleBuildAssets` | `true` for the logic library, else `false` | Reads the authoring tree: derives scene documents, measures and compiles assets, and ships the result under `assets/`. A role-free test or tool can opt in, and receives no `CapsuleAssets`. |
| `CapsuleTileSize` | unset | Requires every imported tile map to use this positive pixel size. Set it on the logic project when the game has one global tile size. |
| `CapsuleShipping` | `true` for the duration of a publish | Switches the build to shipping shape. See [Development builds](#development-builds). |
| `CapsuleSymbolsDirectory` | the publish directory's path with `-symbols` appended | Receives a publish's symbols: the native pdb under NativeAOT, the managed pdbs otherwise. |

### Package and source properties

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleSourcePath` | unset | Points at an engine clone, relative to `Directory.Build.local.props`. |
| `CapsuleSourceOverrides` | unset | Replaces Capsule-family packages with clones, as `<PackageId>=<path>` separated by `;`, each path relative to the repository root. |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where the build stages Capsule's XML documentation and the JSON Schema of every authored format, in both modes. A relative path resolves against the repository root, the directory holding `Directory.Build.targets`, else `Directory.Build.props`, else the project's own directory. |
| `CapsuleSubstratePackage` | `MonoGame.Framework.DesktopGL` | The substrate package `Capsule.Runtime` compiles against, with `CapsuleSubstrateVersion` (default `3.8.5.1`). Source mode only, for a private platform module retargeting the host. |
| `CapsuleSourceConfiguration` | `Release` | The configuration an engine clone's own projects and every derivation's tool project build in, whatever the game builds. Set `Debug` to step into engine source. |

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
whose alpha is all zero reads as fully opaque. Most image viewers draw a `BI_RGB` alpha bitmap on black,
and a transparent icon looks black-backed there.

## Build derivations

A package joins a game's build only by declaring a derivation in its `buildTransitive` targets: a tool that
turns sources of its own format into files Capsule builds as though they were authored. Capsule runs the
tool as its own process and never loads a package's code into the build.
[`build/Capsule.Derivations.targets`](../build/Capsule.Derivations.targets) defines the declaration:

```xml
<ItemGroup>
  <CapsuleDerivation Include="Tiled"
                     Sources="**/*.tmj"
                     DependsOn="**/*.tsj"
                     OutputExtensions=".scene.json"
                     Properties="CapsuleTileSize=$(CapsuleTileSize)"
                     ToolProject="$(MSBuildThisFileDirectory)../src/JAG.Capsule.Tiled/JAG.Capsule.Tiled.csproj"
                     ToolAssembly="$(MSBuildThisFileDirectory)../tools/net10.0/any/JAG.Capsule.Tiled.dll" />
</ItemGroup>
```

`Include` names the derivation, as one path segment. `Sources` lists globs below the asset root, separated
by `;`, and each file they match is one source. `DependsOn` optionally lists globs below the asset root of
files the sources read. `Properties` optionally lists `Name=Value` pairs for the tool, separated by `;`. A
property named there reads its final value, whatever order the targets are imported in. `ToolProject` is the
tool's project in a clone, and it builds in the configuration of Capsule's own build tool. `ToolAssembly` is
the tool's assembly in the package. Capsule runs the project when it exists and the assembly otherwise.

A build that finds a source stale runs the tool once, from the project directory:

```text
<tool> <asset root> <out directory> <sources file> [<Name>=<Value>]...
```

`OutputExtensions` names one or more extensions separated by `;`, and a source derives one file for each.
A derivation declaring `.sheet.json;.png` derives `hero.sheet.json` and `hero.png` from `hero.aseprite`. A
source is stale when any of its outputs is missing or older than it. A changed dependency, property or
declaration makes every source stale. Two sources that derive one output, as `hero.aseprite` and `hero.psd`
do under `.png`, fail the build naming both. The check ignores case, the way asset keys do.

The sources file names one stale source per line, relative to the project directory, and each
`Name=Value` is one of `Properties`. The tool writes every output of each source under the out directory,
`obj/capsule/derived/<name>/`, at the source's path below the asset root with its last extension replaced by
each of `OutputExtensions`. Each line the tool writes to stderr becomes one build error, and a line about one
source names that source. The tool exits 0 when every source derived, 1 when any failed, and 2 on a usage
error.

The out directory is part of the asset tree. A derived file keys as the authored file at its path would, of
whatever kind, and a key an authored file already claims fails the build naming both. A publish leaves out
what a development-only directory of the asset tree covers. A declared source is never read as an asset.
Every file in the out directory that is not a declared output of a current source is deleted, and so is
every out directory no current declaration names, including after the last declaration is removed. Paths
compare with case, so an output whose source was renamed only in case is deleted too.

## A private platform module

A console runtime is a private repository, one per platform, holding whatever its NDA covers, and not a
branch of the engine:

1. Subclass `HostPlatform` there. `OpenContent` and `OpenSaveStorage` are its abstract members, and every
   window and audio member has a no-window default to override where the platform has a policy.
2. Put the game's shell for that host family in the same repository, referencing the module and passing it
   to `CapsuleBoot.Configure`. The logic project is untouched.
3. Consume Capsule in source mode at a pinned tag (`CapsuleSourcePath`) and swap the substrate with
   `CapsuleSubstratePackage` and `CapsuleSubstrateVersion`. An unmodified checkout retargets.

Isolation at publish is the reference graph. A shell references one platform module, and a desktop
assembly is absent from a console publish rather than trimmed out of it. The engine's CI publishes the
desktop path, and a private module is tested by its own repository
([`architecture.md`](architecture.md#platforms)).
