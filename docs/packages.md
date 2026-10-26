# Writing a Capsule package

After this page you can publish a package that Capsule games reference, name it so it never collides with
Capsule's own packages, extend a game's build with an importer, and port Capsule to another platform. Each
type's XML docs hold its rules.

A Capsule package is an ordinary NuGet package. It references `JAG.Capsule` for the API and ships the C#
a game calls. A game adds it with `dotnet add package`, as it adds any other package. Capsule keeps no
registry of packages and asks for no manifest.

| Need | Piece |
| --- | --- |
| C# a game calls | A library referencing `JAG.Capsule` |
| Turn an editor's files into assets | An `IAssetImporter` in a library referencing `JAG.Capsule.Build` |
| Configure a game's whole asset build | `CapsuleBuild` in the game's build project |
| Port Capsule to another platform | A `HostPlatform` subclass in a private port |

## What a package receives

Referencing `JAG.Capsule` gives a project the API, the staged API reference and the
[development-build axis](build-and-publish.md#development-builds), as in a game. The asset pipeline, the
generator and Capsule's build defaults run only in a project that declares a game role.

## Naming

A package's ID and its root namespace both start with its publisher's own prefix:

| Name | Form | Example |
| --- | --- | --- |
| Package ID | `<Publisher>.Capsule.<Name>` | `Acme.Capsule.Pathfinding` |
| Root namespace | `<Publisher>.Capsule.<Name>` | `Acme.Capsule.Pathfinding` |
| Package tag | `capsule-engine` | `capsule-engine` |

The bare `Capsule` namespace root belongs to the engine. A package that declares `Capsule.Pathfinding`
collides with any later engine namespace of that name, in every game that references both. The
`JAG.Capsule` package ID prefix belongs to JAG Studios, whose own packages use it, as `JAG.Capsule.Tiled`
does. The `capsule-engine` tag lets a package search find every Capsule package in one place.

## The build project

Every build of a game's logic project builds its build project first and runs it once, from the logic
project's directory. The run reads every file under `Assets/` and writes what the game ships and the
`CapsuleAssets` it compiles. Game-wide build configuration and importers live on `CapsuleBuild`:

```csharp
using Capsule.Build;
using JAG.Capsule.Tiled;

return CapsuleBuild.Configure(args)
    .AddImporter(new TiledImporter())
    .WithTileSize(16)
    .Run();
```

The run reuses what it derived before. A derivation runs again only when the content of a file it reads,
its resolved settings, the code that derives it or the .NET runtime changed, or a file it shipped is gone
or rewritten. `dotnet clean` clears the cache.

## Writing an importer

An importer turns an editor's own format into files Capsule reads as though they were authored. It is a
public class implementing `IAssetImporter`, in a library that references `JAG.Capsule.Build` in place of
`JAG.Capsule`. A game references the package from its build project and adds the importer there, as above.
`IAssetImporter`'s XML documentation is the contract and shows one. An importer reads and writes only
through its `AssetImportContext`.

Each output keys and ships as an authored file at its path would. A key an authored file already claims
fails the build naming both, as does an output two sources write. Outputs land in `obj/capsule/imported/`,
or `obj/capsule-shipping/imported/` for a publish, and are never committed. `AssetImportContext` shows an importer's test.

## Developing a package against a game

A game builds against a package's clone through `CapsuleSourceOverrides`
([Consuming Capsule](build-and-publish.md#consuming-capsule)). The package's repository supplies the
`build/<PackageId>.targets` that override reads, referencing the package's project in place of the package.
An importer runs inside the game's build process and must compile against the game's engine. A package
clone that builds against an engine clone names that same engine clone.

## Porting to another platform

A console port is a private repository, one per platform:

1. Subclass `HostPlatform` there, overriding what the platform's SDL3 lacks or its certification demands.
2. Put the game's shell for that platform in the same repository, passing the subclass to
   `EngineBuilder.WithPlatform`. The logic project is untouched.
3. Consume Capsule in source mode at a pinned tag (`CapsuleSourcePath`). An unmodified checkout builds.

The port supplies its platform's licensed SDL3, linked statically under the library name `SDL3` or shipped
beside the game.

When the target's GPU reads a shader format of its own, the shell names the format and its compiler's absolute
path, and its publish carries that format alone:

```xml
<ItemGroup>
  <CapsuleShaderFormat Include="console" Tool="$(ConsoleSdk)/compile-shader" />
</ItemGroup>
<PropertyGroup>
  <CapsuleShaderFormats>console</CapsuleShaderFormats>
</PropertyGroup>
```

The build runs the tool once per stage of every shader, and of the engine's own sprite shader, as
`--stage <vertex|fragment> --entry main --hlsl <file> --spirv <file> --out <file>`. The HLSL and the SPIR-V
are the stage in SDL's binding layout. The tool writes the format's code to the output file and exits 0, and
the build ships it as `<key>.<format>.shader`.
