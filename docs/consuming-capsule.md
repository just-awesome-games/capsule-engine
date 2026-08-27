# Consuming Capsule

The build configuration a game repository sets up to compile against Capsule, in the order it does
it. What the engine's API does is documented on the API itself; this file is the wiring.

Released games restore exact SemVer versions from NuGet.org. Engine development replaces those
packages with a local source clone for one build, without editing a project file.

## 1. The packages

Capsule uses NuGet.org's standard public source and needs no Capsule-specific credentials.

All three packages in a release carry one version: `JAG.Capsule`, `JAG.Capsule.Runtime`, and the
tooling-only `JAG.Capsule.Build`. The package boundary is the purity boundary — `JAG.Capsule`
carries every module that touches no substrate, and the host is a package of its own — while
assemblies and namespaces stay `Capsule.*`. A game pins one exact `CapsuleVersion`, commits the
resulting lock files, and upgrades it deliberately.

## 2. Lay out the repo

```
my-game/
  MyGame.sln
  MyGame.Shell/               # the host executable
    MyGame.Shell.csproj
    Program.cs
    Icon.ico                  # optional, see below
    Icon.bmp                  # optional, see below
  MyGame.Game/                # game logic, substrate-free
    MyGame.Game.csproj
  MyGame.Tests/               # xUnit over MyGame.Game, run headless
  asset-sources/              # the committed authoring plane
    maps/                     # .tmj + .tsj tilesets, and hand-authored .map.json
    textures/                 # .png
    audio/                    # .ogg, .wav
    fonts/                    # .ttf, .otf
  Directory.Build.props
  Directory.Build.targets
  global.json
  .editorconfig
```

`MyGame.Game` is the logic the purity rule below holds substrate-free; `MyGame.Shell` is the host
that owns the window, the device and file IO. The shell sets `<AssemblyName>` to the bare game
name, so the shipped executable is `MyGame.exe` rather than `MyGame.Shell.exe`.

`asset-sources/` is top-level and owned by no project: a second target shell feeds from the same
maps, so putting them under one shell would make it the odd owner of what the others build from.

**`assets/` beside the executable does not appear above because nothing authors it.** It is
build-owned and wholly derived — maps at `assets/maps`, everything else flat under its domain at
`assets/<domain>` — and none of it is committed. Capsule has no asset scanner to hide a file from:
what ships is exactly what the hooks copy there, and an authoring source stays unshipped by living
under `asset-sources/`.

Within a domain root, sources nest however the game likes and the shipped tree is flat, so two
assets in one domain sharing a file name fail the build. A file under a domain root whose extension
that domain does not admit fails the build too, rather than silently never shipping — an
intermediate the game keeps around belongs outside these roots.

## 3. Declare package and local-source resolution

The game root's `Directory.Build.props` owns the one version and the optional source override:

```xml
<Import Project="$(MSBuildThisFileDirectory)Directory.Build.local.props"
        Condition="Exists('$(MSBuildThisFileDirectory)Directory.Build.local.props')" />

<PropertyGroup>
  <CapsuleVersion>0.2.0</CapsuleVersion>
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

`JAG.Capsule.Build` reaches every project and stays inert until a project declares a Capsule role.
It supplies map import, source generation and the compile-time architectural checks, and
contributes nothing to a game's output or publish set.

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

Both files change only when the consumption contract does; ordinary engine releases change
`CapsuleVersion` alone.

For the engine-and-game development loop, add `Directory.Build.local.props` to the game's
`.gitignore` and create the ignored file in the game root:

```xml
<!-- Directory.Build.local.props -->
<Project>
  <PropertyGroup>
    <CapsuleSourcePath>../capsule-engine</CapsuleSourcePath>
  </PropertyGroup>
</Project>
```

Every ordinary IDE and `dotnet` invocation now builds against Capsule source, with breakpoints and
source navigation working normally, and the committed package version and lock files unchanged.
Delete the file to return to packages, or set `CapsuleUsePackages=true` to force package mode for
one command — which is how release tooling regenerates and validates the canonical lock files.
Without the persistent file, `dotnet build -p:CapsuleSourcePath=../capsule-engine` does the same
for one command.

## 4. Declare the roles

`JAG.Capsule.Build` reaches every project, so each project with a Capsule role says which one it
is. The project that ships content and boots the game:

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

Every hook and every generator arrives through those two words, so gaining one is never new wiring
here. Each logic project publishes a generated registry provider for the classes in that assembly,
and the one shell project aggregates every referenced provider and emits `GameBoot` already
holding the combined registry — so a game may split logic across modules with no registration and
no reflection. Exactly one project takes the shell role; tests and ordinary libraries take neither.

The shell role also derives the game's maps and ships its assets. A project with no role that still
has to read that content — a test project driving a map-backed scene, a headless smoke binary —
asks for either half itself and gets the same content beside its own executable:

```xml
<PropertyGroup>
  <CapsuleImportMaps>true</CapsuleImportMaps>
  <CapsuleShipAssets>true</CapsuleShipAssets>
</PropertyGroup>
```

Every way to get a role wrong is a `CAP0xx` build error naming the project and the fix.

### The game's icon

The shell role also gives the game its icon, from two files beside the shell project:

| File | Where it appears | Format |
| --- | --- | --- |
| `Icon.ico` | the executable, in Explorer and on shortcuts | multi-size `.ico`, 16 through 256 |
| `Icon.bmp` | the window and the taskbar | 128x128, 32-bit, uncompressed |

Each falls back on its own to Capsule's mark, so a game with neither still ships an icon rather than
the backend's, and a warning names either half that fell back while the other did not. `Icon.bmp` is
read at window creation and a run-length encoded or bitfield BMP is silently no icon at all, so it is
the plain 32-bit form or nothing.

Either half is overridable by the ordinary means, which takes that file out of the picture and is
never warned about: `<ApplicationIcon>` points the executable's icon wherever the shell likes, and an
`EmbeddedResource` the shell declares with `LogicalName="Icon.bmp"` is the window's.

### Naming the content, never the path

A logic project's generated `GameAssets` gives every asset the build ships a typed handle, the way
`GameScenes` gives every scene a registration:

```csharp
using Capsule.Assets;
using Capsule.Assets.Generated;

TextureHandle hero = GameAssets.Textures.Hero;          // asset-sources/textures/hero.png
AudioHandle step = GameAssets.Audio.FootstepStone;      // asset-sources/audio/footstep-stone.ogg
```

The member is the file's stem in PascalCase, so a misspelling is a compile error rather than a file
that turns out to be missing on somebody else's machine. Two stems that differ only in how they
separate words — `foot-step` and `foot_step` — reach one member and fail the build, as does a stem
no identifier can come out of, and one that lands on the name of its own domain's class —
`audio/audio.wav`.

A handle is data: it carries the stem and the extension of the file it came from, and resolves
nothing — which is why logic code may hold one without breaking the purity rule below. The pair is
sufficient on its own to locate the asset, at `assets/<domain>/<stem><extension>` beside the
executable, so whatever ends up reading the bytes takes a handle and never probes a directory for
whichever extension the game happened to author.

## 5. Wire the references

```xml
<!-- MyGame.Game.csproj, and any test project -->
<ItemGroup Condition="'$(CapsuleSourceRoot)' == ''">
  <PackageReference Include="JAG.Capsule" Version="[$(CapsuleVersion)]" />
</ItemGroup>

<ItemGroup Condition="'$(CapsuleSourceRoot)' != ''">
  <ProjectReference Include="$(CapsuleSourceRoot)/Capsule/Capsule.csproj" />
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

The package and source branches expose the same assembly graph. Logic takes the substrate-free set
and the shell alone takes Runtime, so the purity boundary is visible at the call site.

## 6. Lock the restore

`Directory.Build.props` enables package lock files in package mode, every package-consuming project
commits its own `packages.lock.json`, and CI restores with `--locked-mode` so a restore can never
silently move a version. Source mode is for development and bypasses the game lock files, so
replacing packages with project references does not dirty the committed graph. The release build
omits `CapsuleSourcePath` and proves the locked package graph.

## The purity rule

`MyGame.Game` references **only Capsule modules that carry no substrate reference** —
`Capsule.Core`, `Capsule.Maps` and `Capsule.Scenes` today — and never `Capsule.Runtime`. Being
substrate-free is the admission test rather than membership in a fixed list, and it is the same
test `JAG.Capsule` is defined by, so a module the set gains arrives in that package and the rule
never weakens to let it in.

**File IO is the host's.** A pure module supplies the parser and the model; the host layer reads
the bytes and hands the parsed value in. Game logic therefore takes its content already in hand
and never touches a path, which is what keeps a logic test a pure function of its inputs. A test
project follows the same rule: it takes neither role, references what the logic assembly does, and
drives a scene through a `SceneSimulation` with no window, device or clock.

The module references hold that boundary mechanically, and Capsule's analyzer rejects a logic
project that reaches Runtime, MonoGame, nondeterministic host services, or asynchronous execution.
`Capsule.Runtime` privatises its backend's compile assets so no backend type reaches the shell
either. A game repository never adds a backend `PackageReference` of its own.
