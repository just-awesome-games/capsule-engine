using Capsule.Diagnostics;
using Capsule.Persistence;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Persistence;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime;

/// <summary>
/// Where a run reads its shipped content, keeps its saves and its crash log, how its window is raised,
/// where its sound plays, and which languages the player prefers. Every member defaults to SDL3's answer
/// on Windows, Linux and macOS.
/// </summary>
/// <remarks>
/// A run uses the defaults unless the shell passes a subclass to <see cref="EngineBuilder.WithPlatform"/>.
/// A port subclasses this for what its platform's SDL3 lacks or its certification demands
/// (<c>docs/packages.md</c>). Only the engine calls these members.
/// </remarks>
public class HostPlatform
{
    /// <summary>Creates the default platform, or the base of a port's.</summary>
    protected internal HostPlatform()
    {
    }

    /// <summary>
    /// Opens shipped content for reading. A path is the build's own, relative to the publish root
    /// with forward slashes and no leading separator, and is validated before it arrives here.
    /// </summary>
    /// <remarks>
    /// The default reads beside the executable, or from a macOS bundle's resources. The returned stream
    /// must be seekable, and the caller disposes it. Calls may arrive from any thread, several at once.
    /// </remarks>
    /// <exception cref="FileNotFoundException">Nothing ships at that path.</exception>
    protected internal virtual Stream OpenContent(string relativePath) =>
        File.OpenRead(Path.Combine(ContentFolder.Resolve(), relativePath));

    /// <summary>
    /// The medium save documents are kept on when the shell named neither a storage nor a
    /// directory. Called once per windowed run.
    /// </summary>
    /// <remarks>
    /// The default is a <see cref="DirectorySaveStorage"/> in the <c>saves</c> subfolder of the game's
    /// per-user local folder (<c>docs/persistence.md</c> lists the per-OS paths). A headless run never
    /// asks.
    /// </remarks>
    /// <param name="localFolderName">The game's local folder name: one safe directory name.</param>
    protected internal virtual ISaveStorage OpenSaveStorage(string localFolderName) =>
        new DirectorySaveStorage(Path.Combine(LocalFolder.Resolve(localFolderName), LocalFolder.SavesSubfolder));

    /// <summary>
    /// Records an exception escaping a windowed run. The host rethrows it afterwards whatever this
    /// does.
    /// </summary>
    /// <remarks>
    /// The default overwrites <c>crash.log</c> in the game's per-user local folder, beside its
    /// <c>saves</c> subfolder, headed by the time, the game's and Capsule's versions, the system, and the
    /// executable's SHA-256, which matches the log to the build's symbols. A shipping build then tells the player in a message box titled with the
    /// game's name and naming the log, because its window closes with no console. Must not throw.
    /// </remarks>
    protected internal virtual void ReportCrash(string gameName, string localFolderName, Exception exception)
    {
        string? log = CrashLog.TryWrite(gameName, localFolderName, exception);
        if (!Development.IsSupported)
        {
            CrashLog.TryShow(gameName, log);
        }
    }

    /// <summary>
    /// Brings the window to the front once, on the first frame drawn after it is shown. The default
    /// raises it through SDL3, and on Windows also claims the foreground the system would refuse.
    /// </summary>
    /// <param name="window">SDL's window, an <c>SDL_Window*</c>.</param>
    protected internal virtual void RaiseWindow(nint window)
    {
        Sdl3.SDL_RaiseWindow(window);
        WindowsForeground.Claim(window);
    }

    /// <summary>Opens the output the host's mix plays on, or returns null for a run with no sound.</summary>
    /// <remarks>
    /// Called once, on the game thread as the window's content loads. The output follows the system's
    /// default device for the rest of the run, and consumes frames on the clock while there is none. A
    /// platform that cannot open its output logs why and returns null, and the run plays silently. The
    /// default plays through an SDL3 stream on the system's default output and follows that output as it
    /// moves.
    /// </remarks>
    /// <param name="leadFrames">
    /// The frames the host means to keep queued ahead of the device. An output that sizes its device's
    /// buffer keeps it within half of this, and reports what it got through
    /// <see cref="AudioOutput.DeviceFrames"/>.
    /// </param>
    protected internal virtual AudioOutput? OpenAudioOutput(int leadFrames) => SdlAudioOutput.TryOpen(leadFrames);

    /// <summary>
    /// The player's preferred languages as BCP 47 tags, most preferred first. The default is the
    /// operating system's, as SDL3 reports them.
    /// </summary>
    /// <remarks>
    /// Asked at most once, when a windowed run no driver plays starts in a game with string tables. A
    /// console's port answers with its system language setting. Must not throw.
    /// </remarks>
    protected internal virtual IReadOnlyList<string> PreferredLanguages() => Sdl3.PreferredLocales();
}
