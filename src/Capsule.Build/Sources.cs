using Capsule.Assets;
using Capsule.Generators;

namespace Capsule.Build;

/// <summary>One type of authored source, admitted by its extension wherever it sits under <c>Assets/</c>.</summary>
/// <param name="suffix">The word its members end in, as <c>player.png</c> is <c>PlayerTexture</c>.</param>
/// <param name="extensions">The extensions it admits, matched without regard to case.</param>
internal sealed class AssetType(string suffix, params string[] extensions)
{
    internal static readonly AssetType Textures = new("Texture", ".png");

    internal static readonly AssetType Audio = new("Sound", ".ogg", ".wav");

    internal static readonly AssetType Fonts = new("Font", ".fnt");

    internal static readonly AssetType Shaders = new("Shader", ".fx");

    internal static readonly AssetType Scenes = new("Scene", ".scene.json");

    internal static readonly AssetType Sprites = new("Sheet", ".sheet.json");

    internal static readonly AssetType Atlases = new("Atlas", ".atlas.json");

    private static readonly AssetType[] Admitting = [Textures, Audio, Fonts, Shaders, Scenes, Sprites, Atlases];

    internal string Suffix { get; } = suffix;

    internal string[] Extensions { get; } = extensions;

    /// <summary>The type admitting <paramref name="name"/> by its extension, or null for a file none reads.</summary>
    internal static AssetType? Of(string name) => Array.Find(Admitting, type => type.Extension(name) is not null);

    /// <summary>The admitted extension <paramref name="name"/> ends in, as it spelled it, or null.</summary>
    internal string? Extension(string name)
    {
        foreach (string extension in Extensions)
        {
            if (name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return name[^extension.Length..];
            }
        }

        return null;
    }
}

/// <summary>One source after the key pass, which is how everything downstream spells it.</summary>
/// <param name="Key">Its path under <c>Assets/</c> normalized segment by segment, with no extension. It ships at that path.</param>
/// <param name="Extension">The admitted extension in lower case, as a shipped copy and every handle spell it.</param>
/// <param name="Path">Where the source is, relative to the working directory. Every message names it.</param>
internal readonly record struct Source(AssetType Type, string Key, string Extension, string Path);

/// <summary>
/// The key pass: the single place an authored path becomes the key the registry, the shipped path and
/// every document reference spell it by. No engine rule dictates how a game organizes or spells what
/// it authors.
/// </summary>
internal static class Keys
{
    /// <summary>Classifies and keys every request into <paramref name="keyed"/>, in request order.</summary>
    /// <returns>How many requests failed, each reported against its source.</returns>
    internal static int Derive(BuildRequests requests, List<Source> keyed, TextWriter error)
    {
        Dictionary<(AssetType, string), string> claimedBy = [];
        int failures = 0;

        foreach (Request request in requests.Sources)
        {
            try
            {
                if (Key(requests.AssetRoot, request) is not { } source)
                {
                    continue;
                }

                if (claimedBy.TryGetValue((source.Type, source.Key), out string? claimant))
                {
                    throw new FormatException(
                        $"keys as \"{source.Key}\", which '{claimant}' already claims. Two sources whose paths differ only in spelling are one asset, so rename one.");
                }

                claimedBy.Add((source.Type, source.Key), source.Path);
                keyed.Add(source);
            }
            catch (FormatException ex)
            {
                error.WriteLine($"{request.Path}: {ex.Message}");
                failures++;
            }
        }

        return failures;
    }

    /// <summary>The key of <paramref name="path"/>, a key or an authored path with no extension.</summary>
    /// <param name="subject">What a refusal says of the source, as <c>is authored at "x"</c>.</param>
    /// <exception cref="FormatException">A segment is no C# name, or names a reserved device.</exception>
    internal static string Of(string path, string? subject = null)
    {
        subject ??= $"is authored at \"{path}\"";
        if (TypeNaming.NormalizeKey(path, out string? rejected) is not { } key)
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
    internal static string Below(string root, string path) =>
        System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

    private static Source? Key(string root, Request request)
    {
        // A module states its document's key. One naming none is keyed by its stem.
        if (request.Document is { } document)
        {
            string name = System.IO.Path.GetFileName(request.Path);
            string extension = document.Extension(name) ?? System.IO.Path.GetExtension(name);
            string authored = request.Key.Length > 0 ? request.Key : name[..^extension.Length];

            return new Source(document, Of(authored), extension.ToLowerInvariant(), request.Path);
        }

        string below = Below(root, request.Path);
        if (AssetType.Of(below) is not { } type)
        {
            return null;
        }

        string admitted = type.Extension(below)!;

        return new Source(type, Of(below[..^admitted.Length]), admitted.ToLowerInvariant(), request.Path);
    }
}
