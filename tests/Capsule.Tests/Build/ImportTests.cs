using Capsule.Build;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// An importer's sources as the build hands them over: each one imported in place of being read, and
/// again only when an input changed, its outputs keyed as though authored, and its defects reported
/// against it.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class ImportTests
{
    internal const string Scene = """{"entities": []}""";

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

    // A file an import read or probed is an input of that import alone, and a probed file that appears is an edit.
    [Theory]
    [InlineData("Assets/Parts/shared.part", "Assets/Scenes/a.note")]
    [InlineData("Assets/Parts/unread.part")]
    [InlineData("Assets/Parts/absent.part", "Assets/Scenes/d.note")]
    public void AnEditedFile_ImportsAgainExactlyTheSourcesThatReadOrProbedIt(string edited, params string[] imported)
    {
        using ToolWorkspace workspace = Including();
        workspace.Succeed();

        workspace.Write(edited, """{"size": [8, 8], "entities": []}""");
        workspace.Succeed();

        Assert.Equal(imported, Imports(workspace));
    }

    // The importer runs again and reports the missing file as it reports any defect.
    [Fact]
    public void ADeletedFileAnImportRead_ImportsItsSourceAgain()
    {
        using ToolWorkspace workspace = Including();
        workspace.Succeed();

        File.Delete("Assets/Parts/shared.part");
        string errors = workspace.Fail();

        Assert.Equal($"Assets/Scenes/a.note: {NoteImporter.Missing}{Environment.NewLine}", errors);
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

        Assert.Equal(shipping ? 0 : 1, workspace.Run(shipping ? ["--shipping"] : []));
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

    // An importer's own test runs it with no build. Every path it read or probed is an input, even one
    // that was missing or whose read threw.
    [Fact]
    public void AStandaloneContext_CapturesOutputsAndEveryPathItReadOrProbed()
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/Scenes/a.note", NoteImporter.Include + "Assets/Scenes/gone.part");
        workspace.Write("Assets/Scenes/d.note", NoteImporter.Optional + "Assets/Parts/absent.part");
        AssetImportContext including = new("Assets/Scenes/a.note", "Assets");
        AssetImportContext probing = new(Path.GetFullPath("Assets/Scenes/d.note"), "Assets") { TileSize = 16 };

        Assert.Throws<FormatException>(() => new NoteImporter().Import(including));
        new NoteImporter().Import(probing);

        Assert.Equal(["Assets/Scenes/a.note", "Assets/Scenes/gone.part"], including.Inputs);
        Assert.Equal(["Assets/Scenes/d.note", "Assets/Parts/absent.part"], probing.Inputs);
        (string assetPath, byte[] contents) = Assert.Single(probing.Outputs);
        Assert.Equal("Scenes/d.scene.json", assetPath);
        Assert.Equal(Scene, System.Text.Encoding.UTF8.GetString(contents));
        Assert.False(Directory.Exists(ToolWorkspace.Out));
    }

    // Two notes that each include a part, a third that includes none, a fourth that probes for a missing part, and a part nothing reads.
    private static ToolWorkspace Including()
    {
        ToolWorkspace workspace = new() { Configure = static build => build.AddImporter(new NoteImporter()) };
        workspace.Write("Assets/Parts/shared.part", Scene);
        workspace.Write("Assets/Parts/other.part", Scene);
        workspace.Write("Assets/Parts/unread.part", Scene);
        workspace.Write("Assets/Scenes/a.note", NoteImporter.Include + "Assets/Parts/shared.part");
        workspace.Write("Assets/Scenes/b.note", NoteImporter.Include + "Assets/Parts/other.part");
        workspace.Write("Assets/Scenes/c.note", Scene);
        workspace.Write("Assets/Scenes/d.note", NoteImporter.Optional + "Assets/Parts/absent.part");

        return workspace;
    }

    private static string[] Imports(ToolWorkspace workspace) =>
        [.. workspace.Built.Where(static built => built.StartsWith("import: ", StringComparison.Ordinal)).Select(static built => built["import: ".Length..])];
}

/// <summary>
/// Imports each source it claims by writing its text at its own path with <paramref name="output"/> for
/// an extension. A source reading <see cref="Include"/> and a path writes that file's text instead. A
/// source reading <see cref="Optional"/> and a path does so when the file exists, and writes an
/// empty scene otherwise.
/// </summary>
internal sealed class NoteImporter(string extension = ".note", string output = ".scene.json") : IAssetImporter
{
    /// <summary>A source whose text is this has a format defect, reported with this message.</summary>
    internal const string Broken = "is broken.";

    internal const string Include = "include ";

    internal const string Optional = "optional ";

    /// <summary>What a source reports when the file it includes is gone.</summary>
    internal const string Missing = "includes a file that is gone.";

    public IReadOnlyList<string> Extensions { get; } = [extension];

    public void Import(AssetImportContext context)
    {
        string text = context.ReadAllText(context.SourcePath);
        if (text.StartsWith(Include, StringComparison.Ordinal))
        {
            try
            {
                text = context.ReadAllText(text[Include.Length..]);
            }
            catch (FileNotFoundException)
            {
                throw new FormatException(Missing);
            }
        }
        else if (text.StartsWith(Optional, StringComparison.Ordinal))
        {
            string path = text[Optional.Length..];
            text = context.Exists(path) ? context.ReadAllText(path) : ImportTests.Scene;
        }

        context.Write(Path.ChangeExtension(context.AssetPath, output), text == Broken ? throw new FormatException(Broken) : text);
    }
}
