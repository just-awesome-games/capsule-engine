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
/// foreground once shown, keeps drawing through a modal resize, reads focus from the windowing
/// library, and on Windows opens at its display's scale. Sound plays on the operating system's default
/// output and follows it as it moves. The player's preferred languages are the operating system's, as
/// the windowing library reports them. Construct one per boot. It holds no state.
/// </remarks>
public sealed class DesktopPlatform : HostPlatform
{
    /// <summary>Creates the platform, which loads the windowing library before the host does.</summary>
    public DesktopPlatform() => SdlPlatform.Load();

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
    protected override void RaiseWindow(WindowHandle window)
    {
        SdlPlatform.RaiseWindow(window.Value);

        // Windows refuses the raise alone after a launch through the dotnet muxer.
        WindowsForeground.Claim(SdlPlatform.NativeWindowHandle(window.Value));
    }

    /// <inheritdoc/>
    protected override bool HasInputFocus(WindowHandle window) => SdlPlatform.HasInputFocus(window.Value);

    /// <inheritdoc/>
    protected override bool IsMinimized(WindowHandle window) => SdlPlatform.IsMinimized(window.Value);

    /// <inheritdoc/>
    protected override void ConfineCursor(WindowHandle window, bool confined) =>
        SdlPlatform.ConfineCursor(window.Value, confined);

    /// <inheritdoc/>
    protected override IDisposable? WatchWindowRedraw(WindowHandle window, Action<int, int> redraw)
    {
        ArgumentNullException.ThrowIfNull(redraw);

        return new SdlPlatform.RedrawWatch(() =>
        {
            SdlPlatform.WindowSize(window.Value, out int width, out int height);
            redraw(width, height);
        });
    }

    /// <inheritdoc/>
    protected override (int Width, int Height, float Scale) WindowDisplay(WindowHandle window)
    {
        float scale = WindowsDisplay.Scale(SdlPlatform.NativeWindowHandle(window.Value));
        if (!SdlPlatform.DisplayExtent(window.Value, out int width, out int height))
        {
            (width, height, _) = base.WindowDisplay(window);
        }

        return (width, height, scale);
    }

    /// <inheritdoc/>
    protected override AudioOutput? OpenAudioOutput() => SdlAudioOutput.TryOpen();

    /// <inheritdoc/>
    protected override IReadOnlyList<string> PreferredLanguages() => SdlPlatform.PreferredLocales();
}
