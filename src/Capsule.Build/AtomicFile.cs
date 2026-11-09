namespace Capsule.Build;

internal static class AtomicFile
{
    private const int Block = 1 << 16;

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
            if (!Same(targetPath, temporaryPath))
            {
                File.Move(temporaryPath, targetPath, overwrite: true);
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    /// <summary>Writes <paramref name="text"/> to <paramref name="path"/> as <see cref="Write"/> does, every line ending in one line feed.</summary>
    /// <remarks>Generated source is then the same on every machine, whatever newline it was built with.</remarks>
    internal static void WriteText(string path, string text) =>
        Write(path, temporary => File.WriteAllText(temporary, text.Replace("\r\n", "\n", StringComparison.Ordinal)));

    // Whether a file is at target holding exactly the bytes at written, read a block of each at a time.
    private static bool Same(string target, string written)
    {
        if (!File.Exists(target))
        {
            return false;
        }

        using FileStream left = File.OpenRead(target);
        using FileStream right = File.OpenRead(written);
        if (left.Length != right.Length)
        {
            return false;
        }

        byte[] buffer = new byte[2 * Block];
        Span<byte> one = buffer.AsSpan(0, Block);
        Span<byte> other = buffer.AsSpan(Block);
        int read;
        while ((read = left.ReadAtLeast(one, Block, throwOnEndOfStream: false)) > 0)
        {
            if (right.ReadAtLeast(other, read, throwOnEndOfStream: false) != read || !one[..read].SequenceEqual(other[..read]))
            {
                return false;
            }
        }

        return true;
    }
}
