# Capsule.Cli

Capsule's dev-time command-line utilities. No game references it, which is why every line of
Tiled parsing lives here rather than in `Capsule.Scenes`: a game runtime never links an authoring
format.

## Commands

| Command | Consumes | Emits |
| --- | --- | --- |
| `import-tiled` | Tiled `*.tmj` maps and the `*.tsj` tilesets they reference | `<out>/<scene>.scene.json`, translated and stamped with its source |
| `import-native` | `*.scene.json` already in Capsule's own format | the same document re-emitted canonically, so nothing ships unvalidated |

Both write one document per source, attempt every source, and exit 0 when all succeeded, 1 when
any failed, and 2 on a usage error. Run the tool with no arguments for the full contract — the
flags, the `--scenes-from` list form, and their exact meanings.

## Inside

`Program` holds the command-line surface, `SceneDocumentTool` the per-source work, and `Tiled/`
the importer and its JSON model.

## How it ships

As `tools/` content in the `JAG.Capsule.Build` package; see [`../Capsule.Build/`](../Capsule.Build/README.md).
The build invokes it incrementally through `build/Capsule.SceneDocuments.targets`, and direct use
is for diagnosing an import.

## Further reading

The document format and the supported Tiled subset: [`docs/scenes.md`](../../docs/scenes.md).
