# Capsule.Levels

Capsule levels are plain JSON: a tile grid, a palette of tile types and a list of typed
entities, constructible by hand, from code, or by any tool that can write the shape. Tiled is
the suggested third-party editor and reaches the format through `Capsule.Levels.Cli` — it is
upstream authoring, and the level file is the source of truth a game reads.

## Tiled

Download it from [mapeditor.org](https://www.mapeditor.org) ([docs](https://doc.mapeditor.org)),
then add the install directory to `PATH` — on Windows that is `C:\Program Files\Tiled`.

Two Windows quirks worth knowing before you conclude something is broken:

- **`tiled.exe` is a GUI-subsystem application.** Run from a console it prints nothing at all —
  no output, no error, no exit message — while doing exactly what it was asked. Silence is not
  failure.
- **`tmxrasterizer.exe` renders a map to PNG headlessly**, which is how you look at a map
  without opening the editor.

## The CLI

```
# generate a level from a Tiled map (deterministic; re-running an unchanged map is a no-op diff)
Capsule.Levels.Cli import-tiled maps/room.tmj levels/room.level.json

# number any entity written without an id, then rewrite the file canonically
Capsule.Levels.Cli assign-ids levels/room.level.json

# check levels, and that generated ones still match their source — the pre-commit gate
Capsule.Levels.Cli validate levels/*.level.json
```

`validate` exits non-zero and writes to stderr on any failure, so it drops straight into a
hook or a CI step.

## Conventions

- **A tile's meaning is its tileset tile's Class**, unique across the map's tilesets. Every
  Class in a tileset enters the palette whether or not it is painted, so painting a new type
  never renumbers the ones already in a committed level.
- **Anything with identity is an object with a Class.** Object layers carry entities; tile
  layers are anonymous terrain. Layer *type* decides which is which — layer names are yours.
- **Entity ids are monotonic through `nextEntityId`** and never hand-authored. Ids are never
  reused, and deleting an entity never rewinds the counter. `assign-ids` fills the gaps.
- **A file with a `source` block is generated.** Edit the source and re-import; `validate`
  fails loudly on anything hand-edited into it.
