# Texture atlases

An atlas is a build-time packing of textures onto shared pages, declared by a manifest and invisible to game code. The runtime serves a packed handle from its page and moves the region by where that texture's texels landed, so adding, splitting or removing an atlas changes no C# and no document.

## Manifest

`Assets/Atlases/<name>.atlas.json`:

```json
{
  "textures": ["biomes/forest/**", "actors/*", "props/crate"],
  "maxSize": 4096
}
```

| Field      | Meaning                                                                                                                                                                                                                                                                                       |
| ---------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `textures` | Non-empty array of globs over texture keys ([named assets](consuming-capsule.md#named-assets), no extension, spelt in key form): `*` is any run within one segment, `**` alone is everything below the directory it ends and any depth elsewhere. A pattern matching nothing fails the build. |
| `maxSize`  | Optional; the largest extent a page may reach on either axis, a power of two no larger than 8192. Default 4096.                                                                                                                                                                               |

Any other member fails the build, as does a texture two manifests both match. The atlas's name is the manifest's key under `Atlases/`. Fonts never pack: a bitmap font's pages ship under `assets/fonts/` beside the font.

## Pages

Members are placed by MaxRects (best short side fit, no rotation) in an order fixed by size and key, so one input packs byte-identically on every machine. Two texels stay clear between placements and every member's outer texel is duplicated one texel outward on every side, so clamped linear sampling, sub-texel scaling and tiling at a region's edge never read a neighbour; nothing in the manifest tunes either. When a page is full the next opens: pages are `<name>.0`, `<name>.1`, … under the textures domain, each trimmed to its packed extent rounded up to a multiple of four. A member that cannot fit a page with its border fails the build naming the texture.

Pages ship straight-alpha at `assets/textures/<name>.<n>.png`, beside one map at `assets/textures/atlases.json` naming each packed key's page and the texel its `(0, 0)` landed on:

```json
{
  "textures": {
    "actors/player": { "page": "game.0", "x": 545, "y": 1 }
  }
}
```

A packed member does not ship on its own.

## Residency

The runtime reads the map once at boot and maps a scene's preload set through it before anything loads, so a page is loaded once for any number of its members and, at a transition, released only when no member of the incoming scene wants it — the rule [`architecture.md`](architecture.md#rendering-and-media) states for a texture, applied to the file the texture is in. A member the scene did not preload loads its page on first draw.

## Build

Every `Atlases/**/*.atlas.json` outside a [development-only](consuming-capsule.md#development-only-directories) directory is read, and each atlas keeps a stamp over its manifest and members, so editing one texture repacks the one atlas holding it. The generated `CapsuleAssets.Textures` registry is derived from the sources, not from what ships, so packing leaves it unchanged.
