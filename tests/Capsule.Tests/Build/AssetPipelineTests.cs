using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Capsule.Build;
using Capsule.Build.Caching;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// One process per build, so what one run reports is the whole authoring plane's state: a defect in
/// one kind of source never hides a defect in another, and what ships is exactly what this run
/// derived or reused.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AssetPipelineTests
{
    [Fact]
    public void ARunWithADefectInTwoKinds_ReportsBoth()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "entities": [ { "id": 1, "x": 0, "y": 0 } ] }""");
        workspace.Write("Assets/Audio/hum.wav", "not a wav");

        string errors = workspace.Fail();

        Assert.Contains("Assets/Scenes/broken.scene.json", errors, StringComparison.Ordinal);
        Assert.Contains("has no type", errors, StringComparison.Ordinal);
        Assert.Contains("Assets/Audio/hum.wav", errors, StringComparison.Ordinal);
    }

    // An argument the build does not read is a mismatch between the targets and the tool, never one
    // to skip.
    [Fact]
    public void AnArgumentOfNoKnownKind_IsAUsageError()
    {
        using ToolWorkspace workspace = new();

        Assert.Equal(2, workspace.Run("--requests", "build-requests.txt"));
        Assert.StartsWith("usage:", workspace.Errors, StringComparison.Ordinal);
    }

    // A project may name its asset root with a trailing separator.
    [Fact]
    public void AnAssetRootEndingInASeparator_NamesEachSourceAsWithoutOne()
    {
        using ToolWorkspace workspace = new() { Assets = "Assets/" };
        workspace.Write("Assets/Audio/hum.wav", "not a wav");

        Assert.StartsWith("Assets/Audio/hum.wav: ", workspace.Fail(), StringComparison.Ordinal);
    }

    // A source deleted since the last run stops shipping, with no clean in between.
    [Fact]
    public void ASourceRemovedSinceTheLastRun_NoLongerShips()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Textures/hero.png", string.Empty);
        workspace.Write("Assets/Textures/gone.png", string.Empty);
        workspace.Succeed();

        File.Delete("Assets/Textures/gone.png");
        workspace.Succeed();

        Assert.Equal(["textures/hero.png"], workspace.Shipped);
        Assert.DoesNotContain("Gone", workspace.Generated, StringComparison.Ordinal);
    }

    // A directory holding a .capsuleignore is development-only. A shipping run leaves out every file
    // under it and every file an importer wrote at a path under it, and any other run ships them.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AMarkedDirectory_ShipsOnlyOutsideAShippingRun(bool shipping)
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/hero.png", string.Empty);
        workspace.Write("Assets/Dev/.capsuleignore", string.Empty);
        workspace.Write("Assets/Dev/scratch.png", string.Empty);
        workspace.Write("Assets/Dev/room.note", """{"entities": []}""");
        workspace.Configure = static build => build.AddImporter(new NoteImporter());

        workspace.Succeed(shipping ? ["--shipping"] : []);

        string[] expected = shipping ? ["hero.png"] : ["dev/room.scene.json.gz", "dev/scratch.png", "hero.png"];
        Assert.Equal(expected, workspace.Shipped);
    }

    // An output a run would write unchanged keeps its timestamp, so a run that changes nothing
    // recompiles nothing and copies nothing.
    [Fact]
    public void ARunThatChangesNoOutput_LeavesItsTimestampAlone()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Textures/hero.png", string.Empty);
        workspace.Write("Assets/Scenes/room.scene.json", """{"entities": []}""");
        workspace.Succeed();
        string[] outputs = ["CapsuleAssets.g.cs", "assets/textures/hero.png", "assets/scenes/room.scene.json.gz"];
        DateTime[] written = [.. outputs.Select(static output => File.GetLastWriteTimeUtc(Path.Combine(ToolWorkspace.Out, output)))];

        workspace.Write("Assets/Scenes/room.scene.json", """{"entities": []}""");
        workspace.Succeed();

        Assert.Equal(written, outputs.Select(static output => File.GetLastWriteTimeUtc(Path.Combine(ToolWorkspace.Out, output))));
    }

    // Two runs over one output directory never interleave, as an IDE's build and a command-line build
    // would. A run started while another holds the directory touches nothing until it is released.
    [Fact]
    public async Task ARunStartedWhileAnotherHoldsTheOutput_WaitsForItThenBuilds()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Textures/hero.png", string.Empty);
        Directory.CreateDirectory(ToolWorkspace.Out);
        TaskCompletionSource waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        workspace.Watch = line =>
        {
            if (line.StartsWith("capsule: waiting for another build", StringComparison.Ordinal))
            {
                waiting.TrySetResult();
            }
        };
        Task<int> run;

        using (new FileStream(Path.Combine(ToolWorkspace.Out, AssetPipeline.LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            run = Task.Run(() => workspace.Run());
            await Task.WhenAny(waiting.Task, run).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(waiting.Task.IsCompleted, "The run finished without waiting for the held directory.");
            Assert.Empty(workspace.Shipped);
        }

        Assert.Equal(0, await run);
        Assert.Equal(["textures/hero.png"], workspace.Shipped);
    }

    // An importer's output is part of the asset tree, whatever kind of file it writes. The sheet names
    // a texture authored under Assets/.
    [Theory]
    [InlineData("Textures/hero.png", "")]
    [InlineData("Sprites/prop.sheet.json", """{ "formatVersion": 1, "texture": "sprites/p.png", "frames": [ { "name": "a", "x": 0, "y": 0, "width": 1, "height": 1 } ] }""")]
    [InlineData("Scenes/room.scene.json", """{"entities": []}""")]
    public void AnImportedFile_BuildsAsTheSameFileAuthored(string below, string text)
    {
        (string[] Shipped, string Generated) authored = BuiltFrom("Assets/" + below, text, output: null);
        string extension = below[below.IndexOf('.', StringComparison.Ordinal)..];
        (string[] Shipped, string Generated) imported = BuiltFrom("Assets/" + below[..^extension.Length] + ".note", text, extension);

        Assert.Equal(authored.Shipped, imported.Shipped);
        Assert.Equal(authored.Generated, imported.Generated.Replace(ToolWorkspace.Imported, "Assets/", StringComparison.Ordinal));
    }

    // Derivations run concurrently, and nothing a run leaves or reports depends on which finished
    // first. The defects include a member no atlas can decode and two imports writing one output.
    [Fact]
    public void ARunAcrossCores_LeavesAndReportsExactlyWhatARunOnOneCoreDoes()
    {
        using ToolWorkspace workspace = new();
        const string Scene = """{"entities": []}""";
        for (int i = 0; i < 8; i++)
        {
            workspace.WritePng($"Assets/Textures/t{i}.png", 4, 4, seed: i + 1);
            workspace.WritePng($"Assets/Packed/p{i}.png", 3, 3, seed: i + 1);
            workspace.Write($"Assets/Scenes/n{i}.note", Scene);
        }

        workspace.Write("Assets/Packed/.config.json", """{ "texture": { "atlas": "game" } }""");
        workspace.Write("Assets/game.atlas.json", "{}");
        string[] defects =
        [
            workspace.Write("Assets/Packed/bad.png", "not a png"),
            workspace.Write("Assets/Scenes/b.note", NoteImporter.Broken),
            workspace.Write("Assets/Scenes/n3.memo", Scene),
        ];

        string errors = SameAtEitherDegree(workspace);
        Assert.Contains("Assets/Packed/bad.png: ", errors, StringComparison.Ordinal);
        Assert.Contains("Assets/Scenes/b.note: ", errors, StringComparison.Ordinal);
        Assert.Contains("\"imported/Scenes/n3.scene.json\" is written by 'Assets/Scenes/n3.memo' and 'Assets/Scenes/n3.note'.", errors, StringComparison.Ordinal);

        foreach (string defect in defects)
        {
            File.Delete(defect);
        }

        Assert.Empty(SameAtEitherDegree(workspace));
    }

    // Asserts a cold run at degree 1 reports and leaves exactly what one at degree 8 does, and returns its errors.
    private static string SameAtEitherDegree(ToolWorkspace workspace)
    {
        (string Errors, string Output, string[] Files) one = Cold(workspace, parallelism: 1);
        (string Errors, string Output, string[] Files) many = Cold(workspace, parallelism: 8);

        Assert.Equal(one.Errors, many.Errors);
        Assert.Equal(one.Output, many.Output);
        Assert.Equal(one.Files, many.Files);

        return one.Errors;
    }

    // A run at the given degree from no output: what it reported, and every file it left with its
    // content. The times in the cache file are left out.
    private static (string Errors, string Output, string[] Files) Cold(ToolWorkspace workspace, int parallelism)
    {
        if (Directory.Exists(ToolWorkspace.Out))
        {
            Directory.Delete(ToolWorkspace.Out, recursive: true);
        }

        workspace.Configure = build =>
        {
            build.Parallelism = parallelism;

            return build.AddImporter(new NoteImporter()).AddImporter(new NoteImporter(".memo"));
        };
        workspace.Run();

        string[] files =
        [
            .. Directory.EnumerateFiles(ToolWorkspace.Out, "*", SearchOption.AllDirectories)
                .Where(static path => Path.GetFileName(path) != AssetPipeline.LockFile)
                .Select(static path =>
                {
                    string content = Path.GetFileName(path) == DerivationCache.FileName
                        ? Regex.Replace(File.ReadAllText(path), "\"(started|written)\":\"[^\"]*\"", string.Empty)
                        : Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

                    return Path.GetRelativePath(ToolWorkspace.Out, path).Replace('\\', '/') + " " + content;
                })
                .Order(StringComparer.Ordinal),
        ];

        return (workspace.Errors, workspace.Output, files);
    }

    private static (string[] Shipped, string Generated) BuiltFrom(string path, string text, string? output)
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Sprites/p.png", 1, 1);
        workspace.Write(path, text);
        workspace.Configure = build => build.AddImporter(new NoteImporter(output: output ?? ".scene.json"));
        workspace.Succeed();

        return (workspace.Shipped, workspace.Generated);
    }
}
