# Sprite animation

A frame of animation is a `Sprite` — a texel region of a texture with its own pivot — and a clip is an ordered run of frames, each held for a whole number of fixed steps. Animation is simulation state: it advances on ticks and never on the render clock, so the frame an entity is on is deterministic, assertable headlessly, and readable by gameplay.

A `*.sheet.json` **sprite sheet document** is the authored form. Capsule never packs an atlas: packing is authoring, done by an editor's export, a packer, or a script that writes this document directly.

## Authoring model

A sheet names one texture, the frames it cuts from it, and any clips played over those frames. Frames carry their own regions and pivots, so a packed atlas of trimmed, mixed-size frames is the model and a uniform grid is only one way to author it. A pose variant — the same motion drawn with a weapon raised — is authored as its own clip of the same frame count, per-frame ticks and `loop`, and is entered mid-motion by playing it at the animator's `Tick`.

The sheet itself is not read at run time: the build turns it into game code under `CapsuleAssets.Sprites`, so a misspelt frame or clip is a compile error and no sheet ships beside the executable. The windowed host preloads the textures identified by a sprite renderer and its animator's current clips; a clip introduced later loads its texture on first rendered use unless `CollectAssets` preloaded it.

```csharp
using Capsule.Scenes.Animation;
using Capsule.Scenes.Rendering;

public sealed class Player : Entity
{
    private readonly SpriteRenderer _sprite;
    private readonly SpriteAnimator _animator;

    public Player(EntitySpawn spawn) : base(spawn.Position)
    {
        _sprite = new SpriteRenderer(CapsuleAssets.Sprites.Player.Frames.Idle0);
        Add(_sprite);
        _animator = new SpriteAnimator(_sprite);
        Add(_animator);
    }

    protected override void OnStep(in StepContext context) =>
        _animator.Play(Walking ? CapsuleAssets.Sprites.Player.Clips.Walk : CapsuleAssets.Sprites.Player.Clips.Idle);
}
```

Runtime animation behavior is documented on `SpriteAnimator`, `SpriteClip`, and `AnimationPlayback` in the shipped API reference. Headless animation tests use the scene-simulation pattern in [`consuming-capsule.md` § Testing headlessly](consuming-capsule.md#testing-headlessly).

## Format

`SpriteSheetDocumentFile` reads and writes format version 1 as two-space-indented UTF-8 JSON with LF endings and one trailing newline, so a canonical document is a fixed point of the importer.

```json
{
  "formatVersion": 1,
  "texture": "player.png",
  "frames": [
    { "name": "idle-0", "x": 0, "y": 0, "width": 8, "height": 8, "pivot": [4, 8] }
  ],
  "clips": [
    { "name": "idle", "loop": true, "frames": [ { "frame": "idle-0", "ticks": 30 } ] }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `formatVersion` | Required, and must be supported. |
| `texture` | The path under the textures root, extension included, of the texture every frame is cut from — `"player.png"` is authored at `asset-sources/textures/player.png`, `"actors/player.png"` at `asset-sources/textures/actors/player.png`. Forward slashes only, with no empty, `.` or `..` segment, and a texture the game does not ship fails the document. Geometry is authored here and never inferred from the image. |
| `frames` | At least one. Each carries `name`, `x`, `y`, `width`, `height` and an optional `pivot`, in that order. |
| `clips` | Optional; each carries `name`, an optional `loop`, and `frames`, in that order. Absent or empty is a sheet of frames only — a static sprite is one frame a `SpriteRenderer` draws with no animator — and the writer leaves an empty list out. |
| `name` | Unique within its own list and safe as a C# name: letters, digits, `-` and `_`, never starting with a digit. Frames and clips are separate name spaces, so a frame and a clip may share one. |
| `x`, `y` | The frame's top-left corner in texels of the texture; not negative. |
| `width`, `height` | The frame's extent in texels; at least one on each axis. |
| `pivot` | `[x, y]` in texels of the frame from its own top-left corner, both finite. Absent is that corner, which is what `Sprite.Pivot` defaults to and what the writer emits for it. |
| `loop` | Whether the last entry wraps back to the first. Absent is false, which the writer leaves out. |
| `frames` (of a clip) | At least one entry, each naming a `frame` of this sheet and the `ticks` it is held for. |
| `ticks` | Fixed steps, at least one — never milliseconds. The document declares no rate, so a clip means the same thing whatever the game steps at. |

A `source` block records tool, relative source path, and a hash of the source closure. Its presence marks a derived file, so an authoring source omits it.

Invalid documents throw `SpriteSheetFormatException` and fail the build at the named file.

## From source to game

Games author sheets under `src/asset-sources/sprites/`, and the build validates each one, re-emits it canonically under `obj/`, and renders the whole set as one generated C# file the logic assembly compiles:

```csharp
CapsuleAssets.Sprites.Player.Frames.Idle0   // a Sprite
CapsuleAssets.Sprites.Player.Clips.Idle     // a SpriteClip
```

A sheet's key is its path under the sprites root without either extension, and each directory in it becomes a nested class: `player.sheet.json` declares `CapsuleAssets.Sprites.Player`, `actors/player.sheet.json` declares `CapsuleAssets.Sprites.Actors.Player`. Two sheets of one stem in different directories are two sheets; two sharing a key, or two whose names become one C# identifier in the same directory, fail the build. Derived documents are never committed and nothing ships under `assets/`.

The logic role imports sheets on its own; any other project that has to compile against a game's frames and clips opts in with `<CapsuleImportSprites>`, a project property named in [`consuming-capsule.md`](consuming-capsule.md). The process behind the hook is `Capsule.Build` itself, as it is for scenes.

## Authoring tools

The engine's build wires one format: `*.sheet.json`. A packer's output or an editor's own file enters through an authoring module, on the contract stated in full under [`scenes.md` § Authoring tools](scenes.md#authoring-tools). For sheets: the item is `CapsuleSheetDocument`, the seam target is `CapsuleCollectSheetDocuments`, the properties a module may read are `CapsuleImportSprites`, `CapsuleAssetSourcesDir` and `CapsuleDotNetHost`, and the module converts its own pivot model and time base at derivation — this format's pivots are texels of the frame from its top-left corner, and its durations are ticks of the game's fixed step.
