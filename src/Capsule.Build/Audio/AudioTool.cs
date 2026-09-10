using System.Text;

namespace Capsule.Build.Audio;

/// <summary>
/// The audio half of the build hook: measures every shipped source and renders the whole set as the
/// one C# file a game compiles against. Nothing is derived onto disk — an audio source ships as it
/// was authored.
/// </summary>
internal static class AudioTool
{
    private const string Name = "audio";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Measures every source named in <paramref name="listPath"/>, one <c>key|path</c> per line,
    /// the path relative to the working directory.
    /// </summary>
    /// <param name="listPath">The audio sources to measure.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int EmitFromList(string listPath, string generatedPath, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(error);

        DocumentSource[] sources;
        try
        {
            sources = DocumentSource.Read(listPath, string.Empty);
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot read the source list — {ex.Message}");

            return 1;
        }

        return Emit(sources, generatedPath, output, error);
    }

    /// <summary>Measures <paramref name="sources"/>, as <see cref="EmitFromList"/> does.</summary>
    /// <param name="sources">The audio sources to measure, each with the key it claims.</param>
    /// <param name="generatedPath">Where the generated C# is written.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int Emit(
        IReadOnlyList<DocumentSource> sources,
        string generatedPath,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        AudioNames declared = new();
        List<AudioSourceClip> clips = new(sources.Count);
        int failures = 0;

        foreach (DocumentSource source in sources)
        {
            if (!source.HasSafeKey())
            {
                error.WriteLine(
                    $"{source.Path}: claims clip key \"{source.Key}\"; a key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension.");
                failures++;
                continue;
            }

            try
            {
                declared.Declare(source.Key);

                AudioProbe.Measurement measured = AudioProbe.Measure(source.Path);
                clips.Add(new AudioSourceClip(
                    source.Key,
                    Path.GetExtension(source.Path),
                    measured.DurationSeconds,
                    measured.Loop));
                output.WriteLine($"{Name}: {source.Path} -> {source.Key}");
            }
            catch (Exception ex) when (IsReportable(ex))
            {
                error.WriteLine($"{source.Path}: {ex.Message}");
                failures++;
            }
        }

        if (failures > 0)
        {
            error.WriteLine($"{Name}: {failures} of {sources.Count} source(s) failed");

            return 1;
        }

        try
        {
            // Written whole every time, so a clip deleted since the last build leaves nothing behind
            // for a game to still compile against.
            AtomicFile.Write(
                generatedPath,
                path => File.WriteAllText(path, AudioRegistrySource.Render(clips), Utf8NoBom));
        }
        catch (Exception ex) when (IsReportable(ex))
        {
            error.WriteLine($"{Name}: cannot write '{generatedPath}' — {ex.Message}");

            return 1;
        }

        output.WriteLine($"{Name}: {clips.Count} clip(s) -> {generatedPath}");

        return 0;
    }

    private static bool IsReportable(Exception exception) =>
        exception is AudioFormatException or IOException or UnauthorizedAccessException;
}
