using System.Buffers.Binary;
using Capsule.Build.Textures;

namespace Capsule.Build.Icons;

/// <summary>Encodes square RGBA images as an <c>.ico</c> whose every entry is a PNG.</summary>
internal static class IcoWriter
{
    private const int HeaderSize = 6;

    private const int EntrySize = 16;

    /// <param name="images">Square, straight-alpha RGBA images of at most 256 texels a side, in the order the entries list them.</param>
    internal static void Write(IReadOnlyList<Texels> images, Stream destination)
    {
        byte[][] encoded = new byte[images.Count][];
        for (int i = 0; i < images.Count; i++)
        {
            using MemoryStream png = new();
            PngWriter.Write(images[i].Data, images[i].Width, images[i].Height, 4, png);
            encoded[i] = png.ToArray();
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();
        BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], (ushort)images.Count);
        destination.Write(header);

        int offset = HeaderSize + (EntrySize * images.Count);
        Span<byte> entry = stackalloc byte[EntrySize];
        for (int i = 0; i < images.Count; i++)
        {
            entry.Clear();

            // A side of 256 is written as 0.
            entry[0] = (byte)images[i].Width;
            entry[1] = (byte)images[i].Height;
            BinaryPrimitives.WriteUInt16LittleEndian(entry[4..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(entry[6..], 32);
            BinaryPrimitives.WriteInt32LittleEndian(entry[8..], encoded[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(entry[12..], offset);
            destination.Write(entry);
            offset += encoded[i].Length;
        }

        foreach (byte[] png in encoded)
        {
            destination.Write(png);
        }
    }
}
