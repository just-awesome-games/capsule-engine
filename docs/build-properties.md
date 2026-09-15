# Build properties

Ordinary MSBuild properties, each set in the narrowest project that owns it; paths are absolute or relative to the importing project unless a row says otherwise. The wiring they belong to is [`consuming-capsule.md`](consuming-capsule.md).

## Project roles

| Property           | Value  | Effect                                                                                                                                        |
| ------------------ | ------ | --------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleGameLogic` | `true` | Enables game-boundary analysis, generates the game's scene, entity, and asset registries, compiles its sprite sheets, and defaults scene import and asset shipping on. Set it only on the substrate-free logic library. |
| `CapsuleGameShell` | `true` | Generates `CapsuleBoot` and supplies default application icons. Reads no authoring sources. Set it only on the executable shell.               |

## Authoring sources and output

| Property                 | Default                                       | Effect                                                                                                                                                                 |
| ------------------------ | --------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleAssetSourcesDir` | `Assets` under the importing project          | Locates the authored `Scenes/`, `Sprites/`, `Textures/`, `Audio/`, and `Fonts/` trees. An explicitly named directory must exist. `Sprites/` is read by the compiler in the logic role and carries no property of its own. |
| `CapsuleImportScenes`    | `true` for the logic library; otherwise `false` | Validates and canonically re-emits `*.scene.json` sources, then ships them under `assets/scenes/`. A role-free test or tool can opt in independently. |
| `CapsuleShipAssets`      | `true` for the logic library; otherwise `false` | Ships admitted textures, audio, and font pages under `assets/`. A role-free test or tool can opt in independently.                                                   |
| `CapsuleImportAudio`     | `true` for the logic library; otherwise `false` | Measures every `Audio/` source and compiles it into `CapsuleAssets.Audio`. Nothing ships from here; a role-free project that has to name a clip opts in independently.        |
| `CapsuleTileSize`        | unset                                         | Requires every imported tile map to use this positive pixel size. Set it on the logic project when the game has one global tile size.                                  |
| `CapsuleShipping`        | `true` for the duration of a publish          | Excludes every `.capsuleignore` directory from the compile and from the asset plane, leaves `CAPSULE_DEVELOPMENT` undefined, and sets `Capsule.Development` to `false`; see [Development builds](consuming-capsule.md#development-builds). |

## Package and source properties

| Property                       | Default                                           | Effect                                                                                                                                                                                        |
| ------------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `CapsuleVersion`               | consumer-defined                                  | Pins `JAG.Capsule`, `JAG.Capsule.Runtime`, and `JAG.Capsule.Build` to one release.                                                                                                            |
| `CapsuleSourcePath`            | unset                                             | Points at an engine clone. The standard wiring resolves it relative to the `Directory.Build.props` that declares `CapsuleSourceRoot`, not the command's working directory.                    |
| `CapsuleUsePackages`           | `false`                                           | Set to `true` to ignore a source override and verify the pinned NuGet graph.                                                                                                                  |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where a source build stages Capsule's XML documentation. A relative path is resolved against the repository root. Read only in source mode; a package consumer reads the NuGet cache instead. |
| `CapsuleSourceConfiguration`   | `Release`                                         | The configuration the engine clone's own projects build in under a source consumer, whatever the game builds. Set it to `Debug` to step into engine source.                                   |

## Application icons

A shell with no icon configuration receives Capsule's executable and window icons. Override either or both beside the shell project:

| Input                                            | Effect                                                                                   |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------- |
| `Icon.ico`                                       | Becomes the executable icon through the standard .NET `ApplicationIcon` property.        |
| `Icon.bmp`                                       | Becomes the window and taskbar icon: a 128x128, 32-bit uncompressed BMP, alpha honoured. |
| `ApplicationIcon`                                | Overrides the executable icon with any path accepted by the .NET SDK.                    |
| `EmbeddedResource` with `LogicalName="Icon.bmp"` | Overrides the window icon when the bitmap is not beside the shell project.               |

```xml
<PropertyGroup>
  <ApplicationIcon>branding/MyGame.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <EmbeddedResource Include="branding/MyGame.bmp" LogicalName="Icon.bmp" />
</ItemGroup>
```

Defining only one half is allowed; the build warns because the other half keeps Capsule branding. An `Icon.bmp` whose alpha is entirely zero reads as fully opaque, and most viewers draw a `BI_RGB` alpha bitmap on black, so a transparent one looks black-backed outside the window.
