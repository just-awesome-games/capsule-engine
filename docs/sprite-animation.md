# Sprite animation

A frame of animation is a `Sprite` — a texel region of a texture with its own pivot — and a clip is an ordered run of frames, each held for a whole number of fixed steps: animation is simulation state and advances on ticks, never on the render clock. A `*.sheet.json` sprite sheet document is the authored form. Capsule never packs an atlas; packing is authoring, done by an editor's export, a packer, or a script that writes this document.

A sheet names one texture, the frames it cuts from it, and any clips played over those frames; frames carry their own regions and pivots, so a packed atlas of trimmed, mixed-size frames is the model and a uniform grid one way to author it. The sheet is not read at run time: the build turns it into game code under `CapsuleAssets.Sprites`, so a misspelt frame or clip is a compile error and no sheet ships. Playback is documented on `SpriteAnimator`, `SpriteClip` and `AnimationPlayback`.

## Format

Format version 1, UTF-8 JSON:

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
| `texture` | The key under the textures root, extension included, of the texture every frame is cut from — `"actors/player.png"` is authored at `Assets/Textures/actors/player.png`. Forward slashes, no empty, `.` or `..` segment; any spelling of the key is accepted ([`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets)). A texture the game does not ship fails the sheet. Geometry is authored here, never inferred from the image. |
| `frames` | At least one. Each carries `name`, `x`, `y`, `width`, `height` and an optional `pivot`, in that order. |
| `clips` | Optional; each carries `name`, an optional `loop`, and `frames`, in that order. Absent or empty is a sheet of frames only, with no `Clips` class generated. |
| `name` | Unique within its own list and safe as a C# name: letters, digits, `-` and `_`, never starting with a digit. Frames and clips are separate name spaces. |
| `x`, `y` | The frame's top-left corner in texels of the texture; not negative. |
| `width`, `height` | The frame's extent in texels; at least one on each axis. |
| `pivot` | `[x, y]` in texels of the frame from its own top-left corner, both finite. Absent is that corner, `Sprite.Pivot`'s default. |
| `loop` | Whether the last entry wraps back to the first. Absent is false. |
| `frames` (of a clip) | At least one entry, each naming a `frame` of this sheet and the `ticks` it is held for. |
| `ticks` | Fixed steps, at least one — never milliseconds, so a clip means the same whatever the game steps at. |

A `source` block of `tool`, `path` and `hash` is accepted so a derived sheet may name what it came from; nothing reads it. A sheet the compiler cannot read is `CAP024` at the line of the defect; one cutting from a texture the game does not ship is `CAP025` at the sheet.

## From source to game

Sheets are authored under the logic project's `Assets/Sprites/` and compile in the logic role only. A sheet's key is its path under the sprites root without either extension, normalized as [Named assets](consuming-capsule.md#named-assets) defines, and each directory in it becomes a nested class: `actors/player.sheet.json` declares `CapsuleAssets.Sprites.Actors.Player`, with `Frames.Idle0` a `Sprite` and `Clips.Idle` a `SpriteClip`. Two sheets sharing a key, two whose names become one C# identifier in the same directory, or a sheet keyed `frames` or `clips` fail the build.

## Authoring tools

A packer's output or an editor's own file enters through an authoring module: a package whose `buildTransitive` targets derive a sheet per source into their own `obj/` space and add each derived sheet to the `AdditionalFiles` item, from a target running `BeforeTargets="CapsuleCollectSheetDocuments"` — the compiler reads sheets, so the seam is that target and not `CoreCompile`, whose hooks run too late for the analyzer configuration the SDK writes before the compile. Each derived item carries `CapsuleAssetDomain=sprites` and `CapsuleAssetPath` set to the key as the module spells it, which the engine normalizes. A module may read `CapsuleAssetSourcesDir` and `CapsuleDotNetHost`, under the same rules as a scene module ([`scenes.md` § Authoring tools](scenes.md#authoring-tools)), and converts its own pivot model and time base at derivation: pivots here are texels from the frame's top-left corner, durations are ticks.
