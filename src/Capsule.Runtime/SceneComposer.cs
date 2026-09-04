using Capsule.Assets;
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

    // A scene document's name is its key: the path the build shipped it at under assets/scenes.
    // Judged by the build's own key grammar, so a name that is no key — one climbing out of that
    // directory, or one the hook could never have written a file for — is refused here rather than
    // reaching the file system. A document-backed scene's key comes from its class, so this guards
    // both boot verbs.
    private static string DocumentFileName(string name) =>
        AssetPaths.IsKey(name)
            ? name + DocumentExtension
            : throw new ArgumentException(
                $"A scene document name is '/'-joined key segments of ASCII letters, digits, '-' and '_', none of them a reserved Windows device name (nul, con, ...), with no extension: '{name}'.",
                nameof(name));

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
