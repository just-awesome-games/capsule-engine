using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using Capsule.Rendering;
using Capsule.Tests.Packaging;
using StbImageSharp;

namespace Capsule.Tests.Build;

// What a <name>.font.json bakes from its faces: the characters, metrics, kerning and pages of the bitmap
// font a game compiles against, and when a text edit bakes it again.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class FontBakeTests
{
    // A smooth monospaced face.
    private static readonly byte[] Mono = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Build", "Fixtures", "DejaVuSansMono.ttf"));

    // A proportional face that kerns, and carries letters the mono one lacks.
    private static readonly byte[] Sans = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Build", "Fixtures", "DejaVuSans.ttf"));

    // At em 12 the mono face measures as a hand bake of it does: a line 15 high, the baseline 12 down, and
    // 7 an advance.
    [Fact]
    public void AFace_BakesAtItsEm_LatinOneTheTablesCharactersAndTheReplacement()
    {
        using ToolWorkspace workspace = Workspace("""{ "face": "mono.ttf", "size": 12 }""", "Āb 日");
        workspace.Succeed();
        BitmapFont font = Body(workspace);

        Assert.Equal(15, font.LineHeight);
        Assert.Equal(12, font.Baseline);
        Assert.True(font.TryGetGlyph('A', out Glyph a));
        Assert.Equal(7, a.XAdvance);
        Assert.True(font.TryGetGlyph('é', out _));
        Assert.True(font.TryGetGlyph('Ā', out _));
        Assert.True(font.TryGetGlyph('�', out _));
        Assert.False(font.TryGetGlyph('日', out _));
        Assert.False(font.TryGetGlyph(173, out _));
        Assert.Contains("fonts/body.png", workspace.Shipped);
        Assert.DoesNotContain(workspace.Shipped, static path => path.EndsWith(".ttf", StringComparison.Ordinal) || path.EndsWith(".fnt", StringComparison.Ordinal));
    }

    [Fact]
    public void AFontOutOfTheTables_BakesItsOwnCharactersAlone()
    {
        using ToolWorkspace workspace = Workspace("""{ "face": "mono.ttf", "size": 12, "tables": false, "characters": "Ă" }""", "Ā");
        workspace.Succeed();
        BitmapFont font = Body(workspace);

        Assert.True(font.TryGetGlyph('Ă', out _));
        Assert.True(font.TryGetGlyph('é', out _));
        Assert.False(font.TryGetGlyph('Ā', out _));
    }

    // Ǆ is in the sans face alone. A, V kern by -131 of 2048 units, -2 pixels at em 24, where the mono
    // face kerns nothing.
    [Fact]
    public void AFallbackFace_SuppliesWhatTheFaceLacks_AndAFaceKernsItsLatinPairs()
    {
        using ToolWorkspace workspace = Workspace("""{ "face": "mono.ttf", "size": 12, "fallback": ["sans.ttf"], "characters": "Ǆ" }""", "A");
        workspace.Write("Assets/Fonts/title.font.json", """{ "face": "sans.ttf", "size": 24 }""");
        workspace.Succeed();
        Assembly game = ToolWorkspace.Loaded(workspace.Generated);

        BitmapFont body = Font(game, "BodyFont");
        Assert.True(body.TryGetGlyph('Ǆ', out _));
        Assert.Equal(15, body.LineHeight);
        Assert.Equal(0, body.GetKerning('A', 'V'));
        Assert.Equal(-2, Font(game, "TitleFont").GetKerning('A', 'V'));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Antialias_BlendsEdges_OrKeepsEveryPixelWhole(bool antialias)
    {
        using ToolWorkspace workspace = Workspace($$"""{ "face": "mono.ttf", "size": 12, "antialias": {{(antialias ? "true" : "false")}} }""", "A");
        workspace.Succeed();
        ImageResult page = ImageResult.FromMemory(File.ReadAllBytes(ToolWorkspace.Imported + "Fonts/body.png"), ColorComponents.Grey);

        Assert.Equal(antialias, page.Data.Any(static texel => texel is > 0 and < 255));
        Assert.Contains(page.Data, static texel => texel == 255);
    }

    [Theory]
    [InlineData("""{ "size": 12 }""", "names no face")]
    [InlineData("""{ "face": "mono.ttf" }""", "gives no size")]
    [InlineData("""{ "face": "gone.ttf", "size": 12 }""", "names the face \"gone.ttf\", and no file is at")]
    [InlineData("""{ "face": "notes.ttf", "size": 12 }""", "names the face \"notes.ttf\", which is no TrueType or OpenType font")]
    [InlineData("""{ "face": "cut.ttf", "size": 12 }""", "names the face \"cut.ttf\", which is no TrueType or OpenType font, or is cut short")]
    [InlineData("""{ "face": "mono.ttf", "size": 12, "weight": 700 }""", "has an unknown member at $.weight")]
    [InlineData("""{ "face": "mono.ttf", "size": 12, "fallback": [null] }""", "names the face \"\". Name each face as a .ttf or .otf file relative to this one.")]
    [InlineData("""{ "face": "a\u0000.ttf", "size": 12 }""", "Name each face as a .ttf or .otf file relative to this one.")]
    [InlineData("""{ "face": "index.ttf", "size": 12 }""", "names the face \"index.ttf\", which is no TrueType or OpenType font, or is cut short")]
    [InlineData("""{ "face": "/fonts/mono.ttf", "size": 12 }""", "names the face \"/fonts/mono.ttf\". Name each face as")]
    [InlineData("""{ "face": "short.ttf", "size": 12 }""", "names the face \"short.ttf\", which is no TrueType or OpenType font, or is cut short")]
    [InlineData("""{ "face": "mono.ttf", "size": 0 }""", "gives size 0. Give an em from 1 to 512 pixels.")]
    public void ADefectiveFont_IsRefusedByName(string font, string refusal)
    {
        using ToolWorkspace workspace = Workspace(font, "A");
        workspace.Write("Assets/Fonts/notes.ttf", "not a font");
        workspace.Write("Assets/Fonts/cut.ttf", Mono[..4096]);
        workspace.Write("Assets/Fonts/short.ttf", WithTableLength(Mono, "cmap", 0));
        workspace.Write("Assets/Fonts/index.ttf", WithTableLength(Mono, "loca", 0));

        string errors = workspace.Fail();

        Assert.Contains("Assets/Fonts/body.font.json: ", errors, StringComparison.Ordinal);
        Assert.Contains(refusal, errors, StringComparison.Ordinal);
    }

    // A font bakes again when the tables' characters change, and never for an edit that keeps them.
    [Fact]
    public void ATextEdit_BakesAgainOnlyWhenItChangesTheCharacters()
    {
        const string Baked = "import: Assets/Fonts/body.font.json";
        using ToolWorkspace workspace = Workspace("""{ "face": "mono.ttf", "size": 12 }""", "Āb");
        workspace.Succeed();
        Assert.Contains(Baked, workspace.Built);

        workspace.Write("Assets/Text/en.strings.json", Table("bĀ"));
        workspace.Succeed();
        Assert.DoesNotContain(Baked, workspace.Built);

        workspace.Write("Assets/Text/en.strings.json", Table("Ăb"));
        workspace.Succeed();
        Assert.Contains(Baked, workspace.Built);
    }

    // A table's schema key is no text.
    [Fact]
    public void NoFont_BakesASchemaKey()
    {
        using ToolWorkspace workspace = Workspace("""{ "face": "mono.ttf", "size": 12 }""", "b");
        workspace.Write("Assets/Text/en.strings.json", """{ "$schema": "Ā", "greeting": "b" }""");
        workspace.Succeed();

        Assert.False(Body(workspace).TryGetGlyph('Ā', out _));
    }

    // A string table an importer writes is text a font bakes, as the strings step reads it.
    [Fact]
    public void AnImportedStringTable_IsBaked()
    {
        using ToolWorkspace workspace = ToolWorkspace.Authored(("Fonts/body.font.json", """{ "face": "mono.ttf", "size": 12 }"""), ("Text/en.note", Table("Ā")));
        workspace.Write("Assets/Fonts/mono.ttf", Mono);
        workspace.Configure = static build => build.AddImporter(new NoteImporter(".note", ".strings.json"));
        workspace.Succeed();

        Assert.True(Body(workspace).TryGetGlyph('Ā', out _));
    }

    // The face with one table's declared length replaced.
    private static byte[] WithTableLength(byte[] face, string table, uint length)
    {
        byte[] edited = [.. face];
        int count = BinaryPrimitives.ReadUInt16BigEndian(edited.AsSpan(4));
        for (int record = 12; record < 12 + (count * 16); record += 16)
        {
            if (Encoding.ASCII.GetString(edited, record, 4) == table)
            {
                BinaryPrimitives.WriteUInt32BigEndian(edited.AsSpan(record + 12), length);
            }
        }

        return edited;
    }

    // Fonts/body.font.json over the two faces, and one string table holding text.
    private static ToolWorkspace Workspace(string font, string text)
    {
        ToolWorkspace workspace = ToolWorkspace.Authored(("Fonts/body.font.json", font), ("Text/en.strings.json", Table(text)));
        workspace.Write("Assets/Fonts/mono.ttf", Mono);
        workspace.Write("Assets/Fonts/sans.ttf", Sans);

        return workspace;
    }

    private static string Table(string text) => $$"""{ "greeting": "{{text}}" }""";

    private static BitmapFont Body(ToolWorkspace workspace) => Font(ToolWorkspace.Loaded(workspace.Generated), "BodyFont");

    private static BitmapFont Font(Assembly game, string name) =>
        (BitmapFont)game.GetType("Capsule.Generated.CapsuleAssets")!.GetNestedType("Fonts")!.GetProperty(name)!.GetValue(null)!;
}
