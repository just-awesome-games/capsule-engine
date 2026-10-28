using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Capsule.Scenes;

// The form a scene document ships in beside the executable: its compact JSON, gzipped. The authored
// form stays plain JSON. The gzip header carries a zero timestamp. The same document compresses
// to the same bytes on every build.
internal static class ShippedSceneDocument
{
    internal const string Extension = ".scene.json.gz";

    // The folder of shipped content every document sits under, by its key.
    internal const string Folder = "assets";

    // The level trades build time for size. The reader inflates every level alike.
    internal static void Write(SceneDocument document, string path, CompressionLevel level)
    {
        byte[] json = Encoding.UTF8.GetBytes(document.ToJson());

        using FileStream file = File.Create(path);
        using GZipStream compressed = new(file, level);
        compressed.Write(json);
    }

    // Throws SceneDocumentFormatException when the file is not gzip or the inflated JSON breaks the format.
    // The document keeps the inflated bytes as the bytes of its members. content is seekable, as shipped
    // content is.
    internal static SceneDocument Read(Stream content)
    {
        // A shipped document inflates to a few dozen times its size. The trailer's length is trusted up to
        // this multiple before any data backs it. A document that inflates further grows to it as it does.
        const long TrustedInflatedPerByte = 64;

        byte[] json;
        try
        {
            // Sized from the trailer, which spares an honest document a growing buffer and a copy out of it.
            int recorded = InflatedLength(content);
            json = new byte[(int)Math.Min(recorded, (content.Length - content.Position) * TrustedInflatedPerByte)];
            using GZipStream inflated = new(content, CompressionMode.Decompress);
            int length = 0;
            while (true)
            {
                if (length == json.Length)
                {
                    if (length == recorded)
                    {
                        break;
                    }

                    Array.Resize(ref json, (int)Math.Min(recorded, (long)length * 2));
                }

                int read = inflated.Read(json, length, json.Length - length);
                if (read == 0)
                {
                    throw new EndOfStreamException("The document inflates short of the length its trailer records.");
                }

                length += read;
            }

            // Reading on to the end checks the trailer's length and checksum against what inflated.
            if (inflated.ReadByte() >= 0)
            {
                throw new InvalidDataException("The document inflates past the length its trailer records.");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            throw new SceneDocumentFormatException(
                "the shipped scene document is not valid gzip. Rebuild the game to ship it again.",
                exception);
        }

        return SceneDocument.ParseUtf8(json);
    }

    // The inflated length a gzip trailer's last four bytes record, read without moving the stream. One
    // member under 4 GiB records it exactly, as Write's does. Deflate inflates at most 1032 bytes from
    // one, which refuses a length no data could back.
    private static int InflatedLength(Stream content)
    {
        const int HeaderAndTrailer = 18;
        const long MostInflatedPerByte = 1032;

        long start = content.Position;
        long compressed = content.Length - start;
        if (compressed < HeaderAndTrailer)
        {
            throw new InvalidDataException("The document is shorter than a gzip header and trailer.");
        }

        Span<byte> recorded = stackalloc byte[4];
        content.Position = content.Length - recorded.Length;
        content.ReadExactly(recorded);
        content.Position = start;

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(recorded);
        if (length > Math.Min(compressed * MostInflatedPerByte, Array.MaxLength))
        {
            throw new InvalidDataException("The trailer records a length the document cannot inflate to.");
        }

        return (int)length;
    }
}
