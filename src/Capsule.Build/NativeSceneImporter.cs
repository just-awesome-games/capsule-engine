using System.Security.Cryptography;
using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Build;

public static class NativeSceneImporter
{
    public const string ToolName = "native";

    public static SceneDocument Import(string documentPath, int? tileSize = null)
    {
        // Read once, because the hash is over the source bytes: the source block records what was
        // authored, and an authored file is under no obligation to be canonical.
        byte[] sourceBytes = File.ReadAllBytes(documentPath);
        SceneDocument authored = SceneDocumentFile.Parse(Text(sourceBytes));

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

    // The format is written without one, but an editor may add one, and the JSON reader would
    // find U+FEFF where it expects a brace.
    private static string Text(byte[] utf8)
    {
        ReadOnlySpan<byte> bom = [0xEF, 0xBB, 0xBF];
        ReadOnlySpan<byte> bytes = utf8;

        return Encoding.UTF8.GetString(bytes.StartsWith(bom) ? bytes[bom.Length..] : bytes);
    }
}
