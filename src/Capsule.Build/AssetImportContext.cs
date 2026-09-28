using System.Text;

namespace Capsule.Build;

/// <summary>One source an <see cref="IAssetImporter"/> imports, and the outputs it writes.</summary>
public sealed class AssetImportContext
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly List<(string AssetPath, byte[] Contents)> _outputs = [];

    /// <summary>The source, relative to the logic project's directory with forward slashes, as <c>Assets/Scenes/room-01.tmj</c>.</summary>
    /// <remarks>The build runs in the logic project's directory. The path opens the file as it stands.</remarks>
    public string SourcePath { get; }

    /// <summary>The asset root, relative to the logic project's directory with forward slashes, as <c>Assets</c>.</summary>
    public string AssetRoot { get; }

    /// <summary>The source's path below the asset root with forward slashes, as <c>Scenes/room-01.tmj</c>.</summary>
    public string AssetPath { get; }

    /// <summary>The tile size in pixels that <see cref="CapsuleBuild.WithTileSize"/> configured, or null when the game declares none.</summary>
    public int? TileSize { get; }

    internal AssetImportContext(string sourcePath, string assetRoot, int? tileSize)
    {
        SourcePath = sourcePath;
        AssetRoot = assetRoot;
        AssetPath = Keys.Below(assetRoot, sourcePath);
        TileSize = tileSize;
    }

    internal IReadOnlyList<(string AssetPath, byte[] Contents)> Outputs => _outputs;

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
