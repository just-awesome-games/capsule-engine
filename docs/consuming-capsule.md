# Consuming Capsule

Capsule games use two projects: a substrate-free logic library and a small executable shell. This file contains the MSBuild wiring that cannot live in API comments.

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

Keep `src/asset-sources/` as a sibling of the logic and shell projects. Capsule looks for authored sources at `<project>/../asset-sources` by default, so both role projects find the same source tree without a `CapsuleAssetSourcesDir` override.
Commit `src/asset-sources/`. The build derives `assets/` beside each executable; do not commit or author files there. Scene sources live under `scenes/`; their format and pipeline are in [`scenes.md`](scenes.md).

From the repository root, create the modern solution and add the three projects after writing the project files below:

```text
dotnet new sln --name MyGame
dotnet sln MyGame.slnx add src/MyGame.Game src/MyGame.Shell tests/MyGame.Tests
```

## Shared configuration

Pin one exact Capsule version and give every project the build package:

```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <CapsuleVersion>0.4.0</CapsuleVersion>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
    <CapsuleSourceRoot Condition="'$(CapsuleUsePackages)' != 'true' and '$(CapsuleSourcePath)' != ''">$([MSBuild]::NormalizePath('$(MSBuildThisFileDirectory)', '$(CapsuleSourcePath)'))</CapsuleSourceRoot>
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

The logic role activates source generation and purity analysis:

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

The shell role generates `CapsuleBoot`, imports scene documents, ships assets, and supplies default application icons:

```csharp
using Capsule.Runtime.Generated;
using MyGame.Game;

CapsuleBoot.Configure("My Game").RunScene<MainMenu>();
```

A role-free project that needs derived content — a test project, a headless smoke binary — can opt into `<CapsuleImportScenes>` and `<CapsuleShipAssets>` independently.

## Package and source modes

Commit each package-consuming project's `packages.lock.json` and restore CI with `--locked-mode`. That pairing is package mode only: a source build resolves the engine through project references instead of the locked package graph, so a source-mode restore runs without `--locked-mode`.

For one source build against a sibling engine clone:

```text
dotnet build -p:CapsuleSourcePath=../capsule-engine
```

For persistent local development, place the same property in an ignored `Directory.Build.local.props`. Set `CapsuleUsePackages=true` to force the committed package graph.

`dotnet format` accepts no MSBuild properties, so `-p:CapsuleSourcePath=...` never reaches it. A format gate selects source mode through the `CapsuleSourcePath` environment variable or `Directory.Build.local.props`, both of which MSBuild evaluation picks up.

The source and package branches expose the same assemblies: game logic sees only `JAG.Capsule`; the shell alone sees `JAG.Capsule.Runtime`.

## Publishing

Games ship under NativeAOT. No project file sets `PublishAot`; pass it with a runtime identifier:

```text
dotnet publish src/MyGame.Shell --configuration Release --runtime win-x64 --self-contained true -p:PublishAot=true
```

Keep game code AOT-safe: the NativeAOT publish is the whole-graph gate, and running the published binary proves it boots. The rule and its reason are in [`architecture.md`](architecture.md#nativeaot-floor).

## Seeing your game's output

Game logic cannot reach `System.Console` — the analyzer stops it — so it says things out loud through `Capsule.Diagnostics.Log`:

```csharp
using Capsule.Diagnostics;

Log.Info($"picked up {tile}");
Log.Warning("no spawn point on this map");
```

The shell installs a console sink at boot, and every level goes to standard output in the order it was written, prefixed with the simulation tick:

```text
[   boot] info  main menu started
[     30] warn  no spawn point on this map
```

Run the shell with `dotnet run --project src/MyGame.Shell` and the lines appear in that terminal. A shell launched by double-clicking its executable has no terminal attached and shows nothing; run it from a terminal when you want to read the log.

`Log` is write-only telemetry and reads nothing back, so it does not weaken the determinism contract: a run with a sink installed and a run without one produce the same state transitions.

Nothing is installed until the host runs, so a headless test harness supplies its own. `CollectingLogSink` keeps what it is given:

```csharp
CollectingLogSink log = new();
Log.UseSink(log);

// ... step the simulation ...

Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning);
```

`WithLogSink(sink)` on the engine builder sends the game's output somewhere else instead, and `WithoutLogging()` silences it.

## Build configuration

Capsule's role, content, scene-import, local-development, and icon options are collected in the [build configuration reference](build-configuration.md). Set shared defaults in `Directory.Build.props`; set a role-specific option such as `CapsuleTileSize` or `ApplicationIcon` in the project that owns it.
