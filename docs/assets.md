# Assets

After this page you can add a texture, a sprite sheet, a sound and a font to a game, name each from C#
with no string in sight, and control what is loaded when.

## Named assets

Assets are authored under `Assets/<Domain>/` in the logic project and ship under `assets/<domain>/` at
their key. The domains are `Textures/`, `Sprites/`, `Atlases/`, `Audio/`, `Fonts/` and `Scenes/`.

A key is the authored path below the domain root, forward slashes and no extension, with every
directory segment and the file stem normalized to the kebab form of the identifier it names. `Enemies/Bat.png`,
`enemies/bat.png` and `enemies/Bat.png` are one asset with one identifier `CapsuleAssets.Textures.Enemies.Bat`,
one key `enemies/bat`, and one shipped path `assets/textures/enemies/bat.png`. `Stage1`, `stage1` and
`stage-1` are one segment, `stage-1`. The build normalizes every key a game or an authoring module hands
it, so the runtime sees keys only. A document names an asset by key and extension, `"enemies/bat.png"`,
spelt however the author likes.

A segment that is no C# identifier, two sources keying the same, and C# identifier collisions fail the
build naming the files. Each generated domain and directory class exposes an allocation-free `All` span over
the handles beneath it, except `CapsuleAssets.Scenes`, which holds scene document keys ([`scenes.md`](scenes.md)).

## Textures

`Assets/Textures/**/*.png` ships as `assets/textures/<key>.png` and is named as
`CapsuleAssets.Textures.<Path>`. A `Sprite` is a region of a texture with its own pivot:

```csharp
private static readonly Sprite Field = new(CapsuleAssets.Textures.Hazard, new TextureRegion(0, 0, 16, 24), Body / 2f);
```

`TextureHandle.White` and `Sprite.White` are a built-in white texel, for flat colour with no asset.

## Sprite sheets

A sheet names one texture, the frames it cuts from it, any clips played over those frames, and any
sockets its frames set. Playback is [`rendering.md`](rendering.md#renderers).

Sheets are authored under `Assets/Sprites/` and compiled by the build tool into game code under
`CapsuleAssets.Sprites`, so a misspelt frame, clip or socket is a build error naming the file and the member, and no
sheet ships. An edited sheet reaches IntelliSense on the next build. A sheet's key is its path under the
sprites root without either extension, and each directory in it becomes a nested class:
`actors/player.sheet.json` declares `CapsuleAssets.Sprites.Actors.Player`, with `Frames.Idle0` a `Sprite`
carrying its sockets, `Clips.Idle` a `SpriteClip` and `Sockets.Muzzle` the socket's name.

Format version 1, UTF-8 JSON:

```json
{
  "formatVersion": 1,
  "texture": "player.png",
  "sockets": [
    { "name": "muzzle" }
  ],
  "frames": [
    { "name": "idle-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8], "sockets": { "muzzle": [8, 4] } }
  ],
  "clips": [
    { "name": "idle", "loop": true, "frames": [ { "frame": "idle-0", "ticks": 30 } ] }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `formatVersion` | Required, and must be supported. |
| `texture` | The key under the textures root, extension included, of the texture every frame is cut from. Forward slashes, no empty, `.` or `..` segment, any spelling of the key. A texture the game does not ship fails the sheet. Geometry is authored here, not inferred from the image. |
| `sockets` | Optional, each carrying a `name`, declared between `texture` and `frames`. Absent or empty generates no `Sockets` class. A declared socket no frame sets is an error at the declaration. |
| `frames` | At least one. Each carries `name`, `x`, `y`, `width`, `height`, an optional `pivot` and an optional `sockets`, in that order. |
| `clips` | Optional, each carrying `name`, an optional `loop`, and `frames`, in that order. Absent or empty is a sheet of frames only, with no `Clips` class generated. |
| `name` | Unique within its own list and safe as a C# name: letters, digits, `-` and `_`, not starting with a digit. Frames, clips and sockets are separate name spaces. |
| `x`, `y` | The frame's top-left corner in texels of the texture, not negative. |
| `width`, `height` | The frame's extent in texels, at least one on each axis. |
| `pivot` | `[x, y]` in texels of the frame from its own top-left corner, both finite. Absent is that corner. |
| `sockets` (of a frame) | An object mapping a declared socket name to `[x, y]`, in `pivot`'s texel space. A frame sets the sockets it has a point for and leaves the rest out. A name the sheet does not declare, or one set twice, is an error at the frame. |
| `loop` | Whether the last entry wraps back to the first. Absent is false. |
| `frames` (of a clip) | At least one entry, each naming a `frame` of this sheet and the `ticks` it is held for. |
| `ticks` | Fixed steps the frame is held for, at least one. Not milliseconds. |

A `source` block of `tool`, `path` and `hash` is accepted so a derived sheet may name what it came from.
Nothing reads it.

A socket is a named point on a frame, such as a muzzle or a hand, in the pivot's texel space, so it moves
with the drawing frame by frame. `SpriteRenderer.Socket` returns a child entity placed at that point,
mirrored by the renderer's flips and turned and scaled with the entity as the frame is. A game parents
whatever hangs from the point under that child. A frame that sets no point for a socket leaves the child
where the last frame that did put it.

## Atlases

An atlas is a build-time packing of textures onto shared pages, declared by a manifest and invisible to
game code. The runtime serves a packed handle from its page and moves the region by where that texture's
texels landed, so adding, splitting or removing an atlas changes no C# and no document.

`Assets/Atlases/<name>.atlas.json`:

```json
{
  "textures": ["biomes/forest/**", "actors/*", "props/crate"],
  "maxSize": 4096
}
```

`textures` is a non-empty array of globs over texture keys: `*` is any run within one segment, and `**`
alone is everything below the directory it ends and any depth elsewhere. A pattern matching nothing fails
the build, as does a texture two manifests both match. `maxSize` is optional, the largest extent a page may
reach on either axis, a power of two up to 8192, 4096 by default. Any other member fails the build. Fonts
do not pack.

Members are placed by MaxRects, best short side fit and no rotation, in an order fixed by size and key, so
one input packs byte-identically on every machine. Two texels stay clear between placements, and every
member's outer texel is duplicated one texel outward on every side, so clamped linear sampling, sub-texel
scaling and tiling at a region's edge read no neighbour. When a page is full the next opens: `<name>.0`,
`<name>.1` and so on, each trimmed to its packed extent rounded up to a multiple of four. A member that
cannot fit a page with its border fails the build naming the texture.

Pages ship straight-alpha at `assets/textures/<name>.<n>.png`, beside one map at
`assets/textures/atlases.json` naming each packed key's page and the texel its `(0, 0)` landed on. A packed
member does not ship on its own. Each atlas keeps a stamp over its manifest and members, so editing one
texture repacks only the atlas holding it. `CapsuleAssets.Textures` is derived from the sources, so packing
leaves it unchanged.

## Audio

`Assets/Audio/` takes `.wav` and `.ogg`, and no MP3. Each source is measured at build time into
`CapsuleAssets.Audio.<Key>` as an `AudioClip` carrying its duration and any loop region the file declares. A
source the build cannot measure, or whose region does not fit it, fails the build naming the file. Playing
them is [`audio.md`](audio.md).

## Fonts

A bitmap font is authored under `Assets/Fonts/` in any directory shape.

| Extension | What it is |
| --- | --- |
| `.fnt` | A BMFont description, text flavour, unpacked. Its metrics, glyphs and kerning compile into the logic assembly as a `BitmapFont`. The file does not ship. |
| `.png` | A page the description names. Ships under `assets/fonts/` at its own key. |

A font and its pages are keyed off their authored paths, so a page beside its font ships beside it. A
description naming a page the game does not ship fails the build. `BitmapFont.Default` ships inside the
runtime and needs no asset. Drawing text is [`rendering.md`](rendering.md#text).

## Loading and residency

A scene collects what it needs before it starts, and the runtime preloads it synchronously at the scene
boundary:

```csharp
protected internal override void CollectAssets(AssetCollection assets) => assets.Add(_texture);
```

`Scene.CollectAssets`, `Entity.CollectAssets` and `Component.CollectAssets` are the hooks. The engine's
renderers, audio sources and labels declare what they hold, so an entity that attaches its components in
its constructor is preloaded with them. A resource the scene did not collect loads on first rendered or
audible use and is cached for the rest of that scene. The outgoing scene's resources are released at
transition or exit, except those the incoming preload also uses. A packed texture is resident as its atlas
page, so one page covers any number of its members. A headless run loads no media.

## Authoring tools

Another editor's format enters through an authoring module: a package whose `buildTransitive` targets
derive a document per source into their own `obj/` space and add each derived file to the engine's item.
Scenes go on `CapsuleSceneDocument` from a target running `BeforeTargets="CapsuleCollectSceneDocuments"`
([`scenes.md`](scenes.md#authoring-tools)). Sheets go on `CapsuleSheetDocument` from one running
`BeforeTargets="CapsuleCollectSheetDocuments"`. A module states each document's key as
`%(CapsuleDocumentKey)` and the engine normalizes it, so no module implements the key rule. A module
converts its own pivot, point and time models at derivation: pivots and socket points are texels from the
frame's top-left corner, and durations are ticks.

JAG Studios publishes the Tiled module as `JAG.Capsule.Tiled` from
[capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
