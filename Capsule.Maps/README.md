# Capsule.Maps

A **map** is an authored spatial content document: a `TileGrid` of semantically typed terrain with
optional colour presentation, plus the typed `MapObject`s placed over it. Nothing here knows a
scene exists, and no authoring format is ever a runtime dependency.

A map is versioned plain JSON and its shape is public, so code writes it directly — a test or a
generator builds a `TileGrid` and a `Map` and mints ids from `NextObjectId`, with no file and no
authoring tool in sight. `MapFile` reads and writes that same shape, and its written form is
canonical: fields in order (`formatVersion`, `grid`, `objects`, `nextObjectId`, `source`), two-space
indent, LF, UTF-8 without a BOM, one trailing newline. Re-generating an unchanged map reproduces its
bytes exactly, so a diff shows only real change.

## Format invariants

These bind every map however it was made — one constructor checks a map built from code and one
read from a file, identically, throwing `MapFormatException` on anything the format forbids.

- **`formatVersion` is mandatory.** This build reads and writes version 1, and rejects a missing or
  unsupported version before interpreting the document.
- **A palette entry gives a tile its semantic type and optional colour.** Index 0 is exactly
  `empty` with no colour, reserved for the absence of a tile and never a game's own type; a colour
  there is rejected. Names are unique and non-blank, every tile is an index into the palette, and a
  grid holds exactly `width * height` of them.
- **A colour is written `#rrggbbaa`, lowercase.** An uppercase spelling is rejected rather than
  accepted and written back differently: a map must survive its own round trip byte for byte.
- **Object ids are monotonic through `nextObjectId`** and never hand-authored. Each is at least 1,
  below `nextObjectId`, and unique within the map. Ids are never reused, and deleting an object
  never rewinds the counter.
- **A `source` block is provenance** — the tool, the path it was handed, and the SHA-256 of the
  complete source closure including external tilesets. Its presence means the file is an artifact:
  edit the source and re-import, never the file. The path is relative and forward-slashed, and
  nothing resolves it at runtime.

## Authoring with Tiled

Tiled ([mapeditor.org](https://www.mapeditor.org)) is a door to the format, reached through the
build rather than at runtime. The build dispatches by extension, so a second authoring tool becomes
another item group in [`build/Capsule.Maps.targets`](../build/Capsule.Maps.targets), never new
wiring in the game.

- **`tiled.exe` is a GUI-subsystem binary.** Run from a console it prints nothing at all — no
  output, no error, no exit message — while doing exactly what it was asked. Silence is not failure.
- **`tmxrasterizer.exe` renders a `.tmj` to PNG headlessly**, which is how to look at one without
  opening the editor.

Create or edit a `.tmj` under the game's `asset-sources/maps/` and build. The map is derived into
`obj/` and copied to `Assets/Maps/<name>.map.json` beside the executable, which is where a game
loads it from. **Nothing generated is committed**: the `.tmj` is the source, the map is a build
artifact, and there is no step to remember.

Sources are found at `$(CapsuleAssetSourcesDir)/maps/**/*.tmj`, defaulting to
`$(MSBuildProjectDirectory)/../asset-sources`. A map is named after the `.tmj` it came from.
Referenced `.tsj` tilesets anywhere under that root are inputs too — and so is the importer itself,
so a Capsule upgrade re-derives every map rather than shipping what the previous one wrote. A map
may reference across or upward within the tree; a reference resolving outside it fails, because the
build could not track the file.

The shell role imports maps by definition; anything else that must read them — a test project, a
headless smoke binary — sets `<CapsuleImportMaps>true</CapsuleImportMaps>`
([`docs/consuming-capsule.md`](../docs/consuming-capsule.md)). A game may declare the one tile size
every map is authored at, on the shell project:

```xml
<CapsuleTileSize>16</CapsuleTileSize>
```

A map whose own tile size differs then fails the build, naming the map, its size and the declared
one. Left unset, each map keeps its own.

### How a `.tmj` is read

Importer rules, not format rules: they say how Tiled's vocabulary is read into a map, and bind
nothing about a map built from code.

- **A tile carries both what it is and what it draws as, authored on the tileset tile.** Its
  **Class** is the tile type — the string the palette carries and `TileTypeAt` returns. A **Color**
  custom property named exactly `color` renders it; Tiled writes that alpha-leading as `#AARRGGBB`
  and the importer reorders it. A Class without `color` stays semantic, non-rendering tile data,
  and a String property holding the same text is rejected.
- **A tile's Class is unique across the `.tmj`'s tilesets**, and `empty` is reserved. Every Class in
  a tileset enters the palette whether or not it is painted, so painting a new type never renumbers
  the ones already in use.
- **Anything with identity is an object with a Class.** Object layers carry objects; tile layers are
  anonymous terrain. Layer *type* decides which is which — layer names are yours.
- **Tiled mints the ids.** Its own object ids and `nextobjectid` carry straight through, which is
  what makes them monotonic with nobody maintaining them.
- Capsule imports orthogonal, finite `.tmj`s with square tiles, CSV tile data, unflipped tiles and
  exactly one tile layer. Anything else fails the import, naming the file and the setting to change.

## The importer

`Capsule.Maps.Cli` is the dev-time tool the hook runs, once per build with the `.tmj`s that changed:

```
Capsule.Maps.Cli import-tiled --out <dir> [--tile-size <px>] <map.tmj> [<map.tmj>...]
Capsule.Maps.Cli import-tiled --out <dir> [--tile-size <px>] --maps-from <list.txt>
```

The build uses the second form — a few hundred source paths overflow a command line. Running either
by hand is for debugging an import, never a step in the workflow. It exits 0 when every source
succeeded, 1 when any failed and 2 on a usage error, and per-source failures reach the build output
verbatim. Name sources relatively: each path is stamped into its map's `source` block verbatim, and
the format refuses an absolute one.
