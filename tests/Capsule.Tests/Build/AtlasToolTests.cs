using Capsule.Build.Textures;
using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// The atlas pass inside one run: what it ships in place of its members, which edits repack which
/// atlas, and the texels a page holds.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AtlasToolTests
{
    private const string Map = ToolWorkspace.Out + "/assets/textures.json";

    private const string Game = """{ "texture": { "atlas": "game" } }""";

    [Fact]
    public void ARunWithAnAtlas_ShipsItsPagesAndMapInsteadOfItsMembers()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/Actors/Hero.png", 4, 3);
        workspace.WritePng("Assets/Textures/loose.png", 2, 2);
        workspace.Write("Assets/Textures/Actors/.config.json", Game);
        workspace.Write("Assets/Atlases/game.atlas.json", "{}");

        workspace.Succeed();

        Assert.Equal(["atlases/game.0.png", "textures.json", "textures/loose.png"], workspace.Shipped);
        Assert.Equal("""{"textures":{"textures/actors/hero":{"page":"atlases/game.0","x":1,"y":1}}}""", File.ReadAllText(Map));
    }

    [Fact]
    public void AnEditedMemberOrAtlasFile_RepacksOnlyItsAtlas()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/hero.png", 4, 3);
        workspace.WritePng("Assets/tiles.png", 4, 3);
        workspace.Write("Assets/hero.png.config.json", """{ "atlas": "actors" }""");
        workspace.Write("Assets/tiles.png.config.json", """{ "atlas": "world" }""");
        workspace.Write("Assets/actors.atlas.json", "{}");
        workspace.Write("Assets/world.atlas.json", "{}");
        workspace.Succeed();

        workspace.WritePng("Assets/hero.png", 4, 3, seed: 7);
        workspace.Succeed();

        Assert.Equal(["atlases: Assets/actors.atlas.json"], workspace.Built);

        workspace.Write("Assets/world.atlas.json", """{ "maxSize": 2048 }""");
        workspace.Succeed();

        Assert.Equal(["atlases: Assets/world.atlas.json"], workspace.Built);
    }

    [Fact]
    public void AConfigEditMovingATextureBetweenAtlases_RepacksBoth()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/hero.png", 4, 3);
        workspace.WritePng("Assets/tiles.png", 4, 3);
        workspace.WritePng("Assets/crate.png", 4, 3);
        workspace.Write("Assets/.config.json", """{ "texture": { "atlas": "actors" } }""");
        workspace.Write("Assets/tiles.png.config.json", """{ "atlas": "world" }""");
        workspace.Write("Assets/actors.atlas.json", "{}");
        workspace.Write("Assets/world.atlas.json", "{}");
        workspace.Succeed();

        workspace.Write("Assets/crate.png.config.json", """{ "atlas": "world" }""");
        workspace.Succeed();

        Assert.Equal(["atlases: Assets/actors.atlas.json", "atlases: Assets/world.atlas.json"], workspace.Built);
    }

    // A page holds one format and one sampling, so a draw never splits for sampling inside a page.
    [Fact]
    public void MembersOfOneAtlasDifferingInFormatOrSampling_LandOnDifferentPages()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/hero.png", 4, 3);
        workspace.WriteGreyPng("Assets/glow.png", 4, 3);
        workspace.WritePng("Assets/tiles.png", 4, 3);
        workspace.Write("Assets/.config.json", Game);
        workspace.Write("Assets/game.atlas.json", "{}");
        workspace.Write("Assets/glow.png.config.json", """{ "format": "r8" }""");
        workspace.Write("Assets/tiles.png.config.json", """{ "sampling": "point" }""");

        workspace.Succeed();

        Assert.Equal(["atlases/game.0.png", "atlases/game.1.png", "atlases/game.2.png", "textures.json"], workspace.Shipped);
        Assert.Equal(
            """{"pages":{"atlases/game.1":{"sampling":"point"},"atlases/game.2":{"format":"r8"}},"textures":{"glow":{"page":"atlases/game.2","x":1,"y":1},"hero":{"page":"atlases/game.0","x":1,"y":1},"tiles":{"page":"atlases/game.1","x":1,"y":1}}}""",
            File.ReadAllText(Map));
    }

    // The runtime finds a packed texture by its handle, which the map spells in lower case, so an
    // extension the author capitalized is lowered everywhere the build writes it.
    [Fact]
    public void ATextureSpelledInUpperCase_IsNamedAndShippedInLowerCase()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/Hero.PNG", 4, 3);
        workspace.WritePng("Assets/Textures/Loose.PNG", 2, 2);
        workspace.Write("Assets/Textures/Hero.PNG.config.json", """{ "atlas": "game" }""");
        workspace.Write("Assets/Game.atlas.json", "{}");

        workspace.Succeed();

        Assert.Contains("TextureHandle(\"textures/hero\", \".png\")", workspace.Generated, StringComparison.Ordinal);
        Assert.Equal(["atlases/game.0.png", "textures.json", "textures/loose.png"], workspace.Shipped);
    }

    // Removing the last atlas leaves nothing of it shipping, the map included.
    [Fact]
    public void ARunWithoutTheAtlas_ShipsItsMembersAndNoPage()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/hero.png", 4, 3);
        workspace.Write("Assets/.config.json", Game);
        workspace.Write("Assets/game.atlas.json", "{}");
        workspace.Succeed();

        File.Delete("Assets/.config.json");
        File.Delete("Assets/game.atlas.json");
        workspace.Succeed();

        Assert.Equal(["textures/hero.png"], workspace.Shipped);
    }

    // The atlas file's maxSize bounds its pages, and a member that cannot fit with its border fails.
    [Fact]
    public void ATextureLargerThanItsAtlasMaxSize_FailsNamingTheTexture()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Textures/wide.png", 15, 1);
        workspace.Write("Assets/.config.json", Game);
        workspace.Write("Assets/game.atlas.json", """{ "maxSize": 16 }""");

        Assert.Contains("Assets/Textures/wide.png: is 15x1, which with its 1-texel border exceeds the 16-texel page of the atlas \"game\"", workspace.Fail(), StringComparison.Ordinal);
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
        Texels member = new([.. red, .. green, .. blue, .. clear], 2, 2, 4);
        byte[] page = new byte[6 * 6 * 4];

        TexturePixels.Blit(page, 6, member, 1, 1, border: 1);

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
}
