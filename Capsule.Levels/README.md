# Capsule.Levels

Capsule levels are plain JSON: a tile grid, a palette of tile types and a list of typed
entities. Code writes the shape directly — tests and procedural generation build a `Level` and
mint ids from `nextEntityId` — and Tiled reaches it through the build.

## Authoring a level

Create or edit a `.tmj` in the game's `asset-sources/levels/` and build. The level is derived
into `obj/` and copied to `Assets/Levels/<map>.level.json` beside the executable, which is where
a game loads it from. **Nothing generated is committed**: the map is the source, the level is a
build artifact, and there is no step to remember or forget.

The hook that does this ships with the engine, and a game wires it once at bootstrap — the
repo-root `Directory.Build.targets` import plus `<CapsuleGameShell>true</CapsuleGameShell>` on
the shell project ([`docs/consuming-capsule.md`](../docs/consuming-capsule.md)).

Sources are found at `$(CapsuleAssetSourcesDir)/levels/**/*.tmj`, defaulting to
`$(MSBuildProjectDirectory)/../asset-sources` — the shell one level below the repo root, with
`asset-sources/` its sibling. Set `CapsuleAssetSourcesDir` to move them. A level is named after
its map, so map names are unique across the whole tree.

Referenced `.tsj` tilesets under that same tree are inputs too: editing a tile's Class
re-derives every level, since a tileset is where a level's palette comes from.

## Tiled

Download it from [mapeditor.org](https://www.mapeditor.org) ([docs](https://doc.mapeditor.org)),
then add the install directory to `PATH` — on Windows that is `C:\Program Files\Tiled`.

Two Windows quirks worth knowing before you conclude something is broken:

- **`tiled.exe` is a GUI-subsystem application.** Run from a console it prints nothing at all —
  no output, no error, no exit message — while doing exactly what it was asked. Silence is not
  failure.
- **`tmxrasterizer.exe` renders a map to PNG headlessly**, which is how you look at a map
  without opening the editor.

## Conventions

- **A tile's meaning is its tileset tile's Class**, unique across the map's tilesets. Every
  Class in a tileset enters the palette whether or not it is painted, so painting a new type
  never renumbers the ones already in use.
- **Anything with identity is an object with a Class.** Object layers carry entities; tile
  layers are anonymous terrain. Layer *type* decides which is which — layer names are yours.
- **Entity ids are monotonic through `nextEntityId`** and never hand-authored. Tiled mints them
  for a map; code building a `Level` mints them the same way. Ids are never reused, and deleting
  an entity never rewinds the counter.
- **A `source` block is provenance** — the tool, the map it came from, and that map's hash. Its
  path is whatever the importer was handed, which for a build is the map relative to the shell
  project: `../asset-sources/levels/room.tmj`, a path a person can open. Nothing resolves it at
  runtime.

## The importer

`Capsule.Levels.Cli` is what the hook runs, once per build with the maps that changed:

```
Capsule.Levels.Cli import-tiled --out <dir> <map.tmj> [<map.tmj>...]
Capsule.Levels.Cli import-tiled --out <dir> --maps-from <list.txt>
```

The build uses the second form — a few hundred map paths overflow a command line — and drives
it once per build with the maps that changed, from the shell project directory. Running either
by hand is for debugging an import, never a step in the workflow. It exits 0 when every map
succeeded, 1 when any failed, 2 on a usage error, and its per-map failures reach the build
output verbatim.

Name the maps relatively: each path is stamped into its level's `source` block verbatim, and the
format refuses an absolute one.
