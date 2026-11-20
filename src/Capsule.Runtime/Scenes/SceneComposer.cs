using Capsule.Assets;
using Capsule.Runtime.Assets;
using Capsule.Runtime.Platform;
using Capsule.Scenes;

namespace Capsule.Runtime.Scenes;

// Holds the parsed document last composed and the running scene's. A restart then touches no disk, even after a
// prefetch composed another. The two are one document except between a prefetch and its boundary. It reads the
// document a prefetch names on the asset decode threads. Every member runs on the game thread, and a read's own work
// touches nothing here.
internal sealed class SceneComposer(SceneRegistry scenes, HostPlatform platform)
{
    private Held? _composed;
    private Held? _running;

    // The read a prefetch began and no scene has composed yet. Which step composes it never depends on
    // whether it has finished.
    private Read? _read;

    // The document being read ahead, or null when none is.
    internal string? Reading => _read?.Name;

    // A class the registry backs with a document is composed from that document. One registered
    // plainly is built as it is, and one the registry does not hold is reported missing by it. Resolving
    // any scene ends the read of a document other than its own.
    internal Scene Resolve(in SceneTransition target)
    {
        string? name = DocumentNameOf(target);
        EndReadUnless(name);

        return name is null ? scenes.Create(target.SceneType!) : ComposeDocument(name);
    }

    // Starts reading the document target is composed from and returns true, or returns false when target has
    // no document or this composer holds it. A read already begun for it is kept, and a read of any other
    // document ends.
    internal bool BeginRead(in SceneTransition target)
    {
        if (DocumentNameOf(target) is not { } name || IsHeld(name))
        {
            return false;
        }

        EndReadUnless(name);
        if (_read is null)
        {
            // Where the game thread cannot wait on a worker, the document is read here when it is composed, at
            // the step a worker's read is waited for.
            string path = PathOf(name);
            CancellationTokenSource? cancel = WorkerThreads.CanWaitOn ? new() : null;
            _read = new Read(name, cancel is null ? null : AssetDecodes.Run(() => Load(path), cancel.Token), cancel);
        }

        return true;
    }

    // Ends the read under way. One not yet started never starts, and one under way runs to its end unread.
    internal void EndRead() => EndReadUnless(null);

    // Keeps the document of the scene a boundary opened, which a restart composes again, and lets go of the one
    // the outgoing scene ran. A scene built in code keeps none.
    internal void Entered(SceneKey? scene) => _running = scene is { } key ? HeldAs(key.Name) : null;

    private string? DocumentNameOf(in SceneTransition target) => target.Kind switch
    {
        SceneTransitionKind.Scene => scenes.DocumentNameOf(target.SceneType!),
        SceneTransitionKind.Named => target.DocumentName!,
        _ => throw new InvalidOperationException($"'{target.Kind}' names no scene to compose."),
    };

    private bool IsHeld(string name) => HeldAs(name) is not null;

    private Held? HeldAs(string name) =>
        _composed is { } composed && string.Equals(composed.Name, name, StringComparison.Ordinal) ? composed
        : _running is { } running && string.Equals(running.Name, name, StringComparison.Ordinal) ? running
        : null;

    private void EndReadUnless(string? name)
    {
        if (_read is { } read && !string.Equals(read.Name, name, StringComparison.Ordinal))
        {
            _read = null;
            read.Release();
        }
    }

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

    private static string PathOf(string name) => ShippedSceneDocument.Folder + "/" + DocumentFileName(name);

    private static string DocumentFileName(string name) =>
        AssetPaths.IsKey(name)
            ? name + ShippedSceneDocument.Extension
            : throw new ArgumentException(
                $"A scene document name is '/'-joined key segments of ASCII letters, digits, '-' and '_', none of them a reserved Windows device name (nul, con, ...), with no extension: '{name}'.",
                nameof(name));

    private Scene ComposeDocument(string name)
    {
        string path = PathOf(name);
        SceneDocument document = Hold(name, path);

        try
        {
            return scenes.Create(new SceneKey(name), document);
        }
        catch (SpawnException exception)
        {
            // The scene layer is pure and knows no paths. This layer names the document.
            throw new SpawnException($"{path}: {exception.Message}", exception);
        }
    }

    // A SceneDocument is immutable and its grid hands out read-only spans. Every scene composed from
    // one document may share it.
    private SceneDocument Hold(string name, string path)
    {
        // A held document serves again. The running one becomes the last composed and lets go of a prefetch's.
        if (HeldAs(name) is { } held)
        {
            _composed = held;
            return held.Document;
        }

        // Resolve ended any read of another document. A read under way is this one's.
        SceneDocument document;
        if (_read is { } read)
        {
            // Waits for the read, which rethrows its failure as the read raised it, here where a load on this
            // thread would have raised it.
            _read = null;
            try
            {
                document = read.Task is { } task ? task.GetAwaiter().GetResult() : Load(path);
            }
            finally
            {
                read.Release();
            }
        }
        else
        {
            document = Load(path);
        }

        _composed = new Held(name, document);

        return document;
    }

    private readonly record struct Held(string Name, SceneDocument Document);

    private readonly record struct Read(string Name, Task<SceneDocument>? Task, CancellationTokenSource? Cancel)
    {
        // A read that has not taken its turn never runs. One under way runs to its end, and nothing reads it.
        internal void Release()
        {
            Cancel?.Cancel();
            Cancel?.Dispose();
        }
    }
}
