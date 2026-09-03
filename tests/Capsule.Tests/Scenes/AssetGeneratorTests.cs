using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

public sealed class AssetGeneratorTests
{
    [Fact]
    public void AnAsset_BecomesATypedHandleUnderItsDomain()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: true, "audio/footstep-stone.ogg", "textures/hero.png").Updated;

        INamedTypeSymbol gameAssets = compiled.GetTypeByMetadataName("Capsule.Assets.Generated.GameAssets")!;
        Assert.NotNull(gameAssets);
        INamedTypeSymbol audio = gameAssets.GetTypeMembers().First(t => t.Name == "Audio");
        INamedTypeSymbol textures = gameAssets.GetTypeMembers().First(t => t.Name == "Textures");
        Assert.NotNull(audio.GetMembers("FootstepStone").FirstOrDefault());
        Assert.NotNull(textures.GetMembers("Hero").FirstOrDefault());
    }

    [Fact]
    public void EveryDomain_IsDeclaredWhateverTheGameAuthored()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: true).Updated;

        INamedTypeSymbol gameAssets = compiled.GetTypeByMetadataName("Capsule.Assets.Generated.GameAssets")!;
        Assert.NotNull(gameAssets);
        Assert.NotNull(gameAssets.GetTypeMembers().FirstOrDefault(t => t.Name == "Textures"));
        Assert.NotNull(gameAssets.GetTypeMembers().FirstOrDefault(t => t.Name == "Audio"));
        Assert.NotNull(gameAssets.GetTypeMembers().FirstOrDefault(t => t.Name == "Fonts"));
    }

    [Fact]
    public void TwoNamesThatCollideAsOneIdentifier_FailTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/foot-step.ogg", "audio/foot_step.wav").Diagnostics;

        Assert.Equal("CAP016", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void OneNameInTwoDomains_IsTwoAssets()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/hero.ogg", "textures/hero.png");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        INamedTypeSymbol gameAssets = compiled.GetTypeByMetadataName("Capsule.Assets.Generated.GameAssets")!;
        Assert.NotNull(gameAssets);
        INamedTypeSymbol audio = gameAssets.GetTypeMembers().First(t => t.Name == "Audio");
        INamedTypeSymbol textures = gameAssets.GetTypeMembers().First(t => t.Name == "Textures");
        Assert.NotNull(audio.GetMembers("Hero").FirstOrDefault());
        Assert.NotNull(textures.GetMembers("Hero").FirstOrDefault());
    }

    [Theory]
    [InlineData("audio/audio.wav")]
    [InlineData("textures/all.png")]
    public void AnAssetTakingANameItsDomainReserves_FailsTheBuild(string asset)
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, asset).Diagnostics;

        Assert.Equal("CAP018", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    // Boot loads exactly the textures the build registered, so the whole domain has to be
    // nameable in one place the generated registry provider can read.
    [Fact]
    public void EveryTexture_ReachesTheBootRegistryThroughOneList()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) =
            GeneratorHarness.CompileWithAssets(logic: true, "textures/hero.png", "textures/tiles.png", "audio/hit.wav");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.GameAssetsFile);
        Assert.Contains("TextureHandle[] All", generated, StringComparison.Ordinal);
        Assert.Contains("Hero,", generated, StringComparison.Ordinal);
        Assert.Contains("Tiles,", generated, StringComparison.Ordinal);

        Assert.Contains(
            "GameAssets.Textures.All",
            GeneratorHarness.Emitted(compiled, GeneratorHarness.RegistryProviderFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheGeneratedRegistry_CompilesOverEveryDomain()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(
            logic: true,
            "audio/footstep-stone.ogg",
            "textures/hero.png",
            "fonts/body_text.ttf").Updated;

        Assert.NotNull(compiled.GetTypeByMetadataName("Capsule.Assets.Generated.GameAssets"));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));
    }

    [Theory]
    [InlineData("textures/01-intro.png")]
    [InlineData("textures/hero sprite.png")]
    public void AFileNameThatCannotBecomeAnIdentifier_FailsTheBuild(string asset)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithAssets(logic: true, asset).Diagnostics;

        Assert.Equal("CAP017", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void TheShell_GetsNoRegistry()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: false, "textures/hero.png").Updated;

        Assert.Null(GeneratorHarness.Emission(compiled, GeneratorHarness.GameAssetsFile));
    }
}
