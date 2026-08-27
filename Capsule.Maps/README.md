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
  complete source closure, external tilesets included. Its presence means the file is an artifact:
  edit the file the path names and rebuild, never this one. The path is relative and
  forward-slashed, and nothing resolves it at runtime.

## Where a map comes from

Every map a game ships is derived at build time from a source under
`$(CapsuleAssetSourcesDir)/maps`, and the build dispatches by extension. Two extensions are wired,
and a third authoring tool becomes another item group in
[`build/Capsule.Maps.targets`](../build/Capsule.Maps.targets), never new wiring in the game.

| Source | Read by |
| --- | --- |
| `*.tmj` | Tiled's vocabulary, translated into the format below. |
| `*.map.json` | The format itself, authored by hand. |

Whichever it came from, the map is derived into `obj/` and copied to
`assets/maps/<name>.map.json` beside the executable, which is where a game loads it from. A map is
named after its source and the output tree is flat, so two sources sharing a name fail the build —
across the two kinds as much as within one. **Nothing generated is committed**: the source is the
source, the map is a build artifact, and there is no step to remember.

Sources are found at `$(CapsuleAssetSourcesDir)/maps/**`, defaulting to
`$(MSBuildProjectDirectory)/../asset-sources`. The importer itself is an input, so a Capsule
upgrade re-derives every map rather than shipping what the previous one wrote.

The shell role imports maps by definition; anything else that must read them — a test project, a
headless smoke binary — sets `<CapsuleImportMaps>true</CapsuleImportMaps>`
([`docs/consuming-capsule.md`](../docs/consuming-capsule.md)). A game may declare the one tile size
every map is authored at, on the shell project:

```xml
<CapsuleTileSize>16</CapsuleTileSize>
```

A map whose own tile size differs then fails the build, naming the map, its size and the declared
one. Left unset, each map keeps its own.

## Authoring by hand

A `.map.json` under `asset-sources/maps/` is the format above, written directly — by a person or by
a tool of the game's own. It is a source and not a shipped file: the build validates it, re-emits
it canonically, and stamps its `source` block, so a map that would fail to load fails the build
instead, and what ships is byte-identical to what any other source of the same map would produce.

Author no `source` block. One in a source is overwritten by the provenance of the file itself,
which is the file being edited.

A shipped map read back in as a source round-trips: the two kinds derive into one directory under
one naming rule, so a map that came out of Tiled can be lifted into `asset-sources/maps/` and
hand-edited from then on, with only its provenance changing.

## Authoring with Tiled

Tiled ([mapeditor.org](https://www.mapeditor.org)) is the other door to the format, reached through
the build rather than at runtime.

- **`tiled.exe` is a GUI-subsystem binary.** Run from a console it prints nothing at all — no
  output, no error, no exit message — while doing exactly what it was asked. Silence is not failure.
- **`tmxrasterizer.exe` renders a `.tmj` to PNG headlessly**, which is how to look at one without
  opening the editor.

Create or edit a `.tmj` under the game's `asset-sources/maps/` and build. Referenced `.tsj` tilesets
anywhere under the asset-source root are inputs too, so painting with a retyped tile re-derives the
maps that use it. A map may reference across or upward within the tree; a reference resolving
outside it fails, because the build could not track the file.

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

`Capsule.Maps.Cli` is the dev-time tool the hook runs, once per build per source kind, with the
sources of that kind that changed:

```
Capsule.Maps.Cli import-tiled  --out <dir> [--tile-size <px>] <map.tmj> [<map.tmj>...]
Capsule.Maps.Cli import-tiled  --out <dir> [--tile-size <px>] --maps-from <list.txt>
Capsule.Maps.Cli import-native --out <dir> [--tile-size <px>] <map.map.json> [<map.map.json>...]
Capsule.Maps.Cli import-native --out <dir> [--tile-size <px>] --maps-from <list.txt>
```

The build uses the list form — a few hundred source paths overflow a command line. Running any of
them by hand is for debugging an import, never a step in the workflow. Each exits 0 when every
source succeeded, 1 when any failed and 2 on a usage error, and per-source failures reach the build
output verbatim. Name sources relatively: each path is stamped into its map's `source` block
verbatim, and the format refuses an absolute one.
