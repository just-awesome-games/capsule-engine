# Scenes

A scene is one world: its ordered contents and a camera. A `*.scene.json` scene document is its serialized form — data, carrying no behaviour. Tile maps are one engine-native entry type, not a separate kind of scene.

## Authoring model

Data and behaviour are separate halves, and a game takes either or both:

| Combination | What the game writes | How it boots |
| --- | --- | --- |
| Document only | `test.scene.json` under the logic project's `Assets/Scenes/`, and no class | `RunScene("test")` composes a plain `Scene` from it |
| Document and class | that document, plus `class Test : Scene` with the constructor `public Test(SceneContent content) : base(content)` | `RunScene<Test>()` or `RunScene("test")` — either loads the document, then constructs `Test` |
| Class only | `class Test : Scene` with a public parameterless constructor | `RunScene<Test>()` runs the scene as it builds itself |

The `SceneContent` constructor is the opt-in: taking one and handing it to `base` claims a document, the one keyed as the class's namespace names ([Entries and composition](#entries-and-composition)) unless `[SceneDocument("key")]` names another. A class declaring both constructor shapes is a compile error. A composed scene's assets are collected before `OnStart` (`Scene.CollectAssets`, `AssetCollection`).

## Format

`SceneDocumentFile` reads and writes format version 5 as two-space-indented UTF-8 JSON with LF endings and one trailing newline, so a canonical document is a fixed point of the importer. A document is one uniform list of entries:

```json
{
  "formatVersion": 5,
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
      },
      "zIndex": -10
    },
    { "id": 2, "type": "coin", "x": 8, "y": 0 },
    { "id": 3, "type": "banner", "x": 32, "y": 0, "scale": [2, 3], "zIndex": 10 }
  ],
  "nextEntityId": 4
}
```

- `formatVersion` is required and must be supported.
- Every entry carries `id`, `type`, `x` and `y` in that order, all required; `scale`, `zIndex` and then `properties` follow where the entry carries them.
- `scale` is `[x, y]`, both finite and greater than zero; absent is identity. It is the raw authored factor and what it scales is the entity's constructor's decision; a `scale` on a `tile-map` entry is rejected.
- `zIndex` is the entry's draw band, applied to the spawned entity's `ZIndex` after construction. What draws later is the higher sum of the band and the renderer's own offset within it, ties broken by file order and then attachment order. Absent leaves the band the entity class gave itself; an authored `0` overrides it, and the writer emits the field only where the entry authors one.
- IDs are unique, positive and lower than `nextEntityId`; deleted IDs are not reused. `entities` may be empty.
- A `source` block records tool, relative source path and SHA-256 of the source closure; its presence marks a derived file, so an authoring source omits it.
- `properties` is a contract per entry type, consumed by whatever constructs that entry, never a reflective set-by-name bag. Only the engine's `tile-map` declares one; properties on any other type are rejected at parse.

Invalid documents throw `SceneDocumentFormatException`.

### The tile map entry

`tile-map` is reserved by the engine; a document may carry any number, interleaved with game entities, all anchored at the world origin and drawn by their `zIndex` bands. Its properties are `tileSize`, `width`, `height`, `texture`, `columns`, `tileTypes` and `tiles`; palette index 0 is `empty`, which carries neither cell nor layer, and `tiles` holds exactly `width × height` palette indices. Each other palette entry carries a `type` name and may carry:

| Field | Meaning |
| --- | --- |
| `texture` | The key under the textures root, extension included, of the texture every drawn tile is cut from, spelt any way ([`consuming-capsule.md` § Named assets](consuming-capsule.md#named-assets)); forward slashes, no empty, `.` or `..` segment. Absent on a grid that draws nothing. |
| `columns` | How many cells wide that texture is; required with `texture`, at least 1, absent without one. |
| `cell` | Which cell of the texture a tile of this type draws, counted across a row of `columns` then down from cell 0, square at `tileSize`. Absent is a semantic tile: queryable, may collide, draws nothing. |
| `layer` | The collision layer every tile of this type is on, as one name the game owns; a query or mover meets the tile when its own filter names that layer. Absent is decoration. Several types may share a layer. |
| `collidableFaces` | Which sides collide, an array of `"left"`, `"right"`, `"top"` or `"bottom"` (grid directions in a Y-down world). Absent is all four: a whole tile, with sides shared with an adjacent four-sided tile generating no contact. A smaller set is that many one-directional edges, each stopping only what crosses it into the tile. |

A `cell` on a grid naming no `texture`, a `texture` no entry draws a cell of, an unknown face name, `collidableFaces` on a tile with no `layer`, and an empty `collidableFaces` all fail the document. A tile map whose palette collides with nothing registers no collider.

### Entries and composition

Every `type` other than `tile-map` names an entity class in the game's own logic assembly, claimed the way a scene claims a document: a concrete `Entity` with one public constructor taking an `EntitySpawn` claims the key its namespace names, and `[SpawnType("key")]` names another whole key. The one rule for scenes and entities: the type's namespace under the assembly's root namespace, minus a leading `Scenes` or `Entities` segment and minus a trailing segment repeating the type's own name, kebab-cased per segment and joined with `/`, then the kebab-cased type name — `MyGame.Entities.Enemies.Bat` claims `enemies/bat`, `MyGame.Entities.Player.Player` claims `player`, `MyGame.Scenes.Stage1.Room01` claims `stage-1/room-01`; a type outside the root namespace claims its kebab-cased name alone. A spawn type no class claims fails the scene at load.

## From source to game

Documents are authored under the logic project's `Assets/Scenes/`; the build validates each, re-emits it canonically under `obj/`, stamps its provenance, and copies it to `assets/scenes/<key>.scene.json` beside the executable. A document's key is its path under the scenes root without either extension, normalized as [Named assets](consuming-capsule.md#named-assets) defines, and the class that composes it is the one whose own key matches. Two sources sharing a key fail the build; derived documents are never committed. The logic role imports scenes on its own; any other project opts in with `<CapsuleImportScenes>`, and `<CapsuleTileSize>` declares the one tile size every scene must match ([`consuming-capsule.md`](consuming-capsule.md#build-configuration-reference)).

## Authoring tools

An editor's own format enters through an authoring module: a package whose `buildTransitive` targets derive a document per source into their own `obj/` space and add each to the `CapsuleSceneDocument` item from a target running `BeforeTargets="CapsuleCollectSceneDocuments"`. The engine validates, canonicalizes and ships those documents exactly as hand-authored ones, preserving the module's `source` block. A module states each document's key as `%(CapsuleDocumentKey)` — the root-relative path, forward slashes, `/`-joined segments of ASCII letters, digits, hyphens and underscores, none a reserved Windows device name, no extension; a document naming none is keyed by its stem at the root — and the engine normalizes it, so a module implements no part of the key rule.

Three rules hold a module's targets:

- Read `CapsuleImportScenes`, `CapsuleAssetSourcesDir`, `CapsuleTileSize` and `CapsuleDotNetHost` only inside targets: NuGet imports package targets in no promised order, so a property a role derives is final at execution time, not at evaluation.
- Collect a glob first and set its key in a second item group naming the metadata qualified — `%(MyModuleSource.RecursiveDir)` — since `%(RecursiveDir)` on a glob's own `Include` inside a target batches over the target and comes back empty.
- Carry `Exclude="@(_CapsuleDevelopmentOnly)"` on every authoring glob, or a directory a game marked development-only still ships through the module's format ([`consuming-capsule.md`](consuming-capsule.md#development-only-directories)).

JAG Studios publishes the Tiled module as `JAG.Capsule.Tiled` from [capsule-engine-tiled](https://github.com/just-awesome-games/capsule-engine-tiled).
