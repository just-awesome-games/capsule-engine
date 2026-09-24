using System.Text.RegularExpressions;
using Capsule.Build.Atlases;
using Capsule.Tests.Documents;
using StbImageSharp;

namespace Capsule.Tests.Build;

/// <summary>
/// The atlas pass inside one run: what it ships in place of its members, what its stamp lets a
/// second run skip, the texels a page holds, and the manifests it refuses.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AtlasToolTests
{
    private const string Page = ToolWorkspace.Out + "/assets/atlases/game.0.png";

    private const string Map = ToolWorkspace.Out + "/assets/atlases.json";

    private const string Manifest = "Assets/Atlases/game.atlas.json";

    [Fact]
    public void ARunWithAnAtlas_ShipsItsPagesAndMapInsteadOfItsMembers()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/Actors/Hero.png", 4, 3);
        workspace.WritePng("Assets/Textures/loose.png", 2, 2);
        workspace.Write(Manifest, """{ "textures": ["textures/actors/*"] }""");

        workspace.Succeed();

        Assert.Equal(["atlases.json", "atlases/game.0.png", "textures/loose.png"], workspace.Shipped);

        string map = File.ReadAllText(Map);
        Assert.Contains("\"textures/actors/hero\"", map, StringComparison.Ordinal);
        Assert.DoesNotContain("\"textures/loose\"", map, StringComparison.Ordinal);
        Assert.Contains("\"page\": \"atlases/game.0\"", map, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRunOverUnchangedInputs_RepacksNothingAndAChangedMemberRepacks()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["**"] }""");

        workspace.Succeed();
        DateTime packed = File.GetLastWriteTimeUtc(Page);
        string map = File.ReadAllText(Map);

        workspace.Succeed();

        Assert.Contains("atlas atlases/game: up to date", workspace.Output, StringComparison.Ordinal);
        Assert.Equal(packed, File.GetLastWriteTimeUtc(Page));
        Assert.Equal(map, File.ReadAllText(Map));

        workspace.WritePng("Assets/Textures/hero.png", 4, 3, seed: 7);
        workspace.Succeed();

        Assert.Contains("atlas atlases/game: 1 texture(s) packed", workspace.Output, StringComparison.Ordinal);
    }

    // The runtime finds a packed texture by its handle, which the map spells in lower case, so an
    // extension the author capitalized is lowered everywhere the build writes it.
    [Fact]
    public void ATextureSpelledInUpperCase_IsNamedAndShippedInLowerCase()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/Hero.PNG", 4, 3);
        workspace.WritePng("Assets/Textures/Loose.PNG", 2, 2);
        workspace.Write(Manifest, """{ "textures": ["textures/hero"] }""");

        workspace.Succeed();

        Assert.Contains("TextureHandle(\"textures/hero\", \".png\")", workspace.Generated, StringComparison.Ordinal);
        Assert.Equal(["atlases.json", "atlases/game.0.png", "textures/loose.png"], workspace.Shipped);
    }

    // Removing the last atlas leaves nothing of it shipping.
    [Fact]
    public void ARunWithoutTheAtlas_ShipsItsMembersAndNoPage()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["**"] }""");
        workspace.Succeed();

        File.Delete(Manifest);
        workspace.Succeed();

        Assert.Equal(["textures/hero.png"], workspace.Shipped);
    }

    [Fact]
    public void ATextureTwoAtlasesMatch_FailsNamingBothManifests()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["**"] }""");
        workspace.Write("Assets/Atlases/other.atlas.json", """{ "textures": ["textures/hero"] }""");

        Assert.Contains(
            "Assets/Atlases/other.atlas.json: packs 'Assets/Textures/hero.png', which 'Assets/Atlases/game.atlas.json' already packs",
            workspace.Fail(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void APatternMatchingNothing_FailsNamingThePattern()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["textures/hero", "props/**"] }""");

        Assert.Contains("\"props/**\" matches no texture", workspace.Fail(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATextureLargerThanAPage_FailsNamingTheTexture()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/wide.png", 63, 2);
        workspace.Write(Manifest, """{ "textures": ["**"], "maxSize": 64 }""");

        Assert.Contains("Assets/Textures/wide.png: is 63x2", workspace.Fail(), StringComparison.Ordinal);
    }

    // A 2x2 member placed at (1, 1) on a 6x6 page: its own texels land where placed, its edges and
    // corners repeat one texel outward, and nothing else changes.
    [Fact]
    public void Blit_ExtrudesEveryEdgeAndCornerAndLeavesTheRestUntouched()
    {
        byte[] red = [255, 0, 0, 255];
        byte[] green = [0, 255, 0, 255];
        byte[] blue = [0, 0, 255, 128];
        byte[] clear = [0, 0, 0, 0];
        ImageResult member = new() { Width = 2, Height = 2, Data = [.. red, .. green, .. blue, .. clear] };
        byte[] page = new byte[6 * 6 * 4];

        AtlasStep.Blit(page, 6, member, 1, 1);

        (int X, int Y, byte[] Expected)[] texels =
        [
            (1, 1, red), (2, 1, green), (1, 2, blue), (2, 2, clear),
            (0, 0, red), (1, 0, red), (0, 1, red),
            (2, 0, green), (3, 0, green), (3, 1, green),
            (0, 2, blue), (0, 3, blue), (1, 3, blue),
            (3, 2, clear), (2, 3, clear), (3, 3, clear),
            (4, 0, clear), (4, 4, clear), (0, 4, clear), (5, 5, clear),
        ];
        foreach ((int x, int y, byte[] expected) in texels)
        {
            Assert.Equal(expected, page[(((y * 6) + x) * 4)..((((y * 6) + x) * 4) + 4)]);
        }
    }

    [Theory]
    [InlineData("biomes/forest/**", "biomes/forest/deep/oak", true)]
    [InlineData("biomes/forest/**", "biomes/forest", false)]
    [InlineData("actors/*", "actors/bosses/wyrm", false)]
    [InlineData("**/oak", "oak", true)]
    public void AGlob_MatchesWholeSegmentsAndAnyDepthOnlyThroughDoubleStar(string pattern, string key, bool expected)
    {
        Regex match = AtlasStep.ReadManifest($$"""{ "textures": ["{{pattern}}"] }""").Patterns[0].Match;

        Assert.Equal(expected, match.IsMatch(key));
    }

    [Theory]
    [InlineData("""{ "textures": ["**"], "padding": 4 }""", "padding")]
    [InlineData("""{ "maxSize": 2048 }""", "no textures")]
    [InlineData("""{ "textures": ["**"], "maxSize": 3000 }""", "maxSize of 3000")]
    [InlineData("""{ "textures": ["Actors/*.png"] }""", "*.png")]
    public void ReadManifest_RefusesADocumentOutsideTheFormatNamingTheDefect(string json, string defect)
    {
        FormatException error = Assert.Throws<FormatException>(() => AtlasStep.ReadManifest(json));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }
}
