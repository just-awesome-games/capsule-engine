# Assets

After this page you can add a texture, a sprite sheet, a sound and a font to a game, name each from C#
with no string in sight, and control what is loaded when.

## Named assets

Assets are authored anywhere under `Assets/` in the logic project, organized by type, by object or any
other way. `CapsuleAssets` mirrors that tree, one nested class per folder, and names each file for its
name and its type:

| Extension | Type | `Assets/Player/player.*` is |
| --- | --- | --- |
| `.png` | A texture | `CapsuleAssets.Player.PlayerTexture`, a `TextureHandle` |
| `.sheet.json` | A sprite sheet | `CapsuleAssets.Player.PlayerSheet`, a class of frames, clips and sockets |
| `.wav`, `.ogg` | A sound | `CapsuleAssets.Player.PlayerSound`, an `AudioClip` |
| `.fnt` | A bitmap font | `CapsuleAssets.Player.PlayerFont`, a `BitmapFont` |
| `.fx` | A shader | `CapsuleAssets.Player.PlayerShader`, a `Shader` |
| `.scene.json` | A scene document | `CapsuleAssets.Player.PlayerScene`, the document's key |
| `.atlas.json` | An atlas manifest | Not named |

A file of one type shares its name with files of others and with its folder, so a player's texture, sheet
and sounds sit together in `Player/`. The build reads no other file, so an editor's own sources can sit
beside what it exports.

What ships beside the executable is the same tree under `assets/`: a texture or sound as authored, and
a scene document or shader in its compiled form. A sheet and a font's description compile into the
game and ship nothing. The build lays it out under the logic project's
`obj/.../capsule/assets/` first.

A key is a file's path below `Assets/`, forward slashes and no extension, with every folder and the file
name normalized to the kebab form of the identifier it names. An extension ships in lower case. `Enemies/Bat.png` and `enemies/bat.png` are
one texture with one key `enemies/bat`, one member `CapsuleAssets.Enemies.BatTexture`, and one shipped
path `assets/enemies/bat.png`. `Stage1`, `stage1` and `stage-1` are one segment, `stage-1`. The build
normalizes every key a game or an authoring module hands it, and the runtime sees keys only. A document
names an asset by key and extension, `"enemies/bat.png"`, spelt however the author likes.

A segment that is no C# identifier, two files of one type keying the same, such as `hit.wav` and `hit.ogg`,
and a name C# would refuse fail the build naming the files. A folder's class exists once a file under it
does.

Every `CapsuleAssets` member is written by the build. An editor that refreshes its build when the project's
files change updates them as a file is added, renamed, moved or deleted. An edit inside a file reaches
them on the next build.

## Textures

A `.png` is a texture. A `Sprite` is a region of one with its own pivot:

```csharp
private static readonly Sprite Field = new(CapsuleAssets.Textures.HazardTexture, new TextureRegion(0, 0, 16, 24), Body / 2f);
```

`TextureHandle.White` and `Sprite.White` are a built-in white texel, for flat colour with no asset.

## Sprite sheets

A sheet names one texture, the frames it cuts from it, any clips played over those frames, and any
sockets its frames set. Playback is [`rendering.md`](rendering.md#renderers).

A `.sheet.json` is compiled by the build into game code. A misspelt frame, clip or socket is a build
error naming the file and the member, and no sheet ships. `Sprites/actors/player.sheet.json` declares
`CapsuleAssets.Sprites.Actors.PlayerSheet`, with `Frames.Idle0` a `Sprite` carrying its sockets,
`Clips.Idle` a `SpriteClip` and `Sockets.Muzzle` the socket's name.

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
| `texture` | The texture's key, extension included, of the texture every frame is cut from. Forward slashes, no empty, `.` or `..` segment, any spelling of the key. A texture the game does not ship fails the sheet. Geometry is authored here, not inferred from the image. |
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

A derived sheet may name what it came from in a `source` block of `tool`, `path` and `hash`. Nothing
reads it.

A socket is a named point on a frame, such as a muzzle or a hand, in the pivot's texel space. It moves
with the drawing frame by frame. `SpriteRenderer.Socket` returns a child entity placed at that point,
mirrored by the renderer's flips and turned and scaled with the entity as the frame is. A game parents
whatever hangs from the point under that child. A frame that sets no point for a socket leaves the child
where the last frame that did put it.

## Atlases

An atlas is a build-time packing of textures onto shared pages, declared by a manifest and invisible to
game code. The runtime serves a packed handle from its page and moves the region by where that texture's
texels landed. Adding, splitting or removing an atlas changes no C# and no document.

`<name>.atlas.json`, anywhere under `Assets/`:

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

Members are placed by MaxRects, best short side fit and no rotation, in an order fixed by size and key.
One input packs byte-identically on every machine. Two texels stay clear between placements, and every
member's outer texel is duplicated one texel outward on every side. Clamped linear sampling, sub-texel
scaling and tiling at a region's edge then read no neighbour. When a page is full the next opens: `<name>.0`,
`<name>.1` and so on, each trimmed to its packed extent rounded up to a multiple of four. A member that
cannot fit a page with its border fails the build naming the texture.

Pages ship straight-alpha beside their manifest, `Atlases/game.atlas.json` packing onto
`assets/atlases/game.0.png`, with one map at `assets/atlases.json` naming each packed key's page and the
texel its `(0, 0)` landed on. A packed member does not ship on its own. Each atlas keeps a stamp over its
manifest and members, and editing one texture repacks only the atlas holding it. A texture's member is
derived from its source, and packing leaves it unchanged.

## Audio

A sound is a `.wav` or an `.ogg`, and no MP3. Each is measured at build time into an `AudioClip`
carrying its duration and any loop region the file declares. A
source the build cannot measure, or whose region does not fit it, fails the build naming the file. Playing
them is [`audio.md`](audio.md).

## Fonts

A bitmap font is a `.fnt` and the `.png` pages it names beside it.

| Extension | What it is |
| --- | --- |
| `.fnt` | A BMFont description, text flavour, unpacked. Its metrics, glyphs and kerning compile into the logic assembly as a `BitmapFont`. The file does not ship. |
| `.png` | A page the description names, beside it. It is an ordinary texture, and no atlas packs it. |

A font naming a page the game does not author fails the build, as does a defect in the description, at
its line. Drawing text, and the font that needs no asset, is [`rendering.md`](rendering.md#text).

## Shaders

A shader is an `.fx`, a fragment function the build wraps in the engine's sprite shader and
compiles ([`rendering.md`](rendering.md#your-own-shader)). Each ships compiled at its path as `.mgfx`,
and its member carries the parameters the build read from it. A compile error fails the build at the shader's file and line. Shaders compile on
any desktop operating system with nothing to install.

## Loading and residency

A scene collects what it needs before it starts, and the runtime preloads it at the scene boundary. Files
are read and decoded on worker threads, and the scene starts once the last has landed:

```csharp
protected internal override void CollectAssets(AssetCollection assets) => assets.Add(_texture);
```

`Scene.CollectAssets`, `Entity.CollectAssets` and `Component.CollectAssets` are the hooks. The engine's
renderers, audio sources and labels declare what they hold, and a renderer's material declares its
shader and its textures. An entity that attaches its components in its
constructor is preloaded with them. A resource the scene did not collect loads on first rendered or
audible use, logs that at info, and is cached for the rest of that scene. The outgoing scene's resources
are released at transition or exit, except those the incoming preload also uses. A packed texture is
resident as its atlas page, and one page covers any number of its members. A headless run loads no
media.

`Run.PrefetchScene<TScene>()` starts loading a scene's preloads before it is requested, and the request
then waits only for what has not landed.

## Authoring tools

A sheet from another editor's format enters through an authoring module, the way a scene document does
([`scenes.md`](scenes.md#authoring-tools)). The module adds each derived sheet to `CapsuleSheetDocument`
from a target running `BeforeTargets="CapsuleCollectSheetDocuments"`. It converts its own pivot, point and
time models at derivation. Pivots and socket points are texels from the frame's top-left corner, and
durations are ticks.
