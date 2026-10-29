# Scenes

After this page you can author a scene as a document, a class or both, place entities in it, and move
between scenes. Each type's XML docs hold its rules.

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
namespace names under `Assets/`, keyed as [Entries and composition](#entries-and-composition) states.
`MyGame.Scenes.Test` claims `Assets/Scenes/test.scene.json`. A class declaring both constructor shapes is a compile error.

Every document has a generated `SceneKey` in `CapsuleAssets`, one nested class per folder.
`Assets/Scenes/halls/hall.scene.json` is `CapsuleAssets.Scenes.Halls.HallScene`, whose `Name` is
`scenes/halls/hall`. `--scene` takes a scene class name or a document key, and a class name wins when a
value is both.

## Format

A document is a tree of objects: the scene, its entries, and any object a member holds. This one places a
tile map, a lamp, a switch that names the lamp, and a door to the hall:

```json
{
  "size": [320, 180],
  "ambient": "#484c68",
  "entities": [
    {
      "type": "tile-map",
      "zIndex": -10,
      "tileSize": 16,
      "width": 4,
      "height": 2,
      "texture": "textures/terrain.png",
      "columns": 4,
      "tileTypes": [
        { "name": "empty" },
        { "name": "wall", "cell": 0, "layer": "solid" }
      ],
      "tiles": [
        0, 0, 0, 0,
        1, 1, 1, 1
      ],
      "collider": true
    },
    { "id": 1, "type": "lamp", "x": 16, "y": 8, "color": "#ffd08a" },
    { "type": "switch", "x": 32, "y": 8, "lamp": 1 },
    { "type": "door", "x": 48, "y": 0, "destination": "scenes/halls/hall" }
  ]
}
```

The game declares the three entity classes in `MyGame.Entities`:

```csharp
public sealed class Lamp : Entity
{
    [Authorable]
    public ColorRgba Color { get; set; } = ColorRgba.White;

    public Lamp(EntitySpawn spawn) : base(spawn) { }
}

public sealed class Switch : Entity
{
    [Authorable]
    public Lamp? Lamp { get; set; }

    public Switch(EntitySpawn spawn) : base(spawn) { }
}

public sealed class Door : Entity
{
    [Authorable(Required = true)]
    public SceneKey Destination { get; set; }

    public Door(EntitySpawn spawn) : base(spawn) { }
}
```

Each JSON object is a C# object. Its reserved keys are structure, listed in code as `SceneDocumentKeys`, and every
other key sets the member of that name its class marks `[Authorable]`:

| Object | Reserved keys | Every other key sets a member of |
| --- | --- | --- |
| The document | `$schema`, `baseScene`, `entities` | the composing scene: the class claiming the document, the `baseScene` it names, or `Scene`, whose own are `size`, `clearColor`, `ambient`, `sampling` and `camera` |
| An entry | `type`, `id`, `x`, `y`, `rotation` (degrees), `scale`, `zIndex`, `scrollFactor` | the entity class its `type` names |
| A member's object | `type` | the object the member holds, or else a new one of the member's class or of the subclass its `type` names |

An entry's spawn keys reach the entity's constructor as an `EntitySpawn`. An `id` is needed only on an entry
another names, and ids are unique. `AuthorableAttribute` documents how a key is named and when a value lands. The
format's JSON Schema documents every reserved field
([Editor completion](configuring-assets.md#editor-completion)). An invalid document throws
`SceneDocumentFormatException`, naming the defect.

### Member values

| Member type | JSON form |
| --- | --- |
| `bool`, `int`, `float`, `string` | the JSON value |
| `Vector2` | `[x, y]` |
| `ColorRgba` | `"#rrggbb"` or `"#rrggbbaa"` |
| `Rect` | its edges, `[left, top, right, bottom]`, with right no less than left and bottom no less than top |
| an enum | a member's name camel-cased, `"iceCave"`, and a `[Flags]` enum's names joined by commas, `"north, east"` |
| `TextureHandle`, `AudioClip` | key and extension, `"textures/lamp.png"`, preloaded with the scene |
| `SceneKey` | the document's key, `"scenes/halls/hall"` |
| `TextKey` | the text's key path in the source language, `"hints.jump"` |
| an `Entity` subclass or interface | the target entry's id, readable from `OnStart` on |
| a definition: a `readonly` struct or immutable record with `public static readonly` instances | an instance's name camel-cased, `"default"`, shared by every placement naming it |
| a type with `[JsonConverter]` | its converter's form, checked at load |
| a class declaring `[Authorable]` members | an object of them, `{ "speed": 30 }`, filling the instance the member holds or else a new one. Its `type` key names a subclass to construct in its place. |
| `T[]` of any of these | `[a, b, c]`, a new array for each scene |
| `ReadOnlyMemory<T>` of a built-in type, an enum or a definition | `[a, b, c]`, read once per document and shared by every scene composed from it |
| a nullable of any of these | also `null` |

### Validating every document

Every game should carry this one test, which validates every shipped document without running the game:

```csharp
[Fact]
public void EveryScenePlacementIsValid() => CapsuleScenes.Registry.ComposeAll();
```

### The tile map entry

`tile-map` places the engine's `TileMap`, whose members are its grid and each of whose `tileTypes` is a
`TileType` object. A document may carry any number, interleaved with game entities, all anchored at the world
origin and drawn by their `zIndex` bands. A scene that authors no `size` spans its largest map.

### Entries and composition

Every `type` names an entity class: the engine's `tile-map`, or one in the game's logic assembly. A concrete
`Entity` with one public constructor taking an `EntitySpawn` claims the key its namespace names. Code places the
same entity through the same constructor with `new EntitySpawn(position) { Rotation = turn }`.

One rule keys every class a document names: entities, a member object's subclasses, `TileType` ones among them,
a document's `baseScene`, and a scene class claiming a document.
Take the type's namespace below the assembly's root namespace. Drop a leading `Entities`, `Cameras`, `Tiles` or
`Scenes` segment and a trailing segment repeating the type's own name. Kebab-case each segment, join them with
`/`, then append the kebab-cased type name. `[TypeKey("key")]` on any of these classes claims that key instead,
kebab-cased the same way:

| Type | Key |
| --- | --- |
| `MyGame.Entities.Lamp` | `lamp` |
| `MyGame.Entities.Props.Lamp.Lamp` | `props/lamp` |
| `MyGame.Cameras.RoomCamera` as a `camera` object's `type` | `room-camera` |
| `MyGame.Scenes.LitRoom` as a `baseScene` | `lit-room` |
| `MyGame.Scenes.Stage1.Room01` claiming a document | `scenes/stage-1/room-01` |

A class claiming a document keeps the leading segment, because the document's key is its path. A type
outside the root namespace claims its kebab-cased name. A type key no class claims fails the scene at load.

## Changing scenes

The shell boots the first scene with `RunScene`, and a step asks for the next with `Run.RequestScene` or
loads its media early with `Run.PrefetchScene`. A request may carry an `IScenePayload`, which the opened
scene reads as its `EntryPayload` from its `CollectAssets` onward. The payload can choose what the scene preloads,
and a prefetch takes the payload its request will carry. A test reads the request the host would act on as a
`SceneTransition`.
A scene composed from a document reports that document's key as `Scene.DocumentKey`, and a scene built in code
reports null. A payload names the room it came from by it, and a save keys a record to a room by it.

One scene runs at a time, and its entities leave with it. State that must outlive a scene lives on the
run's attached object, `Run.State<T>()`. A menu over a paused world is an entity in the same scene
([Pausing](entities.md#pausing)).

```csharp
public sealed record SlotChoice(int Slot) : IScenePayload;

// In the title menu's step: prefetch while Start is highlighted, and open the hall when it is pressed.
Run.PrefetchScene(CapsuleAssets.Scenes.Halls.HallScene, new SlotChoice(1));
Run.RequestScene(CapsuleAssets.Scenes.Halls.HallScene, new SlotChoice(1));

// In the scene class claiming the hall's document.
protected override void OnStart()
{
    int slot = EntryPayload is SlotChoice choice ? choice.Slot : 0;
}
```

## From source to game

Documents are authored anywhere under the logic project's `Assets/`. The build ships each compiled at
`assets/<key>.scene.json.gz`. A document's key is its source path without either extension, keyed as
[named assets](assets.md#named-assets) defines. Two sources sharing a key fail the build.

An editor's own format enters through an authoring module, a package that ships an
[importer](packages.md#writing-an-importer). The importer builds a `SceneDocument` of entries
carrying their members as JSON and writes it with `document.ToJson()`. The engine validates, keys and
ships each document it writes like a hand-authored one at that path. JAG Studios publishes the Tiled module as
`JAG.Capsule.Tiled` from [capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
