using Capsule.Persistence;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Desktop.Audio;
using Capsule.Runtime.Desktop.Persistence;
using Capsule.Runtime.Persistence;

namespace Capsule.Runtime.Desktop;

/// <summary>Windows, Linux and macOS from one shell.</summary>
/// <remarks>
/// Content is read beside the executable. Saves are a <see cref="DirectorySaveStorage"/> in the
/// <c>saves</c> subfolder of the game's per-user local folder, with <c>crash.log</c> beside it
/// (<c>docs/persistence.md</c> lists the per-OS paths). The window is raised and claims the
/// foreground once shown. Sound plays on the operating system's default output and follows it as it
/// moves. The player's preferred languages are the operating system's, as SDL reports them. Construct
/// one per boot. It holds no state.
/// </remarks>
public sealed class DesktopPlatform : HostPlatform
{
    /// <inheritdoc/>
    protected override Stream OpenContent(string relativePath) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, relativePath));

    /// <inheritdoc/>
    protected override ISaveStorage OpenSaveStorage(string localFolderName) =>
        new DirectorySaveStorage(Path.Combine(LocalFolder.Resolve(localFolderName), LocalFolder.SavesSubfolder));

    /// <inheritdoc/>
    protected override void ReportCrash(string localFolderName, Exception exception) =>
        CrashLog.TryWrite(localFolderName, exception);

    /// <inheritdoc/>
    protected override void RaiseWindow(nint window)
    {
        base.RaiseWindow(window);

        // Windows refuses the raise alone after a launch through the dotnet muxer.
        WindowsForeground.Claim(SdlPlatform.NativeWindowHandle(window));
    }

    /// <inheritdoc/>
    protected override AudioOutput? OpenAudioOutput() => SdlAudioOutput.TryOpen();

    /// <inheritdoc/>
    protected override IReadOnlyList<string> PreferredLanguages() => SdlPlatform.PreferredLocales();
}
