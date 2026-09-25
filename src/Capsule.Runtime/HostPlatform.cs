using Capsule.Persistence;
using Capsule.Runtime.Audio;

namespace Capsule.Runtime;

/// <summary>
/// Where a run reads its shipped content, keeps its saves and its crash log, and how the shipped
/// backend treats its window and its audio output.
/// </summary>
/// <remarks>
/// A shell hands one to <c>CapsuleBoot.Configure</c> beside the game's name.
/// <c>Capsule.Runtime.Desktop</c> implements it for Windows, Linux and macOS, and a private
/// platform module subclasses this for another host (<c>docs/architecture.md</c>). Only the engine
/// calls these members. A headless run needs only the two abstract members, and every virtual
/// member has a working default.
/// </remarks>
public abstract class HostPlatform
{
    /// <summary>
    /// Opens shipped content for reading. A path is the build's own, relative to the publish root
    /// with forward slashes and no leading separator, and is validated before it arrives here.
    /// </summary>
    /// <remarks>
    /// The caller disposes the stream. Calls may arrive from any thread, several at once.
    /// </remarks>
    /// <exception cref="FileNotFoundException">Nothing ships at that path.</exception>
    protected internal abstract Stream OpenContent(string relativePath);

    /// <summary>
    /// The medium save documents are kept on when the shell named neither a storage nor a
    /// directory. Called once per windowed run.
    /// </summary>
    /// <remarks>A headless run never asks.</remarks>
    /// <param name="localFolderName">The game's local folder name: one safe directory name.</param>
    protected internal abstract ISaveStorage OpenSaveStorage(string localFolderName);

    /// <summary>
    /// Records an exception escaping a windowed run. The host rethrows it afterwards whatever this
    /// does.
    /// </summary>
    /// <remarks>Must not throw.</remarks>
    protected internal virtual void ReportCrash(string localFolderName, Exception exception)
    {
    }

    /// <summary>Brings the window to the front once, on the first frame drawn after the backend shows it.</summary>
    protected internal virtual void RaiseWindow(WindowHandle window)
    {
    }

    /// <summary>
    /// Whether the window holds keyboard focus, sampled every frame. True by default.
    /// </summary>
    /// <remarks>
    /// An inactive window claims no pointer input and ducks to the run's unfocused volume.
    /// </remarks>
    protected internal virtual bool HasInputFocus(WindowHandle window) => true;

    /// <summary>Holds the pointer inside the window, or releases it. Does nothing by default.</summary>
    /// <remarks>
    /// Called on the game thread when the run's cursor changes. The platform releases the hold while
    /// the window is unfocused and restores it when focus returns.
    /// </remarks>
    /// <param name="window">The window to hold the pointer in.</param>
    /// <param name="confined">Whether to hold the pointer or release it.</param>
    protected internal virtual void ConfineCursor(WindowHandle window, bool confined)
    {
    }

    /// <summary>
    /// Calls <paramref name="redraw"/> whenever the window is resized or exposed inside the
    /// platform's own event handling, for a host whose modal resize blocks the game loop.
    /// </summary>
    /// <remarks>
    /// Calls arrive on the thread that installed the watch, and the host guards against re-entry. A
    /// null result leaves the window's contents standing during a resize. Disposing the result stops
    /// the calls. The host disposes it before the renderer.
    /// </remarks>
    /// <param name="window">The window to watch.</param>
    /// <param name="redraw">
    /// Draws the settled frame. Receives the window's client width and height in pixels, read at
    /// the call.
    /// </param>
    protected internal virtual IDisposable? WatchWindowRedraw(WindowHandle window, Action<int, int> redraw) => null;

    /// <summary>
    /// Attaches to the output the sound device opened, for a platform that follows the system's
    /// default output as it moves. Called once on the game thread after the device opens.
    /// </summary>
    /// <remarks>A null result leaves sound on the output the run opened.</remarks>
    /// <param name="defaultChanged">
    /// Call when the default output changes. Safe from any thread, and only notes the change.
    /// </param>
    protected internal virtual AudioOutput? WatchDefaultAudioOutput(Action defaultChanged) => null;
}
