namespace Capsule.Runtime;

internal static class CrashLog
{
    internal static void TryWrite(string appName, Exception exception)
    {
        try
        {
            // Local app data, not BaseDirectory: install locations are often read-only.
            // Overwrite keeps the file bounded; the latest crash is the one that matters.
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
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
