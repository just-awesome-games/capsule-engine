# Scenes

A scene is one world: its ordered contents and a camera. A `*.scene.json` scene document is its serialized
form, data carrying no behaviour. Tile maps are one engine-native entry type, not a separate kind of scene.

## Authoring model

Data and behaviour are separate halves, and a game takes either or both:

| Combination | What the game writes | How it boots |
| --- | --- | --- |
| Document only | `Assets/Scenes/test.scene.json` in the logic project | `RunScene(CapsuleAssets.Scenes.TestScene)` composes a plain `Scene` from it. |
| Document naming a `baseScene` | that document, naming an abstract `class Base : Scene` | The generator emits a sealed scene deriving from `Base` and composes it from the document. |
| Document and class | that document, plus `class Test : Scene` in `MyGame.Scenes` with the constructor `public Test(SceneContent content) : base(content)` | `RunScene(CapsuleAssets.Scenes.TestScene)` loads the document and constructs `Test`. `RunScene<Test>()` also works. |
| Class only | `class Test : Scene` with a public parameterless constructor | `RunScene<Test>()` runs the scene as it builds itself. |

Name a document by its key even when a class claims it. The key survives adding or removing the class.

The `SceneContent` constructor is the opt-in. A class taking one claims the document at the path its
namespace names under `Assets/`, unless `[SceneDocument("key")]` names another. `MyGame.Scenes.Test`
claims `Assets/Scenes/test.scene.json`. A class declaring both constructor shapes is a compile error.

Every document has a generated `SceneKey` in `CapsuleAssets`, one nested class per folder.
`Assets/Scenes/halls/hall.scene.json` is `CapsuleAssets.Scenes.Halls.HallScene`, whose `Name` is
`scenes/halls/hall`. `--scene` takes a scene class name or a document key, and a class name wins when a
value is both.

## Format

`SceneDocumentFile` reads and writes format version 8. Its canonical form is two-space-indented UTF-8 JSON
with LF endings and one trailing newline. A document is one uniform list of entries:

```json
{
  "formatVersion": 8,
  "size": [320, 192],
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
        ],
        "collider": true
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

The format's JSON Schema documents every field ([Editor completion](configuring-assets.md#editor-completion)).
An invalid document throws `SceneDocumentFormatException`, naming the defect.

A top-level key sets the `Scene` property of the same name before any subclass constructor body runs.
Code assigning that property still wins.

An entry's position, `rotation`, `scale`, `zIndex` and `scrollFactor` reach the entity's constructor as
an `EntitySpawn`, applied as the `Entity(EntitySpawn)` constructor documents.

### The tile map entry

`tile-map` is reserved by the engine. A document may carry any number, interleaved with game entities, all
anchored at the world origin and drawn by their `zIndex` bands. The canonical form writes `tiles` one grid
row per line.

Each palette entry composes a `TileType`, or the subclass its `type` names. A palette entry's `properties`
set that class's `[Authorable]` members, as an entity entry's do. `TileMap.TileAt` and `TileContact2D.Type`
return the entry's instance, read as `map.TileAt(x, y).Name` or matched as `map.TileAt(x, y) is Ice ice`.
`TileMap.SetTile` paints by name. `"collider": true` in the properties gives the map a
`TileMapCollider2D`, and a map without it only draws. How tiles collide is
[`collision.md`](collision.md#terrain).

### Entries and composition

Every `type` other than `tile-map` names an entity class in the game's own logic assembly. A concrete
`Entity` with one public constructor taking an `EntitySpawn` claims the key its namespace names, and
`[SpawnType("key")]` names another key. Code places the same entity through the same constructor with
`new EntitySpawn(position) { Rotation = turn }`.

One rule keys entities, cameras, tile types and a document's `baseScene`. Take the type's namespace below
the assembly's root namespace. Drop a leading `Entities`, `Cameras`, `Tiles` or `Scenes` segment and a
trailing segment repeating the type's own name. Kebab-case each segment, join them with `/`, then append
the kebab-cased type name:

| Type | Key |
| --- | --- |
| `MyGame.Entities.Enemies.Bat` | `enemies/bat` |
| `MyGame.Entities.Player.Player` | `player` |
| `MyGame.Tiles.Ice` | `ice` |
| `MyGame.Scenes.PlayableRoom` as a `baseScene` | `playable-room` |
| `MyGame.Scenes.Stage1.Room01` claiming a document | `scenes/stage-1/room-01` |

A class claiming a document keeps the leading segment, because the document's key is its path. A type
outside the root namespace claims its kebab-cased name. A spawn type no class claims fails the build
(`CAP034`), or fails the scene at load in a document the build never saw.

#### Properties

An entry's `properties` object sets the members its class marks `[Authorable]`:

```csharp
[Authorable]
public float Rise { get; set; } = 64f;
```

```json
{ "id": 11, "type": "lift", "x": 496, "y": 170, "properties": { "rise": 40 } }
```

`AuthorableAttribute` documents how a key is named, when a value lands and each type's JSON form. The
document's top-level `properties` sets the members of the class composing the scene the same way: the
class claiming the document, or the `baseScene` it names.

## From source to game

Documents are authored anywhere under the logic project's `Assets/`. The build validates each, re-emits it
canonically, stamps its provenance in a `source` block, and ships it gzipped at
`assets/<key>.scene.json.gz`. A document's key is its source path without either extension, keyed as
[named assets](assets.md#named-assets) defines. Two sources sharing a key fail the build.
`CapsuleBuild.WithTileSize` declares the tile size every scene must match
([`build-and-publish.md`](build-and-publish.md#the-build-project)).

An editor's own format enters through an authoring module, a package that ships an
[importer](build-and-publish.md#writing-an-importer). The engine validates, canonicalizes, keys and ships
each document it writes like a hand-authored one at that path. JAG Studios publishes the Tiled module as
`JAG.Capsule.Tiled` from [capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
