using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

public sealed class AssetGeneratorTests
{
    [Fact]
    public void AnAsset_BecomesATypedHandleUnderItsDomain()
    {
        string generated = GeneratorHarness.Emitted(
            GeneratorHarness.CompileWithAssets(logic: true, "audio/footstep-stone.ogg", "textures/hero.png").Updated,
            GeneratorHarness.GameAssetsFile);

        Assert.Contains(
            "public static global::Capsule.Assets.AudioHandle FootstepStone => new global::Capsule.Assets.AudioHandle(\"footstep-stone\", \".ogg\");",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Capsule.Assets.TextureHandle Hero => new global::Capsule.Assets.TextureHandle(\"hero\", \".png\");",
            generated,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDomain_IsDeclaredWhateverTheGameAuthored()
    {
        string generated = GeneratorHarness.Emitted(
            GeneratorHarness.CompileWithAssets(logic: true).Updated,
            GeneratorHarness.GameAssetsFile);

        Assert.Contains("public static class Textures", generated, StringComparison.Ordinal);
        Assert.Contains("public static class Audio", generated, StringComparison.Ordinal);
        Assert.Contains("public static class Fonts", generated, StringComparison.Ordinal);
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
        (ImmutableArray<Diagnostic> diagnostics, Compilation updated) =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/hero.ogg", "textures/hero.png");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        string generated = GeneratorHarness.Emitted(updated, GeneratorHarness.GameAssetsFile);
        Assert.Contains("AudioHandle Hero", generated, StringComparison.Ordinal);
        Assert.Contains("TextureHandle Hero", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssetNamedAfterItsDomain_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/audio.wav").Diagnostics;

        Assert.Equal("CAP018", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
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
