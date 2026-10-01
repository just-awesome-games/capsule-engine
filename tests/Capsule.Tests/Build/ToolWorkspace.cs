using System.Reflection;
using Capsule.Build;
using Capsule.Build.Shaders;
using Capsule.Build.Textures;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// A game's project directory as the build sees it: files authored under <c>Assets/</c>, one run on the
/// command line the targets would pass, and what that run left under <c>obj/capsule/</c>.
/// </summary>
internal sealed class ToolWorkspace : IDisposable
{
    internal const string Out = "obj/capsule";

    internal const string Imported = Out + "/imported/";

    /// <summary>The shader tools this test project restored, which every run names as the targets do.</summary>
    internal static readonly ShaderTools ShaderTools = new(Metadata("CapsuleDxc"), Metadata("CapsuleSpirvCross"));

    private readonly SceneDocumentFixtures.Workspace _workspace = new();

    /// <summary>The asset root every run names, relative to the workspace.</summary>
    internal string Assets { get; set; } = "Assets";

    /// <summary>What the game's build project configures on every run.</summary>
    internal Func<CapsuleBuild, CapsuleBuild> Configure { get; set; } = static build => build;

    /// <summary>Called with each line the run writes to its output stream, as the run writes it.</summary>
    internal Action<string>? Watch { get; set; }

    /// <summary>What the last run wrote to its error stream.</summary>
    internal string Errors { get; private set; } = string.Empty;

    /// <summary>What the last run wrote to its output stream.</summary>
    internal string Output { get; private set; } = string.Empty;

    /// <summary>The <c>CapsuleAssets</c> source the last successful run generated.</summary>
    internal string Generated => File.ReadAllText(Path.Combine(Out, "CapsuleAssets.g.cs"));

    /// <summary>The name of every derivation the last run ran, as <c>textures: Assets/hero.png</c>, in the order it ran them.</summary>
    internal string[] Built =>
        [.. Output.Split(Environment.NewLine)
            .Select(static line => line.Split(": built ", 2))
            .Where(static parts => parts.Length == 2)
            .Select(static parts => $"{parts[0]}: {parts[1]}")];

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
        PngWriter.Write(texels, width, height, 4, png);

        return Write(name, png.ToArray());
    }

    /// <summary>An 8-bit greyscale PNG, as an r8 texture is authored.</summary>
    internal string WriteGreyPng(string name, int width, int height)
    {
        byte[] values = [.. Enumerable.Range(0, width * height).Select(static i => (byte)(i * 17))];
        using MemoryStream png = new();
        PngWriter.Write(values, width, height, 1, png);

        return Write(name, png.ToArray());
    }

    /// <summary>Runs the build over <c>Assets/</c>, with <paramref name="options"/> between the paths and the shader tools the targets always pass.</summary>
    /// <returns>The run's exit code.</returns>
    internal int Run(params string[] options)
    {
        StringWriter output = new WatchedWriter(Watch);
        StringWriter error = new();
        int exitCode = Configure(CapsuleBuild.Configure(["--assets", Path.GetFullPath(Assets), "--out", Out, .. options, "--shader-tools", ShaderTools.Dxc, ShaderTools.SpirvCross])).Run(output, error);
        Output = output.ToString();
        Errors = error.ToString();

        return exitCode;
    }

    /// <summary>Runs, and asserts the run succeeded.</summary>
    internal void Succeed(params string[] options) =>
        Assert.True(Run(options) == 0, Errors);

    /// <summary>Runs, asserts the run failed, and hands back what it reported.</summary>
    internal string Fail(params string[] options)
    {
        Assert.Equal(1, Run(options));

        return Errors;
    }

    /// <summary>The value of the test assembly's metadata <paramref name="key"/>, which the test project sets.</summary>
    internal static string Metadata(string key) =>
        typeof(ToolWorkspace).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == key).Value!;

    public void Dispose() => _workspace.Dispose();

    private sealed class WatchedWriter(Action<string>? watch) : StringWriter
    {
        public override void WriteLine(string? value)
        {
            base.WriteLine(value);
            watch?.Invoke(value ?? string.Empty);
        }
    }
}
