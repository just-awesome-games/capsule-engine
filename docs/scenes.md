# Scenes

A scene is one world: its ordered contents and a camera. Capsule has a single scene concept, and a `*.scene.json` **scene document** is its serialized form — a scene's data, carrying no behaviour. Tile maps are one engine-native entry type, not a requirement or a separate kind of scene.

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

`Scene(SceneContent)` composes one `TileMap` or game entity per entry in file order. Its `Size` spans the largest tile map it carries. The document is construction data and is not retained; a subclass queries the composed entities when it needs them.

Transitions name a scene the same two ways: `RequestScene<T>` a class, `RequestScene(name)` a document. Both resolve through `SceneRegistry`, which games never build — the source generator emits it from the assembly's own classes.

## Format

`SceneDocumentFile` reads and writes format version 2 as two-space-indented UTF-8 JSON with LF endings and one trailing newline, so an unchanged document re-derives byte for byte. A document is one uniform list of entries:

```json
{
  "formatVersion": 2,
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
        "tileTypes": [
          { "type": "empty" },
          { "type": "ground", "color": "#4a5568ff", "layer": "solid" },
          { "type": "ledge", "color": "#718096ff", "layer": "ledge", "collidableFaces": ["top"] }
        ],
        "tiles": [0, 1]
      }
    },
    { "id": 2, "type": "coin", "x": 8, "y": 0 }
  ],
  "nextEntityId": 3
}
```

- `formatVersion` is required and must be supported.
- Every entry carries `id`, `type`, `x` and `y` in that order — all four are required; `properties` follows when the type declares a contract.
- IDs are unique, positive, and lower than `nextEntityId`, across every entry. Deleted IDs are not reused.
- `entities` may be empty: that is a valid empty scene.
- A `source` block records tool, relative source path, and SHA-256 of the source closure. Its presence marks a derived file, so an authoring source omits it.

`properties` is a contract per entry type, consumed by whatever constructs that entry — never a reflective set-by-name bag. Exactly one type declares one today: the engine's own `tile-map`. Its properties are `tileSize`, `width`, `height`, `tileTypes`, and `tiles`; palette index 0 is `empty`, `tiles` contains exactly `width * height` palette indices, and colors use lowercase `#rrggbbaa`. Properties on any other type are rejected at parse.

A palette entry may also carry `layer` and `collidableFaces`, which are what a tile of that type collides as. Face names are grid directions in a Y-down world, so `top` is the tile's -Y side.

| Field | Meaning |
| --- | --- |
| `layer` | The collision layer every tile of this type is on, as one name. Absent is decoration: the tile collides as nothing. The name is the game's own — the engine reserves none — and a query or a mover meets the tile when its own filter names that layer. The tile type is identity and is never a layer, so several types may share one. |
| `collidableFaces` | Which sides collide, as an array of `"left"`, `"right"`, `"top"` or `"bottom"`. Absent is all four: the whole tile, with sides shared with an adjacent four-sided tile generating no contact, so a flat run of them is one surface with no seams to catch on. A smaller set is that many one-directional edges — each stops only what crosses it into the tile, never motion along it and never something that started on the far side. |

Any other face name fails the document, as does `collidableFaces` on a tile with no `layer`, an empty `collidableFaces`, and the `collision` field the two of them replaced. The reserved `empty` entry carries neither a colour nor a layer. A tile map whose palette collides with nothing registers no collider at all.

Invalid documents throw `SceneDocumentFormatException`.

### Entries and composition

`tile-map` is reserved by the engine, so no game class may claim it as a spawn type. A document may carry zero or more tile maps, interleaved with game entities; all are anchored at the world origin and file order determines draw order. This permits background and foreground layers without making tile maps mandatory. Every other `type` names an entity class in the game's own logic assembly, claimed the way a scene claims a document: a concrete `Entity` with one public constructor taking an `EntitySpawn` claims its kebab-cased class name, and `[SpawnType("type")]` names another. A type no class claims fails the scene at load.

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

Capsule imports `.tmj` files that are orthogonal, finite, square-tiled, CSV-encoded, and unflipped. Tile and object layers become document entries in authored order; a map may contain any number of either.

- A tileset tile's Class becomes its semantic tile type. It is identity alone and is never a collision layer.
- An optional Color property named `color` becomes its presentation color.
- An optional String property named `layer` becomes the tile's collision layer, and an optional String property named `collidableFaces` is a comma-separated list of face names — both trimmed, with blank entries dropped. A property that is present but names nothing is refused rather than read as an absent one. More than one name in `layer`, an unknown face name, `collidableFaces` without `layer`, or either declared as any other property type fails the import, as does a tile still carrying a `collision` property.
- Tile Classes are unique across all referenced tilesets; `empty` is reserved.
- Object layers contain objects whose Class becomes the spawn type.
- Tiled's object IDs are preserved. Tile layers take consecutive IDs beginning at the source's `nextobjectid`, and the document's `nextEntityId` follows them.
- Referenced `.tsj` files must remain under the asset-source root.

Unsupported input fails the build with the file and the violated constraint.

Tiled's Windows GUI executable writes no console output even on success. Use `tmxrasterizer` when a headless PNG preview is needed.
