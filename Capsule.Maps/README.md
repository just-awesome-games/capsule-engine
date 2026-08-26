# Capsule.Maps

A **map** is an authored spatial content document: a `TileGrid` of terrain and what its tiles draw
as, plus the typed `MapObject`s placed over it. A **scene** is the runtime container a map may be composed
into. This module is built along that line: nothing here knows a scene exists, and the project
takes no package references, reaching only `Capsule.Core` for the `ColorRgba` a palette entry
carries — so no authoring format is ever a runtime dependency.

A map is plain JSON and its shape is public, so code writes it directly: a test or a procedural
generator constructs a `TileGrid` and a `Map` and mints object ids from `NextObjectId`, with no
file and no authoring tool in sight. `MapFile` reads and writes that same shape, and its written
form is canonical — the fields in order (`grid`, `objects`, `nextObjectId`, `source`), two-space
indent, LF, UTF-8 without a BOM, one trailing newline — so re-generating an unchanged map
reproduces its bytes exactly and a diff shows only real change. Whatever the format forbids
throws a `MapFormatException` from the constructor, so a map that exists is one a game can trust.

Tiled is the other door, reached through the build rather than at runtime — a door, not the
format. The build dispatches by extension, so a second authoring tool becomes another item group
in [`build/Capsule.Maps.targets`](../build/Capsule.Maps.targets) and never new wiring in the game.

## Format invariants

These bind every map however it was made — one constructor checks a map built from code and one
read from a file, identically.

- **A palette entry is a tile type and what it draws as** — a `TileDefinition` of a name and a
  colour. Index 0 is exactly `empty` with no colour, reserved for the absence of a tile and never
  a game's own type; a colour there is rejected, and every other entry must carry one. Names are
  unique and non-blank, every tile is an index into the palette, and a grid holds exactly
  `width * height` of them.
- **A colour is written `#rrggbbaa`, lowercase.** An uppercase spelling is rejected rather than
  accepted and written back differently: a map must survive its own round trip byte for byte.
- **Object ids are monotonic through `nextObjectId`** and never hand-authored. Each is at least 1,
  below `nextObjectId`, and unique within the map. Ids are never reused, and deleting an object
  never rewinds the counter.
- **A `source` block is provenance** — the tool, the path it was handed, and that source's
  SHA-256. Its presence means the file is an artifact: edit the source and re-import, never the
  file. The path is relative and forward-slashed so it means the same thing on every machine, and
  nothing resolves it at runtime.

## Authoring with Tiled

Create or edit a `.tmj` in the game's `asset-sources/maps/` and build. Its map is derived into
`obj/` and copied to `Assets/Maps/<name>.map.json` beside the executable, which is where a game
loads it from. **Nothing generated is committed**: the `.tmj` is the source, the map is a build
artifact, and there is no step to remember or forget.

The hook that does this ships with the engine, and a game wires it once at bootstrap — the
repo-root `Directory.Build.targets` import plus `<CapsuleGameShell>true</CapsuleGameShell>` on
the shell project ([`docs/consuming-capsule.md`](../docs/consuming-capsule.md)).

Sources are found at `$(CapsuleAssetSourcesDir)/maps/**/*.tmj`, defaulting to
`$(MSBuildProjectDirectory)/../asset-sources` — the shell one level below the repo root, with
`asset-sources/` its sibling. Set `CapsuleAssetSourcesDir` to move them. A map is named after the
`.tmj` it came from, so those file names are unique across the whole tree.

Referenced `.tsj` tilesets under that same tree are inputs too: editing a tile's Class or its
colour re-derives every map, since a tileset is where a palette comes from. So is the importer
itself — pull a Capsule change that alters how a map is derived and the next build re-derives the
lot rather than shipping what the previous importer wrote.

A game may declare the one tile size every map is authored at, on the same shell project:

```xml
<CapsuleTileSize>16</CapsuleTileSize>
```

The hook hands it to the importer, and a map whose own tile size differs fails the build naming
the map, its size and the declared one. Left unset, no size is imposed and each map keeps its own.

### Installing Tiled

Download it from [mapeditor.org](https://www.mapeditor.org) ([docs](https://doc.mapeditor.org)),
then add the install directory to `PATH` — on Windows that is `C:\Program Files\Tiled`.

Two Windows quirks worth knowing before you conclude something is broken:

- **`tiled.exe` is a GUI-subsystem application.** Run from a console it prints nothing at all —
  no output, no error, no exit message — while doing exactly what it was asked. Silence is not
  failure.
- **`tmxrasterizer.exe` renders a `.tmj` to PNG headlessly**, which is how you look at one
  without opening the editor.

### Authoring a tile

A tile carries both what it is and what it draws as, and both are authored on the tileset tile
rather than on any map that paints it:

1. Open the tileset in Tiled and select the tile.
2. Set its **Class** to the tile type — the string the palette carries and `TileTypeAt` returns.
3. In **Custom Properties**, add a property named exactly `color`, of type **Color**, and pick
   the colour the tile draws as.

Tiled writes that colour as `#AARRGGBB`, alpha leading — or `#RRGGBB` where it is fully opaque —
and the importer reorders it into the map format's `#rrggbbaa`.

A tile with a Class fails the import unless it carries a `color` property **declared as a Color** —
a String property holding the same `#AARRGGBB` text is rejected as firmly as a missing one. Either
failure names the tileset, the tile id and the Class. There is no default and no fallback: a tile
authored without an appearance would otherwise render as something plausible, and a mis-authored
tileset has to fail rather than look right.

### How a `.tmj` is read

These are the importer's rules rather than the format's: they say how Tiled's vocabulary is read
into a map, and bind nothing about a map built from code.

- **A tile's Class is unique across the `.tmj`'s tilesets**, and `empty` is reserved, so no tile
  may claim it. Every Class in a tileset enters the palette whether or not it is painted, so
  painting a new type never renumbers the ones already in use.
- **Anything with identity is an object with a Class.** Object layers carry objects; tile layers
  are anonymous terrain. Layer *type* decides which is which — layer names are yours.
- **Tiled mints the ids.** Its own object ids and `nextobjectid` carry straight through, which is
  what makes them monotonic with nobody maintaining them.
- Capsule imports orthogonal, finite `.tmj`s with square tiles, CSV tile data, unflipped tiles and
  exactly one tile layer. Anything else fails the import naming the file and the setting to
  change.

## The importer

`Capsule.Maps.Cli` is the dev-time tool the hook runs, once per build with the `.tmj`s that
changed:

```
Capsule.Maps.Cli import-tiled --out <dir> [--tile-size <px>] <map.tmj> [<map.tmj>...]
Capsule.Maps.Cli import-tiled --out <dir> [--tile-size <px>] --maps-from <list.txt>
```

The build uses the second form — a few hundred source paths overflow a command line — and runs it
from the shell project directory, passing `--tile-size` whatever the shell set `CapsuleTileSize`
to and omitting it where nothing was declared. Running either by hand is for debugging an import,
never a step in the workflow. It exits 0 when every source succeeded, 1 when any failed, 2 on a
usage error, and its per-source failures reach the build output verbatim.

Name the sources relatively: each path is stamped into its map's `source` block verbatim, and the
format refuses an absolute one.
