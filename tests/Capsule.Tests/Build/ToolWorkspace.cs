using Capsule.Build;
using Capsule.Build.Atlases;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// A game's project directory as the build tool sees it: files authored under <c>Assets/</c>, one run
/// over the manifest the targets would write for them, and what that run left under
/// <c>obj/capsule/</c>.
/// </summary>
internal sealed class ToolWorkspace : IDisposable
{
    internal const string Out = "obj/capsule";

    private readonly SceneDocumentFixtures.Workspace _workspace = new();

    /// <summary>What the last run wrote to its error stream.</summary>
    internal string Errors { get; private set; } = string.Empty;

    /// <summary>What the last run wrote to its output stream.</summary>
    internal string Output { get; private set; } = string.Empty;

    /// <summary>The <c>CapsuleAssets</c> source the last successful run generated.</summary>
    internal string Generated => File.ReadAllText(Path.Combine(Out, "CapsuleAssets.g.cs"));

    /// <summary>Whether the last run stamped itself, which it does only when every step succeeded.</summary>
    internal bool Stamped => File.Exists(Path.Combine(Out, "build.stamp"));

    /// <summary>Every shipped path, as the game finds it below <c>assets/</c>.</summary>
    internal string[] Shipped =>
        Directory.Exists(Path.Combine(Out, "assets"))
            ? [.. Directory.EnumerateFiles(Path.Combine(Out, "assets"), "*", SearchOption.AllDirectories)
                .Select(static path => Path.GetRelativePath(Path.Combine(Out, "assets"), path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)]
            : [];

    internal string Write(string name, string text) => _workspace.Write(name, text);

    internal string Write(string name, byte[] bytes)
    {
        string path = _workspace.Write(name, string.Empty);
        File.WriteAllBytes(path, bytes);

        return path;
    }

    /// <summary>An opaque PNG whose texels are a fixed function of position and seed.</summary>
    internal string WritePng(string name, int width, int height, int seed = 1)
    {
        byte[] texels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            texels[i * 4] = (byte)(i * seed);
            texels[(i * 4) + 3] = 255;
        }

        using MemoryStream png = new();
        AtlasStep.Encode(texels, width, height, png);

        return Write(name, png.ToArray());
    }

    /// <summary>
    /// Runs the tool over every file under <c>Assets/</c>, as the targets hand them, plus
    /// <paramref name="lines"/>: options, or documents a module derived.
    /// </summary>
    /// <returns>The run's exit code.</returns>
    internal int Run(params string[] lines)
    {
        string[] authored = Directory.Exists("Assets")
            ? [.. Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(static path => "asset|" + Path.GetFullPath(path))]
            : [];
        string requests = Write("requests.txt", string.Join('\n', ["root|" + Path.GetFullPath("Assets"), .. lines, .. authored]));

        StringWriter output = new();
        StringWriter error = new();
        int exitCode = BuildRun.Run(requests, Out, output, error);
        Output = output.ToString();
        Errors = error.ToString();

        return exitCode;
    }

    /// <summary>Runs, and asserts the run succeeded.</summary>
    internal void Succeed(params string[] lines) =>
        Assert.True(Run(lines) == 0, Errors);

    /// <summary>Runs, asserts the run failed and left no stamp, and hands back what it reported.</summary>
    internal string Fail(params string[] lines)
    {
        Assert.Equal(1, Run(lines));
        Assert.False(Stamped);

        return Errors;
    }

    public void Dispose() => _workspace.Dispose();
}
