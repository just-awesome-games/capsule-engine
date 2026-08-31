# Scenes

A scene is one world: the terrain under it, the entities on it, and a camera. Capsule has a single scene concept, and a `*.scene.json` **scene document** is its serialized form — a scene's data, carrying no behaviour.

## Authoring model

Data and behaviour are separate halves, and a game takes either or both:

| Combination | What the game writes | How it boots |
| --- | --- | --- |
| Document only | `test.scene.json` under `src/asset-sources/scenes/`, and no class | `RunScene("test")` composes a plain `Scene` from it |
| Document and class | that document, plus `class Test : Scene` whose constructor is `public Test(SceneContent content) : base(content)` | `RunScene<Test>()` or `RunScene("test")` — either loads the document, then constructs `Test` |
| Class only | `class Test : Scene` with a public parameterless constructor | `RunScene<Test>()` runs the scene as it builds itself |

The `SceneContent` constructor is the opt-in: taking one and handing it to `base` is what claims a document. The document claimed is the class name kebab-cased — `OpeningRoom` claims `opening-room` — unless `[SceneDocument("name")]` names another. A document no class claims composes into a plain `Scene`; a class no document backs is built as it is. A class declaring both constructor shapes is a compile error, not a choice made at boot.

```csharp
[SceneDocument("room-01")]
public sealed class OpeningRoom(SceneContent content) : Scene(content)
{
    protected override void OnStart() => Camera.Teleport(FindSingle<Player>().Position);
}
```

`Scene(SceneContent)` adds the document's terrain as a `TileMap` when it carries one, takes `Size` from it, then spawns one entity per remaining entry in file order. The document is construction data and is not retained; a subclass that needs the terrain later asks `FindFirst<TileMap>()`.

Transitions name a scene the same two ways: `RequestScene<T>` a class, `RequestScene(name)` a document. Both resolve through `SceneRegistry`, which games never build — the source generator emits it from the assembly's own classes.

## Format

`SceneDocumentFile` reads and writes format version 1 as two-space-indented UTF-8 JSON with LF endings and one trailing newline, so an unchanged document re-derives byte for byte. A document is one uniform list of entries:

```json
{
  "formatVersion": 1,
  "entities": [
    {
      "id": 1,
      "type": "tile-map",
      "x": 0,
      "y": 0,
      "properties": {
        "tileSize": 16,
        "width": 2,
        "height": 1,
        "tileTypes": [{ "type": "empty" }, { "type": "ground", "color": "#4a5568ff" }],
        "tiles": [0, 1]
      }
    },
    { "id": 2, "type": "coin", "x": 8, "y": 0 }
  ],
  "nextEntityId": 3
}
```

- `formatVersion` is required and must be supported.
- Every entry carries `id`, `type`, `x` and `y` in that order — all four are required, the terrain entry included; `properties` follows when the type declares a contract.
- IDs are unique, positive, and lower than `nextEntityId`, across every entry including the terrain. Deleted IDs are not reused.
- `entities` may be empty: that is a valid empty scene.
- A `source` block records tool, relative source path, and SHA-256 of the source closure. Its presence marks a derived file, so an authoring source omits it.

`properties` is a contract per entry type, consumed by whatever constructs that entry — never a reflective set-by-name bag. Exactly one type declares one today: the engine's own `tile-map`. Properties on any other type are rejected at parse.

Invalid documents throw `SceneDocumentFormatException`.

### The `tile-map` entry

`tile-map` is the terrain, reserved by the engine: no game class may claim it as a spawn type.

- A document carries at most one, and it is the first entry. File order is composition order, so terrain composes under everything placed on it.
- Its `properties` are the grid: `tileSize`, `width`, `height`, `tileTypes`, `tiles`.
- Palette index 0 is `empty` with no color. Other tile types are unique and non-blank.
- `tiles` contains exactly `width * height` palette indices.
- Colors use lowercase `#rrggbbaa`.
- Terrain is anchored at the world origin, so its `x` and `y` are 0.
- Omit the entry entirely for a scene of entities alone; nothing then draws terrain and the scene spans nothing until it sets its own size.

### Entity entries

Every remaining `type` names an entity class in the game's own logic assembly, claimed the way a scene claims a document: a concrete `Entity` with one public constructor taking an `EntitySpawn` claims its kebab-cased class name, and `[SpawnType("type")]` names another. A type no class claims fails the scene at load.

## From source to game

Games author scene sources under `src/asset-sources/scenes/`, and a document reaches the runtime through either front door:

| Source | Import |
| --- | --- |
| `*.scene.json` | Validated and canonicalized native scene JSON. |
| `*.tmj` | Finite orthogonal Tiled maps translated at build time. |

Both derive the same canonical `*.scene.json`. The build writes it under `obj/`, stamps its provenance, and copies it to `assets/scenes/<name>.scene.json` beside the executable, which is the only place the runtime looks. Sources sharing a stem fail the build because that directory is flat, and derived documents are never committed.

The shell role imports scenes on its own; any other project that needs them opts in, and a game may declare the one tile size every scene must match. Both are project properties, named in [`consuming-capsule.md`](consuming-capsule.md).

`Capsule.Cli` does the deriving, through `import-native` and `import-tiled`; run it with no arguments for its command-line contract. The build invokes it incrementally, and direct use is for diagnosing an import.

## Tiled subset

The runtime loads only the native scene document. The Tiled importer is a build-time translator into it, so dropping Tiled deletes no runtime code.

Capsule imports `.tmj` files that are orthogonal, finite, square-tiled, CSV-encoded, unflipped, and contain exactly one tile layer.

- A tileset tile's Class becomes its semantic tile type.
- An optional Color property named `color` becomes its presentation color.
- Tile Classes are unique across all referenced tilesets; `empty` is reserved.
- Object layers contain objects whose Class becomes the spawn type.
- Tiled's object IDs are preserved. The terrain entry takes the source's `nextobjectid`, and the document's `nextEntityId` is one past it.
- Referenced `.tsj` files must remain under the asset-source root.

Unsupported input fails the build with the file and the violated constraint.

Tiled's Windows GUI executable writes no console output even on success. Use `tmxrasterizer` when a headless PNG preview is needed.
