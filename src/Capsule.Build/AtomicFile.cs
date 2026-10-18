namespace Capsule.Build;

internal static class AtomicFile
{
    /// <summary>Writes <paramref name="path"/> through a temporary beside it.</summary>
    /// <remarks>
    /// An unchanged result leaves the file and its timestamp alone. Nothing downstream of it copies or
    /// compiles again.
    /// </remarks>
    internal static void Write(string path, Action<string> write)
    {
        string targetPath = Path.GetFullPath(path);
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            write(temporaryPath);
            if (!File.Exists(targetPath) || !File.ReadAllBytes(targetPath).AsSpan().SequenceEqual(File.ReadAllBytes(temporaryPath)))
            {
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    internal static void WriteText(string path, string text) =>
        Write(path, temporary => File.WriteAllText(temporary, text));
}
