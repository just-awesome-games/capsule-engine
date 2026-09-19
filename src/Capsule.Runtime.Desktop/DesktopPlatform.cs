using Capsule.Persistence;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Desktop.Audio;
using Capsule.Runtime.Desktop.Persistence;
using Capsule.Runtime.Persistence;

namespace Capsule.Runtime.Desktop;

/// <summary>
/// Windows, Linux and macOS from one shell. Content is read beside the executable. Saves are a
/// <see cref="DirectorySaveStorage"/> in the <c>saves</c> subfolder of the game's per-user local
/// folder, with <c>crash.log</c> beside it (<c>docs/persistence.md</c> lists the per-OS paths). The
/// window is raised and claims the foreground once shown, keeps drawing through a modal resize, and
/// reads focus from the windowing library. Sound follows the operating system's default output as it
/// moves. Holds no state, so construct one per boot.
/// </summary>
public sealed class DesktopPlatform : HostPlatform
{
    /// <inheritdoc/>
    public override Stream OpenContent(string relativePath) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, relativePath));

    /// <inheritdoc/>
    public override ISaveStorage OpenSaveStorage(string localFolderName) =>
        new DirectorySaveStorage(Path.Combine(LocalFolder.Resolve(localFolderName), LocalFolder.SavesSubfolder));

    /// <inheritdoc/>
    public override void ReportCrash(string localFolderName, Exception exception) =>
        CrashLog.TryWrite(localFolderName, exception);

    /// <inheritdoc/>
    public override void RaiseWindow(WindowHandle window)
    {
        SdlPlatform.RaiseWindow(window.Value);

        // A launch through the dotnet muxer breaks the foreground permission chain, so Windows
        // refuses the raise on its own.
        WindowsForeground.Claim(SdlPlatform.NativeWindowHandle(window.Value));
    }

    /// <inheritdoc/>
    public override bool HasInputFocus(WindowHandle window) => SdlPlatform.HasInputFocus(window.Value);

    /// <inheritdoc/>
    public override IDisposable? WatchWindowRedraw(WindowHandle window, Action<int, int> redraw)
    {
        ArgumentNullException.ThrowIfNull(redraw);

        return new SdlPlatform.RedrawWatch(() =>
        {
            SdlPlatform.WindowSize(window.Value, out int width, out int height);
            redraw(width, height);
        });
    }

    /// <inheritdoc/>
    public override AudioOutput? WatchDefaultAudioOutput(Action defaultChanged) =>
        OpenAlOutput.TryAttach(defaultChanged);
}
