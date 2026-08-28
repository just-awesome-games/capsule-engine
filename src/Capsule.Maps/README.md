# Capsule.Maps

`Capsule.Maps` defines Capsule's runtime map model and canonical JSON format. Authoring-format parsers live in `Capsule.Maps.Cli` and never ship in a game.

A map contains a `TileGrid` and typed `MapObject` values. `MapFile` reads and writes format version 1 as two-space-indented UTF-8 JSON with LF endings and one trailing newline.

## Format invariants

- `formatVersion` is required and must be supported.
- Palette index 0 is `empty` with no color. Other tile types are unique and non-blank.
- A grid contains exactly `width * height` palette indices.
- Colors use lowercase `#rrggbbaa`.
- Object IDs are unique, positive, and lower than `nextObjectId`; deleted IDs are not reused.
- A `source` block records tool, relative source path, and SHA-256 of the source closure. Its presence marks a derived file, not an authoring source.

Invalid maps throw `MapFormatException`.

## Build pipeline

With the canonical game layout, games author sources under `src/asset-sources/maps/`:

| Source | Import |
| --- | --- |
| `*.map.json` | Validated and canonicalized native map JSON. |
| `*.tmj` | Finite orthogonal Tiled maps imported at build time. |

The build writes derived maps under `obj/` and copies them to `assets/maps/<name>.map.json` beside the executable. Sources sharing a stem fail because the shipped directory is flat. Derived maps are never committed.

The shell role imports maps automatically. A role-free test or tool can set `<CapsuleImportMaps>true</CapsuleImportMaps>`. Set `<CapsuleTileSize>` on the shell to reject maps authored at another tile size.

For a native `.map.json` source, omit `source`; the build validates the document and writes provenance into the derived copy.

## Tiled subset

Capsule imports `.tmj` files that are orthogonal, finite, square-tiled, CSV-encoded, unflipped, and contain exactly one tile layer.

- A tileset tile's Class becomes its semantic tile type.
- An optional Color property named `color` becomes its presentation color.
- Tile Classes are unique across all referenced tilesets; `empty` is reserved.
- Object layers contain objects whose Class becomes the spawn type.
- Tiled's object IDs and `nextobjectid` are preserved.
- Referenced `.tsj` files must remain under the asset-source root.

Unsupported input fails the build with the file and violated constraint.

## Importer

`Capsule.Maps.Cli` exposes `import-native` and `import-tiled` commands. Run the tool with `--help` for its command-line contract. The build invokes it incrementally; direct use is for diagnosing an import.

Tiled's Windows GUI executable writes no console output even on success. Use `tmxrasterizer` when a headless PNG preview is needed.
