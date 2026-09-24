using System.Globalization;
using Capsule.Build.Shaders;

namespace Capsule.Build;

/// <summary>One source the targets hand the build, as a line of the manifest named it.</summary>
/// <param name="Path">Where the source is, relative to the working directory and with forward slashes.</param>
/// <param name="Document">The type a module derived it as, or null for a file under the asset root.</param>
/// <param name="Key">The key a module claims for its document, or empty to key it by its stem.</param>
internal readonly record struct Request(string Path, AssetType? Document = null, string Key = "");

/// <summary>
/// The manifest the targets write and a run reads, one <c>kind|value...</c> line each:
/// <c>root|&lt;dir&gt;</c>, <c>tile-size|&lt;px&gt;</c>, <c>shader-tools|&lt;dxc&gt;|&lt;spirv-cross&gt;</c>,
/// then one <c>asset|&lt;path&gt;</c> per file under the asset root and one
/// <c>scene|&lt;path&gt;|&lt;key&gt;</c> or <c>sheet|&lt;path&gt;|&lt;key&gt;</c> per document a
/// module derived. The targets rewrite it whenever the authored set or an option changes, which
/// makes it the run's incremental input.
/// </summary>
/// <param name="AssetRoot">The authoring tree, <c>Assets/</c>, relative to the working directory.</param>
/// <param name="TileSize">The tile size every imported grid must match, or null to impose none.</param>
/// <param name="ShaderTools">The shader tools the build downloaded, or null when the game has no shader.</param>
/// <param name="Sources">Every source, in the order the targets wrote them.</param>
internal sealed record BuildRequests(string AssetRoot, int? TileSize, ShaderTools? ShaderTools, IReadOnlyList<Request> Sources)
{
    /// <summary>Reads the manifest at <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">A line is of no kind the manifest declares, or states no value.</exception>
    internal static BuildRequests Read(string path)
    {
        string root = string.Empty;
        int? tileSize = null;
        ShaderTools? tools = null;
        List<Request> sources = [];

        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Trim().Length == 0)
            {
                continue;
            }

            switch (line.Split('|'))
            {
                case ["root", string directory]:
                    root = Relative(directory);
                    break;
                case ["tile-size", string declared]:
                    tileSize = int.TryParse(declared, NumberStyles.None, CultureInfo.InvariantCulture, out int size) && size > 0
                        ? size
                        : throw new FormatException(
                            $"declares a tile size of \"{declared}\". A declared tile size is a positive whole number of pixels.");
                    break;
                case ["shader-tools", string dxc, string spirvCross]:
                    tools = new ShaderTools(dxc, spirvCross);
                    break;
                case ["asset", string asset]:
                    sources.Add(new Request(Relative(asset)));
                    break;
                case ["scene", string document, string key]:
                    sources.Add(new Request(Relative(document), AssetType.Scenes, key));
                    break;
                case ["sheet", string document, string key]:
                    sources.Add(new Request(Relative(document), AssetType.Sprites, key));
                    break;
                default:
                    throw new FormatException($"holds the line \"{line}\", which is no kind of line this build reads.");
            }
        }

        return new BuildRequests(root, tileSize, tools, sources);
    }

    // Every path a message names is relative to the project, the working directory, so a build log
    // and a document's provenance carry no machine's own layout.
    private static string Relative(string path) =>
        System.IO.Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
}
