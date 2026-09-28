# Writing a Capsule package

After this page you can publish a package that Capsule games reference, name it so it never collides with
Capsule's own packages, and take part in a game's build when your package turns authoring files into assets.

## What a package is

A Capsule package is an ordinary NuGet package. It references `JAG.Capsule` for the API and ships the C#
a game calls. A game adds it with `dotnet add package`, as it adds any other package. Capsule keeps no
registry of packages and asks for no manifest.

Referencing `JAG.Capsule` gives a project the API, the staged API reference and the development-build
switch: `CAPSULE_DEVELOPMENT` and development-only directories compile out of a publish, as in a game.
Restore also downloads the shader compilers, which reach no compile and no output. The asset pipeline, the
generator and Capsule's build defaults run only in a project that declares a game role.
[Consuming Capsule](build-and-publish.md#consuming-capsule) describes the roles.

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

## Taking part in the build

Most packages never touch the build. A package that turns an editor's files into Capsule assets ships an
importer: a public class implementing `IAssetImporter`, in a library that references `JAG.Capsule.Build`
in place of `JAG.Capsule`. A game references the package from its build project and adds the importer in
that project's `Program.cs`. [Writing an importer](build-and-publish.md#writing-an-importer) is the contract.

## Developing a package against a game

A game builds against a package's clone in place of the published package. Its ignored
`Directory.Build.local.props` names the clone in `CapsuleSourceOverrides`, and the clone supplies
`build/<PackageId>.targets`, which references the package's project in its place. The game's build
project then compiles the importer from source. An importer runs inside the game's build process
and must compile against the game's engine. A clone that builds against an engine clone names
that same engine clone in its own `Directory.Build.local.props`.
[Consuming Capsule](build-and-publish.md#consuming-capsule) shows the file.
