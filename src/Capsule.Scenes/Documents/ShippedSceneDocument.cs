using System.IO.Compression;
using System.Text;

namespace Capsule.Scenes.Documents;

// The form a scene document ships in beside the executable: its compact JSON, gzipped. The authored
// form stays plain JSON. The gzip header carries a zero timestamp, so the same document compresses
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
    // The document keeps the inflated bytes as the bytes of its members.
    internal static SceneDocument Read(Stream content)
    {
        using MemoryStream json = new();
        try
        {
            using GZipStream inflated = new(content, CompressionMode.Decompress);
            inflated.CopyTo(json);
        }
        catch (InvalidDataException exception)
        {
            throw new SceneDocumentFormatException(
                "the shipped scene document is not valid gzip. Rebuild the game to ship it again.",
                exception);
        }

        return SceneDocument.ParseUtf8(json.ToArray());
    }
}
