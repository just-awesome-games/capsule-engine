using System.Security.Cryptography;
using System.Text;
using Capsule.Scenes.Documents;

namespace Capsule.Cli;

public static class NativeSceneImporter
{
    public const string ToolName = "native";

    public static SceneDocument Import(string documentPath, int? tileSize = null)
    {
        // Read once, because the hash is over the source bytes: the source block records what was
        // authored, and an authored file is under no obligation to be canonical.
        byte[] sourceBytes = File.ReadAllBytes(documentPath);
        SceneDocument authored = SceneDocumentFile.Parse(Text(sourceBytes));

        if (tileSize is { } declared && authored.Grid is { } grid && grid.TileSize != declared)
        {
            throw new SceneDocumentFormatException(
                $"the scene document has {grid.TileSize}px tiles but the game declares {declared}px; set grid.tileSize to {declared}, or change CapsuleTileSize.");
        }

        SceneDocumentSource source = new(
            ToolName,
            documentPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)));

        return new SceneDocument(authored.TileMap, authored.Entities.ToArray(), authored.NextEntityId, source);
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
