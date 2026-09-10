using Capsule.Assets;
using Capsule.Generators;

namespace Capsule.Build.Keys;

/// <summary>
/// The key pass: the one place an authored path becomes the key everything downstream spells it by.
/// MSBuild cannot derive a key — the rule is one C# function shared with the generators — so the
/// targets hand this every authored path and read the derived names back out of the files it
/// writes. Whatever the request spelled, the key, the shipped path and the generated identifier all
/// come from here, so no engine rule dictates how a game spells a directory below a domain root.
/// </summary>
internal static class KeyTool
{
    /// <summary>The separator a request line and every derived line write between fields.</summary>
    internal const char Separator = '|';

    private const string Name = "keys";

    private const string Document = Scenes.SceneDocumentTool.DocumentExtension;

    /// <summary>Every group the targets ask about, in the order they are written.</summary>
    private static readonly string[] Groups = ["textures", "fonts", "audio", "scenes", "sprites"];

    /// <summary>
    /// Derives the keys of every request in <paramref name="requestsPath"/> — one
    /// <c>group|path|extension|source</c> per line, the source relative to the working directory —
    /// and writes the derived names into <paramref name="outputDirectory"/>.
    /// </summary>
    /// <param name="requestsPath">The authored paths to key.</param>
    /// <param name="outputDirectory">Where the derived name files are written.</param>
    /// <param name="derivedScenesDirectory">Where the scene importer writes its documents.</param>
    /// <param name="output">A summary line.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every request was keyed, 1 when any failed.</returns>
    internal static int Derive(
        string requestsPath,
        string outputDirectory,
        string derivedScenesDirectory,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        List<Request> requests = [];
        try
        {
            Directory.CreateDirectory(outputDirectory);
            foreach (string line in File.ReadAllLines(requestsPath))
            {
                if (Parse(line) is { } request)
                {
                    requests.Add(request);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{Name}: cannot read '{requestsPath}' — {ex.Message}");

            return 1;
        }

        List<Keyed> keyed = new(requests.Count);
        Dictionary<string, string> claimedBy = new(StringComparer.Ordinal);
        int failures = 0;

        foreach (Request request in requests)
        {
            if (TypeNaming.NormalizeKey(request.Path, out string? rejected) is not { } key)
            {
                error.WriteLine(
                    $"{request.Source}: is authored at \"{request.Path}\", whose \"{rejected}\" is no C# name; every segment of a path under a domain root is letters, digits, '-' and '_', and does not start with a digit.");
                failures++;
                continue;
            }

            if (!AssetPaths.IsKey(key))
            {
                error.WriteLine(
                    $"{request.Source}: is authored at \"{request.Path}\", which keys as \"{key}\"; a segment of a key is no reserved Windows device name (nul, con, ...).");
                failures++;
                continue;
            }

            string claim = request.Group + "/" + key;
            if (claimedBy.TryGetValue(claim, out string? claimant))
            {
                error.WriteLine(
                    $"{request.Source}: keys as \"{key}\", which '{claimant}' already claims; two sources that differ only in how their path is spelled are one asset, so rename one of them.");
                failures++;
                continue;
            }

            claimedBy.Add(claim, request.Source);
            keyed.Add(new Keyed(request.Group, key, request.Extension, request.Source));
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {requests.Count} source(s) failed");

            return 1;
        }

        try
        {
            Write(keyed, outputDirectory, derivedScenesDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{Name}: cannot write into '{outputDirectory}' — {ex.Message}");

            return 1;
        }

        output.WriteLine($"{Name}: {keyed.Count} source(s) keyed into {outputDirectory}");

        return 0;
    }

    // Six files, each holding the exact strings one caller needs, so no target has to take a key
    // apart again: the whole shipped path for the content hooks, key and source for the document
    // hooks, and the texture names a sheet is matched against.
    private static void Write(List<Keyed> keyed, string outputDirectory, string derivedScenesDirectory)
    {
        List<string> shipped = [];
        List<string> textures = [];
        List<string> audio = [];
        List<string> scenes = [];
        List<string> sprites = [];
        List<string> sceneContent = [];

        string derived = derivedScenesDirectory.EndsWith('/') || derivedScenesDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? derivedScenesDirectory
            : derivedScenesDirectory + "/";

        foreach (Keyed entry in keyed)
        {
            switch (entry.Group)
            {
                case "textures":
                    textures.Add(entry.Key + entry.Extension);
                    shipped.Add(Shipped(entry));
                    break;

                case "fonts":
                    shipped.Add(Shipped(entry));
                    break;

                case "audio":
                    shipped.Add(Shipped(entry));
                    audio.Add(entry.Key + Separator + entry.Source);
                    break;

                case "scenes":
                    scenes.Add(entry.Key + Separator + entry.Source);
                    sceneContent.Add(
                        $"assets/scenes/{entry.Key}{Document}{Separator}{derived}{entry.Key}{Document}");
                    break;

                // "sprites"; Parse admits no other group.
                default:
                    sprites.Add(entry.Key + Separator + entry.Source);
                    break;
            }
        }

        Write(outputDirectory, "shipped-assets.txt", shipped);
        Write(outputDirectory, "textures.txt", textures);
        Write(outputDirectory, "audio.txt", audio);
        Write(outputDirectory, "scenes.txt", scenes);
        Write(outputDirectory, "sprites.txt", sprites);
        Write(outputDirectory, "scene-content.txt", sceneContent);
    }

    private static string Shipped(Keyed entry) =>
        $"assets/{entry.Group}/{entry.Key}{entry.Extension}{Separator}{entry.Source}";

    private static void Write(string directory, string name, List<string> lines) =>
        AtomicFile.Write(Path.Combine(directory, name), path => File.WriteAllLines(path, lines));

    // group|path|extension|source. The extension is empty for a document, whose key carries none.
    private static Request? Parse(string line)
    {
        string entry = line.Trim();
        if (entry.Length == 0)
        {
            return null;
        }

        int group = entry.IndexOf(Separator);
        int path = group < 0 ? -1 : entry.IndexOf(Separator, group + 1);
        int extension = path < 0 ? -1 : entry.IndexOf(Separator, path + 1);
        if (extension < 0)
        {
            return null;
        }

        string name = entry[..group];

        // MSBuild hands over the path as the platform spelled it; a key has one separator.
        return Array.IndexOf(Groups, name) < 0
            ? null
            : new Request(
                name,
                entry[(group + 1)..path].Replace('\\', '/'),
                entry[(path + 1)..extension],
                entry[(extension + 1)..]);
    }

    private readonly record struct Request(string Group, string Path, string Extension, string Source);

    private readonly record struct Keyed(string Group, string Key, string Extension, string Source);
}
