# Scenes

A scene is one world: its ordered contents and a camera. A `*.scene.json` scene document is its serialized
form, data carrying no behaviour. Tile maps are one engine-native entry type, not a separate kind of scene.

## Authoring model

Data and behaviour are separate halves, and a game takes either or both:

| Combination | What the game writes | How it boots |
| --- | --- | --- |
| Document only | `Assets/Scenes/test.scene.json` in the logic project, and no class | `scenes/test` registers itself and `RunScene(CapsuleAssets.Scenes.TestScene)` composes a plain `Scene` from it |
| Document naming a `baseScene` | that document, naming an abstract `class Base : Scene` under `baseScene`, and no class of its own | The generator emits a sealed scene deriving from `Base`, and `RunScene(CapsuleAssets.Scenes.TestScene)` composes it from the document |
| Document and class | that document, plus `class Test : Scene` in `MyGame.Scenes` with the constructor `public Test(SceneContent content) : base(content)` | `RunScene(CapsuleAssets.Scenes.TestScene)` loads the document and then constructs `Test`. Name the document by its key even when a class claims it, because the key survives adding or removing the class. `RunScene<Test>()` also works |
| Class only | `class Test : Scene` with a public parameterless constructor | `RunScene<Test>()` runs the scene as it builds itself |

The `SceneContent` constructor is the opt-in. Taking one and handing it to `base` claims the document at
the path the class's namespace names under `Assets/`, so `MyGame.Scenes.Test` claims
`Assets/Scenes/test.scene.json`, unless `[SceneDocument("key")]` names another. A class declaring both constructor shapes is a compile error. A
composed scene's assets are collected before `OnStart` ([`assets.md`](assets.md#loading-and-residency)).

Every document has a generated `SceneKey` in `CapsuleAssets`, one nested class per folder.
`Assets/Scenes/halls/hall.scene.json` is `CapsuleAssets.Scenes.Halls.HallScene`, whose `Name` is
`scenes/halls/hall`. `--scene` takes a scene class name or a document key, and a
class name wins when a value is both.

## Format

`SceneDocumentFile` reads and writes format version 7 as two-space-indented UTF-8 JSON with LF endings and
one trailing newline. A canonical document is a fixed point of the importer. A document is one uniform
list of entries:

```json
{
  "formatVersion": 8,
  "size": [320, 192],
  "scrollCenter": [160, 288],
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
          { "name": "empty" },
          { "name": "ground", "cell": 0, "layer": "solid" },
          { "name": "ledge", "cell": 2, "layer": "ledge", "oneWay": true }
        ],
        "tiles": [
          0, 0, 2, 0,
          1, 1, 1, 1
        ]
      },
      "zIndex": -10
    },
    { "id": 2, "type": "coin", "x": 8, "y": 0 },
    { "id": 3, "type": "banner", "x": 32, "y": 0, "rotation": 90, "scale": [2, 3], "zIndex": 10 },
    { "id": 4, "type": "hills", "x": 0, "y": 100, "zIndex": -20, "scrollFactor": [0.5, 1] }
  ],
  "nextEntityId": 5
}
```

A top-level key sets the `Scene` property of the same name before any subclass constructor body runs, and
code assigning that property still wins. The top-level keys run in the order `formatVersion`, `baseScene`,
`camera`, `size`, `scrollCenter`, `clearColor`, `ambient`, `sampling`, `properties`, `entities`, `nextEntityId`, `source`.

- `formatVersion` is required and must be supported.
- `baseScene` names an abstract `Scene` subclass. The generator emits the sealed scene deriving from it
  and registers the document as that scene. Absent composes a plain `Scene`. The base must be
  abstract.
- `camera` names a concrete `Camera` subclass with an accessible parameterless constructor, installed by
  the `Scene(SceneContent)` constructor. Absent leaves the scene's default camera in place, and a
  subclass assigning `Camera` in its own constructor body still wins. The generated registration
  supplies the camera. A `SceneContent` built by hand from a document carries no camera, `properties` or
  tile types, and composes the scene with its default camera and every palette entry as a plain `TileType`.
  A test composes a document as a run does through `CapsuleScenes.Registry`, which the build generates into
  `Capsule.Generated` beside `CapsuleEntities.Registry`. `Create(SceneKey, SceneDocument)` composes a shipped
  document's key into its class. `Content<TScene>(SceneDocument)` returns the content for any scene class,
  abstract included, with its `properties` applier and the tile types but no camera. A test subclass
  constructs from it, as in `new TestRoom(CapsuleScenes.Registry.Content<PlayableRoom>(document))`.
- `size` is `[w, h]`, both finite and greater than zero, and sets `Scene.Size`. Absent keeps the extent of the
  document's tile maps.
- `scrollCenter` is `[x, y]`, both finite. It is the camera centre at which every layer sits as authored, written as `ScrollCenter` to every camera the
  composed scene installs. Absent leaves each camera its own, which is half its viewport unless it set one.
- `clearColor` is `"#rrggbb"` or `"#rrggbbaa"` with an `ff` alpha, and sets `Scene.ClearColor`. Code spells
  the same value `ColorRgba.FromHex("#484c68")`. Hex reads in either case and is written lowercase as
  `"#rrggbb"`. There is no shorthand or named form.
- `ambient` is a colour in the same form and sets `Scene.Ambient`.
- `sampling` is `"linear"` or `"point"` and sets `Scene.Sampling`. Absent keeps the game's setting.
- `properties` is an object whose keys set the composing class's `[Authorable]` members ([Properties](#properties)).
- Every entry carries `id`, `type`, `x` and `y` in that order, all required. `rotation`, `scale`,
  `zIndex`, `scrollFactor` and then `properties` follow where the entry carries them.
- `rotation` is the turn in degrees, clockwise on screen, and absent is 0. The writer emits it only where
  it is non-zero. The spawn carries it in radians to the entity's constructor, which applies it before its
  own body runs and may override it. A class adding a collider or body to a turned entity fails there. A
  `rotation` on a `tile-map` entry is rejected.
- `scale` is `[x, y]`, both finite and greater than zero, and absent is identity. It is the raw authored
  factor, and the entity's constructor decides what it scales. A `scale` on a `tile-map` entry is rejected.
- `zIndex` is the entry's draw band ([`rendering.md`](rendering.md#two-layers-and-draw-order)). The spawn
  carries the authored band to the entity's constructor, which applies it before its own body runs and may
  override it. The writer emits the field only where the entry authors one. On a `tile-map` entry it
  applies to the composed map.
- `scrollFactor` is `[x, y]`, both finite, and behaves like `zIndex`: carried to the constructor,
  overridable, emitted only where authored. On a `tile-map` entry it applies to the composed map, and a map
  whose palette names a collision layer is rejected with it.
- IDs are unique, positive and lower than `nextEntityId`, and deleted IDs are not reused. `entities` may be
  empty.
- A `source` block records tool, relative source path and SHA-256 of the source closure. Its presence marks
  an imported file, and an authoring source omits it.
- `properties` is an object, and its contract belongs to the entry's type. The engine's `tile-map` declares
  its own below, and a game entity's class declares the rest ([Properties](#properties)).

Invalid documents throw `SceneDocumentFormatException`.

A root `"$schema"` key may name the format's published JSON Schema, `https://raw.githubusercontent.com/just-awesome-games/capsule-engine/main/schemas/scene.schema.json`, as [Editor completion](configuring-assets.md#editor-completion) describes. The reader ignores the key, and the writer never emits it. The local copies are for reading.

### The tile map entry

`tile-map` is reserved by the engine. A document may carry any number, interleaved with game entities, all
anchored at the world origin and drawn by their `zIndex` bands. Its properties are `tileSize`, `width`,
`height`, `texture`, `columns`, `tileTypes`, `tiles` and `transforms`. Palette index 0 is named `empty` and
carries nothing else. `tiles` holds `width x height` palette indices, one grid row per line so a map reads
as its shape. `transforms` is an optional grid of the same shape that mirrors or turns each tile's drawing
and collision shape: 1 mirrors it left to right, 2 top to bottom, 4 swaps its axes before either, the sum
combines them, and absent is all 0.
Each other palette entry carries a `name`, unique in its palette, and may carry:

| Field | Meaning |
| --- | --- |
| `texture` | The texture's key, extension included, of the texture every drawn tile is cut from, spelt any way ([`assets.md`](assets.md#named-assets)). Forward slashes, no empty, `.` or `..` segment. Absent on a grid that draws nothing. |
| `columns` | How many cells wide that texture is. Required with `texture`, at least 1, absent without one. |
| `type` | The key of the `TileType` subclass the entry composes, named the way an entity entry's `type` names its class. Absent is a plain `TileType`. |
| `cell` | Which cell of the texture a tile of this type draws, counted across a row of `columns` then down from cell 0, square at `tileSize`. Absent is a semantic tile: queryable, may collide, draws nothing. |
| `layer` | The collision layer every tile of this type is on, one name the game owns. A query or mover meets the tile when its own filter names that layer. Absent is decoration. Several entries may share a layer. |
| `shape` | The convex polygon the tile collides as, three or four `[x, y]` points in pixels from the tile's top-left corner with Y down, each within `[0, tileSize]`. `[[0, 16], [16, 0], [16, 16]]` is a 16-pixel slope rising to the right. Absent is the whole tile. |
| `oneWay` | `true` for a tile that blocks only a body coming down onto it from above ([`collision.md`](collision.md#one-way-surfaces)). Absent is `false`. |
| `solidSides` | `true` for a `oneWay` tile that also blocks from the sides and passes a body only from below. Without `oneWay` it fails the document. Absent is `false`. |
| `properties` | An object setting the members the entry's class marks `[Authorable]`, as an entity's [`properties`](#properties) do. |

A `cell` on a grid naming no `texture`, a `texture` no entry draws a cell of, a `shape` or `oneWay` on a
tile with no `layer`, and a `shape` that is not convex or reaches outside its tile fail the document. A tile map
whose palette collides with nothing registers no collider ([`collision.md`](collision.md#terrain)).
`TileMap.SetTile` changes what a cell draws and collides as at run time, `TileMap.RemoveTile` clears it,
`TileMap.TileAt` reads it, and `TileMap.CellAt` finds the cell a world position falls in.

A palette entry is an instance of `TileType`, or of the subclass its `type` names. One instance serves every
cell painted with it. State that belongs to one cell lives on an entity. `TileMap.TileAt` and
`TileContact2D.Type` return that instance, read as `map.TileAt(x, y).Name` or matched as
`map.TileAt(x, y) is Ice ice`. `SetTile` paints by name.
The build checks a palette entry's `type` and `properties` like an entity entry's. A tile type needs an
accessible parameterless constructor and refuses `Required = true`, entity references and `required` members.

### Entries and composition

Every `type` other than `tile-map` names an entity class in the game's own logic assembly, claimed the way a
scene claims a document. A concrete `Entity` with one public constructor taking an `EntitySpawn` (beside any
other constructor) claims the key its namespace names, and `[SpawnType("key")]` names another key.

The spawn is what every entity honours: position, rotation, scale, band and scroll factor. A property is
what one class declares. Code places the same entity through the same constructor with
`new EntitySpawn(position) { Rotation = turn }`, and that spawn has id 0 and no type.

One rule covers entities, cameras, tile types and a document's `baseScene`: the type's namespace under the
assembly's root namespace, minus a leading `Entities`, `Cameras`, `Tiles` or `Scenes` segment and minus a
trailing segment repeating the type's own name, kebab-cased per segment and joined with `/`, then the
kebab-cased type name. `MyGame.Entities.Enemies.Bat` claims `enemies/bat`, `MyGame.Entities.Player.Player`
claims `player`, `MyGame.Tiles.Ice` claims `ice`, and `MyGame.Scenes.PlayableRoom` is the `baseScene`
`playable-room`. A class claiming a document keeps the leading segment, since the document's key is its path:
`MyGame.Scenes.Stage1.Room01` claims `scenes/stage-1/room-01`.
A type outside the root namespace claims its kebab-cased name. A spawn type no class claims fails the
build (`CAP034`), and fails the scene at load in a document the build never saw. A claiming constructor that
does not pass its spawn to a base constructor taking one is `CAP026` at that constructor.

#### Properties

An entry's `properties` object sets the members its class marks `[Authorable]`:

```csharp
[Authorable]
public float Rise { get; set; } = 64f;
```

```json
{ "id": 11, "type": "lift", "x": 496, "y": 170, "properties": { "rise": 40 } }
```

The XML documentation on `AuthorableAttribute` states how a key is named and each type's JSON form. The
build names what a member cannot take.

The document's top-level `properties` sets the members of the class composing the scene the same way: the class
claiming the document, or the `baseScene` it names. A plain `Scene` declares none. The values land after every entry
is built and before the derived constructor body runs. That body can read a reference. An assignment in that body
wins. `type`, `baseScene` and `camera` pick what to construct, the engine's own fields are typed top-level keys, and a
class's own members go under `properties`, as `"properties": { "music": "audio/music/room.ogg" }`.

## From source to game

Documents are authored anywhere under the logic project's `Assets/`. The build validates each, re-emits it
canonically, stamps its provenance, and ships it gzipped at `assets/<key>.scene.json.gz` beside the
executable. `gzip -d` restores the compact JSON. A document's key is its source path without either
extension, keyed as [named assets](assets.md#named-assets) defines. A document registers itself: the class claiming its key composes it, and one no class claims
composes a plain `Scene`. Two sources
sharing a key fail the build, and imported documents are not committed. The logic role imports scenes on its
own, and any other project opts in with `CapsuleBuildAssets`. `CapsuleBuild.WithTileSize` in the build project
declares the tile size every scene must match ([`build-and-publish.md`](build-and-publish.md#the-build-project)).

## Authoring tools

An editor's own format enters through an authoring module: a package that ships an
[importer](build-and-publish.md#writing-an-importer). It writes a document per source at the source's path,
and the engine validates, canonicalizes and ships each like a hand-authored document at that path,
preserving the module's `source` block. The engine keys it as [named assets](assets.md#named-assets)
defines, and no module implements the key rule. A publish leaves out a document imported under a
[development-only directory](build-and-publish.md#development-only-directories). Sprite sheets and textures
enter the same way ([`assets.md`](assets.md#authoring-tools)).

JAG Studios publishes the Tiled module as `JAG.Capsule.Tiled` from
[capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
