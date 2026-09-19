using Capsule.Persistence;
using Capsule.Runtime.Audio;

namespace Capsule.Runtime;

/// <summary>
/// Where a run reads its shipped content, keeps its saves and its crash log, and how the shipped
/// backend treats its window and its audio output. A shell hands one to
/// <c>CapsuleBoot.Configure</c> beside the game's name. <c>Capsule.Runtime.Desktop</c> implements it
/// for Windows, Linux and macOS, and a private platform module subclasses this for another host
/// (<c>docs/architecture.md</c>). A headless run needs only the two abstract members. Every window
/// member defaults to doing nothing.
/// </summary>
public abstract class HostPlatform
{
    /// <summary>
    /// Opens shipped content for reading. A path is the build's own, relative to the publish root
    /// with forward slashes and no leading separator, and is validated before it arrives here. The
    /// caller disposes the stream.
    /// </summary>
    /// <exception cref="FileNotFoundException">Nothing ships at that path.</exception>
    public abstract Stream OpenContent(string relativePath);

    /// <summary>
    /// The medium save documents are kept on when the shell named neither a storage nor a
    /// directory. Called once per windowed run. A headless run never asks.
    /// </summary>
    /// <param name="localFolderName">The game's local folder name: one safe directory name.</param>
    public abstract ISaveStorage OpenSaveStorage(string localFolderName);

    /// <summary>
    /// Records an exception escaping a windowed run. The host rethrows it afterwards whatever this
    /// does. Must not throw.
    /// </summary>
    public virtual void ReportCrash(string localFolderName, Exception exception)
    {
    }

    /// <summary>Brings the window to the front once, on the first frame drawn after the backend shows it.</summary>
    public virtual void RaiseWindow(WindowHandle window)
    {
    }

    /// <summary>
    /// Whether the window holds keyboard focus, sampled every frame. An inactive window claims no
    /// pointer input and ducks to the run's unfocused volume.
    /// </summary>
    public virtual bool HasInputFocus(WindowHandle window) => true;

    /// <summary>
    /// Asks to have <paramref name="redraw"/> called whenever the window is resized or exposed from
    /// inside the platform's own event handling, for a host whose modal resize blocks the game loop.
    /// Called on the thread that installed it, and the host guards against re-entry. Null leaves the
    /// window's contents standing while a resize is in progress. Disposing the result stops the
    /// calls, and the host disposes it ahead of the renderer.
    /// </summary>
    /// <param name="window">The window to watch.</param>
    /// <param name="redraw">
    /// Draws the settled frame. Receives the window's client width and height in pixels, read at
    /// the call.
    /// </param>
    public virtual IDisposable? WatchWindowRedraw(WindowHandle window, Action<int, int> redraw) => null;

    /// <summary>
    /// Attaches to the output the sound device opened so it can follow the system's default output
    /// as it moves. Called once after the device opens, on the game thread. Null leaves sound on
    /// the output the run opened.
    /// </summary>
    /// <param name="defaultChanged">
    /// Call when the default output changes. Safe from any thread, and only notes the change.
    /// </param>
    public virtual AudioOutput? WatchDefaultAudioOutput(Action defaultChanged) => null;
}
