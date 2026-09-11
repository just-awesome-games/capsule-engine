using System.Collections.Immutable;
using System.Reflection;
using Capsule.Assets;
using Capsule.Rendering;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// What the compiler will and will not read out of a BMFont source, and what a game compiles
// against once it has: the whole font is literal data by the time the game runs.
public sealed class FontGeneratorTests
{
    private const string Info = "info face=\"Test\" size=12 bold=0 padding=0,0,0,0 spacing=1,1 outline=0";

    private const string Common = "common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=1 packed=0 alphaChnl=0";

    private const string Page = "page id=0 file=\"menu.png\"";

    private const string CharA = "char id=65 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 xadvance=5 page=0 chnl=15";

    private const string CharB = "char id=66 x=4 y=0 width=4 height=6 xoffset=0 yoffset=2 xadvance=6 page=0 chnl=15";

    private const string Kerning = "kerning first=65 second=66 amount=-2";

    [Fact]
    public void AFont_CompilesIntoAMemberCarryingItsMetricsGlyphsAndKerning()
    {
        BitmapFont font = Font(
            Compiled(
                ("fonts/menu.fnt", Source(Info, Common, Page, "chars count=2", CharA, CharB, "kernings count=1", Kerning)),
                ("fonts/menu.png", null)),
            "Menu");

        Assert.Equal(15, font.LineHeight);
        Assert.Equal(12, font.Base);
        Assert.Equal([TextureHandle.FontPage("menu", ".png")], font.Pages.ToArray());
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
                ("fonts/menu.fnt", Source(Info, Common, Page, "chars count=9", CharA, CharB, Kerning)),
                ("fonts/menu.png", null)),
            "Menu");

        Assert.True(font.TryGetGlyph('B', out _));
        Assert.Equal(-2, font.GetKerning('A', 'B'));
    }

    [Fact]
    public void MultiplePages_AreCarriedInPageIdOrder()
    {
        BitmapFont font = Font(
            Compiled(
                ("fonts/menu.fnt", Source(
                    Info,
                    "common lineHeight=15 base=12 scaleW=32 scaleH=32 pages=2 packed=0",
                    "page id=1 file=\"menu_1.png\"",
                    Page,
                    CharA,
                    "char id=66 x=4 y=0 width=4 height=6 xoffset=0 yoffset=2 xadvance=6 page=1 chnl=15")),
                ("fonts/menu.png", null),
                ("fonts/menu_1.png", null)),
            "Menu");

        Assert.Equal(
            [TextureHandle.FontPage("menu", ".png"), TextureHandle.FontPage("menu-1", ".png")],
            font.Pages.ToArray());
        Assert.True(font.TryGetGlyph('B', out Glyph b));
        Assert.Equal(1, b.Page);
    }

    // A directory under the fonts root is a class of its own and a set of its own, and every class
    // above it carries what is beneath.
    [Fact]
    public void ADirectoryUnderTheFontsRoot_BecomesANestedClassAndItsOwnSet()
    {
        Assembly game = Probed(
            "CapsuleAssets.Fonts.All.Length, CapsuleAssets.Fonts.Ui.All.Length",
            ("fonts/ui/menu.fnt", Source(Info, Common, Page, CharA)),
            ("fonts/ui/menu.png", null),
            ("fonts/body.fnt", Source(Info, Common, Page, CharA)),
            ("fonts/menu.png", null));

        Assert.Equal([2, 1], Counts(game));
        Assert.Equal(15, Font(game, "Body").LineHeight);
        Assert.Equal(15, Font(game, "Ui", "Menu").LineHeight);
    }

    // The pages are keyed under the font's own directory, so a nested font's page resolves beside
    // it rather than at the fonts root, and the handle carries the extension the build shipped.
    [Fact]
    public void APage_IsNamedByTheKeyItShipsAt()
    {
        BitmapFont font = Font(
            Compiled(
                ("fonts/ui/menu.fnt", Source(Info, Common, "page id=0 file=\"Menu.PNG\"", CharA)),
                ("fonts/ui/menu.PNG", null)),
            "Ui",
            "Menu");

        Assert.Equal([TextureHandle.FontPage("ui/menu", ".PNG")], font.Pages.ToArray());
    }

    // A page the build does not ship — excluded as development-only, or never authored — would
    // carry a handle that finds no file at run time, so the font fails the build instead.
    [Fact]
    public void APageTheGameDoesNotShip_FailsTheBuild()
    {
        Diagnostic refused = Assert.Single(Refused(("fonts/menu.fnt", Source(Info, Common, Page, CharA))));

        Assert.Equal("CAP023", refused.Id);
        Assert.Contains("assets/fonts/menu", refused.GetMessage(), StringComparison.Ordinal);

        // The page is the game's to author, not one line's defect, so the '.fnt' itself is named.
        Assert.Equal("fonts/menu.fnt", refused.Location.GetLineSpan().Path);
        Assert.Equal(0, refused.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void APagePathThatIsNoKey_FailsTheBuild()
    {
        Assert.Equal(
            "CAP023",
            Assert.Single(Refused(("fonts/menu.fnt", Source(Info, Common, "page id=0 file=\"../outside.png\"", CharA)))).Id);
    }

    [Theory]

    // The three flavours the format has; only the text one is read.
    [InlineData("BMF", "binary")]
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
        Diagnostic refused = Assert.Single(Refused(("fonts/menu.fnt", Malformed(lines)), ("fonts/menu.png", null)));

        Assert.Equal("CAP022", refused.Id);
        Assert.Contains(defect, refused.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("fonts/menu.fnt", refused.Location.GetLineSpan().Path);
    }

    // A defect one line carries is anchored to that line, so the build error navigates to it.
    [Theory]
    [InlineData("@common|@page|@a|char id=66 x=0 y=0 width=4 height=6 xoffset=1 yoffset=2 page=0", 3)]
    [InlineData("@common|@page|@a|@b|kerning first=67 second=66 amount=-2", 4)]
    [InlineData("@common|@page|@a|page id=1 file=\"menu.tga\"", 3)]
    [InlineData("common lineHeight=0 base=12 scaleW=32 scaleH=32 pages=1|@page|@a", 0)]
    public void ADefectOnOneLine_IsAnchoredToThatLine(string lines, int line)
    {
        Diagnostic refused = Assert.Single(Refused(("fonts/menu.fnt", Malformed(lines)), ("fonts/menu.png", null)));

        FileLinePositionSpan at = refused.Location.GetLineSpan();

        Assert.Equal("fonts/menu.fnt", at.Path);
        Assert.Equal(line, at.StartLinePosition.Line);
    }

    // The compiler reads text; a font it cannot decode is named rather than silently skipped.
    [Fact]
    public void AFontTheCompilerCannotRead_FailsTheBuild()
    {
        Assert.Equal("CAP022", Assert.Single(Refused(("fonts/menu.fnt", null))).Id);
    }

    [Theory]
    [InlineData("fonts/fonts.fnt", "CAP018")]
    [InlineData("fonts/all.fnt", "CAP018")]
    [InlineData("fonts/01-menu.fnt", "CAP017")]
    public void AKeyTheGeneratedClassesCannotDeclare_FailsTheBuild(string asset, string diagnostic)
    {
        Assert.Equal(diagnostic, Assert.Single(Refused((asset, Source(Info, Common, Page, CharA)), ("fonts/menu.png", null))).Id);
    }

    [Fact]
    public void TwoFontsThatBecomeOneIdentifier_FailTheBuild()
    {
        Assert.Equal(
            "CAP016",
            Assert.Single(Refused(
                ("fonts/main-menu.fnt", Source(Info, Common, Page, CharA)),
                ("fonts/main_menu.fnt", Source(Info, Common, Page, CharA)),
                ("fonts/menu.png", null))).Id);
    }

    // The class is declared whatever the game authored, so a call site naming the domain compiles
    // and the last font removed leaves an empty set rather than a missing class.
    [Fact]
    public void AGameShippingNoFont_StillDeclaresTheDomain()
    {
        Assert.Equal([0], Counts(Probed("CapsuleAssets.Fonts.All.Length", ("textures/hero.png", null))));
    }

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

    private static Assembly Compiled(params (string Path, string? Content)[] assets) =>
        Probed(string.Empty, assets);

    // A game naming the sets it was given: a span cannot come back through reflection, so each set
    // is counted where a call site counts it.
    private static Assembly Probed(string sets, params (string Path, string? Content)[] assets)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(
            "using Capsule.Assets.Generated;\n\nnamespace Game;\n\npublic static class Probe\n{\n"
            + "    public static int[] Counts => new int[] { " + sets + " };\n}\n",
            logic: true,
            assets);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        return GeneratorHarness.Loaded(compiled);
    }

    private static int[] Counts(Assembly game) =>
        (int[])game.GetType("Game.Probe")!.GetProperty("Counts")!.GetValue(null)!;

    private static IEnumerable<Diagnostic> Refused(params (string Path, string? Content)[] assets) =>
        GeneratorHarness.Errors(GeneratorHarness.CompileWithSources(logic: true, assets).Diagnostics);

    // CapsuleAssets.Fonts.<path>, as the game names it.
    private static BitmapFont Font(Assembly game, params string[] path)
    {
        Type declaring = game.GetType("Capsule.Assets.Generated.CapsuleAssets")!.GetNestedType("Fonts")!;

        for (int i = 0; i < path.Length - 1; i++)
        {
            declaring = declaring.GetNestedType(path[i])!;
        }

        return (BitmapFont)declaring.GetProperty(path[path.Length - 1])!.GetValue(null)!;
    }
}
