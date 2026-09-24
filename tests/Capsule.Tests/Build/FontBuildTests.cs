using System.Collections.Immutable;
using System.Reflection;
using Capsule.Assets;
using Capsule.Rendering;
using Capsule.Tests.Documents;
using Capsule.Tests.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Build;

// What the build will and will not read out of a BMFont source, and what a game compiles against
// once it has: the whole font is literal data by the time the game runs.
[Collection(SceneWorkspaceCollection.Name)]
public sealed class FontBuildTests
{
    private const string Info = "info face=\"Test\" size=12 bold=0 padding=0,0,0,0 spacing=1,1 outline=0";

    private const string Common = "common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=1 packed=0 alphaChnl=0";

    private const string Page = "page id=0 file=\"menu.png\"";

    private const string CharA = "char id=65 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 xadvance=5 page=0 chnl=15";

    private const string CharB = "char id=66 x=4 y=0 width=4 height=6 xoffset=0 yoffset=2 xadvance=6 page=0 chnl=15";

    private const string Kerning = "kerning first=65 second=66 amount=-2";

    private static readonly ImmutableArray<MetadataReference> References =
        GeneratorHarness.Referenced(null, typeof(BitmapFont).Assembly, typeof(object).Assembly);

    [Fact]
    public void AFont_CompilesIntoAMemberCarryingItsMetricsGlyphsAndKerning()
    {
        BitmapFont font = Font(
            Compiled(
                ("Fonts/menu.fnt", Source(Info, Common, Page, "chars count=2", CharA, CharB, "kernings count=1", Kerning)),
                ("Fonts/menu.png", null)),
            "Fonts",
            "MenuFont");

        Assert.Equal(15, font.LineHeight);
        Assert.Equal(12, font.Baseline);
        Assert.Equal([new TextureHandle("fonts/menu", ".png")], font.Pages.ToArray());
        Assert.True(font.TryGetGlyph('A', out Glyph a));
        Assert.Equal(new Glyph('A', 0, new TextureRegion(0, 0, 4, 6), 1, 2, 5), a);
        Assert.Equal(-2, font.GetKerning('A', 'B'));
    }

    // The counts are informational: two chars behind a "count=9" line is two chars.
    [Fact]
    public void ADeclaredCountThatDisagreesWithTheLines_IsIgnored()
    {
        BitmapFont font = Font(
            Compiled(
                ("Fonts/menu.fnt", Source(Info, Common, Page, "chars count=9", CharA, CharB, Kerning)),
                ("Fonts/menu.png", null)),
            "Fonts",
            "MenuFont");

        Assert.True(font.TryGetGlyph('B', out _));
        Assert.Equal(-2, font.GetKerning('A', 'B'));
    }

    [Fact]
    public void MultiplePages_AreCarriedInPageIdOrder()
    {
        BitmapFont font = Font(
            Compiled(
                ("Fonts/menu.fnt", Source(
                    Info,
                    "common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=2 packed=0",
                    "page id=1 file=\"menu_1.png\"",
                    Page,
                    CharA,
                    "char id=66 x=4 y=0 width=4 height=6 xoffset=0 yoffset=2 xadvance=6 page=1 chnl=15")),
                ("Fonts/menu.png", null),
                ("Fonts/menu_1.png", null)),
            "Fonts",
            "MenuFont");

        Assert.Equal(
            [new TextureHandle("fonts/menu", ".png"), new TextureHandle("fonts/menu-1", ".png")],
            font.Pages.ToArray());
        Assert.True(font.TryGetGlyph('B', out Glyph b));
        Assert.Equal(1, b.Page);
    }

    // A font may live anywhere under Assets/, beside its pages, which are ordinary textures however
    // the font spelled them.
    [Fact]
    public void AFontAnywhere_NamesItsPagesAsTheTexturesBesideIt()
    {
        using ToolWorkspace workspace = Authored(
            ("Fonts/ui/menu.fnt", Source(Info, Common, Page, CharA)),
            ("Fonts/ui/menu.png", null),
            ("Menus/body.fnt", Source(Info, Common, "page id=0 file=\"BODY.png\"", CharA)),
            ("Menus/Body.png", null));
        workspace.Succeed();

        Assembly game = Loaded(workspace.Generated);
        Assert.Equal(15, Font(game, "Fonts", "Ui", "MenuFont").LineHeight);
        Assert.Equal([new TextureHandle("menus/body", ".png")], Font(game, "Menus", "BodyFont").Pages.ToArray());
        Assert.Equal(["fonts/ui/menu.png", "menus/body.png"], workspace.Shipped);
        Assert.Contains("TextureHandle BodyTexture", workspace.Generated, StringComparison.Ordinal);
    }

    // A glyph's region is in its page's own texels, so no atlas packs a page, however wide its glob.
    [Fact]
    public void NoAtlas_PacksAFontsPage()
    {
        using ToolWorkspace workspace = Authored(("Fonts/menu.fnt", Source(Info, Common, Page, CharA)));
        workspace.WritePng("Assets/Fonts/menu.png", 32, 32);
        workspace.WritePng("Assets/hero.png", 4, 4);
        workspace.Write("Assets/game.atlas.json", """{ "textures": ["**"] }""");
        workspace.Succeed();

        Assert.Equal(["atlases.json", "fonts/menu.png", "game.0.png"], workspace.Shipped);
    }

    // A page the build does not ship would carry a handle that finds no file at run time, so the
    // font fails the build instead.
    [Theory]
    [InlineData("menu.png")]
    [InlineData("../outside.png")]
    public void APageTheGameDoesNotAuthor_FailsTheBuild(string page) =>
        Assert.Contains(
            $"Assets/Fonts/menu.fnt: names page \"{page}\"",
            Refused(("Fonts/menu.fnt", Source(Info, Common, $"page id=0 file=\"{page}\"", CharA))),
            StringComparison.Ordinal);

    // Text the build cannot decode is a defect, not a replacement character in a skipped line.
    [Fact]
    public void AFontThatIsNoUtf8_FailsTheBuild()
    {
        using ToolWorkspace workspace = Authored(("Fonts/menu.png", null));
        workspace.Write("Assets/Fonts/menu.fnt", [.. System.Text.Encoding.UTF8.GetBytes(Source(Info, Common, Page, CharA)), 0xFF]);

        Assert.Contains("Assets/Fonts/menu.fnt: ", workspace.Fail(), StringComparison.Ordinal);
    }

    [Theory]

    // The three flavours the format has; only the text one is read.
    [InlineData("BMF", "binary")]
    [InlineData("<?xml version=\"1.0\"?>\n<font/>", "XML")]

    // The page a glyph is cut from, and the rectangle it is cut at.
    [InlineData("common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=1 packed=1|page id=0 file=\"menu.png\"|char id=65 x=0 y=0 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=0", "packed")]
    [InlineData("@common|page id=0 file=\"menu.tga\"|char id=65 x=0 y=0 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=0", ".png")]
    [InlineData("@common|@page|char id=65 x=0 y=0 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=3", "page 3")]
    [InlineData("@common|@page|char id=65 x=30 y=0 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=0", "32x32 page")]
    [InlineData("@common|@page|char id=65 x=0 y=30 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=0", "32x32 page")]
    [InlineData("@common|@page|char id=65 x=-1 y=0 width=4 height=6 xoffset=0 yoffset=0 xadvance=5 page=0", "32x32 page")]

    // Two ints that each parse can still sum past what one holds; a wrapped sum would read as a
    // rectangle inside the page.
    [InlineData("@common|@page|char id=65 x=2147483647 y=0 width=2147483647 height=6 xoffset=0 yoffset=0 xadvance=5 page=0", "32x32 page")]
    [InlineData("@common|@page|@a|@a", "65 twice")]
    [InlineData("@common|@page|@a|kerning first=65 second=66 amount=-2", "no glyph")]
    [InlineData("common lineHeight=0 base=12 scaleW=32 scaleH=32 pages=1|@page|@a", "lineHeight")]
    [InlineData("common lineHeight=-4 base=12 scaleW=32 scaleH=32 pages=1|@page|@a", "lineHeight")]
    [InlineData("@common|@a", "no page")]

    // A field the font is measured by has to be there and to parse: read as zero, a missing width
    // bakes an invisible glyph and a missing xadvance collapses the line's spacing.
    [InlineData("common lineHeight=15 base=12 scaleH=32 pages=1|@page|@a", "scaleW")]
    [InlineData("common lineHeight=15 base=12 scaleW=oops scaleH=32 pages=1|@page|@a", "scaleW")]
    [InlineData("common lineHeight=15 base=12 scaleW=32 scaleH=32|@page|@a", "pages")]
    [InlineData("@common|page file=\"menu.png\"|@a", "id")]
    [InlineData("@common|page id=0|@a", "file")]
    [InlineData("@common|@page|char id=65 x=0 y=0 height=6 xoffset=1 yoffset=2 xadvance=5 page=0", "width")]
    [InlineData("@common|@page|char id=65 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 page=0", "xadvance")]
    [InlineData("@common|@page|char x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 xadvance=5 page=0", "id")]
    [InlineData("@common|@page|char id=65 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 xadvance=99999999999 page=0", "xadvance")]
    [InlineData("@common|@page|@a|@b|kerning first=65 second=66", "amount")]
    public void AFontTheBuildCannotRead_FailsTheBuildNamingTheDefect(string lines, string defect)
    {
        string refused = Refused(("Fonts/menu.fnt", Malformed(lines)), ("Fonts/menu.png", null));

        Assert.Contains("Assets/Fonts/menu.fnt: ", refused, StringComparison.Ordinal);
        Assert.Contains(defect, refused, StringComparison.Ordinal);
    }

    // A defect one line carries names that line.
    [Theory]
    [InlineData("@common|@page|@a|char id=66 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 page=0", 4)]
    [InlineData("@common|@page|@a|@b|kerning first=67 second=66 amount=-2", 5)]
    [InlineData("@common|@page|@a|page id=1 file=\"menu.tga\"", 4)]
    [InlineData("common lineHeight=0 base=12 scaleW=32 scaleH=32 pages=1|@page|@a", 1)]
    public void ADefectOnOneLine_NamesThatLine(string lines, int line) =>
        Assert.Contains(
            $"Assets/Fonts/menu.fnt: line {line} ",
            Refused(("Fonts/menu.fnt", Malformed(lines)), ("Fonts/menu.png", null)),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("Fonts/menu-font/menu.fnt", "inside a generated class of that name")]
    [InlineData("Fonts/01-menu.fnt", "no C# name")]
    public void AKeyTheGeneratedClassesCannotDeclare_FailsTheBuild(string asset, string because) =>
        Assert.Contains(because, Refused((asset, Source(Info, Common, Page, CharA)), (asset[..(asset.LastIndexOf('/') + 1)] + "menu.png", null)), StringComparison.Ordinal);

    [Fact]
    public void TwoFontsThatBecomeOneIdentifier_FailTheBuild() =>
        Assert.Contains(
            "already claims",
            Refused(
                ("Fonts/main-menu.fnt", Source(Info, Common, Page, CharA)),
                ("Fonts/main_menu.fnt", Source(Info, Common, Page, CharA)),
                ("Fonts/menu.png", null)),
            StringComparison.Ordinal);

    private static string Source(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    // '@' names one of the good lines, so a case says only what it is testing.
    private static string Malformed(string lines) =>
        Source(
            [.. lines.Split('|').Select(static line => line switch
            {
                "@common" => Common,
                "@page" => Page,
                "@a" => CharA,
                "@b" => CharB,
                _ => line,
            })]);

    // Each file under Assets/. A null body is a page, which the build copies and never decodes.
    private static ToolWorkspace Authored(params (string Path, string? Content)[] files)
    {
        ToolWorkspace workspace = new();
        foreach ((string path, string? content) in files)
        {
            workspace.Write("Assets/" + path, content ?? string.Empty);
        }

        return workspace;
    }

    private static Assembly Compiled(params (string Path, string? Content)[] files)
    {
        using ToolWorkspace workspace = Authored(files);
        workspace.Succeed();

        return Loaded(workspace.Generated);
    }

    private static string Refused(params (string Path, string? Content)[] files)
    {
        using ToolWorkspace workspace = Authored(files);

        return workspace.Fail();
    }

    // The generated registry as the game runs it, so a member hands back what it declares.
    private static Assembly Loaded(string generated) =>
        GeneratorHarness.Loaded(CSharpCompilation.Create(
            "FontSpecs",
            [CSharpSyntaxTree.ParseText(generated)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)));

    // CapsuleAssets.<path>, as the game names it.
    private static BitmapFont Font(Assembly game, params string[] path)
    {
        Type declaring = game.GetType("Capsule.Generated.CapsuleAssets")!;

        for (int i = 0; i < path.Length - 1; i++)
        {
            declaring = declaring.GetNestedType(path[i])!;
        }

        return (BitmapFont)declaring.GetProperty(path[^1])!.GetValue(null)!;
    }
}
