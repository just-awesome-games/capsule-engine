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

    /// <summary>The names the generated classes take for themselves.</summary>
    internal static string? Reserved(string identifier, bool leaf) =>
        string.Equals(identifier, RegistryFile.ListMember, StringComparison.Ordinal)
            ? "is the set member every generated class declares"
            : null;

    /// <summary>
    /// The C# text declaring <paramref name="clips"/>. A clip's key is its path under the audio
    /// root, and each directory in it becomes a nested class, so <c>steps/stone</c> is declared as
    /// <c>CapsuleAssets.Audio.Steps.Stone</c>. Ordered by key so the output is the same on every
    /// machine whatever order the build collected the sources in.
    /// </summary>
    /// <param name="clips">Each clip's key paired with the extension and duration measured for it.</param>
    internal static string Render(IReadOnlyList<AudioSourceClip> clips)
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

        foreach (AudioSourceClip clip in ordered)
        {
            registry.Add(clip.Key, clip.Key, clip);
        }

        StringBuilder source = RegistryFile.Open();
        registry.Append(source, "        ");

        return RegistryFile.Close(source);
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
