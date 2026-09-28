namespace Capsule.Build;

/// <summary>Turns authored sources of one format into files the build reads as though they were authored.</summary>
/// <remarks>
/// A game's build project adds an importer with <see cref="CapsuleBuild.AddImporter"/>. Every run
/// imports each claimed source again, and a claimed source is never read as an asset itself.
/// </remarks>
/// <example>
/// <code>
/// public sealed class NoteImporter : IAssetImporter
/// {
///     public IReadOnlyList&lt;string&gt; Extensions { get; } = [".note"];
///
///     public void Import(AssetImportContext context) =&gt;
///         context.Write(Path.ChangeExtension(context.AssetPath, ".scene.json"), NoteScenes.Translate(context.SourcePath));
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
