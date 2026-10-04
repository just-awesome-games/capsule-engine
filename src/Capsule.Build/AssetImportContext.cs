using System.Text;

namespace Capsule.Build;

/// <summary>One source an <see cref="IAssetImporter"/> imports, the files it reads, and the outputs it writes.</summary>
/// <example>
/// An importer's own test imports a source on disk and asserts on what it wrote and read:
/// <code>
/// AssetImportContext context = new("Assets/Scenes/room.note", "Assets");
/// new NoteImporter().Import(context);
///
/// Assert.Equal("Scenes/room.scene.json", Assert.Single(context.Outputs).AssetPath);
/// Assert.Equal(["Assets/Scenes/room.note"], context.Inputs);
/// </code>
/// </example>
public sealed class AssetImportContext
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly List<(string AssetPath, byte[] Contents)> _outputs = [];

    private readonly List<string> _inputs = [];

    /// <summary>The source, relative to the logic project's directory with forward slashes, as <c>Assets/Scenes/room-01.tmj</c>.</summary>
    /// <remarks>The build runs in the logic project's directory, where the path opens the file.</remarks>
    public string SourcePath { get; }

    /// <summary>The asset root, relative to the logic project's directory with forward slashes, as <c>Assets</c>.</summary>
    public string AssetRoot { get; }

    /// <summary>The source's path below the asset root with forward slashes, as <c>Scenes/room-01.tmj</c>.</summary>
    public string AssetPath { get; }

    /// <summary>The tile size in pixels that <see cref="CapsuleBuild.WithTileSize"/> configured, or null when the game declares none.</summary>
    public int? TileSize { get; init; }

    /// <summary>Creates a context that imports the source at <paramref name="sourcePath"/> below <paramref name="assetRoot"/> from files on disk.</summary>
    /// <param name="sourcePath">The source relative to the working directory, as <c>Assets/Scenes/room-01.tmj</c>, or an absolute path.</param>
    /// <param name="assetRoot">The asset root relative to the working directory, as <c>Assets</c>, or an absolute path.</param>
    public AssetImportContext(string sourcePath, string assetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);
        SourcePath = sourcePath;
        AssetRoot = assetRoot;
        AssetPath = Keys.Below(assetRoot, sourcePath);
    }

    /// <summary>Every file read or probed through this context, relative to the working directory with forward slashes, in the order first read.</summary>
    /// <remarks>A path is an input from the moment it is read, even when the file is missing or the read throws.</remarks>
    public IReadOnlyList<string> Inputs => _inputs;

    /// <summary>Every output written through this context, by its path below the asset root, in the order written.</summary>
    /// <remarks>The context writes nothing to disk. The build writes these after <see cref="IAssetImporter.Import"/> returns.</remarks>
    public IReadOnlyList<(string AssetPath, byte[] Contents)> Outputs => _outputs;

    /// <summary>Reads the whole file at <paramref name="path"/> and makes it an input of this import.</summary>
    /// <param name="path">The file relative to the logic project's directory as <see cref="SourcePath"/> is spelled, as <c>Assets/Tilesets/cave.tsj</c>, or an absolute path.</param>
    /// <example>
    /// A map reads the tileset it names, relative to itself:
    /// <code>
    /// byte[] tileset = context.ReadAllBytes(Path.Combine(Path.GetDirectoryName(context.SourcePath)!, map.TilesetSource));
    /// </code>
    /// </example>
    public byte[] ReadAllBytes(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.ReadAllBytes(Reads(path));
    }

    /// <summary>Reads the whole file at <paramref name="path"/> as text and makes it an input of this import, as <see cref="ReadAllBytes"/> reads bytes.</summary>
    /// <param name="path">The file relative to the logic project's directory as <see cref="SourcePath"/> is spelled, or an absolute path.</param>
    /// <remarks>The text is UTF-8 unless a byte order mark names another encoding.</remarks>
    public string ReadAllText(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.ReadAllText(Reads(path));
    }

    /// <summary>Whether a file is at <paramref name="path"/>, which makes the path an input of this import whether or not it exists.</summary>
    /// <param name="path">The file relative to the logic project's directory as <see cref="SourcePath"/> is spelled, or an absolute path.</param>
    public bool Exists(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return File.Exists(Reads(path));
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

    // The path relative to the working directory, recorded as an input before anything opens it.
    private string Reads(string path)
    {
        string relative = BuildRequests.Relative(path);
        if (!_inputs.Contains(relative, Keys.PathComparer))
        {
            _inputs.Add(relative);
        }

        return relative;
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
