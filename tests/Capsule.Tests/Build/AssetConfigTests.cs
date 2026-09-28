using Capsule.Tests.Documents;

namespace Capsule.Tests.Build;

/// <summary>
/// Config and atlas files inside one run: how each setting resolves down a chain of folder files and a
/// sidecar, what a member's summary says about it, and the files the build refuses.
/// </summary>
[Collection(SceneWorkspaceCollection.Name)]
public sealed class AssetConfigTests
{
    // The nearest file wins per setting, and an explicit default overrides an inherited value. The
    // nested .config.json also proves a dotfile below Assets/ is discovered.
    [Fact]
    public void EachSetting_ResolvesToTheNearestFileAndTheSummaryNamesIt()
    {
        using ToolWorkspace workspace = new();
        workspace.WritePng("Assets/Actors/hero.png", 4, 3);
        workspace.WritePng("Assets/Actors/foe.png", 4, 3);
        workspace.Write("Assets/.config.json", """{ "texture": { "atlas": "game", "sampling": "point" } }""");
        workspace.Write("Assets/Actors/.config.json", """{ "texture": { "sampling": "linear" } }""");
        workspace.Write("Assets/Actors/hero.png.config.json", """{ "atlas": false }""");
        workspace.Write("Assets/game.atlas.json", "{}");

        workspace.Succeed();

        Assert.Contains(
            """/// <summary><c>actors/foe.png</c>, with atlas "game" from <c>.config.json</c> and sampling "linear" from <c>Actors/.config.json</c>.</summary>""",
            workspace.Generated,
            StringComparison.Ordinal);
        Assert.Contains(
            """/// <summary><c>actors/hero.png</c>, with sampling "linear" from <c>Actors/.config.json</c>.</summary>""",
            workspace.Generated,
            StringComparison.Ordinal);
        Assert.Equal(["actors/hero.png", "atlases/game.0.png", "textures.json"], workspace.Shipped);
    }

    [Theory]
    [InlineData("Assets/.config.json", """{ "texture": { "padding": 2 } }""", "has an unknown kind, setting or value at $.texture.padding. A folder's .config.json is keyed by asset kind")]
    [InlineData("Assets/glow.png.config.json", """{ "sampling": "POINT" }""", "has an unknown kind, setting or value at $.sampling.")]
    [InlineData("Assets/glow.png.config.json", """{ "atlas": true }""", "has an unknown kind, setting or value at $.atlas. A sidecar holds its asset's settings, as { \"format\": \"r8\" }. A texture's settings are \"atlas\" (an atlas name or false)")]
    [InlineData("Assets/.config.json", """{ "texture": """, "is not valid JSON at line 1")]
    [InlineData("Assets/glow.png.config.json", """{ "atlas": null }""", "has a null at $.atlas. Omit a setting to inherit it, or write its default, such as false for \"atlas\".")]
    [InlineData("Assets/glow.config.json", """{ "format": "r8" }""", "configures \"glow\", and no asset file beside it is named that. A sidecar names its asset's whole file name, so rename it to \"glow.png.config.json\"")]
    [InlineData("Assets/hero.fx.config.json", "{}", "configures the shader \"hero.fx\", and a shader has no settings")]
    [InlineData("Assets/glow.png.config.json", """{ "atlas": "World" }""", "sets \"atlas\" to \"world\", and no \"world.atlas.json\" under Assets/ declares that atlas.")]
    [InlineData("Assets/spare.atlas.json", "{}", "declares the atlas \"spare\", and no texture's \"atlas\" setting names it.")]
    [InlineData("Assets/Other/Game.atlas.json", "{}", "declares the atlas \"game\", which 'Assets/Atlases/game.atlas.json' already declares.")]
    [InlineData("Assets/spare.atlas.json", """{ "textures": ["**"] }""", "has an unknown kind, setting or value at $.textures. An atlas file holds only the atlas's own settings")]
    [InlineData("Assets/spare.atlas.json", """{ "maxSize": 3000 }""", "sets \"maxSize\" to 3000. An atlas file holds only the atlas's own settings")]
    public void ADefectiveConfig_FailsNamingItsFile(string file, string text, string defect)
    {
        using ToolWorkspace workspace = new();
        workspace.Write("Assets/hero.fx", "not a shader");
        workspace.WritePng("Assets/glow.png", 2, 2);
        workspace.WritePng("Assets/Tiles/wall.png", 2, 2);
        workspace.Write("Assets/Tiles/wall.png.config.json", """{ "atlas": "game" }""");
        workspace.Write("Assets/Atlases/game.atlas.json", "{}");
        workspace.Write(file, text);

        Assert.Contains($"{file}: {defect}", workspace.Fail(), StringComparison.Ordinal);
    }
}
