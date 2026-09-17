using System.Text.RegularExpressions;
using Capsule.Build;
using Capsule.Build.Atlases;
using StbImageSharp;

namespace Capsule.Tests.Documents;

/// <summary>
/// The atlas pass inside one run: what it ships in place of its members, what its stamp lets a
/// second run skip, the texels a page holds, and the manifests it refuses.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AtlasToolTests
{
    private const string Out = "obj/capsule";

    private const string Stamp = Out + "/build.stamp";

    private const string Page = Out + "/atlases/game.0.png";

    private const string Manifest = "Assets/Atlases/game.atlas.json";

    [Fact]
    public void ARunWithAnAtlas_ShipsItsPagesAndMapInsteadOfItsMembers()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        WritePng(workspace, "Assets/Textures/Actors/Hero.png", 4, 3);
        WritePng(workspace, "Assets/Textures/loose.png", 2, 2);
        workspace.Write(Manifest, """{ "textures": ["actors/*"] }""");

        StringWriter error = new();
        int exitCode = Run(workspace, error, Requests(["Actors/Hero", "loose"]));

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.True(File.Exists(Stamp));

        string[] shipped = File.ReadAllLines(Out + "/shipped-assets.txt");
        Assert.Contains("assets/textures/loose.png|Assets/Textures/loose.png", shipped);
        Assert.DoesNotContain(shipped, static line => line.Contains("actors/hero", StringComparison.Ordinal));
        Assert.Contains(shipped, static line => line.StartsWith("assets/textures/game.0.png|", StringComparison.Ordinal) && line.EndsWith("/atlases/game.0.png", StringComparison.Ordinal));
        Assert.Contains(shipped, static line => line.StartsWith("assets/textures/atlases.json|", StringComparison.Ordinal));

        string map = File.ReadAllText(Out + "/atlases/atlases.json");
        Assert.Contains("\"actors/hero\"", map, StringComparison.Ordinal);
        Assert.DoesNotContain("\"loose\"", map, StringComparison.Ordinal);
        Assert.Contains("\"page\": \"game.0\"", map, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRunOverUnchangedInputs_RepacksNothingAndAChangedMemberRepacks()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        WritePng(workspace, "Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["**"] }""");
        string[] requests = Requests(["hero"]);

        Assert.Equal(0, Run(workspace, TextWriter.Null, requests));
        DateTime packed = File.GetLastWriteTimeUtc(Page);
        string map = File.ReadAllText(Out + "/atlases/atlases.json");

        StringWriter output = new();
        Assert.Equal(0, BuildRun.Run(workspace.Write("requests.txt", string.Join('\n', requests)), Out, output, TextWriter.Null));

        Assert.Contains("atlas game: up to date", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(packed, File.GetLastWriteTimeUtc(Page));
        Assert.Equal(map, File.ReadAllText(Out + "/atlases/atlases.json"));

        WritePng(workspace, "Assets/Textures/hero.png", 4, 3, seed: 7);
        output = new StringWriter();
        Assert.Equal(0, BuildRun.Run(workspace.Write("requests.txt", string.Join('\n', requests)), Out, output, TextWriter.Null));

        Assert.Contains("atlas game: 1 texture(s) packed", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATextureTwoAtlasesMatch_FailsNamingBothManifests()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        WritePng(workspace, "Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["**"] }""");
        workspace.Write("Assets/Atlases/other.atlas.json", """{ "textures": ["hero"] }""");

        StringWriter error = new();
        int exitCode = Run(workspace, error, [.. Requests(["hero"]), "atlases|other||Assets/Atlases/other.atlas.json"]);

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Atlases/other.atlas.json: packs 'Assets/Textures/hero.png', which 'Assets/Atlases/game.atlas.json' already packs", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Stamp));
    }

    [Fact]
    public void APatternMatchingNothing_FailsNamingThePattern()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        WritePng(workspace, "Assets/Textures/hero.png", 4, 3);
        workspace.Write(Manifest, """{ "textures": ["hero", "props/**"] }""");

        StringWriter error = new();
        int exitCode = Run(workspace, error, Requests(["hero"]));

        Assert.Equal(1, exitCode);
        Assert.Contains("\"props/**\" matches no texture", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Stamp));
    }

    [Fact]
    public void ATextureLargerThanAPage_FailsNamingTheTexture()
    {
        using SceneDocumentFixtures.Workspace workspace = new();
        WritePng(workspace, "Assets/Textures/wide.png", 63, 2);
        workspace.Write(Manifest, """{ "textures": ["**"], "maxSize": 64 }""");

        StringWriter error = new();
        int exitCode = Run(workspace, error, Requests(["wide"]));

        Assert.Equal(1, exitCode);
        Assert.Contains("Assets/Textures/wide.png: is 63x2", error.ToString(), StringComparison.Ordinal);
        Assert.False(File.Exists(Stamp));
    }

    // A 2x2 member — red, green over blue, clear — placed at (1, 1) on a 6x6 page: its own texels
    // land where placed, its edges and corners repeat one texel outward, and nothing else changes.
    [Fact]
    public void Blit_ExtrudesEveryEdgeAndCornerAndLeavesTheRestUntouched()
    {
        byte[] red = [255, 0, 0, 255];
        byte[] green = [0, 255, 0, 255];
        byte[] blue = [0, 0, 255, 128];
        byte[] clear = [0, 0, 0, 0];
        ImageResult member = new() { Width = 2, Height = 2, Data = [.. red, .. green, .. blue, .. clear] };
        byte[] page = new byte[6 * 6 * 4];

        AtlasTool.Blit(page, 6, member, 1, 1);

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
        Regex match = AtlasTool.ReadManifest($$"""{ "textures": ["{{pattern}}"] }""").Patterns[0].Match;

        Assert.Equal(expected, match.IsMatch(key));
    }

    [Theory]
    [InlineData("""{ "textures": ["**"], "padding": 4 }""", "padding")]
    [InlineData("""{ "maxSize": 2048 }""", "no textures")]
    [InlineData("""{ "textures": ["**"], "maxSize": 3000 }""", "maxSize of 3000")]
    [InlineData("""{ "textures": ["Actors/*.png"] }""", "*.png")]
    public void ReadManifest_RefusesADocumentOutsideTheFormatNamingTheDefect(string json, string defect)
    {
        FormatException error = Assert.Throws<FormatException>(() => AtlasTool.ReadManifest(json));

        Assert.Contains(defect, error.Message, StringComparison.Ordinal);
    }

    private static string[] Requests(string[] texturePaths) =>
        [.. texturePaths.Select(static path => $"textures|{path}|.png|Assets/Textures/{path}.png"), $"atlases|game||{Manifest}"];

    private static int Run(SceneDocumentFixtures.Workspace workspace, TextWriter error, string[] manifest)
    {
        string requests = workspace.Write("requests.txt", string.Join('\n', manifest));

        return BuildRun.Run(requests, Out, TextWriter.Null, error);
    }

    // An opaque PNG whose texels are a fixed function of position and seed, so a one-byte change is
    // a different file.
    private static void WritePng(SceneDocumentFixtures.Workspace workspace, string name, int width, int height, int seed = 1)
    {
        byte[] texels = new byte[width * height * 4];
        for (int i = 0; i < width * height; i++)
        {
            texels[i * 4] = (byte)(i * seed);
            texels[(i * 4) + 3] = 255;
        }

        using MemoryStream png = new();
        AtlasTool.Encode(texels, width, height, png);
        File.WriteAllBytes(workspace.Write(name, string.Empty), png.ToArray());
    }
}
