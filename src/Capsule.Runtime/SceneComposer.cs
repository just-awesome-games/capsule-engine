using Capsule.Maps;
using Capsule.Scenes;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

// Holds only the current parsed map so restarts do not touch disk.
internal sealed class SceneComposer(SceneRegistry scenes)
{
    // Where the map build hook lands its output in a shell's content, and the extension it
    // writes; a map name resolves against exactly that.
    private const string MapDirectory = "assets/maps";
    private const string MapExtension = ".map.json";

    private string? _heldName;
    private Map? _held;

    internal Scene Resolve(in SceneTarget target) => target.Kind switch
    {
        SceneTargetKind.Scene => ComposeScene(target.SceneType!),
        SceneTargetKind.Map => ComposeMap(target.MapName!),
        _ => throw new InvalidOperationException($"Unknown scene target kind '{target.Kind}'."),
    };

    // Maps ship into one flat directory, so a name that is a path would either escape it or point
    // at a file the hook never wrote, and one Windows resolves as a device would not be a file at
    // all. A map-backed scene's name comes from its class, so this guards both boot verbs.
    private static string MapFileName(string mapName)
    {
        if (!string.Equals(Path.GetFileName(mapName), mapName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A map name is the bare name of its authoring source, not a path: '{mapName}'.",
                nameof(mapName));
        }

        if (!SafeName.IsOneSafeDirectoryName(mapName))
        {
            throw new ArgumentException(
                $"A map name must be a single safe file name: no separators, no reserved device name, and no trailing dot or space: '{mapName}'.",
                nameof(mapName));
        }

        return mapName + MapExtension;
    }

    private Scene ComposeScene(Type sceneType) =>
        scenes.MapNameOf(sceneType) is { } mapName
            ? ComposeMap(mapName)
            : scenes.Create(sceneType);

    private Scene ComposeMap(string mapName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, MapDirectory, MapFileName(mapName));
        Map map = Hold(mapName, path);

        try
        {
            return scenes.CreateForMap(mapName, map);
        }
        catch (SpawnException exception)
        {
            // The scene layer is pure and knows no paths; naming the map is this layer's job.
            throw new SpawnException($"{path}: {exception.Message}", exception);
        }
    }

    // Map is immutable and its grid hands out read-only spans, so every scene composed from one
    // map may share it.
    private Map Hold(string mapName, string path)
    {
        if (_held is { } held && string.Equals(_heldName, mapName, StringComparison.Ordinal))
        {
            return held;
        }

        Map map = MapFile.Load(path);
        _heldName = mapName;
        _held = map;

        return map;
    }
}
