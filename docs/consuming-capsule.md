# Consuming Capsule

Capsule games use two projects: a substrate-free logic library and a small executable shell. This file contains the MSBuild wiring that cannot live in API comments.

## Repository shape

```text
my-game/
  MyGame.Game/
  MyGame.Shell/
  MyGame.Tests/
  asset-sources/
    maps/
    textures/
    audio/
    fonts/
  Directory.Build.props
  Directory.Build.targets
```

`asset-sources/` is committed. The build derives `assets/` beside each executable; do not commit or author files there.

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
    <ProjectReference Include="$(CapsuleSourceRoot)/Capsule/Capsule.csproj" />
  </ItemGroup>
</Project>
```

Tests reference the logic project and `JAG.Capsule`; they take no Capsule role.

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
    <ProjectReference Include="$(CapsuleSourceRoot)/Capsule.Runtime/Capsule.Runtime.csproj" />
  </ItemGroup>
</Project>
```

The shell role generates `GameBoot`, imports maps, ships assets, and supplies default application icons. Set `<CapsuleTileSize>` on this project when every map must use one tile size. Override the executable icon with standard `<ApplicationIcon>` and the window icon with an embedded `Icon.bmp` whose logical name is `Icon.bmp`.

A role-free executable that needs derived content can opt into `<CapsuleImportMaps>` and `<CapsuleShipAssets>` independently.

## Package and source modes

Commit each package-consuming project's `packages.lock.json` and restore CI with `--locked-mode`.

For one source build against a sibling engine clone:

```text
dotnet build -p:CapsuleSourcePath=../capsule-engine
```

For persistent local development, place the same property in an ignored `Directory.Build.local.props`. Set `CapsuleUsePackages=true` to force the committed package graph.

The source and package branches expose the same assemblies: game logic sees only `JAG.Capsule`; the shell alone sees `JAG.Capsule.Runtime`.
