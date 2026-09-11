using Capsule.Build;

namespace Capsule.Tests.Documents;

/// <summary>
/// One process per build, so what one run reports is the whole authoring plane's state: a defect in
/// one kind of source never hides a defect in another, and the stamp the build's incrementality
/// rests on appears only when every step succeeded.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class BuildRunTests
{
    private const string Out = "obj/capsule";

    private const string Stamp = Out + "/build.stamp";

    private const string Authored = """
        { "formatVersion": 5,
          "entities": [
            { "id": 1, "type": "tile-map", "x": 0, "y": 0,
              "properties": { "tileSize": 16, "width": 2, "height": 1,
                              "texture": "terrain.png", "columns": 4,
                              "tileTypes": [ { "type": "empty" }, { "type": "ground", "cell": 0 } ],
                              "tiles": [0, 1] } } ],
          "nextEntityId": 2 }
        """;

    [Fact]
    public void ARunOverAValidManifest_DerivesEveryKindAndStampsItselfLast()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("Assets/Scenes/Stage1/Room01.scene.json", Authored);

        int exitCode = Run(workspace, TextWriter.Null, "scenes|Stage1/Room01||Assets/Scenes/Stage1/Room01.scene.json");

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Out + "/scenes/stage-1/room-01.scene.json"));
        Assert.Equal(
            [$"assets/scenes/stage-1/room-01.scene.json|{Out}/scenes/stage-1/room-01.scene.json"],
            File.ReadAllLines(Out + "/scene-content.txt"));
        Assert.Empty(File.ReadAllLines(Out + "/shipped-assets.txt"));
        Assert.True(File.Exists(Stamp));
    }

    [Fact]
    public void ARunWithADefectInTwoKinds_ReportsBothAndLeavesNoStamp()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("Assets/Scenes/broken.scene.json", """{ "formatVersion": 5, "entities": [ { "id": 1, "type": "tile-map", "x": 0, "y": 0 } ], "nextEntityId": 2 }""");
        workspace.Write("Assets/Audio/hum.wav", "not a wav");

        StringWriter error = new();
        int exitCode = Run(
            workspace,
            error,
            "scenes|broken||Assets/Scenes/broken.scene.json",
            "audio|hum|.wav|Assets/Audio/hum.wav");

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Scenes/broken.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Assets/Audio/hum.wav", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Stamp));
    }

    [Fact]
    public void ARunWhoseManifestDeclaresATileSize_FailsAGridAuthoredAtAnother()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        workspace.Write("Assets/Scenes/hall.scene.json", Authored);

        StringWriter error = new();
        int exitCode = Run(workspace, error, "tile-size|8", "scenes|hall||Assets/Scenes/hall.scene.json");

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Scenes/hall.scene.json", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Stamp));
    }

    private static int Run(SceneDocumentFixtures.Workspace workspace, TextWriter error, params string[] manifest)
    {
        string requests = workspace.Write("requests.txt", string.Join('\n', manifest));

        return BuildRun.Run(requests, Out, TextWriter.Null, error);
    }
}
