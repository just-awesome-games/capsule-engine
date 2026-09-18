using Capsule.Persistence;
using Capsule.Runtime.Audio;

namespace Capsule.Runtime;

/// <summary>
/// The host family a shell boots on: where shipped content is read from, where saves and the
/// crash log land, and how the window and the audio output behave there. The neutral host holds
/// none of this itself, so a shell hands one to <c>CapsuleBoot.Configure</c> beside the game's
/// name; <c>Capsule.Runtime.Desktop</c> ships the one for Windows, Linux and macOS, and a private
/// platform module subclasses this for any other host (<c>docs/platforms.md</c>). The two abstract
/// members are the whole of what a headless run needs; every window member defaults to a host
/// with no window policy of its own.
/// </summary>
public abstract class HostPlatform
{
    /// <summary>
    /// Opens shipped content for reading. Paths are the build's, relative to the publish root with
    /// forward slashes and no leading separator — <c>assets/textures/hero.png</c>,
    /// <c>assets/scenes/hall.scene.json</c> — and are validated before they arrive here, so an
    /// implementation joins and opens. The caller disposes the stream.
    /// </summary>
    /// <exception cref="FileNotFoundException">Nothing ships at that path.</exception>
    /// <exception cref="DirectoryNotFoundException">Nothing ships under that path's directory; treated as <see cref="FileNotFoundException"/> is.</exception>
    public abstract Stream OpenContent(string relativePath);

    /// <summary>
    /// The medium save documents are kept on when the shell named neither a storage nor a
    /// directory (<c>EngineBuilder.WithSaveStorage</c>, <c>WithSaveDirectory</c>). Called once per
    /// windowed run; a headless run never asks.
    /// </summary>
    /// <param name="localFolderName">The game's local folder name: one safe directory name, the slug of its display name unless <c>EngineBuilder.WithLocalFolder</c> replaced it.</param>
    public abstract ISaveStorage OpenSaveStorage(string localFolderName);

    /// <summary>
    /// Records an exception escaping a windowed run, which the host rethrows afterwards whatever
    /// this does. Must not throw. Does nothing unless overridden.
    /// </summary>
    /// <param name="localFolderName">The game's local folder name, as <see cref="OpenSaveStorage"/> receives it.</param>
    /// <param name="exception">The escaping exception.</param>
    public virtual void ReportCrash(string localFolderName, Exception exception)
    {
    }

    /// <summary>
    /// Brings the window to the front once, on the first frame drawn after the backend shows it.
    /// Does nothing unless overridden.
    /// </summary>
    /// <param name="window">The backend's window handle.</param>
    public virtual void RaiseWindow(nint window)
    {
    }

    /// <summary>
    /// Whether the window holds keyboard focus, sampled every frame: an inactive window reads no
    /// pointer as its own and ducks to the run's unfocused volume. True unless overridden.
    /// </summary>
    /// <param name="window">The backend's window handle.</param>
    public virtual bool HasInputFocus(nint window) => true;

    /// <summary>
    /// Asks to have <paramref name="redraw"/> called whenever the window is resized or exposed
    /// from inside the platform's own event handling — for a host whose modal resize blocks the
    /// game loop — on the thread that installed it; the host guards against re-entry. Null unless
    /// overridden, on which the window's contents stand while a resize is in progress. Disposing
    /// the result stops the calls; the host disposes it ahead of the renderer.
    /// </summary>
    /// <param name="window">The backend's window handle.</param>
    /// <param name="redraw">
    /// Draws the settled frame; receives the window's current client width and height in pixels,
    /// read at the call ahead of any event announcing them.
    /// </param>
    public virtual IDisposable? WatchWindowRedraw(nint window, Action<int, int> redraw) => null;

    /// <summary>
    /// Attaches to the output the sound device opened so it can follow the system's default
    /// output as it moves. Called once after the device opens, on the game thread. Null unless
    /// overridden, on which sound stays on the output the run opened.
    /// </summary>
    /// <param name="defaultChanged">
    /// To call when the default output changes; safe from any thread, and does nothing but note
    /// the change.
    /// </param>
    public virtual AudioOutput? WatchDefaultAudioOutput(Action defaultChanged) => null;
}
