# Persistence

Saved state is `Run.Saves`, one store of named JSON documents: settings, a slot, a profile are names, and a game declares each as a `SaveKey<T>` over its own `JsonSerializerContext`. The host restores every document before the first scene composes, persists what each step wrote or deleted after that step — the step that requests exit included — and once more at teardown; reads are synchronous, so simulation code touches no file. A document's `SaveMetadata` is host state, set by the flush and visible from the next step.

A save document is a snapshot of live state, written at a save moment: a checkpoint, the options screen, quit. Gameplay mutates entities, never the document. Split documents by how often they change — settings, a profile, a slot. Inside one, each system contributes and reads back its own part.

The desktop medium keeps `<name>.save.json` per document, UTF-8 without a byte-order mark, LF, two-space indent, whatever formatting the game's context declares:

```json
{
  "metadata": {
    "createdAt": "2026-09-17T10:00:00+01:00",
    "updatedAt": "2026-09-18T11:30:00-07:00"
  },
  "document": {
    "Volume": 5,
    "Name": "a"
  }
}
```

`metadata` is the engine's half and `document` the game's JSON verbatim, hand-editable. A write is staged as `.save.json.tmp` and swapped in, keeping the previous file as `.save.json.bak`; at restore a file that does not parse is set aside as `.save.json.corrupt` and its backup restored in its place, with a warning either way. Where the files go, in reach order:

1. The local folder's `saves` subfolder, the folder slugged from the game's name: `%LOCALAPPDATA%\my-game\saves` on Windows, `$XDG_DATA_HOME/my-game/saves` (else `~/.local/share/my-game/saves`) on Linux, `~/Library/Application Support/my-game/saves` on macOS; `crash.log` sits beside `saves`.
2. `EngineBuilder.WithLocalFolder(name)` renames the folder, so a game renamed after release keeps its saves.
3. `EngineBuilder.WithSaveDirectory(path)`, or `--saves <dir>`, moves the saves directory itself: a portable build, a fresh-install playtest.
4. `EngineBuilder.WithSaveStorage(ISaveStorage)` replaces the medium for one shell. The medium itself is the platform module's `OpenSaveStorage` ([`platforms.md`](platforms.md)): the desktop module's is the folder above, and a platform that mounts a container rather than a folder answers with its own.

A headless run persists nothing unless lever 3 or 4 names a medium: persisted state is initial state, never a developer's own folder. The cloud story is a synchronized folder — Steam Auto-Cloud pointed at the saves directory.
