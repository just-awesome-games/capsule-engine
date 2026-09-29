using System.Text;
using Capsule.Build.Caching;

namespace Capsule.Build;

/// <summary>One source an <see cref="IAssetImporter"/> imports, the files it reads, and the outputs it writes.</summary>
/// <remarks>An importer reads and probes files only through this context, and its outputs reach disk only through <see cref="Write(string, ReadOnlySpan{byte})"/>.</remarks>
public sealed class AssetImportContext
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly List<(string AssetPath, byte[] Contents)> _outputs = [];

    private readonly DerivedFiles _files;

    /// <summary>The source, relative to the logic project's directory with forward slashes, as <c>Assets/Scenes/room-01.tmj</c>.</summary>
    /// <remarks>
    /// The build runs in the logic project's directory. The path opens the file as it stands. The source
    /// is always an input of its import, however the importer reads it.
    /// </remarks>
    public string SourcePath { get; }

    /// <summary>The asset root, relative to the logic project's directory with forward slashes, as <c>Assets</c>.</summary>
    public string AssetRoot { get; }

    /// <summary>The source's path below the asset root with forward slashes, as <c>Scenes/room-01.tmj</c>.</summary>
    public string AssetPath { get; }

    /// <summary>The tile size in pixels that <see cref="CapsuleBuild.WithTileSize"/> configured, or null when the game declares none.</summary>
    public int? TileSize { get; }

    internal AssetImportContext(string sourcePath, string assetRoot, int? tileSize, DerivedFiles files)
    {
        SourcePath = sourcePath;
        AssetRoot = assetRoot;
        AssetPath = Keys.Below(assetRoot, sourcePath);
        TileSize = tileSize;
        _files = files;
    }

    internal IReadOnlyList<(string AssetPath, byte[] Contents)> Outputs => _outputs;

    /// <summary>Reads the whole file at <paramref name="path"/> and makes it an input of this import.</summary>
    /// <param name="path">The file relative to the logic project's directory as <see cref="SourcePath"/> is spelled, as <c>Assets/Tilesets/cave.tsj</c>, or an absolute path.</param>
    /// <remarks>
    /// The build imports the source again when an input changes or is deleted, and reuses the last
    /// outputs otherwise. The build cannot see a file the importer reads any other way.
    /// </remarks>
    /// <example>
    /// A map reads the tileset it names, relative to itself:
    /// <code>
    /// byte[] tileset = context.ReadAllBytes(Path.Combine(Path.GetDirectoryName(context.SourcePath)!, map.TilesetSource));
    /// </code>
    /// </example>
    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.ReadAllBytes(_files.Reads(path));
    }

    /// <summary>Reads the whole file at <paramref name="path"/> as text and makes it an input of this import, as <see cref="ReadAllBytes"/> reads bytes.</summary>
    /// <param name="path">The file relative to the logic project's directory as <see cref="SourcePath"/> is spelled, or an absolute path.</param>
    /// <remarks>The text is UTF-8 unless a byte order mark names another encoding. The build cannot see a file the importer reads any other way.</remarks>
    public string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.ReadAllText(_files.Reads(path));
    }

    /// <summary>Whether a file is at <paramref name="path"/>, which makes the path an input of this import whether or not it exists.</summary>
    /// <param name="path">The file relative to the logic project's directory as <see cref="SourcePath"/> is spelled, or an absolute path.</param>
    /// <remarks>The build imports the source again when a file it probed appears or disappears.</remarks>
    public bool Exists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.Exists(_files.Reads(path));
    }

    /// <summary>Writes an output the build reads as though it were authored at <paramref name="assetPath"/> below the asset root.</summary>
    /// <param name="assetPath">A relative path below the asset root, as <c>Scenes/room-01.scene.json</c>.</param>
    /// <param name="contents">The whole file.</param>
    /// <remarks>Nothing reaches disk unless <see cref="IAssetImporter.Import"/> returns.</remarks>
    public void Write(string assetPath, ReadOnlySpan<byte> contents) => _outputs.Add((Checked(assetPath), contents.ToArray()));

    /// <summary>Writes a text output as UTF-8 with no byte order mark, as <see cref="Write(string, ReadOnlySpan{byte})"/> writes bytes.</summary>
    /// <param name="assetPath">A relative path below the asset root, as <c>Scenes/room-01.scene.json</c>.</param>
    /// <param name="text">The whole file.</param>
    public void Write(string assetPath, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(assetPath, Utf8NoBom.GetBytes(text));
    }

    private static string Checked(string assetPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        string path = assetPath.Replace('\\', '/');
        if (Path.IsPathRooted(path) || path.EndsWith('/') || path.Split('/').Any(static segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException(
                $"\"{assetPath}\" is no file path below the asset root. Name the output relative to the asset root, as \"Scenes/room.scene.json\".",
                nameof(assetPath));
        }

        return path;
    }
}
