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
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "formatVersion": 6, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");
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

    // An output a run would write unchanged keeps its timestamp, so a run that changes nothing
    // recompiles nothing and copies nothing.
    [Fact]
    public void ARunThatChangesNoOutput_LeavesItsTimestampAlone()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Textures/hero.png", string.Empty);
        workspace.Write("Assets/Scenes/room.scene.json", """{"formatVersion": 6, "entities": [], "nextEntityId": 1}""");
        workspace.Succeed();
        string[] outputs = ["CapsuleAssets.g.cs", "capsule-scenes.txt", "assets/textures/hero.png", "assets/scenes/room.scene.json"];
        DateTime[] written = [.. outputs.Select(static output => File.GetLastWriteTimeUtc(Path.Combine(ToolWorkspace.Out, output)))];

        workspace.Write("Assets/Scenes/room.scene.json", """{"formatVersion": 6, "entities": [], "nextEntityId": 1}""");
        workspace.Succeed();

        Assert.Equal(written, outputs.Select(static output => File.GetLastWriteTimeUtc(Path.Combine(ToolWorkspace.Out, output))));
    }
}
