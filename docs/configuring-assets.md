# Configuring assets

After this page you can change how a texture is stored and sampled, pack textures onto atlases, and set
a value for a whole folder or for one file.

## Config files

Assets take the engine's defaults. Most games write no config file. A game changes a default with
optional JSON files anywhere under `Assets/`.

A file named `.config.json`, with nothing before the dot, configures every asset in its folder and in
every folder below it. A folder mixes kinds, and the file's top-level keys are asset kinds. `texture` is
the only kind with settings today:

```json
{ "texture": { "atlas": "game", "sampling": "point" } }
```

A sidecar `<file>.<ext>.config.json` configures the one asset file it names beside it. It holds that
asset's settings directly, with no kind key. `glow.png.config.json` configures `glow.png`:

```json
{ "format": "r8" }
```

A sidecar's name folds case the way a key does. `Glow.PNG.config.json` also configures `glow.png`.
Scene documents, sprite sheets, shaders, sounds and fonts have no settings and take no sidecar. A config
file ships nothing and names no `CapsuleAssets` member.

## The nearest file wins

Each setting resolves on its own. The engine default applies first, then each `.config.json` from
`Assets/` down to the asset's folder, then the asset's sidecar. A nearer value replaces a farther one.
Every default has a spelling. A nearer file sets an inherited value back by writing it. A `null`
fails the build. A file omits a setting to inherit it.

```text
Assets/.config.json                   { "texture": { "atlas": "game", "sampling": "point" } }
Assets/Effects/glow.png.config.json   { "atlas": false, "sampling": "linear" }
```

Every texture packs onto `game` and samples the nearest texel, except `Effects/glow.png`. It ships on
its own and samples linearly. A texture's `CapsuleAssets` member summary names each setting that is not
its default and the file that set it.

## Texture settings

| Setting | Values | Default | Meaning |
| --- | --- | --- | --- |
| `atlas` | a declared atlas name, or `false` | `false` | Packs the texture onto that atlas's pages. |
| `format` | `"rgba"`, `"r8"` | `"rgba"` | `r8` is one 8-bit channel: an 8-bit greyscale PNG's values, or an indexed PNG's palette indices. |
| `sampling` | `"scene"`, `"point"`, `"linear"` | `"scene"` | `scene` follows `Scene.Sampling`. The others override it wherever the texture is drawn or sampled. |

`sampling` changes only the texture's sampler. A pixel-art game sets `WithSampling(TextureSampling.Point)`
instead. That call also snaps sprites to the pixel grid and scales the output by whole pixels. An `r8`
texture's source is a non-interlaced greyscale or indexed PNG. [`rendering.md`](rendering.md#your-own-shader)
shows how a shader reads one.

## Atlases

An atlas is a build-time packing of textures onto shared pages. Game code never sees it. A file
`<name>.atlas.json` anywhere under `Assets/` declares the atlas `<name>`. A texture joins it through
its `atlas` setting. These two files pack every texture onto one atlas:

```text
Assets/Atlases/game.atlas.json   {}
Assets/.config.json              { "texture": { "atlas": "game" } }
```

The name normalizes like a key segment. `Game.atlas.json` declares `game`. The file holds only the
atlas's own settings. `{}` takes every default.

| Setting | Values | Default | Meaning |
| --- | --- | --- | --- |
| `maxSize` | a power of two up to 8192 | 4096 | The largest extent a page reaches on either axis. |

The runtime serves a packed handle from its page and moves the region by where that texture's texels
landed. Adding, splitting or removing an atlas changes no C# and no document. Fonts do not pack.

Members are placed by MaxRects, best short side fit and no rotation, in an order fixed by size and key.
One input packs byte-identically on every machine. Two texels stay clear between placements. Every
member's outer texel is duplicated one texel outward on every side. Clamped linear sampling, sub-texel
scaling and tiling at a region's edge then read no neighbour. A page holds one format and one sampling.
Members differing in either pack onto separate pages. The next page opens when one is full. Each page
is at most `maxSize` texels a side, trimmed to its packed extent rounded up to a multiple of four.

Pages ship under `assets/atlases/`, as straight-alpha RGBA or as greyscale for `r8`. One map at
`assets/textures.json` names each packed key's page and the texel its `(0, 0)` landed on. A packed
member does not ship on its own. Each atlas keeps a stamp over its own settings, its members and their
settings. Editing a texture, its config or an atlas file repacks only the atlases it touches. A
texture's member is derived from its source. Packing leaves the member unchanged. An atlas file ships
nothing and names no member.

## What fails the build

Each of these defects fails the build. The message names the file and the fix.

- Malformed JSON, a `null`, or an unknown kind, setting or value. The message lists the valid ones.
- A sidecar naming no asset file beside it, or one for an asset whose kind has no settings.
- A setting a sidecar sets that cannot apply to its asset, as `atlas` for a font's page. The same
  setting inherited from a folder is ignored for that asset.
- An `atlas` naming no declared atlas. The config file that sets it is named.
- Two files declaring one atlas, or a declared atlas no texture joins. The atlas file is named.
- A member that cannot fit a page of its atlas with its border. The texture is named.
