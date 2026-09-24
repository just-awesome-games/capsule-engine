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
/// The key pass: the single place an authored path becomes the key everything downstream spells it by.
/// The rule is a C# function shared with the generators, and MSBuild cannot call it. The targets hand
/// this every authored path and read the derived names back out of the manifests it writes. The key,
/// the shipped path and the generated identifier all come from here. No engine rule dictates how a
/// game spells a directory below a domain root.
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
                    $"{request.Source}: is authored at \"{request.Path}\", whose \"{rejected}\" is no C# name. Every segment of a path under a domain root is letters, digits, '-' and '_', and does not start with a digit.");
                failures++;
                continue;
            }

            if (!AssetPaths.IsKey(key))
            {
                error.WriteLine(
                    $"{request.Source}: is authored at \"{request.Path}\", which keys as \"{key}\". No segment of a key may be a reserved Windows device name (nul, con, ...).");
                failures++;
                continue;
            }

            string claim = request.Group + "/" + key;
            if (claimedBy.TryGetValue(claim, out string? claimant))
            {
                error.WriteLine(
                    $"{request.Source}: keys as \"{key}\", which '{claimant}' already claims. Two sources whose paths differ only in spelling are one asset, so rename one.");
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
    /// Writes the manifests the targets read their items back from, each holding the strings one
    /// hook needs. No target has to take a key apart again. Both are written on every run, and a hook
    /// never reads what a previous build left behind.
    /// </summary>
    /// <param name="keyed">Everything this run keyed.</param>
    /// <param name="outputDirectory">Where the manifests are written.</param>
    /// <param name="derivedScenesDirectory">Where the scene importer writes its documents.</param>
    /// <param name="packedTextures">The texture keys an atlas packed. Those textures do not ship on their own.</param>
    /// <param name="atlasLines">The shipped-asset lines for every atlas page and map, and every compiled shader.</param>
    /// <param name="sceneFields">Every scene's baseScene and camera, keyed the way the scene itself is.</param>
    internal static void WriteManifests(
        IReadOnlyList<KeyedAsset> keyed,
        string outputDirectory,
        string derivedScenesDirectory,
        HashSet<string> packedTextures,
        List<string> atlasLines,
        IReadOnlyDictionary<string, (string? BaseScene, string? Camera)> sceneFields)
    {
        ArgumentNullException.ThrowIfNull(keyed);
        ArgumentNullException.ThrowIfNull(derivedScenesDirectory);
        ArgumentNullException.ThrowIfNull(sceneFields);

        List<string> shipped = [];
        List<string> sceneContent = [];

        // Forward slashes whatever the platform spelled. MSBuild takes either, and a human compares
        // two builds' output.
        string derived = derivedScenesDirectory.Replace('\\', '/');
        derived = derived.EndsWith('/') ? derived : derived + "/";

        foreach (KeyedAsset entry in keyed)
        {
            if (entry.Group == "scenes")
            {
                // An absent baseScene or camera is an empty field, not a missing one, so every line
                // the targets read back splits into the same four parts.
                (string? baseScene, string? camera) = sceneFields.TryGetValue(entry.Key, out var found) ? found : (null, null);
                sceneContent.Add(
                    $"{entry.Key}{BuildRequests.Separator}{baseScene}{BuildRequests.Separator}{camera}{BuildRequests.Separator}{derived}{entry.Key}{Document}");
            }
            // An atlas manifest and a sprite sheet are compiled in, not shipped, a shader ships as the
            // effect compiled from it, and a texture an atlas packed ships as part of that atlas page.
            else if (entry.Group is not ("atlases" or "sprites" or "shaders")
                && !(entry.Group == "textures" && packedTextures.Contains(entry.Key)))
            {
                shipped.Add(
                    $"assets/{entry.Group}/{entry.Key}{entry.Extension}{BuildRequests.Separator}{entry.Source}");
            }
        }

        shipped.AddRange(atlasLines);

        Write(outputDirectory, "shipped-assets.txt", shipped);
        Write(outputDirectory, "scene-content.txt", sceneContent);
    }

    private static void Write(string directory, string name, List<string> lines) =>
        AtomicFile.Write(Path.Combine(directory, name), path => File.WriteAllLines(path, lines));
}
