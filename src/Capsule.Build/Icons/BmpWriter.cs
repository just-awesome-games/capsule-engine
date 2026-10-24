using System.Buffers.Binary;
using Capsule.Build.Textures;

namespace Capsule.Build.Icons;

/// <summary>Encodes an RGBA image as an uncompressed 32-bit BMP, bottom-up and straight alpha, as the window icon is read.</summary>
internal static class BmpWriter
{
    private const int FileHeaderSize = 14;

    private const int InfoHeaderSize = 40;

    // 72 dots per inch.
    private const int PixelsPerMetre = 2835;

    internal static void Write(Texels image, Stream destination)
    {
        int stride = image.Width * 4;
        int pixels = stride * image.Height;
        Span<byte> header = stackalloc byte[FileHeaderSize + InfoHeaderSize];
        header.Clear();
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header[2..], header.Length + pixels);
        BinaryPrimitives.WriteInt32LittleEndian(header[10..], header.Length);

        // BITMAPINFOHEADER. A positive height is bottom-up, and compression 0 is BI_RGB.
        Span<byte> info = header[FileHeaderSize..];
        BinaryPrimitives.WriteInt32LittleEndian(info, InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(info[4..], image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(info[8..], image.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(info[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(info[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(info[20..], pixels);
        BinaryPrimitives.WriteInt32LittleEndian(info[24..], PixelsPerMetre);
        BinaryPrimitives.WriteInt32LittleEndian(info[28..], PixelsPerMetre);
        destination.Write(header);

        byte[] row = new byte[stride];
        for (int y = image.Height - 1; y >= 0; y--)
        {
            ReadOnlySpan<byte> source = image.Data.AsSpan(y * stride, stride);
            for (int i = 0; i < stride; i += 4)
            {
                row[i] = source[i + 2];
                row[i + 1] = source[i + 1];
                row[i + 2] = source[i];
                row[i + 3] = source[i + 3];
            }

            destination.Write(row);
        }
    }
}
