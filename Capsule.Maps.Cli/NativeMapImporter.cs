using System.Security.Cryptography;
using System.Text;

namespace Capsule.Maps.Cli;

public static class NativeMapImporter
{
    public const string ToolName = "native";

    public static Map Import(string mapPath, int? tileSize = null)
    {
        // Read once, because the hash is over the source bytes: the source block records what was
        // authored, and an authored file is under no obligation to be canonical.
        byte[] sourceBytes = File.ReadAllBytes(mapPath);
        Map authored = MapFile.Parse(Text(sourceBytes));

        if (tileSize is { } declared && authored.Grid.TileSize != declared)
        {
            throw new MapFormatException(
                $"the map has {authored.Grid.TileSize}px tiles but the game declares {declared}px; set grid.tileSize to {declared}, or change CapsuleTileSize.");
        }

        MapSource source = new(
            ToolName,
            mapPath.Replace('\\', '/'),
            Convert.ToHexStringLower(SHA256.HashData(sourceBytes)));

        return new Map(authored.Grid, authored.Objects.ToArray(), authored.NextObjectId, source);
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
