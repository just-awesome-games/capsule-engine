# Sprite animation

A frame of animation is a `Sprite` — a texel region of a texture with its own pivot — and a clip is an ordered run of frames, each held for a whole number of fixed steps. Animation is simulation state: the frame an entity is on is deterministic, assertable headlessly, and readable by gameplay, because it advances on ticks and never on the render clock.

A `*.sheet.json` **sprite sheet document** is the authored form. Capsule never packs an atlas: packing is authoring, done by an editor's export, an open-source packer, or a script that writes this document directly.

## Authoring model

A sheet names one texture, the frames it cuts from it, and the clips played over those frames. Frames carry their own regions and pivots, so a packed atlas of trimmed, mixed-size frames is the model and a uniform grid is only one way to author it.

Nothing is read at run time. The build turns every sheet into game code beside `GameAssets`, so a misspelt frame or clip is a compile error and no sheet ships beside the executable.

```csharp
using Capsule.Scenes.Animation;
using Capsule.Scenes.Rendering;

public sealed class Player : Entity
{
    private readonly SpriteRenderer _sprite;
    private readonly SpriteAnimator _animator;

    public Player(EntitySpawn spawn) : base(spawn.Position)
    {
        _sprite = new SpriteRenderer(GameSprites.Player.Frames.Idle0);
        Add(_sprite);
        _animator = new SpriteAnimator(_sprite);
        Add(_animator);
    }

    protected override void OnStep(in StepContext context) =>
        _animator.Play(Walking ? GameSprites.Player.Clips.Walk : GameSprites.Player.Clips.Idle);
}
```

`SpriteAnimator` is named the renderer it drives rather than finding one, so an entity drawing itself as several sprites animates whichever of them it says. It owns that renderer's `Sprite` and nothing else: `Offset`, `Scale`, `FlipX`, `FlipY` and `Color` stay the game's. `Play` draws its first frame at once and holds it for exactly its own ticks, counted from the frame view of the step it was called in, so a one-tick frame started from an entity's step is drawn for that step. It ignores the clip already playing, so a walk cycle asked for every step keeps running; `Play(clip, restart: true)` replays it from the start. A looping clip never finishes; one that does not holds its last frame and reports `IsFinished` once that frame's ticks have elapsed.

`AnimationPlayback` is the tick cursor underneath — frame index, ticks elapsed, loop wrap, finished, restart — and knows nothing of sprites or renderers. `SpriteClip` composes it with a frame table.

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
| `texture` | The file name, extension included, of the texture every frame is cut from — `"player.png"` is authored at `asset-sources/textures/player.png`. That directory is flat, so the name carries no path segments, and a texture the game does not ship fails the document. Geometry is authored here and never inferred from the image. |
| `frames` | At least one. Each carries `name`, `x`, `y`, `width`, `height` and an optional `pivot`, in that order. |
| `clips` | At least one. Each carries `name`, an optional `loop`, and `frames`, in that order. |
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
GameSprites.Player.Frames.Idle0   // a Sprite
GameSprites.Player.Clips.Idle     // a SpriteClip
```

The sheet's own name becomes the class, so `player.sheet.json` declares `GameSprites.Player`; two sources sharing a stem fail the build. Derived documents are never committed and nothing ships under `assets/`.

The logic role imports sheets on its own; any other project that has to compile against a game's frames and clips opts in with `<CapsuleImportSprites>`, a project property named in [`consuming-capsule.md`](consuming-capsule.md). The process behind the hook is `Capsule.Build` itself, packed unlisted under the package's `tools/`.

## Authoring tools

The engine's build wires one format: `*.sheet.json`. A packer's output or an editor's own file enters through an authoring module — a package whose `buildTransitive` targets derive a document per source into their own `obj/` space and add each derived document to the `CapsuleSheetDocument` item from a target that runs `BeforeTargets="CapsuleCollectSheetDocuments"`. The engine then validates and canonicalizes those documents exactly as hand-authored ones, preserving the module's `source` block. A module converts its own pivot model and time base at derivation, and may read `CapsuleImportSprites`, `CapsuleAssetSourcesDir` and `CapsuleDotNetHost` inside its targets — never at evaluation, since NuGet imports package targets in no promised order.
