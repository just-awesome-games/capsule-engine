using System.Globalization;
using System.Text;
using Capsule.Audio;
using Capsule.Generators;

namespace Capsule.Build.Audio;

/// <summary>
/// Renders every audio source a game ships as one C# file the game compiles. Each clip becomes a
/// typed member carrying the duration the build measured. A misspelt clip is a compile error, and
/// nothing is read at run time to learn how long a sound is.
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
    /// root, and each directory in it becomes a nested class. <c>steps/stone</c> is declared as
    /// <c>CapsuleAssets.Audio.Steps.Stone</c>. Clips are ordered by key, and the output is the same on
    /// every machine whatever order the build collected the sources in.
    /// </summary>
    /// <param name="clips">Each clip's key paired with the extension and duration measured for it.</param>
    /// <param name="key">The clip key that could not be declared, when one could not.</param>
    /// <param name="because">Why it could not be declared beside what the registry already holds.</param>
    /// <returns>The generated source, or null when a key names something C# would refuse.</returns>
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
        RegistryClaimCheck<AudioSourceClip> claimable = RegistryClaims.Check<AudioSourceClip>(
            refusal => refused = Refusals.Because(refusal));

        foreach (AudioSourceClip clip in ordered)
        {
            if (Unnameable(clip.Key) is { } segment)
            {
                refused = $"whose \"{segment}\" is no C# name. Every segment is letters, digits, '-' and '_', and does not start with a digit.";
            }
            else if (registry.Add(clip.Key, clip.Key, clip, claimable))
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

        // Omitted when the source authored no region, so such a clip renders as it did before
        // regions existed and the constructor's default stands.
        if (loop.HasRegion)
        {
            source.Append(", new ").Append(RegionType).Append('(').Append(Number(loop.StartSeconds))
                .Append("D, ").Append(Number(loop.EndSeconds)).Append("D)");
        }

        source.AppendLine(");");
    }

    // Round-trippable and culture-invariant, so every machine writes the same text and AudioClip
    // takes back the double the duration was measured as.
    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
