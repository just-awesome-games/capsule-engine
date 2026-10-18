# Configuring assets

After this page you can change how a texture is stored and sampled, pack textures onto atlases, and set
a value for a whole folder or for one file. The schemas ([Editor completion](#editor-completion)) hold each
setting's values and default.

## Config files

Assets take the engine's defaults, and most games write no config file. A game changes a default with
optional JSON files anywhere under `Assets/`. Textures are the only kind with settings today: `atlas`,
`format` and `sampling`.

A file named `.config.json`, with nothing before the dot, configures every asset in its folder and in
every folder below it. Its top-level keys are asset kinds:

```json
{ "texture": { "atlas": "game", "sampling": "point" } }
```

A sidecar `<file>.<ext>.config.json` configures the one asset file it names beside it. It holds that
asset's settings directly, with no kind key. `mask.png.config.json` configures `mask.png`:

```json
{ "format": "r8" }
```

A sidecar's name folds case the way a key does. A config file ships nothing and names no `CapsuleAssets`
member.

## The nearest file wins

Each setting resolves on its own. The engine default applies first, then each `.config.json` from
`Assets/` down to the asset's folder, then the asset's sidecar. A nearer value replaces a farther one. A
file omits a setting to inherit it, and writes the default's spelling to set an inherited value back.

```text
Assets/.config.json                   { "texture": { "atlas": "game", "sampling": "point" } }
Assets/Backdrops/sky.png.config.json  { "atlas": false, "sampling": "linear" }
```

Every texture packs onto `game` and samples the nearest texel, except `Backdrops/sky.png`. It ships on its
own and samples linearly. A texture's `CapsuleAssets` member summary names each setting that is not its
default and the file that set it.

## Texture settings

An `r8` texture holds one 8-bit channel in a quarter of the memory. The engine's sprite shader draws it as
white at that coverage, as a greyscale font page wants. [`shaders.md`](shaders.md) shows how a shader of
your own reads one.

## Atlases

An atlas is a build-time packing of textures onto shared pages, and game code never sees it. A file
`<name>.atlas.json` anywhere under `Assets/` declares the atlas `<name>`, and a texture joins it through its
`atlas` setting. An atlas file holds the atlas's own settings: `maxSize`, the largest extent a page reaches,
or `{}` for every default. These two files pack every texture onto one atlas:

```text
Assets/Atlases/game.atlas.json   {}
Assets/.config.json              { "texture": { "atlas": "game" } }
```

Adding, splitting or removing an atlas changes no C# and no document. The runtime serves a packed
handle from its page.

Linear sampling, scaling and tiling at a packed region's edge read no neighbour. A page holds one format
and one sampling, and members differing in either pack onto separate pages. Pages ship under
`assets/atlases/`, and a packed member does not ship on its own.

## Editor completion

The engine's repository publishes a JSON Schema for each authored file shape. A file names its schema
with a `"$schema"` key at its root, and an editor then completes and documents every setting:

```json
{ "$schema": "https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/schemas/folder.config.schema.json" }
```

A sidecar names `texture.config.schema.json`, an atlas file `atlas.schema.json`, a sprite sheet
`sheet.schema.json` and a scene document `scene.schema.json` at the same address. The build ignores the
value. Copies to read are staged beside Capsule's API reference
([Consuming Capsule](build-and-publish.md#consuming-capsule)).
