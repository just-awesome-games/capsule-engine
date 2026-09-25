using Capsule.Assets;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;

namespace Capsule.Runtime.Scenes;

// Holds only the current parsed document so restarts do not touch disk.
internal sealed class SceneComposer(SceneRegistry scenes, HostPlatform platform)
{
    // Where the scene-document build hook lands its output in a shell's content. A document name
    // resolves against it and the shipped extension.
    private const string DocumentDirectory = "assets";

    private string? _heldName;
    private SceneDocument? _held;

    // A class the registry backs with a document is composed from that document. One registered
    // plainly is built as it is, and one the registry does not hold is reported missing by it.
    internal Scene Resolve(in SceneTransition target) => target.Kind switch
    {
        SceneTransitionKind.Scene => scenes.DocumentNameOf(target.SceneType!) is { } name
            ? ComposeDocument(name)
            : scenes.Create(target.SceneType!),
        SceneTransitionKind.Named => ComposeDocument(target.DocumentName!),
        _ => throw new InvalidOperationException($"'{target.Kind}' names no scene to compose."),
    };

    // What SceneDocumentFile.Load does for a path, over the platform's shipped content instead.
    private SceneDocument Load(string path)
    {
        using Stream content = platform.OpenContent(path);

        try
        {
            return ShippedSceneDocument.Read(content);
        }
        catch (SceneDocumentFormatException exception)
        {
            throw new SceneDocumentFormatException($"{path}: {exception.Message}", exception);
        }
    }

    private static string DocumentFileName(string name) =>
        AssetPaths.IsKey(name)
            ? name + ShippedSceneDocument.Extension
            : throw new ArgumentException(
                $"A scene document name is '/'-joined key segments of ASCII letters, digits, '-' and '_', none of them a reserved Windows device name (nul, con, ...), with no extension: '{name}'.",
                nameof(name));

    private Scene ComposeDocument(string name)
    {
        string path = DocumentDirectory + "/" + DocumentFileName(name);
        SceneDocument document = Hold(name, path);

        try
        {
            return scenes.CreateFromDocument(name, document);
        }
        catch (SpawnException exception)
        {
            // The scene layer is pure and knows no paths, so this layer names the document.
            throw new SpawnException($"{path}: {exception.Message}", exception);
        }
    }

    // A SceneDocument is immutable and its grid hands out read-only spans, so every scene composed from
    // one document may share it.
    private SceneDocument Hold(string name, string path)
    {
        if (_held is { } held && string.Equals(_heldName, name, StringComparison.Ordinal))
        {
            return held;
        }

        SceneDocument document = Load(path);
        _heldName = name;
        _held = document;

        return document;
    }
}
