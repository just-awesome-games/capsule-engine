namespace Capsule.Build;

internal static class AtomicFile
{
    internal static void Write(string path, Action<string> write)
    {
        string targetPath = Path.GetFullPath(path);
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            write(temporaryPath);
            File.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }
}
