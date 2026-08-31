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
    <CapsuleVersion>0.2.0</CapsuleVersion>
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

The shell role generates `GameBoot`, imports scene documents, ships assets, and supplies default application icons. Set `<CapsuleTileSize>` on this project when every scene must use one tile size, and a scene whose grid differs fails the build. Override the executable icon with standard `<ApplicationIcon>` and the window icon with an embedded `Icon.bmp` whose logical name is `Icon.bmp`.

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
