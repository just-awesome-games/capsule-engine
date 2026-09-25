using System.Runtime.InteropServices;
using StbImageSharp;

namespace Capsule.Runtime.Assets;

// A decoded texture: premultiplied RGBA texels in a pooled buffer of exactly Width * Height * 4 bytes.
internal readonly record struct DecodedTexture(byte[] Texels, int Width, int Height);

// Decodes an image into the texels Texture2D.FromStream uploads with PremultiplyAlpha, on any thread.
internal static class TextureDecoder
{
    internal static unsafe DecodedTexture Decode(Stream file, TexelPool pool, string name)
    {
        int width;
        int height;
        int components;
        byte* straight = StbImage.stbi__load_and_postprocess_8bit(
            new StbImage.stbi__context(file),
            &width,
            &height,
            &components,
            (int)ColorComponents.RedGreenBlueAlpha);

        if (straight == null)
        {
            throw new InvalidDataException($"Texture '{name}' is not an image the decoder reads ({StbImage.stbi__g_failure_reason}). Ship it as a PNG.");
        }

        try
        {
            byte[] texels = pool.Rent(checked(width * height * 4));
            Premultiply(new ReadOnlySpan<byte>(straight, texels.Length), texels);

            return new DecodedTexture(texels, width, height);
        }
        finally
        {
            // The decoder allocates its result with Marshal.AllocHGlobal and exposes no free.
            Marshal.FreeHGlobal((nint)straight);
        }
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

    // DefaultColorProcessors.PremultiplyAlpha's arithmetic to the bit, float scale included.
    private static void Premultiply(ReadOnlySpan<byte> straight, Span<byte> premultiplied)
    {
        for (int index = 0; index < straight.Length; index += 4)
        {
            byte alpha = straight[index + 3];
            float scale = alpha / 255f;
            premultiplied[index] = (byte)(straight[index] * scale);
            premultiplied[index + 1] = (byte)(straight[index + 1] * scale);
            premultiplied[index + 2] = (byte)(straight[index + 2] * scale);
            premultiplied[index + 3] = alpha;
        }
    }
}

// Keeps at most Concurrency buffers of at least 85 000 bytes, replacing the smallest when full.
internal sealed class TexelPool
{
    // Below this a buffer is not on the large object heap, and the GC reclaims it cheaply.
    private const int SmallestPooled = 85_000;

    private readonly List<byte[]> _free = [];
    private readonly Lock _lock = new();

    internal byte[] Rent(int length)
    {
        lock (_lock)
        {
            for (int index = 0; index < _free.Count; index++)
            {
                if (_free[index].Length == length)
                {
                    byte[] buffer = _free[index];
                    _free.RemoveAt(index);
                    return buffer;
                }
            }
        }

        return GC.AllocateUninitializedArray<byte>(length);
    }

    internal void Return(byte[] buffer)
    {
        if (buffer.Length < SmallestPooled)
        {
            return;
        }

        lock (_lock)
        {
            if (_free.Count < AssetDecodes.Concurrency)
            {
                _free.Add(buffer);
                return;
            }

            int smallest = 0;
            for (int index = 1; index < _free.Count; index++)
            {
                if (_free[index].Length < _free[smallest].Length)
                {
                    smallest = index;
                }
            }

            if (_free[smallest].Length < buffer.Length)
            {
                _free[smallest] = buffer;
            }
        }
    }

    internal int Count
    {
        get
        {
            lock (_lock)
            {
                return _free.Count;
            }
        }
    }
}
