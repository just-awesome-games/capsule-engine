using System.Security.Cryptography;
using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

internal static class NativeSceneImporter
{
    internal const string ToolName = "native";

    private const char ByteOrderMark = '\uFEFF';

    internal static SceneDocument Import(string documentPath, int? tileSize = null)
    {
        // Read as bytes, because the hash is over the source bytes: an authored file is under no
        // obligation to be canonical. The format is written without a BOM, but an editor may add
        // one and the JSON reader would find it where it expects a brace.
        byte[] sourceBytes = File.ReadAllBytes(documentPath);
        SceneDocument authored = SceneDocumentFile.Parse(Encoding.UTF8.GetString(sourceBytes).TrimStart(ByteOrderMark));

        if (tileSize is { } declared)
        {
            foreach (SceneDocumentEntry entry in authored.Entries)
            {
                if (entry.TileMap is { Grid.TileSize: var actual } && actual != declared)
                {
                    throw new SceneDocumentFormatException(
                        $"the scene document has {actual}px tiles but the game declares {declared}px; set tileSize to {declared} on every tile-map entry, or change CapsuleTileSize.");
                }
            }
        }

        // A document that arrives stamped was derived by an authoring module, and its block names
        // the file a person edited; re-stamping it would name the intermediate instead.
        SceneDocumentSource source = authored.Source ?? new(
            ToolName,
            documentPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)));

        return new SceneDocument(authored.Entries.ToArray(), authored.NextEntityId, source);
    }
}
