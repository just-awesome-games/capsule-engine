using System.Globalization;
using System.Text;
using Capsule.Audio;
using Capsule.Generators;

namespace Capsule.Build.Audio;

/// <summary>
/// Renders every audio source a game ships as one C# file the game compiles. Each clip becomes a
/// typed member carrying the duration the build measured, so a misspelt clip is a compile error and
/// no file is read at run time to learn how long a sound is.
/// </summary>
internal static class AudioRegistrySource
{
    /// <summary>The generated class every clip is declared under.</summary>
    internal const string RegistryClass = "Audio";

    private const string Domain = "audio";

    private const string ClipType = "global::Capsule.Audio.AudioClip";

    private const string RegionType = "global::Capsule.Audio.AudioLoopRegion";

    /// <summary>
    /// The C# text declaring <paramref name="clips"/>. A clip's key is its path under the audio
    /// root, and each directory in it becomes a nested class, so <c>steps/stone</c> is declared as
    /// <c>CapsuleAssets.Audio.Steps.Stone</c>. Ordered by key so the output is the same on every
    /// machine whatever order the build collected the sources in.
    /// </summary>
    /// <param name="clips">Each clip's key paired with the extension and duration measured for it.</param>
    /// <param name="key">The clip key that could not be declared, when one could not.</param>
    /// <param name="because">Why it could not be declared beside what the registry already holds.</param>
    /// <returns>The generated source, or null where a key names something C# would refuse.</returns>
    internal static string? Render(IReadOnlyList<AudioSourceClip> clips, out string? key, out string? because)
    {
        ArgumentNullException.ThrowIfNull(clips);

        List<AudioSourceClip> ordered = [.. clips];
        ordered.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        RegistryDomain<AudioSourceClip> registry = new(
            RegistryClass,
            Domain,
            ClipType,
            "clip",
            "Every audio clip this game ships, with the duration the build measured for it.",
            AppendClip);

        string? refused = null;
        foreach (AudioSourceClip clip in ordered)
        {
            if (Unnameable(clip.Key) is { } segment)
            {
                refused = $"whose \"{segment}\" is no C# name; every segment is letters, digits, '-' and '_', and does not start with a digit.";
            }
            else if (registry.Add(clip.Key, clip.Key, clip, Claimable(reason => refused = reason)))
            {
                continue;
            }

            key = clip.Key;
            because = $"is keyed \"{clip.Key}\", {refused}";

            return null;
        }

        key = null;
        because = null;

        StringBuilder source = RegistryFile.Open();
        registry.Append(source, "        ");

        return RegistryFile.Close(source);
    }

    /// <summary>The C# text declaring <paramref name="clips"/>, which must all be declarable.</summary>
    internal static string Render(IReadOnlyList<AudioSourceClip> clips) =>
        Render(clips, out _, out string? because) ?? throw new AudioFormatException(because!);

    // Whether an identifier may be declared on this class: not the class's own name (CS0542), not a
    // name the generated members take, and not one already claimed there.
    private static RegistryClaimCheck<AudioSourceClip> Claimable(Action<string> refuse) =>
        (node, identifier, display, leaf) =>
        {
            if (string.Equals(identifier, node.Identifier, StringComparison.Ordinal))
            {
                refuse($"whose '{identifier}' would be declared inside a generated class of that name; name it something else.");

                return false;
            }

            if (string.Equals(identifier, RegistryFile.ListMember, StringComparison.Ordinal))
            {
                refuse($"whose '{identifier}' is the set member every generated class declares; name it something else.");

                return false;
            }

            if (node.ClaimedBy.TryGetValue(identifier, out string? claimed))
            {
                // A directory two clips share is one class, not a collision.
                if (node.Directories.ContainsKey(identifier) && string.Equals(claimed, display, StringComparison.Ordinal))
                {
                    return true;
                }

                refuse($"whose '{identifier}' is already declared in that directory by \"{claimed}\"; two names that differ only in their separators are one C# name.");

                return false;
            }

            return true;
        };

    private static string? Unnameable(string key)
    {
        foreach (string segment in key.Split('/'))
        {
            if (TypeNaming.ToIdentifier(segment) is null)
            {
                return segment;
            }
        }

        return null;
    }

    private static void AppendClip(StringBuilder source, string indent, string identifier, AudioSourceClip clip)
    {
        AudioLoopRegion loop = clip.Loop;

        source.Append(indent).Append("/// <summary><c>").Append(Domain).Append('/').Append(clip.Key)
            .Append(clip.Extension).Append("</c>, ").Append(Number(clip.DurationSeconds)).Append(" seconds long");

        if (loop.HasRegion)
        {
            source.Append(", looping ").Append(Number(loop.StartSeconds)).Append(" to ")
                .Append(Number(loop.EndSeconds)).Append(" seconds");
        }

        source.AppendLine(".</summary>");
        source.Append(indent).Append("public static ").Append(ClipType).Append(' ').Append(identifier)
            .Append(" => new ").Append(ClipType).Append('(');
        source.Append(Literal(clip.Key)).Append(", ").Append(Literal(clip.Extension)).Append(", ")
            .Append(Number(clip.DurationSeconds)).Append('D');

        // Omitted where the source authored none, so a clip with no region renders exactly as it
        // did before regions existed and the constructor's own default stands.
        if (loop.HasRegion)
        {
            source.Append(", new ").Append(RegionType).Append('(').Append(Number(loop.StartSeconds))
                .Append("D, ").Append(Number(loop.EndSeconds)).Append("D)");
        }

        source.AppendLine(");");
    }

    // Round-trippable and invariant: the same text on every machine, and a literal the AudioClip
    // constructor takes as the double it was measured as.
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
