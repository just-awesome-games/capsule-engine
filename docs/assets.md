# Assets

After this page you can add a texture, a sprite sheet, a sound and a font to a game, name each from C#
with no string in sight, and control what is loaded when.

| Extension | Asset type | `Assets/Player/player.*` is |
| --- | --- | --- |
| `.png` | A texture | `CapsuleAssets.Player.PlayerTexture`, a `TextureHandle` |
| `.sheet.json` | A sprite sheet | `CapsuleAssets.Player.PlayerSheet`, a class of frames, clips and sockets |
| `.wav`, `.ogg` | A sound | `CapsuleAssets.Player.PlayerSound`, an `AudioClip` |
| `.fnt` | A bitmap font | `CapsuleAssets.Player.PlayerFont`, a `BitmapFont` |
| `.fx` | A shader | `CapsuleAssets.Player.PlayerShader`, a `Shader` |
| `.scene.json` | A scene document | `CapsuleAssets.Player.PlayerScene`, a `SceneKey` |
| `.config.json` | [Configuration](configuring-assets.md) of textures | Not named |
| `.atlas.json` | An [atlas](configuring-assets.md#atlases) declaration | Not named |

One extension is one kind of asset. Variation within a kind is configuration.

## Named assets

Assets are authored anywhere under `Assets/` in the logic project, organized any way the game likes.
`CapsuleAssets` mirrors that tree, one nested class per folder, and names each file for its name and its
type. A player's texture, sheet and sounds can share a name and sit together in `Player/`. An editor's own
sources can sit beside what it exports, because the build reads only the extensions above and those an importer claims.

A key is a file's path below `Assets/`, forward slashes and no extension, with every folder and the file
name normalized to the kebab form of the identifier it names. `Enemies/Bat.png` and `enemies/bat.png` are
one texture with one key `enemies/bat`, one member `CapsuleAssets.Enemies.BatTexture`, and one shipped
path `assets/enemies/bat.png`. `Stage1`, `stage1` and `stage-1` are one segment, `stage-1`. The runtime
sees keys only. A document names an asset by key and extension, as `"enemies/bat.png"`, in any spelling of
the key. Two files of one type keying the same, such as `hit.wav` and `hit.ogg`, fail the build.

What ships beside the executable is the same tree under `assets/`: a texture or sound as authored, and a
scene document, shader or `r8` texture in its compiled form. A sheet and a font's description compile into
the game and ship nothing.

## Textures

A `Sprite` is a region of a texture with its own pivot:

```csharp
private static readonly Sprite Field = new(CapsuleAssets.Textures.HazardTexture, new TextureRegion(0, 0, 16, 24), Body / 2f);
```

A texture's atlas, format and sampling are set in config files ([`configuring-assets.md`](configuring-assets.md)).

## Sprite sheets

A sheet names one texture, the frames it cuts from it, any clips played over those frames, and any
sockets its frames set. The build compiles it into game code. `Sprites/actors/player.sheet.json` declares
`CapsuleAssets.Sprites.Actors.PlayerSheet`, with `Frames.Idle0` a `Sprite`, `Clips.Idle` a `SpriteClip`
and `Sockets.Muzzle` a socket's name. A misspelt frame or socket is a build error, and no sheet ships.

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

A sheet may also declare `boxes`, named rects a frame sets as `"boxes": { "hurt": { "x": 1, "y": 0, "width": 6, "height": 8 } }`,
and `events`, names a clip entry raises as `"events": ["footstep"]`. A box belongs to the drawing, as a
socket does. An event belongs to the clip entry, and the same frame played by another clip raises none
of it. Each list generates a `Boxes` or `Events` class of names.

The format's JSON Schema documents every field ([Editor completion](configuring-assets.md#editor-completion)).
Pivots, socket points and boxes are texels from the frame's top-left corner, and durations are fixed steps. An
importer converting another editor's sheets converts to these units. Playback and sockets are
[`rendering.md`](rendering.md#renderers).

## Audio

A sound is a `.wav` or an `.ogg`. The build measures each into an `AudioClip` carrying its duration and
any loop region the file declares. Playing them is [`audio.md`](audio.md).

## Fonts

A bitmap font is a BMFont `.fnt` description, text flavour, and the `.png` pages it names beside it. Its
metrics, glyphs and kerning compile into the logic assembly as a `BitmapFont`, and the description does
not ship. A page is an ordinary texture that no atlas packs. A page of greyscale coverage may take
`"format": "r8"` ([`configuring-assets.md`](configuring-assets.md#texture-settings)) and draws as an RGBA
page of white glyphs does, in a quarter of the memory. Drawing text is [`rendering.md`](rendering.md#text).

## Shaders

A shader is an `.fx`, a fragment function the build wraps in the engine's sprite shader and compiles
([`rendering.md`](rendering.md#your-own-shader)). It ships compiled as `.mgfx`, and its member carries the
parameters the build read from it. Shaders compile on any desktop operating system with nothing to
install.

## Loading and residency

A scene collects what it needs before it starts, and the runtime preloads it at the scene boundary on
worker threads. The scene starts once the last file has landed. `Scene.CollectAssets`,
`Entity.CollectAssets` and `Component.CollectAssets` are the hooks:

```csharp
protected override void CollectAssets(AssetCollection assets) => assets.Add(_texture);
```

The engine's renderers, audio sources, labels and materials declare what they hold. An entity that
attaches its components in its constructor is preloaded with them. An `EntityPool<T>` is forwarded from
its owner's hook. A resource the scene did not collect loads on first use, logs that at info, and stays
cached for the rest of the scene.

The outgoing scene's resources are released at transition or exit, except those the incoming preload also
uses. A packed texture is resident as its atlas page. A headless run collects the same way and loads no
media. `Scene.CollectPreloads` lets a test read what a scene preloads, and `Run.PrefetchScene` starts a
scene's preload before it is requested.
