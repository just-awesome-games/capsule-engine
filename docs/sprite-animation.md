# Sprite animation

A frame of animation is a `Sprite` — a texel region of a texture with its own pivot — and a clip is an ordered run of frames, each held for a whole number of fixed steps: animation is simulation state and advances on ticks, never on the render clock. A `*.sheet.json` sprite sheet document is the authored form. A sheet's regions are in the texture it names, however that texture ships: the build's [atlas packing](atlases.md) moves texels between files, never regions.

A sheet names one texture, the frames it cuts from it, any clips played over those frames, and any sockets its frames set; frames carry their own regions, pivots and socket points, so a packed atlas of trimmed, mixed-size frames is the model and a uniform grid one way to author it. The sheet is not read at run time: the build turns it into game code under `CapsuleAssets.Sprites`, so a misspelt frame, clip or socket is a compile error and no sheet ships. Playback is documented on `SpriteAnimator`, `SpriteClip` and `AnimationPlayback`.

A socket is a named point on a frame — a muzzle, a hand, a hitpoint — in the same texel space as the pivot, so it moves with the drawing frame by frame the way the pivot does. Nothing in the animator knows of it: the renderer drawing the frame places a child entity at each socket a game has bound (`SpriteRenderer.Socket`), mirrored about the pivot by the renderer's flips and placed by the entity's turn and scale exactly as the frame is, and a game parents what leaves or hangs from that point under the child. A frame need not set every socket the sheet declares; one that does not leaves the child where the last frame that did put it.

## Format

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
| `texture` | The key under the textures root, extension included, of the texture every frame is cut from — `"actors/player.png"` is authored at `Assets/Textures/actors/player.png`. Forward slashes, no empty, `.` or `..` segment; any spelling of the key is accepted ([`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets)). A texture the game does not ship fails the sheet. Geometry is authored here, never inferred from the image. |
| `sockets` | Optional; each carries a `name`. Declared between `texture` and `frames`, as frames set sockets the way clips play frames. Absent or empty declares none, with no `Sockets` class generated; a declared socket no frame sets is an error at the declaration. |
| `frames` | At least one. Each carries `name`, `x`, `y`, `width`, `height`, an optional `pivot` and an optional `sockets`, in that order. |
| `clips` | Optional; each carries `name`, an optional `loop`, and `frames`, in that order. Absent or empty is a sheet of frames only, with no `Clips` class generated. |
| `name` | Unique within its own list and safe as a C# name: letters, digits, `-` and `_`, never starting with a digit. Frames, clips and sockets are separate name spaces. |
| `x`, `y` | The frame's top-left corner in texels of the texture; not negative. |
| `width`, `height` | The frame's extent in texels; at least one on each axis. |
| `pivot` | `[x, y]` in texels of the frame from its own top-left corner, both finite. Absent is that corner, `Sprite.Pivot`'s default. |
| `sockets` (of a frame) | An object mapping a declared socket name to `[x, y]`, in `pivot`'s texel space, both finite. A frame sets the sockets it has a point for and leaves the rest out; a name the sheet does not declare, or one set twice, is an error at the frame. |
| `loop` | Whether the last entry wraps back to the first. Absent is false. |
| `frames` (of a clip) | At least one entry, each naming a `frame` of this sheet and the `ticks` it is held for. |
| `ticks` | Fixed steps, at least one — never milliseconds, so a clip means the same whatever the game steps at. |

A `source` block of `tool`, `path` and `hash` is accepted so a derived sheet may name what it came from; nothing reads it. A sheet the compiler cannot read is `CAP024` at the line of the defect; one cutting from a texture the game does not ship is `CAP025` at the sheet.

## From source to game

Sheets are authored under the logic project's `Assets/Sprites/` and compile in the logic role only. A sheet's key is its path under the sprites root without either extension, normalized as [Named assets](consuming-capsule.md#named-assets) defines, and each directory in it becomes a nested class: `actors/player.sheet.json` declares `CapsuleAssets.Sprites.Actors.Player`, with `Frames.Idle0` a `Sprite` carrying its sockets, `Clips.Idle` a `SpriteClip` and `Sockets.Muzzle` the socket's name. Two sheets sharing a key, two whose names become one C# identifier in the same directory, or a sheet keyed `frames`, `clips` or `sockets` fail the build.

## Authoring tools

A packer's output or an editor's own file enters through an authoring module: a package whose `buildTransitive` targets derive a sheet per source into their own `obj/` space and add each derived sheet to the `AdditionalFiles` item, from a target running `BeforeTargets="CapsuleCollectSheetDocuments"` — the compiler reads sheets, so the seam is that target and not `CoreCompile`, whose hooks run too late for the analyzer configuration the SDK writes before the compile. Each derived item carries `CapsuleAssetDomain=sprites` and `CapsuleAssetPath` set to the key as the module spells it, which the engine normalizes. A module may read `CapsuleAssetSourcesDir` and `CapsuleDotNetHost`, under the same rules as a scene module ([`scenes.md` § Authoring tools](scenes.md#authoring-tools)), and converts its own pivot, point and time models at derivation: pivots and socket points here are texels from the frame's top-left corner, durations are ticks.
