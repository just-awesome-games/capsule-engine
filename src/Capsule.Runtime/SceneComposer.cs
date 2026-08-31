using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime;

// Holds only the current parsed document so restarts do not touch disk.
internal sealed class SceneComposer(SceneRegistry scenes)
{
    // Where the scene-document build hook lands its output in a shell's content, and the extension
    // it writes; a document name resolves against exactly that.
    private const string DocumentDirectory = "assets/scenes";
    private const string DocumentExtension = ".scene.json";

    private string? _heldName;
    private SceneDocument? _held;

    internal Scene Resolve(in SceneTarget target) => target.Kind switch
    {
        SceneTargetKind.Scene => ComposeType(target.SceneType!),
        SceneTargetKind.Named => ComposeDocument(target.DocumentName!),
        _ => throw new InvalidOperationException($"Unknown scene target kind '{target.Kind}'."),
    };

    // Scene documents ship into one flat directory, so a name that is a path would either escape
    // it or point at a file the hook never wrote, and one Windows resolves as a device would not
    // be a file at all. A document-backed scene's name comes from its class, so this guards both
    // boot verbs.
    private static string DocumentFileName(string name)
    {
        if (!string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"A scene document name is the bare name of its authoring source, not a path: '{name}'.",
                nameof(name));
        }

        if (!SafeName.IsOneSafeDirectoryName(name))
        {
            throw new ArgumentException(
                $"A scene document name must be a single safe file name: no separators, no reserved device name, and no trailing dot or space: '{name}'.",
                nameof(name));
        }

        return name + DocumentExtension;
    }

    // A class the registry backs with a document is composed from it; one it registers plainly is
    // built as it is, and one it does not hold at all is named as missing by the registry.
    private Scene ComposeType(Type sceneType) =>
        scenes.DocumentNameOf(sceneType) is { } name
            ? ComposeDocument(name)
            : scenes.Create(sceneType);

    private Scene ComposeDocument(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, DocumentDirectory, DocumentFileName(name));
        SceneDocument document = Hold(name, path);

        try
        {
            return scenes.CreateFromDocument(name, document);
        }
        catch (SpawnException exception)
        {
            // The scene layer is pure and knows no paths; naming the document is this layer's job.
            throw new SpawnException($"{path}: {exception.Message}", exception);
        }
    }

    // A SceneDocument is immutable and its grid hands out read-only spans, so every scene composed
    // from one document may share it.
    private SceneDocument Hold(string name, string path)
    {
        if (_held is { } held && string.Equals(_heldName, name, StringComparison.Ordinal))
        {
            return held;
        }

        SceneDocument document = SceneDocumentFile.Load(path);
        _heldName = name;
        _held = document;

        return document;
    }
}
