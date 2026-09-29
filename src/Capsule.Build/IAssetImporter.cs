namespace Capsule.Build;

/// <summary>Turns authored sources of one format into files the build reads as though they were authored.</summary>
/// <remarks>
/// <para>
/// A game's build project adds an importer with <see cref="CapsuleBuild.AddImporter"/>. A claimed
/// source is never read as an asset itself.
/// </para>
/// <para>
/// An importer reads and probes files only through its <see cref="AssetImportContext"/>, and writes only
/// through <see cref="AssetImportContext.Write(string, ReadOnlySpan{byte})"/>.
/// </para>
/// <para>
/// An import's inputs are its source and every file it reads through
/// <see cref="AssetImportContext.ReadAllBytes"/> or <see cref="AssetImportContext.ReadAllText"/> or probes
/// through <see cref="AssetImportContext.Exists"/>. A run imports a source again when one of those inputs
/// changed, appeared or was deleted, when an output it wrote is gone, or when the importer's assembly or
/// the configured tile size changed. The build cannot see a file the importer reads any other way.
/// </para>
/// <para>
/// The build calls <see cref="Import"/> concurrently for different sources, and an importer keeps no mutable state between calls.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class NoteImporter : IAssetImporter
/// {
///     public IReadOnlyList&lt;string&gt; Extensions { get; } = [".note"];
///
///     public void Import(AssetImportContext context) =&gt;
///         context.Write(Path.ChangeExtension(context.AssetPath, ".scene.json"), NoteScenes.Translate(context.ReadAllText(context.SourcePath)));
/// }
/// </code>
/// </example>
public interface IAssetImporter
{
    /// <summary>The source extensions this importer claims, as <c>.tmj</c>, matched without regard to case.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Imports one source, writing each output through <paramref name="context"/>.</summary>
    /// <exception cref="FormatException">
    /// The source has a defect. The build reports the message against the source and still builds
    /// every other source. So does an <see cref="IOException"/>. Any other exception stops the build.
    /// </exception>
    void Import(AssetImportContext context);
}
