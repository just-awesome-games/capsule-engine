using Capsule.Assets;

namespace Capsule.Build;

/// <summary>
/// The key pass: the single place an authored path becomes the key the registry, the shipped path and
/// every document reference spell it by. No engine rule dictates how a game organizes or spells what
/// it authors.
/// </summary>
internal static class Keys
{
    /// <summary>The file whose directory, and everything under it, a shipping build leaves out.</summary>
    internal const string DevelopmentOnlyMarker = ".capsuleignore";

    /// <summary>
    /// How the build compares paths on disk, matching the targets' rule: ordinal on Linux, and
    /// case-folded on Windows and macOS.
    /// </summary>
    internal static readonly StringComparison PathComparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary><see cref="PathComparison"/> as a comparer, for a collection keyed by path.</summary>
    internal static readonly StringComparer PathComparer = StringComparer.FromComparison(PathComparison);

    /// <summary>
    /// Classifies and keys every request, and sets <see cref="PipelinePass.Keyed"/> in request order and
    /// <see cref="PipelinePass.Textures"/>.
    /// </summary>
    /// <remarks>
    /// A request that cannot be keyed fails the pass, and nothing downstream sees it. A shipping pass
    /// first leaves out every development-only request.
    /// </remarks>
    internal static void Derive(PipelinePass pass)
    {
        Dictionary<(AssetType, string), string> claimedBy = [];
        List<Source> keyed = [];
        string[] developmentOnly = pass.Requests.Shipping ? DevelopmentOnlyDirectories(pass.Requests) : [];

        foreach (Request request in pass.Requests.Sources)
        {
            try
            {
                string below = Below(request.Root ?? pass.Requests.AssetRoot, request.Path);
                if (IsUnder(developmentOnly, below))
                {
                    continue;
                }

                if (Key(below, request.Path) is not { } source)
                {
                    continue;
                }

                if (claimedBy.TryGetValue((source.Type, source.Key), out string? claimant))
                {
                    throw new FormatException(
                        $"keys as \"{source.Key}\", which '{claimant}' already claims. Two sources whose paths differ only in spelling are one asset, as are an imported file and an authored one at the same path, so rename one.");
                }

                claimedBy.Add((source.Type, source.Key), source.Path);
                keyed.Add(source);
            }
            catch (FormatException ex)
            {
                pass.Fail(request.Path, ex.Message);
            }
        }

        pass.Keyed = keyed;
        pass.Textures = keyed.Where(static source => source.Type == AssetType.Textures)
            .ToDictionary(static texture => texture.Key, StringComparer.Ordinal);
    }

    /// <summary>The key of <paramref name="path"/>, a key or an authored path with no extension.</summary>
    /// <remarks>
    /// Sheets and scenes both key the textures they name here, and check them in different places. A
    /// sheet's texture is checked against what the game ships in <c>SheetStep</c>, because the sheet
    /// compiles into members holding its handle. A scene document ships as data. The generator checks
    /// an asset an entity's properties name against the members marked <c>CapsuleGeneratedAsset</c>,
    /// and a tile map's texture is only keyed.
    /// </remarks>
    /// <param name="subject">What a refusal says of the source, as <c>is authored at "x"</c>.</param>
    /// <exception cref="FormatException">A segment is no C# name, or names a reserved device.</exception>
    internal static string Of(string path, string? subject = null)
    {
        subject ??= $"is authored at \"{path}\"";
        if (AssetPaths.NormalizeKey(path, out string? rejected) is not { } key)
        {
            throw new FormatException(
                $"{subject}, whose \"{rejected}\" is no C# name. Every segment of an asset's path is letters, digits, '-' and '_', and does not start with a digit.");
        }

        return AssetPaths.IsKey(key)
            ? key
            : throw new FormatException(
                $"{subject}, which keys as \"{key}\". No segment of a key may be a reserved Windows device name (nul, con, ...).");
    }

    /// <summary><paramref name="path"/> below <paramref name="root"/>, forward slashes.</summary>
    /// <remarks>A path the walk spelled from the root is cut below it without asking the file system.</remarks>
    internal static string Below(string root, string path) =>
        path.Length > root.Length + 1 && path[root.Length] == '/' && path.StartsWith(root, StringComparison.Ordinal)
            ? path[(root.Length + 1)..]
            : Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Where the extension a sidecar names starts in <paramref name="stem"/>, or -1 when its name holds none.</summary>
    /// <param name="stem">A sidecar's path or key without its <c>.config.json</c>.</param>
    /// <remarks>An asset's name holds no '.', so the first one in the sidecar's file name starts the extension.</remarks>
    internal static int SidecarExtension(string stem) => stem.IndexOf('.', stem.LastIndexOf('/') + 1);

    /// <summary>
    /// Every directory holding a <see cref="DevelopmentOnlyMarker"/>, below the asset root and ending
    /// in '/', or empty for the root itself.
    /// </summary>
    internal static string[] DevelopmentOnlyDirectories(BuildRequests requests) =>
    [
        .. requests.Sources
            .Where(static request => request.Root is null
                && string.Equals(Path.GetFileName(request.Path), DevelopmentOnlyMarker, PathComparison))
            .Select(request => Below(requests.AssetRoot, request.Path)[..^DevelopmentOnlyMarker.Length]),
    ];

    /// <summary>Whether a source placed at <paramref name="below"/> lies under one of <paramref name="directories"/>.</summary>
    /// <remarks>
    /// An imported file is placed at the path its importer wrote it to below the asset root. A file
    /// written under a marked directory goes too.
    /// </remarks>
    internal static bool IsUnder(string[] directories, string below)
    {
        foreach (string directory in directories)
        {
            if (below.StartsWith(directory, PathComparison))
            {
                return true;
            }
        }

        return false;
    }

    /// <param name="below">The source's path below its root.</param>
    /// <param name="path">The source's path, as every message names it.</param>
    private static Source? Key(string below, string path)
    {
        if (AssetType.Of(below) is not { } type)
        {
            return null;
        }

        string admitted = type.Extension(below)!;
        string stem = below[..^admitted.Length];
        int dot = type == AssetType.Configs ? SidecarExtension(stem) : -1;

        // A folder's .config.json has no name of its own.
        string key = type != AssetType.Configs ? Of(stem)
            : stem.Length == 0 ? string.Empty
            : stem[^1] == '/' ? Of(stem[..^1]) + "/"
            : dot < 0 ? Of(stem)
            : Of(stem[..dot]) + stem[dot..].ToLowerInvariant();

        return new Source(type, key, admitted.ToLowerInvariant(), path);
    }
}
