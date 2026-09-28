using Capsule.Build.Shaders;

namespace Capsule.Build;

/// <summary>
/// The manifest the targets write and a run reads, one <c>kind|value...</c> line each:
/// <c>root|&lt;dir&gt;</c>, <c>shader-tools|&lt;dxc&gt;|&lt;spirv-cross&gt;</c>, <c>shipping|true</c> or
/// <c>shipping|false</c>, then one <c>asset|&lt;path&gt;</c> per file under the asset root. The targets rewrite
/// it whenever the set of files or an option changes, which makes it the run's incremental input.
/// </summary>
/// <param name="AssetRoot">The authoring tree, <c>Assets/</c>, relative to the working directory.</param>
/// <param name="ShaderTools">The shader tools the build downloaded, or null when the game has no shader.</param>
/// <param name="Shipping">Whether the build is a publish, which leaves out every development-only source. False when the manifest does not say.</param>
/// <param name="Sources">Every source, in the order the targets wrote them, then every file an importer wrote.</param>
internal sealed record BuildRequests(string AssetRoot, ShaderTools? ShaderTools, bool Shipping, IReadOnlyList<Request> Sources)
{
    /// <summary>Reads the manifest at <paramref name="path"/>.</summary>
    /// <exception cref="FormatException">A line is of no kind the manifest declares, or states no value.</exception>
    internal static BuildRequests Read(string path)
    {
        string root = string.Empty;
        ShaderTools? tools = null;
        bool shipping = false;
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
                case ["shader-tools", string dxc, string spirvCross]:
                    tools = new ShaderTools(dxc, spirvCross);
                    break;
                case ["shipping", string declared]:
                    shipping = bool.TryParse(declared, out bool value)
                        ? value
                        : throw new FormatException(
                            $"declares shipping as \"{declared}\". The shipping record is true or false.");
                    break;
                case ["asset", string asset]:
                    sources.Add(new Request(Relative(asset)));
                    break;
                default:
                    throw new FormatException($"holds the line \"{line}\", which is no kind of line this build reads.");
            }
        }

        return new BuildRequests(root, tools, shipping, sources);
    }

    // Every path a message names is relative to the project, the working directory, so a build log
    // and a document's provenance carry no machine's own layout.
    internal static string Relative(string path) =>
        System.IO.Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
}
