# Build configuration

Capsule is configured with ordinary MSBuild properties. Put a value in the narrowest project that owns it; use the repository's `Directory.Build.props` only when every project should share the value. Paths may be absolute or relative to the project whose build imports Capsule unless a row says otherwise.

## Project roles

Exactly one game project owns logic and exactly one owns the executable shell.

| Property | Value | Effect |
| --- | --- | --- |
| `CapsuleGameLogic` | `true` | Enables game-boundary analysis and generates the game's scene, entity, and asset registries. Set it only on the substrate-free logic library. |
| `CapsuleGameShell` | `true` | Generates `CapsuleBoot` and defaults scene import and asset shipping on. Set it only on the executable shell. |

The complete two-project wiring is in [Consuming Capsule](consuming-capsule.md).

## Authoring sources and output

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleAssetSourcesDir` | `../asset-sources` from the importing project | Locates the authored `scenes/`, `textures/`, `audio/`, and `fonts/` trees. An explicitly named directory must exist. |
| `CapsuleImportScenes` | `true` for the shell; otherwise `false` | Validates and derives `*.scene.json` and `*.tmj` sources, then ships native scene documents under `assets/scenes/`. A role-free test or tool can opt in independently. |
| `CapsuleShipAssets` | `true` for the shell; otherwise `false` | Ships admitted textures, audio, and fonts under `assets/`. A role-free test or tool can opt in independently. |
| `CapsuleTileSize` | unset | Requires every imported tile map to use this positive pixel size. Set it on each project that imports scenes when the game has one global tile size. |

```xml
<PropertyGroup>
  <CapsuleGameShell>true</CapsuleGameShell>
  <CapsuleTileSize>16</CapsuleTileSize>
</PropertyGroup>
```

## Application icons

A shell with no icon configuration receives Capsule's executable and window icons. Override either or both beside the shell project:

| Input | Effect |
| --- | --- |
| `Icon.ico` | Becomes the executable icon through the standard .NET `ApplicationIcon` property. |
| `Icon.bmp` | Becomes the window and taskbar icon. It must be a 128x128, 32-bit uncompressed BMP. |
| `ApplicationIcon` | Overrides the executable icon with any path accepted by the .NET SDK. |
| `EmbeddedResource` with `LogicalName="Icon.bmp"` | Overrides the window icon when the bitmap is not beside the shell project. |

```xml
<PropertyGroup>
  <ApplicationIcon>branding/MyGame.ico</ApplicationIcon>
</PropertyGroup>

<ItemGroup>
  <EmbeddedResource Include="branding/MyGame.bmp" LogicalName="Icon.bmp" />
</ItemGroup>
```

Defining only one half is allowed, but the build warns because the other half retains Capsule branding.

## Package and engine-source development

These properties belong to the consumer repository's package/source switch shown in [Consuming Capsule](consuming-capsule.md); Capsule's imported build targets consume the resulting references.

| Property | Default | Effect |
| --- | --- | --- |
| `CapsuleVersion` | consumer-defined | Pins `JAG.Capsule`, `JAG.Capsule.Runtime`, and `JAG.Capsule.Build` to one release. |
| `CapsuleSourcePath` | unset | Points at an engine clone. The standard wiring resolves it relative to the `Directory.Build.props` that declares `CapsuleSourceRoot`, not the command's working directory. |
| `CapsuleUsePackages` | `false` | Set to `true` to ignore a source override and verify the pinned NuGet graph. |
| `CapsuleApiReferenceDirectory` | `artifacts/capsule-api` under the repository root | Where a source build stages Capsule's XML documentation. A relative path is resolved against the repository root. Read only in source mode; a package consumer reads the NuGet cache instead. |

`CapsuleSourceRoot` is the normalized internal result of `CapsuleSourcePath`; consumers should not set it directly.
