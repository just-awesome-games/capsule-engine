using System.Runtime.InteropServices;
using Capsule.Runtime.Platform;

namespace Capsule.Runtime.Assets;

// A texture's texels, rows packed at Width * BytesPerTexel bytes: premultiplied RGBA at four bytes a
// texel, or an r8 texture's values at one. Texels the engine makes are a managed array the collector
// owns. Texels decoded from a file stay in the SDL surface they were decoded into, which the holder
// disposes once they are uploaded or dropped. Dispose is safe on any thread and more than once.
internal sealed unsafe class DecodedTexture : IDisposable
{
    private readonly byte[]? _texels;
    private nint _surface;

    internal int Width { get; }

    internal int Height { get; }

    internal int BytesPerTexel { get; }

    internal DecodedTexture(byte[] texels, int width, int height, int bytesPerTexel = 4)
    {
        _texels = texels;
        Width = width;
        Height = height;
        BytesPerTexel = bytesPerTexel;
    }

    // Takes ownership of surface, whose rows the decoder has packed.
    internal DecodedTexture(Sdl3.Surface* surface, int bytesPerTexel)
    {
        _surface = (nint)surface;
        Width = surface->Width;
        Height = surface->Height;
        BytesPerTexel = bytesPerTexel;
    }

    // Whether a decoded file's surface is destroyed, safe to read on any thread. Engine-made texels are
    // never released.
    internal bool Released => _texels is null && Volatile.Read(ref _surface) == 0;

    // Throws once a decoded file's surface is destroyed. Only the holder reads it, never while another
    // thread may dispose it.
    internal ReadOnlySpan<byte> Texels
    {
        get
        {
            if (_texels is not null)
            {
                return _texels;
            }

            ObjectDisposedException.ThrowIf(_surface == 0, this);

            return new ReadOnlySpan<byte>(((Sdl3.Surface*)_surface)->Pixels, Width * Height * BytesPerTexel);
        }
    }

    public void Dispose()
    {
        nint surface = Interlocked.Exchange(ref _surface, 0);
        if (surface != 0)
        {
            Sdl3.SDL_DestroySurface(surface);
        }
    }
}

// Decodes a PNG into premultiplied texels in SDL's memory, on any thread. A single-channel texture
// decodes to its red channel, which is a greyscale file's grey, and is never premultiplied.
internal static class TextureDecoder
{
    internal static unsafe DecodedTexture Decode(Stream file, string name, bool singleChannel = false)
    {
        (nint bytes, int length) = ReadAll(file);
        Sdl3.Surface* decoded;
        try
        {
            decoded = Sdl3.SDL_LoadPNG_IO(Sdl3.SDL_IOFromConstMem((void*)bytes, (nuint)length), closeSource: true);
        }
        finally
        {
            NativeMemory.Free((void*)bytes);
        }

        if (decoded == null)
        {
            throw new InvalidDataException($"Texture '{name}' is not a PNG the decoder reads ({Sdl3.Error()}). Export it again as a PNG.");
        }

        Sdl3.Surface* surface = decoded;
        if (decoded->Format != Sdl3.PixelFormatRgba32)
        {
            surface = Sdl3.SDL_ConvertSurface(decoded, Sdl3.PixelFormatRgba32);
            Sdl3.SDL_DestroySurface((nint)decoded);
            if (surface == null)
            {
                throw new InvalidOperationException($"Texture '{name}' could not be converted to RGBA ({Sdl3.Error()}). Free memory, or ship it smaller.");
            }
        }

        // Each pass writes at or before the byte it reads. The texels pack in place.
        int width = surface->Width;
        int pitch = surface->Pitch;
        byte* pixels = surface->Pixels;
        if (singleChannel)
        {
            for (int y = 0; y < surface->Height; y++)
            {
                byte* from = pixels + ((nint)y * pitch);
                byte* to = pixels + ((nint)y * width);
                for (int x = 0; x < width; x++)
                {
                    to[x] = from[x * 4];
                }
            }

            return new DecodedTexture(surface, 1);
        }

        for (int y = 0; y < surface->Height; y++)
        {
            Span<byte> row = new(pixels + ((nint)y * pitch), width * 4);
            Premultiply(row);
            if (pitch != width * 4)
            {
                row.CopyTo(new Span<byte>(pixels + ((nint)y * width * 4), width * 4));
            }
        }

        return new DecodedTexture(surface, 4);
    }

    // Premultiply's inverse, rounded to nearest. A texel premultiplied at low alpha lost precision
    // that no inverse recovers, and a fully transparent texel comes back black.
    internal static void Unpremultiply(Span<byte> texels)
    {
        for (int index = 0; index < texels.Length; index += 4)
        {
            byte alpha = texels[index + 3];
            if (alpha is 0 or 255)
            {
                continue;
            }

            float scale = 255f / alpha;
            texels[index] = (byte)MathF.Min(255f, MathF.Round(texels[index] * scale));
            texels[index + 1] = (byte)MathF.Min(255f, MathF.Round(texels[index + 1] * scale));
            texels[index + 2] = (byte)MathF.Min(255f, MathF.Round(texels[index + 2] * scale));
        }
    }

    // Truncates each channel times a float alpha scale. A texture decoded here holds the bytes of the
    // same file loaded by the device, as the engine's own font page is.
    private static void Premultiply(Span<byte> texels)
    {
        for (int index = 0; index < texels.Length; index += 4)
        {
            byte alpha = texels[index + 3];
            float scale = alpha / 255f;
            texels[index] = (byte)(texels[index] * scale);
            texels[index + 1] = (byte)(texels[index + 1] * scale);
            texels[index + 2] = (byte)(texels[index + 2] * scale);
        }
    }

    // The rest of the stream in native memory the caller frees. A file's bytes never reach the managed
    // heap.
    private static unsafe (nint Bytes, int Length) ReadAll(Stream file)
    {
        int capacity = file.CanSeek ? checked((int)(file.Length - file.Position)) : 64 * 1024;
        byte* bytes = (byte*)NativeMemory.Alloc((nuint)Math.Max(capacity, 1));
        int length = 0;
        try
        {
            if (file.CanSeek)
            {
                file.ReadExactly(new Span<byte>(bytes, capacity));
                return ((nint)bytes, capacity);
            }

            while (true)
            {
                if (length == capacity)
                {
                    capacity = checked(capacity * 2);
                    bytes = (byte*)NativeMemory.Realloc(bytes, (nuint)capacity);
                }

                int read = file.Read(new Span<byte>(bytes + length, capacity - length));
                if (read == 0)
                {
                    return ((nint)bytes, length);
                }

                length += read;
            }
        }
        catch
        {
            NativeMemory.Free(bytes);
            throw;
        }
    }
}
