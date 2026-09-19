# Persistence

After this page you can declare a save document, write it at a save moment, read it back at boot, and
say where the files go.

## Declare a document

`Run.Saves` is one store of named JSON documents: settings, a slot, a profile are names. A game declares
each as a `SaveKey<T>` over its own `JsonSerializerContext`, beside the rest of its declarations at its
assembly root:

```csharp
public sealed record GameSettings
{
    public bool SoundOn { get; init; } = true;
}

public static class GameSaves
{
    /// <summary>The settings document. A first run has none, so it reads as the default settings.</summary>
    public static readonly SaveKey<GameSettings> Settings =
        new("settings", GameSaveContext.Default.GameSettings, new GameSettings());
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GameSettings))]
internal sealed partial class GameSaveContext : JsonSerializerContext;
```

The two-argument constructor declares a key whose absent document throws on read. The three-argument one
gives it a fallback, serialized once and deserialized afresh on each such read, so the instance is not
handed out. `TryRead` reads a key with no fallback.

## Write it and read it

A document is a snapshot of live state, written at a save moment: a checkpoint, the options screen, quit.
Gameplay mutates entities, not the document.

```csharp
GameSettings stored = Run.Saves.Read(GameSaves.Settings);
GameSettings settings = stored with { SoundOn = !stored.SoundOn };

Run.Saves.Write(GameSaves.Settings, settings);
Run.Audio.SetVolume(AudioBuses.Sfx, settings.SoundOn ? 1f : 0f);
```

The host restores every document before the first scene composes, so a scene reads its own state from
`OnStart` on:

```csharp
protected override void OnStart() =>
    Run.Audio.SetVolume(AudioBuses.Sfx, Run.Saves.Read(GameSaves.Settings).SoundOn ? 1f : 0f);
```

The host persists what each step wrote or deleted after that step, the step that requests exit included,
and once more at teardown. Reads are synchronous, so simulation code touches no file. `Exists` and
`Delete` complete the store, `Names` lists what it holds, and a document's `SaveMetadata` is host state,
set by the flush and visible from the next step.

Split documents by how often they change: settings, a profile, a slot. Inside one document, each system
contributes and reads back its own part.

## The file format

The desktop medium keeps `<name>.save.json` per document, UTF-8 without a byte-order mark, LF, two-space
indent, and whatever formatting the game's context declares:

```json
{
  "metadata": {
    "createdAt": "2026-09-17T10:00:00+01:00",
    "updatedAt": "2026-09-18T11:30:00-07:00"
  },
  "document": {
    "SoundOn": true
  }
}
```

`metadata` is the engine's half and `document` the game's JSON verbatim, hand-editable. A write is staged
as `.save.json.tmp` and swapped in, keeping the previous file as `.save.json.bak`. At restore, a file that
does not parse is set aside as `.save.json.corrupt` and its backup restored in its place, with a warning
either way. A field the document does not carry reads as its own default, and one the game no longer
declares is ignored, so a save written by an older build still loads.

## Where the files go

In reach order, each lever overriding the one above it:

1. The local folder's `saves` subfolder, the folder slugged from the game's name:
   `%LOCALAPPDATA%\my-game\saves` on Windows, `$XDG_DATA_HOME/my-game/saves` (else
   `~/.local/share/my-game/saves`) on Linux, `~/Library/Application Support/my-game/saves` on macOS.
   `crash.log` sits beside `saves`.
2. `EngineBuilder.WithLocalFolder(name)` renames the folder, so a game renamed after release keeps its
   saves.
3. `EngineBuilder.WithSaveDirectory(path)`, or `--saves <dir>`, moves the saves directory itself: a
   portable build, a fresh-install playtest.
4. `EngineBuilder.WithSaveStorage(ISaveStorage)` replaces the medium for one shell. The medium itself is
   the platform module's `OpenSaveStorage`, so a platform that mounts a container instead of a folder
   answers with its own ([`architecture.md`](architecture.md#platforms)).

A headless run persists nothing unless lever 3 or 4 names a medium, so persisted state is initial state
and not a developer's own folder. In a test, `Run.Saves` is in memory at either boundary
([`testing.md`](testing.md)). The cloud story is a synchronized folder, such as Steam Auto-Cloud pointed
at the saves directory.
