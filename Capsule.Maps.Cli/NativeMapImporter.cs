using System.Security.Cryptography;
using System.Text;

namespace Capsule.Maps.Cli;

/// <summary>
/// Reads a hand-authored map in Capsule's own format. Validation is the format's own — the text
/// goes through <see cref="MapFile"/> like any other map — and the only thing added is provenance,
/// so a native source is a source like a Tiled one and the shipped plane stays wholly derived.
/// A failure describes the fault and leaves naming the file to the caller, as the Tiled importer
/// does.
/// </summary>
public static class NativeMapImporter
{
    /// <summary>The value stamped into a re-emitted map's <c>source.tool</c>.</summary>
    public const string ToolName = "native";

    /// <summary>
    /// Imports the map at <paramref name="mapPath"/>. The path is stamped into the map's source
    /// block exactly as given, separators normalised — so it must be relative, and it means what
    /// it means from the working directory this ran in.
    /// </summary>
    /// <param name="tileSize">
    /// The tile size the game declares, which every map it imports must match. Null declares
    /// nothing, and each map keeps its own.
    /// </param>
    /// <exception cref="MapFormatException">The file is not a map this build can read.</exception>
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
