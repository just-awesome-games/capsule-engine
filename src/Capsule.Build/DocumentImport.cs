namespace Capsule.Build;

/// <summary>
/// The loop both document tools import through: create the output directory, refuse a key that is
/// no key or one already claimed, derive each source, and report every failure anchored to it.
/// </summary>
internal static class DocumentImport
{
    /// <summary>Derives one source to <paramref name="documentPath"/>, throwing on a defect.</summary>
    internal delegate void Derive(DocumentSource source, string documentPath);

    /// <param name="name">What the hook calls itself in a summary line.</param>
    /// <param name="noun">What one document is called in a failure — <c>scene</c> or <c>sheet</c>.</param>
    /// <param name="outputDirectory">Where the canonical documents are written.</param>
    /// <param name="sources">The sources to import, each with the key it claims.</param>
    /// <param name="extension">Both halves of the extension a derived document carries.</param>
    /// <param name="reportable">Whether an exception is a defect to report rather than to throw on.</param>
    /// <param name="derive">The per-source step.</param>
    /// <param name="output">Progress, one line per source.</param>
    /// <param name="error">Failures, each anchored to the source that failed.</param>
    /// <returns>0 when every source succeeded, 1 when any failed.</returns>
    internal static int Run(
        string name,
        string noun,
        string outputDirectory,
        IReadOnlyList<DocumentSource> sources,
        string extension,
        Func<Exception, bool> reportable,
        Derive derive,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception ex) when (reportable(ex))
        {
            error.WriteLine($"{name}: cannot create '{outputDirectory}' — {ex.Message}");
            return 1;
        }

        Dictionary<string, string> claimedBy = new(StringComparer.OrdinalIgnoreCase);
        int failures = 0;

        foreach (DocumentSource source in sources)
        {
            if (!source.HasSafeKey())
            {
                error.WriteLine(
                    $"{source.Path}: claims {noun} key \"{source.Key}\"; a key is one or more '/'-joined segments of ASCII letters, digits, hyphens and underscores, none of them a reserved Windows device name (nul, con, ...), and carries no extension.");
                failures++;
                continue;
            }

            string documentPath = Path.Combine(outputDirectory, source.Key + extension);

            if (!claimedBy.TryAdd(documentPath, source.Path))
            {
                error.WriteLine(
                    $"{source.Path}: would overwrite the {noun} document of '{claimedBy[documentPath]}'; a document is written at the key its source claims, so keys must be unique.");
                failures++;
                continue;
            }

            try
            {
                // The key nests, so the directory the document lands in may not exist yet.
                Directory.CreateDirectory(Path.GetDirectoryName(documentPath)!);
                derive(source, documentPath);
                output.WriteLine($"{name}: {source.Path} -> {documentPath}");
            }
            catch (Exception ex) when (reportable(ex))
            {
                error.WriteLine($"{source.Path}: {Unanchored(ex.Message, source.Path)}");
                failures++;
            }
        }

        if (failures == 0)
        {
            return 0;
        }

        error.WriteLine($"{name}: {failures} of {sources.Count} source(s) failed");

        return 1;
    }

    // The path is the prefix of every line already, so a message a document reader anchored to the
    // same path is not anchored to it twice.
    private static string Unanchored(string message, string sourcePath)
    {
        string prefix = sourcePath + ": ";

        return message.StartsWith(prefix, StringComparison.Ordinal) ? message[prefix.Length..] : message;
    }
}
