using Capsule.Runtime.Desktop.Persistence;

namespace Capsule.Runtime.Desktop;

internal static class CrashLog
{
    internal static void TryWrite(string folderName, Exception exception)
    {
        try
        {
            // The per-user local folder, not BaseDirectory, because install locations are often
            // read-only. Overwriting keeps the file bounded, and only the latest crash matters.
            string directory = LocalFolder.Resolve(folderName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(
                Path.Combine(directory, "crash.log"),
                $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // A failed log write must not mask the original exception.
        }
    }
}
