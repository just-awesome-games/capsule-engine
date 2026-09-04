using Capsule.Assets;

namespace Capsule.Build;

/// <summary>
/// One authored document and the key it claims: its path under its root, forward slashes and no
/// extensions — <c>enemies/bat</c>, or <c>bat</c> for a document at the root. The key is where the
/// derived document is written and what the generated registry declares it as.
/// </summary>
/// <param name="Key">The document's root-relative key.</param>
/// <param name="Path">Where the source is, relative to the working directory.</param>
internal readonly record struct DocumentSource(string Key, string Path)
{
    /// <summary>The separator a source list writes between a key and its path.</summary>
    internal const char Separator = '|';

    /// <summary>
    /// Whether <see cref="Key"/> is a key at all. A key arriving from an authoring module is input
    /// like any other, and one that is no key would write outside the directory the build owns.
    /// </summary>
    internal bool HasSafeKey() => AssetPaths.IsKey(Key);

    /// <summary>
    /// The sources a batch file names, one <c>key|path</c> per line. Blank lines are skipped; a
    /// line with no separator is the path alone, keyed by its file name without extensions.
    /// </summary>
    internal static DocumentSource[] Read(string listPath, string documentExtension)
    {
        List<DocumentSource> sources = [];

        foreach (string line in File.ReadAllLines(listPath))
        {
            string entry = line.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            int separator = entry.IndexOf(Separator);
            sources.Add(separator < 0
                ? new DocumentSource(Stem(entry, documentExtension), entry)
                : new DocumentSource(entry[..separator], entry[(separator + 1)..]));
        }

        return [.. sources];
    }

    // Not GetFileNameWithoutExtension: a document's extension is two of them, and stripping one
    // would leave a scene keyed 'room.scene'.
    private static string Stem(string sourcePath, string documentExtension)
    {
        string name = System.IO.Path.GetFileName(sourcePath);

        return name.EndsWith(documentExtension, StringComparison.OrdinalIgnoreCase)
            ? name[..^documentExtension.Length]
            : System.IO.Path.GetFileNameWithoutExtension(name);
    }
}
