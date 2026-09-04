# Scenes

A scene is one world: its ordered contents and a camera. Capsule has a single scene concept, and a `*.scene.json` **scene document** is its serialized form — a scene's data, carrying no behaviour. Tile maps are one engine-native entry type, not a requirement or a separate kind of scene.

## Authoring model

Data and behaviour are separate halves, and a game takes either or both:

| Combination | What the game writes | How it boots |
| --- | --- | --- |
| Document only | `test.scene.json` under `src/asset-sources/scenes/`, and no class | `RunScene("test")` composes a plain `Scene` from it |
| Document and class | that document, plus `class Test : Scene` whose constructor is `public Test(SceneContent content) : base(content)` | `RunScene<Test>()` or `RunScene("test")` — either loads the document, then constructs `Test` |
| Class only | `class Test : Scene` with a public parameterless constructor | `RunScene<Test>()` runs the scene as it builds itself |

The `SceneContent` constructor is the opt-in: taking one and handing it to `base` is what claims a document. The document claimed is the key the class's namespace names — `MyGame.Scenes.OpeningRoom` claims `opening-room`, `MyGame.Scenes.Stage1.Room01` claims `stage-1/room-01` — unless `[SceneDocument("key")]` names another; Entries and composition below states that rule in full. A document no class claims composes into a plain `Scene`; a class no document backs is built as it is. A class declaring both constructor shapes is a compile error.

```csharp
[SceneDocument("room-01")]
public sealed class OpeningRoom(SceneContent content) : Scene(content)
{
    protected override void OnStart() => Camera = new FollowCamera();
}
```

A scene installs a `Camera` subclass that owns its span, its subject and its framing: it finds its subject in its own `OnStart` — where the scene is composed and searchable through the camera's `Scene` — and settles its framing in `OnLateStep`. Installing a camera cuts to it rather than sweeping from where the previous one sat.

Register in `OnAddedToScene`, discover in `OnStart`; the two lifecycle axes are in [`architecture.md`](architecture.md#determinism-contract).

`Scene(SceneContent)` composes one `TileMap` or game entity per entry in file order. Its `Size` spans the largest tile map it carries. The document is construction data and is not retained; a subclass queries the composed entities when it needs them.

Transitions name a scene the same two ways: `RequestScene<T>` a class, `RequestScene(name)` a document. Both resolve through `SceneRegistry`, which games never build — the source generator emits it from the assembly's own classes.

## Format

`SceneDocumentFile` reads and writes format version 4 as two-space-indented UTF-8 JSON with LF endings and one trailing newline, so a canonical document is a fixed point of the importer. A document is one uniform list of entries:

```json
{
  "formatVersion": 4,
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
        "texture": "terrain.png",
        "columns": 4,
        "tileTypes": [
          { "type": "empty" },
          { "type": "ground", "cell": 0, "layer": "solid" },
          { "type": "ledge", "cell": 2, "layer": "ledge", "collidableFaces": ["top"] }
        ],
        "tiles": [0, 1]
      }
    },
    { "id": 2, "type": "coin", "x": 8, "y": 0 },
    { "id": 3, "type": "banner", "x": 32, "y": 0, "scale": [2, 3] }
  ],
  "nextEntityId": 4
}
```

- `formatVersion` is required and must be supported.
- Every entry carries `id`, `type`, `x` and `y` in that order — all four are required; `scale` and then `properties` follow where the entry carries them.
- `scale` is `[x, y]`, both components finite and greater than zero. Absent is identity, which is what the writer emits for an entry at its authored size. It is the raw authored factor: what it scales — a sprite, a collider through `Shape2D.Scaled`, or nothing — is the entity's constructor's decision. A `scale` on the `tile-map` entry is rejected, since terrain is anchored and unscaled.
- IDs are unique, positive, and lower than `nextEntityId`, across every entry. Deleted IDs are not reused.
- `entities` may be empty: that is a valid empty scene.
- A `source` block records tool, relative source path, and SHA-256 of the source closure. Its presence marks a derived file, so an authoring source omits it.

`properties` is a contract per entry type, consumed by whatever constructs that entry — never a reflective set-by-name bag. Exactly one type declares one today: the engine's own `tile-map`. Its properties are `tileSize`, `width`, `height`, `texture`, `columns`, `tileTypes`, and `tiles`; palette index 0 is `empty`, and `tiles` contains exactly `width * height` palette indices. Properties on any other type are rejected at parse.

A palette entry may also carry `cell`, `layer` and `collidableFaces`: what a tile of that type draws, and what it collides as. Face names are grid directions in a Y-down world, so `top` is the tile's -Y side.

| Field | Meaning |
| --- | --- |
| `texture` | The path under the textures root, extension included, of the texture every drawn tile is cut from — `"terrain.png"` is authored at `asset-sources/textures/terrain.png`, `"terrain/cave.png"` at `asset-sources/textures/terrain/cave.png`, and each loads from `assets/textures/` at the same path. Forward slashes only, with no empty, `.` or `..` segment; which extensions the domain admits is the build's to decide. Absent on a grid that draws nothing. |
| `columns` | How many cells wide that texture is. Required with `texture` and at least 1; absent without one. |
| `cell` | Which cell of the texture a tile of this type draws, counted across a row of `columns` and then down from cell 0, square at `tileSize`. Absent is a semantic tile: it is queryable, it may collide, and it draws nothing. |
| `layer` | The collision layer every tile of this type is on, as one name. Absent is decoration: the tile collides as nothing. The name is the game's own — the engine reserves none — and a query or a mover meets the tile when its own filter names that layer. The tile type is identity and is never a layer, so several types may share one. |
| `collidableFaces` | Which sides collide, as an array of `"left"`, `"right"`, `"top"` or `"bottom"`. Absent is all four: the whole tile, with sides shared with an adjacent four-sided tile generating no contact, so a flat run of them is one surface with no seams to catch on. A smaller set is that many one-directional edges — each stops only what crosses it into the tile, never motion along it and never something that started on the far side. |

Texture and cell are strict in both directions: a `cell` on a grid naming no `texture` fails the document, and so does a `texture` no palette entry draws a cell of. Any other face name fails the document, as does `collidableFaces` on a tile with no `layer`, an empty `collidableFaces`, and the `collision` field the two of them replaced. The reserved `empty` entry carries neither a cell nor a layer. A tile map whose palette collides with nothing registers no collider at all.

Invalid documents throw `SceneDocumentFormatException`.

### Entries and composition

`tile-map` is reserved by the engine, so no game class may claim it as a spawn type. A document may carry zero or more tile maps, interleaved with game entities; all are anchored at the world origin and file order determines draw order. This permits background and foreground layers without making tile maps mandatory. Every other `type` names an entity class in the game's own logic assembly, claimed the way a scene claims a document: a concrete `Entity` with one public constructor taking an `EntitySpawn` claims the key its namespace names, and `[SpawnType("key")]` names another whole key. That key is the type's namespace under the assembly's root namespace, minus a leading `Entities` segment and minus a trailing segment repeating the type's own name, kebab-cased per segment and joined with `/`, then the kebab-cased type name — `MyGame.Entities.Enemies.Bat` claims `enemies/bat`, `MyGame.Entities.Player.Player` claims `player`. A type outside the root namespace claims its kebab-cased name alone. A type no class claims fails the scene at load.

## From source to game

Games author scene documents under `src/asset-sources/scenes/`, and the build validates each one, re-emits it canonically under `obj/`, stamps its provenance, and copies it to `assets/scenes/<key>.scene.json` beside the executable. A document's key is its path under the scenes root without either extension, so `scenes/stage-1/room-01.scene.json` is keyed `stage-1/room-01` and ships there; the class that composes it is the one whose own key matches. Two sources sharing a stem in different directories are two documents; two sharing a key fail the build, and derived documents are never committed. The shell role imports scenes on its own; any other project that needs them opts in with `<CapsuleImportScenes>`, and `<CapsuleTileSize>` declares the one tile size every scene must match — both are project properties named in [`consuming-capsule.md`](consuming-capsule.md).

The process behind the hook is `Capsule.Build` itself, packed unlisted under the package's `tools/`, and the build is its only caller.

## Authoring tools

The engine's build wires one format per document kind — `*.scene.json` here, `*.sheet.json` in [`sprite-animation.md`](sprite-animation.md). An editor's own format enters through an authoring module: a package whose `buildTransitive` targets derive a document per source into their own `obj/` space and add each derived document to the `CapsuleSceneDocument` item from a target that runs `BeforeTargets="CapsuleCollectSceneDocuments"`. The engine then validates, canonicalizes, and ships those documents exactly as hand-authored ones, preserving the module's `source` block so the shipped document names the file a person edited.

A module states the key each document claims as `%(CapsuleDocumentKey)` on the item — the root-relative path, forward slashes, one or more `/`-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (`nul`, `con`, …), carrying no extension. A document that names none is keyed by its stem at the root.

A module globbing its own sources inside a target cannot read `%(RecursiveDir)` on the glob's own `Include`: metadata there batches over the target and comes back empty. Collect the glob first, then set the key in a second item group that names the metadata qualified — `%(MyModuleSource.RecursiveDir)`, the module's own item — before stamping `CapsuleDocumentKey`. The engine's own globs run at evaluation time, which is why the targets here use the bare form.

A module may read `CapsuleImportScenes`, `CapsuleAssetSourcesDir`, `CapsuleTileSize`, and `CapsuleDotNetHost`, and reads them only inside its targets: NuGet imports package targets in no promised order, so a property a role derives is final at execution time, not at evaluation.

JAG Studios publishes the Tiled module as `JAG.Capsule.Tiled` from [capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
