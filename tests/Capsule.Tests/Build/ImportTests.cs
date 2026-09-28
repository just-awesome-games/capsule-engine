using Capsule.Build;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// An importer's sources as the build hands them over: each one imported in place of being read, its
/// outputs keyed as though authored, and its defects reported against it.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ImportTests
{
    private const string Scene = """{"formatVersion": 7, "entities": [], "nextEntityId": 1}""";

    // What a source imported to on an earlier run is gone, not just left unrequested.
    [Fact]
    public void AnOutputWhoseSourceIsGone_DoesNotSurviveTheNextRun()
    {
        using ToolWorkspace workspace = new();
        workspace.Configure = static build => build.AddImporter(new NoteImporter());
        workspace.Write("Assets/Scenes/old.note", Scene);
        workspace.Succeed();

        File.Move("Assets/Scenes/old.note", "Assets/Scenes/new.note");
        workspace.Succeed();

        Assert.False(File.Exists(ToolWorkspace.Imported + "Scenes/old.scene.json"));
        Assert.Equal(["scenes/new.scene.json.gz"], workspace.Shipped);
    }

    [Fact]
    public void ASourceWithAFormatDefect_FailsByNameAndTheOthersStillBuild()
    {
        using ToolWorkspace workspace = new();
        workspace.Configure = static build => build.AddImporter(new NoteImporter());
        workspace.Write("Assets/Scenes/broken.note", NoteImporter.Broken);
        workspace.Write("Assets/Scenes/room.note", Scene);

        string errors = workspace.Fail();

        Assert.Equal($"Assets/Scenes/broken.note: {NoteImporter.Broken}{Environment.NewLine}", errors);
        Assert.True(File.Exists(ToolWorkspace.Out + "/assets/scenes/room.scene.json.gz"));
    }

    // A shipping run never imports what it would not ship. A defect under a development-only directory
    // fails only an ordinary run.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADefectUnderAMarkedDirectory_FailsOnlyAnOrdinaryRun(bool shipping)
    {
        using ToolWorkspace workspace = new();
        workspace.Configure = static build => build.AddImporter(new NoteImporter());
        workspace.Write("Assets/Dev/.capsuleignore", string.Empty);
        workspace.Write("Assets/Dev/broken.note", NoteImporter.Broken);

        Assert.Equal(shipping ? 0 : 1, workspace.Run($"shipping|{shipping}"));
    }

    // An import that would overwrite another's output names both sources. One that claims an authored
    // file's key names the file and the output, as any two sources of one key do.
    [Theory]
    [InlineData("Assets/Scenes/room.memo", "Assets/Scenes/room.note")]
    [InlineData("Assets/Scenes/room.scene.json", ToolWorkspace.Imported + "Scenes/room.scene.json")]
    public void TwoSourcesOfOneScene_FailTheBuildNamingBoth(string first, string second)
    {
        using ToolWorkspace workspace = new();
        workspace.Configure = static build => build.AddImporter(new NoteImporter()).AddImporter(new NoteImporter(".memo"));
        workspace.Write(first, Scene);
        workspace.Write("Assets/Scenes/room.note", Scene);

        string errors = workspace.Fail();

        Assert.Contains(first, errors, StringComparison.Ordinal);
        Assert.Contains(second, errors, StringComparison.Ordinal);
    }

    // A claimed extension a Capsule type reads would take its sources away from the engine, and two
    // importers of one extension would each expect to be the one reading it.
    [Theory]
    [InlineData(".png", "an extension Capsule reads itself")]
    [InlineData(".json", "an extension Capsule reads itself")]
    [InlineData(".NOTE", "Add one importer per source extension")]
    public void AnOverlappingClaim_FailsConfigurationNamingTheFix(string extension, string message)
    {
        CapsuleBuild build = CapsuleBuild.Configure([]).AddImporter(new NoteImporter());

        ArgumentException refused = Assert.Throws<ArgumentException>(() => build.AddImporter(new NoteImporter(extension)));

        Assert.Contains(message, refused.Message, StringComparison.Ordinal);
    }
}

/// <summary>Imports each source it claims by writing its text at its own path with <paramref name="output"/> for an extension.</summary>
internal sealed class NoteImporter(string extension = ".note", string output = ".scene.json") : IAssetImporter
{
    /// <summary>A source whose text is this has a format defect, reported with this message.</summary>
    internal const string Broken = "is broken.";

    public IReadOnlyList<string> Extensions { get; } = [extension];

    public void Import(AssetImportContext context)
    {
        string text = File.ReadAllText(context.SourcePath);

        context.Write(Path.ChangeExtension(context.AssetPath, output), text == Broken ? throw new FormatException(Broken) : text);
    }
}
