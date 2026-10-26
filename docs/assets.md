# Assets

After this page you can add a texture, a sprite sheet, a sound and a font to a game, name each from C#
with no string in sight, and control what is loaded when. Each type's XML docs hold its rules.

An asset is a file under the logic project's `Assets/`. The build names each one in the generated
`CapsuleAssets`, as `CapsuleAssets.<Folder>.<Name><Kind>`, and ships what the game loads.

| Extension | Asset type | Example file and its member |
| --- | --- | --- |
| `.png` | A texture | `Textures/lamp.png` is `CapsuleAssets.Textures.LampTexture`, a `TextureHandle` |
| `.sheet.json` | A sprite sheet | `Sprites/lamp.sheet.json` is `CapsuleAssets.Sprites.LampSheet`, a class of frames, clips and sockets |
| `.clip.json` | A [value clip](animation.md#value-clips) | `Clips/pulse.clip.json` is `CapsuleAssets.Clips.PulseClip`, a `ValueClip<float>` |
| `.wav`, `.ogg` | A sound | `Audio/click.wav` is `CapsuleAssets.Audio.ClickSound`, an `AudioClip` |
| `.fnt` | A bitmap font | `Fonts/menu.fnt` is `CapsuleAssets.Fonts.MenuFont`, a `BitmapFont` |
| `.fx` | A shader | `Shaders/fade.fx` is `CapsuleAssets.Shaders.FadeShader`, a `Shader` |
| `.scene.json` | A scene document | `Scenes/hall.scene.json` is `CapsuleAssets.Scenes.HallScene`, a `SceneKey` |
| `.strings.json` | One language's [texts](localization.md) | Each key of the source language is a `CapsuleStrings` member, a `TextKey` |
| `.config.json` | [Configuration](configuring-assets.md) of textures | Not named |
| `.atlas.json` | An [atlas](configuring-assets.md#atlases) declaration | Not named |
| `Icon.png` at the root | The [application icon](build-and-publish.md#application-icons-and-manifest), not a texture | Not named |

One extension is one kind of asset. Variation within a kind is configuration.

## Named assets

Assets are authored anywhere under `Assets/` in the logic project, organized any way the game likes.
`CapsuleAssets` mirrors that tree, one nested class per folder, and names each file for its name and its
type. A lamp's texture, sheet and sound can share a name and sit together in `Lamp/`. An editor's own
sources can sit beside what it exports, because the build reads only the extensions above and those an importer claims.

A key is a file's path below `Assets/`, forward slashes and no extension, with every folder and the file
name normalized to the kebab form of the identifier it names. `Props/Crate.png` and `props/crate.png` are
one texture with one key `props/crate`, one member `CapsuleAssets.Props.CrateTexture`, and one shipped
path `assets/props/crate.png`. `Stage1`, `stage1` and `stage-1` are one segment, `stage-1`. The runtime
sees keys only. A document names an asset by key and extension, as `"props/crate.png"`, in any spelling of
the key. Two files of one type keying the same, such as `click.wav` and `click.ogg`, fail the build.

What ships beside the executable is the same tree under `assets/`: a texture, an `.ogg` or a float `.wav`
as authored, and a scene document, shader, `r8` texture or integer `.wav` in its compiled form. A sheet, a value clip and a font's description
compile into the game and ship nothing.

## Textures

A `Sprite` is a region of a texture with its own pivot:

```csharp
private static readonly Sprite Bulb = new(CapsuleAssets.Textures.LampTexture, new TextureRegion(0, 0, 16, 16), new Vector2(8f, 16f));
```

A texture's atlas, format and sampling are set in config files ([`configuring-assets.md`](configuring-assets.md)).

## Sprite sheets

A sheet names one texture, the frames it cuts from it, any clips played over those frames, and any
sockets, boxes and events they set. The build compiles it into game code.
`Sprites/lamp.sheet.json` declares `CapsuleAssets.Sprites.LampSheet`, with `Frames.Lit0` a `Sprite`,
`Clips.Flicker` a `SpriteClip`, and `Sockets`, `Boxes` and `Events` classes of names. A misspelt frame or
socket is a build error. The sheet format, its units and playing its clips are [`animation.md`](animation.md).
An importer converting another editor's sheets converts to those units.

## Audio

A sound is a `.wav` or an `.ogg`. The build measures each into an `AudioClip` carrying its duration and
any loop region the file declares. Playing them is [`audio.md`](audio.md).

A `.wav` of 8-, 16-, 24- or 32-bit integer samples ships as FLAC, `Audio/click.wav` as
`assets/audio/click.flac`. FLAC is lossless, and the host decodes it to the source's exact samples when the
clip loads. A `.wav` of float samples, which FLAC cannot carry, ships as authored. The clip keeps its
`.wav` name in code and in scene documents.

## Fonts

A bitmap font is a BMFont `.fnt` description, text flavour, and the `.png` pages it names beside it. Its
metrics, glyphs and kerning compile into the logic assembly as a `BitmapFont`, and the description does
not ship. A page is an ordinary texture that no atlas packs. Drawing text is [`rendering.md`](rendering.md#text).

## Shaders

A shader is an `.fx`, a fragment function the build wraps in the engine's sprite shader and compiles
([`shaders.md`](shaders.md)). It ships compiled once per GPU format, as `.spirv.shader`,
`.dxil.shader`, `.msl.shader` and `.glsl.shader`, and its member carries the parameters the build read
from it. Shaders compile on any desktop operating system with nothing to
install.

## Loading and residency

A scene collects what it needs before it starts, and the runtime preloads it at the scene boundary on
worker threads. The scene starts once the last file has landed. `Scene.CollectAssets`,
`Entity.CollectAssets` and `Component.CollectAssets` are the hooks:

```csharp
protected override void CollectAssets(AssetCollection assets) => assets.Add(_texture);
```

The engine's renderers, audio sources, labels and materials declare what they hold. An entity that
attaches its components in its constructor is preloaded with them. A scene's pool is declared with
`assets.Pool<T>(capacity)` ([Pooling](entities.md#pooling)), and an `EntityPool<T>` a
spawner holds is forwarded from its hook. A resource the scene did not collect loads on first use, logs
that at info, and stays cached for the rest of the scene.

The outgoing scene's resources are released at transition or exit, except those the incoming preload also
uses. A packed texture is resident as its atlas page. A headless run collects the same way and loads no
media. `Scene.CollectPreloads` lets a test read what a scene preloads, and `Run.PrefetchScene` starts a
scene's preload before it is requested.
