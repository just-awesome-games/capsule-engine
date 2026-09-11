using Capsule.Assets;
using Capsule.Generators;

namespace Capsule.Build.Keys;

/// <summary>One request after its key is derived, which is how everything downstream spells it.</summary>
/// <param name="Group">The domain root the source was authored under.</param>
/// <param name="Key">The authored path normalized segment by segment.</param>
/// <param name="Extension">The extension the shipped file carries, empty for a document.</param>
/// <param name="Source">Where the source is, relative to the working directory.</param>
internal readonly record struct KeyedAsset(string Group, string Key, string Extension, string Source);

/// <summary>
/// The key pass: the one place an authored path becomes the key everything downstream spells it by.
/// MSBuild cannot derive a key — the rule is one C# function shared with the generators — so the
/// targets hand this every authored path and read the derived names back out of the manifests it
/// writes. Whatever the request spelled, the key, the shipped path and the generated identifier all
/// come from here, so no engine rule dictates how a game spells a directory below a domain root.
/// </summary>
internal static class KeyTool
{
    private const string Name = "keys";

    private const string Document = Scenes.SceneDocumentTool.DocumentExtension;

    /// <summary>Keys every request into <paramref name="keyed"/>, in request order.</summary>
    /// <param name="requests">The authored paths to key.</param>
    /// <param name="keyed">Receives one entry per request that keyed.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>How many requests failed.</returns>
    internal static int Derive(IReadOnlyList<AssetRequest> requests, List<KeyedAsset> keyed, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(keyed);
        ArgumentNullException.ThrowIfNull(error);

        Dictionary<string, string> claimedBy = new(StringComparer.Ordinal);
        int failures = 0;

        foreach (AssetRequest request in requests)
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
            keyed.Add(new KeyedAsset(request.Group, key, request.Extension, request.Source));
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {requests.Count} source(s) failed");
        }

        return failures;
    }

    /// <summary>
    /// Writes the manifests the targets read their items back from, each holding the exact strings
    /// one hook needs so no target has to take a key apart again. Both are written whatever was
    /// keyed, so a hook reading one never reads what a previous build left behind.
    /// </summary>
    /// <param name="keyed">Everything this run keyed.</param>
    /// <param name="outputDirectory">Where the manifests are written.</param>
    /// <param name="derivedScenesDirectory">Where the scene importer writes its documents.</param>
    internal static void WriteManifests(
        IReadOnlyList<KeyedAsset> keyed,
        string outputDirectory,
        string derivedScenesDirectory)
    {
        ArgumentNullException.ThrowIfNull(keyed);
        ArgumentNullException.ThrowIfNull(derivedScenesDirectory);

        List<string> shipped = [];
        List<string> sceneContent = [];

        // One separator, whatever the platform spelled: a manifest line is read back by MSBuild,
        // which takes either, and by a human comparing two builds' output.
        string derived = derivedScenesDirectory.Replace('\\', '/');
        derived = derived.EndsWith('/') ? derived : derived + "/";

        foreach (KeyedAsset entry in keyed)
        {
            if (entry.Group == "scenes")
            {
                sceneContent.Add(
                    $"assets/scenes/{entry.Key}{Document}{BuildRequests.Separator}{derived}{entry.Key}{Document}");
            }
            else
            {
                shipped.Add(
                    $"assets/{entry.Group}/{entry.Key}{entry.Extension}{BuildRequests.Separator}{entry.Source}");
            }
        }

        Write(outputDirectory, "shipped-assets.txt", shipped);
        Write(outputDirectory, "scene-content.txt", sceneContent);
    }

    private static void Write(string directory, string name, List<string> lines) =>
        AtomicFile.Write(Path.Combine(directory, name), path => File.WriteAllLines(path, lines));
}
