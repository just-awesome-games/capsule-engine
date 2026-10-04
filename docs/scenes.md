# Scenes

A scene is one world: its ordered contents and a camera. A `*.scene.json` scene document is its serialized
form, data carrying no behaviour. The engine's tile map is placed like any other entity.

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

A document is a tree of objects: the scene, its entries, and any object a member holds.

```json
{
  "baseScene": "playable-room",
  "size": [320, 192],
  "ambient": "#484c68",
  "camera": { "type": "room-camera", "lead": 24 },
  "entities": [
    {
      "type": "tile-map",
      "zIndex": -10,
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
    { "id": 3, "type": "lift", "x": 32, "rise": 40 },
    { "type": "switch", "x": 64, "lift": 3, "movement": { "type": "patrol", "speed": 30 } },
    { "type": "door", "x": 96, "exit": { "destination": "scenes/hall", "arriveAt": "west" } },
    { "type": "hills", "y": 100, "zIndex": -20, "scrollFactor": [0.5, 1] }
  ]
}
```

Each JSON object is a C# object. Its reserved keys are structure, and every other key sets the member of that
name its class marks `[Authorable]`:

| Object | Reserved keys | Every other key sets a member of |
| --- | --- | --- |
| The document | `$schema`, `baseScene`, `entities` | the composing scene: the class claiming the document, the `baseScene` it names, or `Scene`, whose own are `size`, `clearColor`, `ambient`, `sampling` and `camera` |
| An entry | `type`, `id`, `x`, `y`, `rotation`, `scale`, `zIndex`, `scrollFactor` | the entity class its `type` names |
| A member's object | `type` | the object the member holds, or else a new one of the member's class or of the subclass its `type` names |

An entry's spawn keys reach the entity's constructor as an `EntitySpawn`, applied as the `Entity(EntitySpawn)`
constructor documents. An absent `x` or `y` is 0. An `id` is needed only on an entry another names, and ids are
unique. A key no member takes fails the scene at load, naming the document, the entry and the key's path, and
one test catches it before a player does ([`testing.md`](testing.md)).
`AuthorableAttribute` documents how a key is named, when a value lands and each type's JSON form. The format's
JSON Schema documents every reserved field ([Editor completion](configuring-assets.md#editor-completion)). An
invalid document throws `SceneDocumentFormatException`, naming the defect.

### The tile map entry

`tile-map` places the engine's `TileMap`, whose members are its grid and each of whose `tileTypes` is a
`TileType` object. A document may carry any number, interleaved with game entities, all anchored at the world
origin and drawn by their `zIndex` bands. A scene that authors no `size` spans its largest map. How tiles
collide is [`collision.md`](collision.md#terrain).

### Entries and composition

Every `type` names an entity class: the engine's `tile-map`, or one in the game's logic assembly. A concrete
`Entity` with one public constructor taking an `EntitySpawn` claims the key its namespace names, and
`[SpawnType("key")]` names another key. Code places the same entity through the same constructor with
`new EntitySpawn(position) { Rotation = turn }`.

One rule keys entities, a member object's subclasses, `TileType` ones among them, and a document's `baseScene`. Take the type's
namespace below the assembly's root namespace. Drop a leading `Entities`, `Cameras`, `Tiles` or `Scenes` segment
and a trailing segment repeating the type's own name. Kebab-case each segment, join them with `/`, then append
the kebab-cased type name:

| Type | Key |
| --- | --- |
| `MyGame.Entities.Enemies.Bat` | `enemies/bat` |
| `MyGame.Entities.Player.Player` | `player` |
| `MyGame.Cameras.RoomCamera` as a `camera` object's `type` | `room-camera` |
| `MyGame.Scenes.PlayableRoom` as a `baseScene` | `playable-room` |
| `MyGame.Scenes.Stage1.Room01` claiming a document | `scenes/stage-1/room-01` |

A class claiming a document keeps the leading segment, because the document's key is its path. A type
outside the root namespace claims its kebab-cased name. A spawn type no class claims fails the scene at load.

## From source to game

Documents are authored anywhere under the logic project's `Assets/`. The build parses each, re-emits it
compact, and ships it gzipped at `assets/<key>.scene.json.gz`. A document's key is its source path
without either extension, keyed as [named assets](assets.md#named-assets) defines. Two sources sharing a key fail the build.

An editor's own format enters through an authoring module, a package that ships an
[importer](build-and-publish.md#writing-an-importer). The importer builds a `SceneDocument` of entries
carrying their members as JSON and writes it with `SceneDocumentFile.ToJson`. The engine validates, keys and
ships each document it writes like a hand-authored one at that path. JAG Studios publishes the Tiled module as
`JAG.Capsule.Tiled` from [capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
