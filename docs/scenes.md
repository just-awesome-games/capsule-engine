\# Scenes

A scene is one world: its ordered contents and a camera. A `*.scene.json` scene document is its serialized
form, data carrying no behaviour. Tile maps are one engine-native entry type, not a separate kind of scene.

## Authoring model

Data and behaviour are separate halves, and a game takes either or both:

| Combination | What the game writes | How it boots |
| --- | --- | --- |
| Document only | `test.scene.json` under the logic project's `Assets/Scenes/`, and no class | `test` registers itself and `RunScene(CapsuleAssets.Scenes.Test)` composes a plain `Scene` from it |
| Document naming a `baseScene` | that document, naming an abstract `class Base : Scene` under `baseScene`, and no class of its own | The generator emits a sealed scene deriving from `Base`, and `RunScene(CapsuleAssets.Scenes.Test)` composes it from the document |
| Document and class | that document, plus `class Test : Scene` with the constructor `public Test(SceneContent content) : base(content)` | `RunScene(CapsuleAssets.Scenes.Test)` loads the document and then constructs `Test`. Name the document by its key even when a class claims it, because the key survives adding or removing the class. `RunScene<Test>()` also works |
| Class only | `class Test : Scene` with a public parameterless constructor | `RunScene<Test>()` runs the scene as it builds itself |

The `SceneContent` constructor is the opt-in. Taking one and handing it to `base` claims the document keyed
as the class's namespace names ([Entries and composition](#entries-and-composition)), unless
`[SceneDocument("key")]` names another. A class declaring both constructor shapes is a compile error. A
composed scene's assets are collected before `OnStart` ([`assets.md`](assets.md#loading-and-residency)).

Every document has a generated key constant under `CapsuleAssets.Scenes`, one nested class per directory.
`halls/hall` is `CapsuleAssets.Scenes.Halls.Hall`. `--scene` takes a scene class name or a document key, and a
class name wins when a value is both.

## Format

`SceneDocumentFile` reads and writes format version 6 as two-space-indented UTF-8 JSON with LF endings and
one trailing newline, so a canonical document is a fixed point of the importer. A document is one uniform
list of entries:

```json
{
  "formatVersion": 6,
  "size": [320, 192],
  "scrollOrigin": [0, 192],
  "ambient": "#484c68",
  "entities": [
    {
      "id": 1,
      "type": "tile-map",
      "x": 0,
      "y": 0,
      "properties": {
        "tileSize": 16,
        "width": 4,
        "height": 2,
        "texture": "terrain.png",
        "columns": 4,
        "tileTypes": [
          { "type": "empty" },
          { "type": "ground", "cell": 0, "layer": "solid" },
          { "type": "ledge", "cell": 2, "layer": "ledge", "collidableFaces": ["top"] }
        ],
        "tiles": [
          0, 0, 2, 0,
          1, 1, 1, 1
        ]
      },
      "zIndex": -10
    },
    { "id": 2, "type": "coin", "x": 8, "y": 0 },
    { "id": 3, "type": "banner", "x": 32, "y": 0, "scale": [2, 3], "zIndex": 10 },
    { "id": 4, "type": "hills", "x": 0, "y": 100, "zIndex": -20, "scrollFactor": [0.5, 1] }
  ],
  "nextEntityId": 5
}
```

A top-level key sets the `Scene` property of the same name before any subclass constructor body runs, and
code assigning that property still wins. The top-level keys run in the order `formatVersion`, `baseScene`,
`camera`, `size`, `scrollOrigin`, `clearColor`, `ambient`, `sampling`, `entities`, `nextEntityId`, `source`.

- `formatVersion` is required and must be supported.
- `baseScene` names an abstract `Scene` subclass. The generator emits the sealed scene deriving from it
  and registers the document as that scene. Absent composes a plain `Scene`. The base must be
  abstract because a template is never a loadable scene itself.
- `camera` names a concrete `Camera` subclass with an accessible parameterless constructor, installed by
  the `Scene(SceneContent)` constructor. Absent leaves the scene's default camera in place, and a
  subclass assigning `Camera` in its own constructor body still wins.
- `size` is `[w, h]`, both finite and greater than zero, and sets `Scene.Size`. Absent keeps the extent of the
  document's tile maps.
- `scrollOrigin` is `[x, y]`, both finite. It is the camera corner at which every layer sits as authored, written as `ScrollOrigin` to every camera the
  composed scene installs. Absent leaves each camera its own, zero unless it set one.
- `clearColor` is `"#rrggbb"` or `"#rrggbbaa"` with an `ff` alpha, and sets `Scene.ClearColor`. Code spells
  the same value `ColorRgba.FromHex("#484c68")`. Hex reads in either case and is written lowercase as
  `"#rrggbb"`. There is no shorthand or named form.
- `ambient` is a colour in the same form and sets `Scene.Ambient`.
- `sampling` is `"linear"` or `"point"` and sets `Scene.Sampling`. Absent keeps the game's setting.
- Every entry carries `id`, `type`, `x` and `y` in that order, all required. `scale`, `zIndex`,
  `scrollFactor` and then `properties` follow where the entry carries them.
- `scale` is `[x, y]`, both finite and greater than zero, and absent is identity. It is the raw authored
  factor, and the entity's constructor decides what it scales. A `scale` on a `tile-map` entry is rejected.
- `zIndex` is the entry's draw band. What draws later is the higher sum of the band and the renderer's own
  offset within it, ties broken by file order and then attachment order. The spawn carries the authored band
  to the entity's constructor, which applies it before its own body runs and may override it. The writer
  emits the field only where the entry authors one. On a `tile-map` entry it applies to the composed map.
- `scrollFactor` is `[x, y]`, both finite, and behaves like `zIndex`: carried to the constructor,
  overridable, emitted only where authored. On a `tile-map` entry it applies to the composed map, and a map
  whose palette names a collision layer is rejected with it.
- IDs are unique, positive and lower than `nextEntityId`, and deleted IDs are not reused. `entities` may be
  empty.
- A `source` block records tool, relative source path and SHA-256 of the source closure. Its presence marks
  a derived file, so an authoring source omits it.
- `properties` is a contract per entry type, consumed by whatever constructs that entry, and not a
  set-by-name bag. Only the engine's `tile-map` declares one. Properties on any other type are rejected at
  parse.

Invalid documents throw `SceneDocumentFormatException`.

### The tile map entry

`tile-map` is reserved by the engine. A document may carry any number, interleaved with game entities, all
anchored at the world origin and drawn by their `zIndex` bands. Its properties are `tileSize`, `width`,
`height`, `texture`, `columns`, `tileTypes` and `tiles`. Palette index 0 is `empty`, carrying neither cell nor
layer. `tiles` holds `width x height` palette indices, one grid row per line so a map reads as its shape.
Each other palette entry carries a `type` name and may carry:

| Field | Meaning |
| --- | --- |
| `texture` | The key under the textures root, extension included, of the texture every drawn tile is cut from, spelt any way ([`assets.md`](assets.md#named-assets)). Forward slashes, no empty, `.` or `..` segment. Absent on a grid that draws nothing. |
| `columns` | How many cells wide that texture is. Required with `texture`, at least 1, absent without one. |
| `cell` | Which cell of the texture a tile of this type draws, counted across a row of `columns` then down from cell 0, square at `tileSize`. Absent is a semantic tile: queryable, may collide, draws nothing. |
| `layer` | The collision layer every tile of this type is on, one name the game owns. A query or mover meets the tile when its own filter names that layer. Absent is decoration. Several types may share a layer. |
| `collidableFaces` | Which sides collide, an array of `"left"`, `"right"`, `"top"` or `"bottom"` (grid directions in a Y-down world). Absent is all four, a solid tile, and a side shared with an adjacent four-sided tile generates no contact. A smaller set is that many one-directional edges, each stopping what crosses it into the tile. |

A `cell` on a grid naming no `texture`, a `texture` no entry draws a cell of, an unknown face name,
`collidableFaces` on a tile with no `layer`, and an empty `collidableFaces` fail the document. A tile map
whose palette collides with nothing registers no collider ([`collision.md`](collision.md#terrain)).
`TileMap.SetTile` changes what a cell draws and collides as at run time, `TileMap.RemoveTile` clears it,
`TileMap.TileAt` reads it, and `TileMap.CellAt` finds the cell a world position falls in.

### Entries and composition

Every `type` other than `tile-map` names an entity class in the game's own logic assembly, claimed the way a
scene claims a document. A concrete `Entity` with one public constructor taking an `EntitySpawn` (beside any
other constructor) claims the key its namespace names, and `[SpawnType("key")]` names another key.

One rule covers scenes, entities and cameras: the type's namespace under the assembly's root namespace,
minus a leading `Scenes`, `Entities` or `Cameras` segment and minus a trailing segment repeating the
type's own name, kebab-cased per segment and joined with `/`, then the kebab-cased type name.
`MyGame.Entities.Enemies.Bat` claims `enemies/bat`, `MyGame.Entities.Player.Player` claims `player`,
`MyGame.Scenes.Stage1.Room01` claims `stage-1/room-01`, and `MyGame.Scenes.Crowd1k` claims `crowd-1k`.
A type outside the root namespace claims its kebab-cased name. A spawn type no class claims fails the
scene at load. A claiming constructor that does
not pass its spawn to a base constructor taking one is `CAP026` at that constructor.

## From source to game

Documents are authored under the logic project's `Assets/Scenes/`. The build validates each, re-emits it
canonically under `obj/`, stamps its provenance, and copies it to `assets/scenes/<key>.scene.json` beside the
executable. A document's key is its path under the scenes root without either extension, normalized as
[named assets](assets.md#named-assets) defines. A document registers itself: the class whose own key matches
composes it, and one no class claims composes a plain `Scene`. Two sources
sharing a key fail the build, and derived documents are not committed. The logic role imports scenes on its
own, and any other project opts in with `CapsuleImportScenes`. `CapsuleTileSize` declares the tile size every
scene must match ([`build-and-publish.md`](build-and-publish.md#build-properties)).

## Authoring tools

An editor's own format enters through an authoring module: a package whose `buildTransitive` targets derive a
document per source into their own `obj/` space and add each to the `CapsuleSceneDocument` item from a target
running `BeforeTargets="CapsuleCollectSceneDocuments"`. The engine validates, canonicalizes and ships them
like hand-authored documents, preserving the module's `source` block. A module states each document's key as
`%(CapsuleDocumentKey)`: the root-relative path with no extension, `/`-joined segments of ASCII letters,
digits, hyphens and underscores, none a reserved Windows device name. A document naming none is keyed by its
stem at the root. The engine normalizes the key, so no module implements the key rule. Sprite sheets enter
the same way, on `CapsuleSheetDocument` ([`assets.md`](assets.md#authoring-tools)).

Three rules hold a module's targets:

- Read `CapsuleImportScenes`, `CapsuleAssetSourcesDir`, `CapsuleTileSize` and `CapsuleDotNetHost` only inside
  targets. NuGet imports package targets in no promised order, so a property a role derives is final at
  execution time and not at evaluation.
- Collect a glob first and set its key in a second item group, naming the metadata qualified as
  `%(MyModuleSource.RecursiveDir)`. `%(RecursiveDir)` on a glob's own `Include` inside a target batches over
  the target and comes back empty.
- Carry `Exclude="@(_CapsuleDevelopmentOnly)"` on every authoring glob, or a directory a game marked
  development-only still ships through the module's format
  ([`build-and-publish.md`](build-and-publish.md#development-only-directories)).

JAG Studios publishes the Tiled module as `JAG.Capsule.Tiled` from
[capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
