using Capsule.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Capsule.Runtime.Rendering;

// Saving the surface a frame was drawn on as a PNG. Read-back and encoding failures are logged
// rather than thrown into the frame loop, and a destination is replaced only once a whole PNG is in
// hand, so whatever is already there survives a capture that failed.
internal static class FrameCapture
{
    // The capture is staged beside its destination under a name ending in this suffix. A random
    // segment precedes it, so a capture never touches a file it did not create.
    internal const string TemporarySuffix = ".tmp";

    // Whether this frame drew at all. A back buffer with no area, as a minimised window has,
    // presents nothing and leaves no frame to save — including behind a render target, which is
    // drawn but never presented.
    internal static bool CanCapture(GraphicsDevice device) =>
        device.PresentationParameters.BackBufferWidth > 0 && device.PresentationParameters.BackBufferHeight > 0;

    // Saves the surface the world was drawn on as a PNG at path, creating the directory it names
    // and overwriting the file. Called after Draw and before the frame is presented, while that
    // surface still holds the frame: target where a render resolution is configured, whose extent
    // is independent of the window, and the back buffer where there is none.
    internal static void Save(GraphicsDevice device, RenderTarget2D? target, string path)
    {
        if (!CanCapture(device))
        {
            return;
        }

        byte[] png;

        try
        {
            using MemoryStream encoded = new();
            EncodeSurface(device, target, encoded);
            png = encoded.ToArray();
        }
        catch (Exception error)
        {
            Log.Warning($"Frame capture to '{path}' failed before writing: {error.Message}");
            return;
        }

        Write(png, path);
    }

    // Encoded PNG lands on a temporary sibling this call creates exclusively and moves onto the
    // destination only once it is whole: neither a partial write nor a denied one touches the file
    // already at path, nor any other file already beside it. Resolving path is part of the
    // protected operation, so a path the file system rejects is logged rather than thrown.
    internal static void Write(byte[] png, string path)
    {
        // Null until this call owns a staging file, so cleanup never deletes a sibling it found.
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

    // The temporary is all a failed write can have left behind, and a truncated PNG nobody can
    // read is worse than none.
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

    private static void EncodeSurface(GraphicsDevice device, RenderTarget2D? target, Stream destination)
    {
        if (target is not null)
        {
            target.SaveAsPng(destination, target.Width, target.Height);
            return;
        }

        PresentationParameters backBuffer = device.PresentationParameters;
        int width = backBuffer.BackBufferWidth;
        int height = backBuffer.BackBufferHeight;

        Color[] pixels = new Color[width * height];
        device.GetBackBufferData(pixels);

        using Texture2D surface = new(device, width, height);
        surface.SetData(pixels);
        surface.SaveAsPng(destination, width, height);
    }
}
