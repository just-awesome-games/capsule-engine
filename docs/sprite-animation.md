# Sprite animation

A frame of animation is a `Sprite` — a texel region of a texture with its own pivot — and a clip is an ordered run of frames, each held for a whole number of fixed steps. Animation is simulation state: it advances on ticks and never on the render clock. A `*.sheet.json` **sprite sheet document** is the authored form, and Capsule never packs an atlas: packing is authoring, done by an editor's export, a packer, or a script that writes this document directly.

## Authoring model

A sheet names one texture, the frames it cuts from it, and any clips played over those frames. Frames carry their own regions and pivots, so a packed atlas of trimmed, mixed-size frames is the model and a uniform grid is only one way to author it.

The sheet itself is not read at run time: the build turns it into game code under `CapsuleAssets.Sprites`, so a misspelt frame or clip is a compile error and no sheet ships beside the executable.

Runtime animation behaviour is documented on `SpriteAnimator`, `SpriteClip` and `AnimationPlayback`.

## Format

The build reads format version 1 as UTF-8 JSON.

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
| `texture` | The key under the textures root, extension included, of the texture every frame is cut from — `"player.png"` is authored at `Assets/Textures/player.png`, `"actors/player.png"` at `Assets/Textures/actors/player.png`. Forward slashes only, with no empty, `.` or `..` segment; any spelling of the key is accepted and the generated handle carries the key, per [`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets). A texture the game does not ship fails the sheet. Geometry is authored here and never inferred from the image. |
| `frames` | At least one. Each carries `name`, `x`, `y`, `width`, `height` and an optional `pivot`, in that order. |
| `clips` | Optional; each carries `name`, an optional `loop`, and `frames`, in that order. Absent or empty is a sheet of frames only — a static sprite is one frame a `SpriteRenderer` draws with no animator — and no `Clips` class is generated for it. |
| `name` | Unique within its own list and safe as a C# name: letters, digits, `-` and `_`, never starting with a digit. Frames and clips are separate name spaces, so a frame and a clip may share one. |
| `x`, `y` | The frame's top-left corner in texels of the texture; not negative. |
| `width`, `height` | The frame's extent in texels; at least one on each axis. |
| `pivot` | `[x, y]` in texels of the frame from its own top-left corner, both finite. Absent is that corner, which is what `Sprite.Pivot` defaults to. |
| `loop` | Whether the last entry wraps back to the first. Absent is false. |
| `frames` (of a clip) | At least one entry, each naming a `frame` of this sheet and the `ticks` it is held for. |
| `ticks` | Fixed steps, at least one — never milliseconds. The document declares no rate, so a clip means the same thing whatever the game steps at. |

A `source` block of `tool`, `path` and `hash` is accepted so a derived sheet may name what it came from; nothing reads it.

A sheet the compiler cannot read is `CAP024` at the line the defect is on; one cutting from a texture the game does not ship is `CAP025` at the sheet.

## From source to game

Games author sheets under the logic project's `Assets/Sprites/`, and the compiler reads each one and emits its frames and clips as members of the logic assembly's own asset registry:

```csharp
CapsuleAssets.Sprites.Player.Frames.Idle0   // a Sprite
CapsuleAssets.Sprites.Player.Clips.Idle     // a SpriteClip
```

A sheet's key is its path under the sprites root without either extension, normalized as [`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets) defines, and each directory in it becomes a nested class: `actors/player.sheet.json` declares `CapsuleAssets.Sprites.Actors.Player`. Two sheets sharing a key, or two whose names become one C# identifier in the same directory, fail the build, as does a sheet keyed `frames` or `clips` — the two classes a sheet declares inside itself. Nothing ships under `assets/` for a sheet.

Sheets compile in the logic role only.

## Authoring tools

The engine's build wires one format: `*.sheet.json`. A packer's output or an editor's own file enters through an authoring module: a package whose `buildTransitive` targets derive a sheet per source into their own `obj/` space and add each derived sheet to the `AdditionalFiles` item from a target that runs `BeforeTargets="CapsuleCollectSheetDocuments"`. A sheet is read by the compiler rather than by the build tool, and the metadata that says what it is reaches the generator through the analyzer configuration the SDK writes before the compile — which is why the seam is that target and not `CoreCompile`, whose hooks are a build too late to be captured.

Each derived item carries `CapsuleAssetDomain=sprites` and `CapsuleAssetPath` set to the key as the module spells it: the sheet's path under the sprites root, forward slashes, carrying neither extension. The engine normalizes that key, per [`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets), so a module spells one however its own authoring tree does and implements no part of the rule.

The properties a module may read are `CapsuleAssetSourcesDir` and `CapsuleDotNetHost`. The same two hazards a scene module works under apply unchanged here — read Capsule properties only inside targets, and never `%(RecursiveDir)` on a glob's own `Include` — with `Exclude="@(_CapsuleDevelopmentOnly)"` on every authoring glob, as [`scenes.md` § Authoring tools](scenes.md#authoring-tools) states in full.

A module converts its own pivot model and time base at derivation: this format's pivots are texels of the frame from its top-left corner, and its durations are ticks of the game's fixed step.
