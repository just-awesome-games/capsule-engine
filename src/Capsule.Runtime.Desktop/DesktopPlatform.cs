using Capsule.Persistence;
using Capsule.Runtime.Audio;
using Capsule.Runtime.Desktop.Audio;
using Capsule.Runtime.Desktop.Persistence;
using Capsule.Runtime.Persistence;

namespace Capsule.Runtime.Desktop;

/// <summary>
/// The desktop host family — Windows, Linux and macOS from one shell. Content is read beside the
/// executable; saves are <see cref="DirectorySaveStorage"/> in the <c>saves</c> subfolder of the
/// game's per-user local folder, and <c>crash.log</c> sits beside it (<c>docs/persistence.md</c>
/// lists the per-OS paths); the window is raised and claims the foreground once shown, keeps
/// drawing through a modal resize, and reads focus from the windowing library; sound follows the
/// operating system's default output as it moves. Stateless: construct one per boot.
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
    public override void RaiseWindow(nint window)
    {
        SdlPlatform.RaiseWindow(window);

        // A launch through the dotnet muxer breaks the foreground permission chain, so Windows
        // refuses the raise on its own.
        WindowsForeground.Claim(SdlPlatform.NativeWindowHandle(window));
    }

    /// <inheritdoc/>
    public override bool HasInputFocus(nint window) => SdlPlatform.HasInputFocus(window);

    /// <inheritdoc/>
    public override IDisposable? WatchWindowRedraw(nint window, Action<int, int> redraw)
    {
        SdlPlatform.WatchWindowRedraw(() =>
        {
            SdlPlatform.WindowSize(window, out int width, out int height);
            redraw(width, height);
        });

        return RedrawWatch.Instance;
    }

    /// <inheritdoc/>
    public override AudioOutput? WatchDefaultAudioOutput(Action defaultChanged) =>
        OpenAlOutput.TryAttach(defaultChanged);

    // The watch is one per process, so its handle carries nothing.
    private sealed class RedrawWatch : IDisposable
    {
        internal static readonly RedrawWatch Instance = new();

        public void Dispose() => SdlPlatform.StopWatchingWindowRedraw();
    }
}
