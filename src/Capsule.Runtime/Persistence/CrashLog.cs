using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Persistence;

// The default platform's crash log: crash.log in the game's per-user local folder, beside its saves.
// The time and the build head the exception. They name the symbols that decode the log's frames.
internal static class CrashLog
{
    // The log's path, or null when it could not be written.
    internal static string? TryWrite(string gameName, string folderName, Exception exception)
    {
        try
        {
            // The per-user local folder, not the content directory, because install locations are often
            // read-only. Overwriting keeps the file bounded, and only the latest crash matters.
            string directory = LocalFolder.Resolve(folderName);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "crash.log");
            File.WriteAllText(path, Text(gameName, DateTimeOffset.UtcNow, exception));

            return path;
        }
        catch (Exception)
        {
            // A failed log write must not mask the original exception.
            return null;
        }
    }

    internal static string Text(string gameName, DateTimeOffset now, Exception exception) =>
        $"{now:O}{Environment.NewLine}{BuildIdentity.Describe(gameName)}{Environment.NewLine}{exception}{Environment.NewLine}";

    // Blocks until the player dismisses the box. SDL itself may be what failed to load, and a box that
    // cannot show is skipped.
    internal static void TryShow(string gameName, string? log)
    {
        try
        {
            Sdl3.SDL_ShowSimpleMessageBox(
                Sdl3.MessageBoxError,
                gameName,
                log is null ? $"{gameName} stopped because of an error." : $"{gameName} stopped because of an error. The details are in {log}",
                nint.Zero);
        }
        catch (Exception)
        {
        }
    }
}
