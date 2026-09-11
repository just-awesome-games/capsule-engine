using System.Globalization;

namespace Capsule.Build;

/// <summary>One authored source the targets ask the build pass about.</summary>
/// <param name="Group">The domain root it was authored under: textures, fonts, audio or scenes.</param>
/// <param name="Path">Its path below that root as the platform spelled it; the key pass normalizes it.</param>
/// <param name="Extension">The extension the shipped file carries, empty for a document.</param>
/// <param name="Source">Where the source is, relative to the working directory.</param>
internal readonly record struct AssetRequest(string Group, string Path, string Extension, string Source);

/// <summary>
/// The one manifest a build writes and one run reads: option lines, then one
/// <c>group|path|extension|source</c> request per line. It is rewritten whenever the authored set or
/// a declared option changes, which is what makes it the incremental input of the whole pass.
/// </summary>
/// <param name="TileSize">The tile size every imported grid must match, or null to impose none.</param>
/// <param name="Assets">The sources to key, in the order the targets composed them.</param>
internal readonly record struct BuildRequests(int? TileSize, IReadOnlyList<AssetRequest> Assets)
{
    /// <summary>The separator a request line and every derived line write between fields.</summary>
    internal const char Separator = '|';

    private const string TileSizeOption = "tile-size";

    /// <summary>Every group the targets ask about, in the order they are written.</summary>
    private static readonly string[] Groups = ["textures", "fonts", "audio", "scenes"];

    /// <summary>Reads the manifest at <paramref name="path"/>, ignoring a line it does not name.</summary>
    /// <exception cref="FormatException">An option line states a value that is no value.</exception>
    internal static BuildRequests Read(string path)
    {
        int? tileSize = null;
        List<AssetRequest> assets = [];

        foreach (string line in File.ReadAllLines(path))
        {
            string entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            // At most four fields: a source path is the remainder, whatever it holds.
            string[] fields = entry.Split(Separator, 4);

            if (fields is [TileSizeOption, string declared])
            {
                tileSize = int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0
                    ? size
                    : throw new FormatException(
                        $"declares a tile size of \"{declared}\"; a declared tile size is a positive whole number of pixels.");
                continue;
            }

            if (fields is [string group, string authored, string extension, string source]
                && Array.IndexOf(Groups, group) >= 0)
            {
                // MSBuild hands over the path as the platform spelled it; a key has one separator.
                assets.Add(new AssetRequest(group, authored.Replace('\\', '/'), extension, source));
            }
        }

        return new BuildRequests(tileSize, assets);
    }
}
