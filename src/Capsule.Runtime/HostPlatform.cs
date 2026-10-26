using Capsule.Persistence;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Sdl;

namespace Capsule.Runtime;

/// <summary>
/// Where a run reads its shipped content, keeps its saves and its crash log, how its window is raised,
/// where its sound plays, and which languages the player prefers.
/// </summary>
/// <remarks>
/// A shell hands one to <c>CapsuleBoot.Configure</c> beside the game's name.
/// <c>Capsule.Runtime.Desktop</c> implements it for Windows, Linux and macOS, and a private
/// platform module subclasses this for another host (<c>docs/packages.md</c>). Only the engine
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
    /// The returned stream must be seekable. The caller disposes the stream. Calls may arrive from any
    /// thread, several at once.
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

    /// <summary>
    /// Brings the window to the front once, on the first frame drawn after it is shown. Raises it
    /// through SDL by default.
    /// </summary>
    /// <param name="window">SDL's window, an <c>SDL_Window*</c>.</param>
    protected internal virtual void RaiseWindow(nint window) => Sdl3.SDL_RaiseWindow(window);

    /// <summary>Opens the output the host's mix plays on, or returns null for a run with no sound.</summary>
    /// <remarks>
    /// Called on the game thread as the window's content loads, and again on an interval while the
    /// output last returned is not <see cref="AudioOutput.Connected"/>. A platform that cannot open its
    /// output logs why and returns null, and the run plays silently. The output follows the system's
    /// default device itself.
    /// </remarks>
    protected internal virtual AudioOutput? OpenAudioOutput() => null;

    /// <summary>
    /// The player's preferred languages as BCP 47 tags, most preferred first. Empty by default.
    /// </summary>
    /// <remarks>
    /// Asked at most once, when a windowed run no driver plays starts in a game with string tables. A
    /// console's platform module answers with its system language setting. Must not throw.
    /// </remarks>
    protected internal virtual IReadOnlyList<string> PreferredLanguages() => [];
}
