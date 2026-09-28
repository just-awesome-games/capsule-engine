using Capsule.Build;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// One process per build, so what one run reports is the whole authoring plane's state: a defect in
/// one kind of source never hides a defect in another, the stamp the build's incrementality rests on
/// appears only when every step succeeded, and what ships is exactly what this run derived.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class BuildRunTests
{
    [Fact]
    public void ARunWithADefectInTwoKinds_ReportsBothAndLeavesNoStamp()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "formatVersion": 7, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");
        workspace.Write("Assets/Audio/hum.wav", "not a wav");

        string errors = workspace.Fail();

        Assert.Contains("Assets/Scenes/broken.scene.json", errors, StringComparison.Ordinal);
        Assert.Contains("declares no properties", errors, StringComparison.Ordinal);
        Assert.Contains("Assets/Audio/hum.wav", errors, StringComparison.Ordinal);
    }

    // A manifest line the build does not read is a mismatch between the targets and the tool, never
    // a line to skip.
    [Fact]
    public void AManifestLineOfNoKnownKind_FailsTheRun()
    {
        using ToolWorkspace workspace = new();

        Assert.Contains("no kind of line", workspace.Fail("textures|hero|.png|Assets/Textures/hero.png"), StringComparison.Ordinal);
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
        workspace.Write("Assets/Dev/room.note", """{"formatVersion": 7, "entities": [], "nextEntityId": 1}""");
        workspace.Configure = static build => build.AddImporter(new NoteImporter());

        workspace.Succeed($"shipping|{shipping}");

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
        workspace.Write("Assets/Scenes/room.scene.json", """{"formatVersion": 7, "entities": [], "nextEntityId": 1}""");
        workspace.Succeed();
        string[] outputs = ["CapsuleAssets.g.cs", "assets/textures/hero.png", "assets/scenes/room.scene.json.gz"];
        DateTime[] written = [.. outputs.Select(static output => File.GetLastWriteTimeUtc(Path.Combine(ToolWorkspace.Out, output)))];

        workspace.Write("Assets/Scenes/room.scene.json", """{"formatVersion": 7, "entities": [], "nextEntityId": 1}""");
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

        using (new FileStream(Path.Combine(ToolWorkspace.Out, BuildRun.LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
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
    [InlineData("Scenes/room.scene.json", """{"formatVersion": 7, "entities": [], "nextEntityId": 1}""")]
    public void AnImportedFile_BuildsAsTheSameFileAuthored(string below, string text)
    {
        (string[] Shipped, string Generated) authored = BuiltFrom("Assets/" + below, text, output: null);
        string extension = below[below.IndexOf('.', StringComparison.Ordinal)..];
        (string[] Shipped, string Generated) imported = BuiltFrom("Assets/" + below[..^extension.Length] + ".note", text, extension);

        Assert.Equal(authored.Shipped, imported.Shipped);
        Assert.Equal(authored.Generated, imported.Generated.Replace(ToolWorkspace.Imported, "Assets/", StringComparison.Ordinal));
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
