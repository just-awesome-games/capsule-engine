using Capsule.Diagnostics;

namespace Capsule.Runtime.Rendering;

// Saving the surface a frame was drawn on as a PNG. Read-back and encoding failures are logged instead
// of thrown into the frame loop, and a destination is replaced once a complete PNG is in hand.
// Whatever is already there survives a failed capture.
internal static class FrameCapture
{
    // The capture is staged beside its destination under a name ending in this suffix. A random segment
    // precedes it, and a capture never touches a file it did not create.
    internal const string TemporarySuffix = ".tmp";

    // Writes the PNG encode returns to path, creating the directory it names and overwriting the file.
    internal static void Save(Func<byte[]> encode, string path)
    {
        byte[] png;

        try
        {
            png = encode();
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed before writing: {error.Message}");
            return;
        }

        Write(png, path);
    }

    // The encoded PNG lands on a temporary sibling this call creates exclusively and moves onto the
    // destination once it is complete. Neither a partial write nor a denied one touches the file
    // already at path or any other file beside it. Resolving path is inside the protected operation.
    // A path the file system rejects is logged instead of thrown.
    internal static void Write(byte[] png, string path)
    {
        // Null until this call owns a staging file. Cleanup never deletes a sibling it found.
        string? created = null;

        try
        {
            string full = Path.GetFullPath(path);

            if (Path.GetDirectoryName(full) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            string temporary = full + '.' + Path.GetRandomFileName() + TemporarySuffix;

            using (FileStream staging = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = temporary;
                staging.Write(png);
            }

            File.Move(temporary, full, overwrite: true);
            created = null;
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed while writing: {error.Message}");

            if (created is not null)
            {
                Discard(created);
            }
        }
    }

    // The temporary is all a failed write can have left behind, and a truncated PNG is worse than none.
    private static void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (Exception error)
        {
            Log.Warning($"Removing the failed frame capture at '{temporary}' failed: {error.Message}");
        }
    }
}
