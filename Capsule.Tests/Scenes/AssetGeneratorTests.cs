using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The <c>GameAssets</c> registry: what the build ships is what the game can name, and every way
/// a file name can fail to become a member is a build error rather than a member nobody meant.
/// </summary>
public sealed class AssetGeneratorTests
{
    // The naming contract in both directions: the member is the file's stem PascalCased, and the
    // handle it hands out carries that stem back — which is what a loader will resolve against.
    [Fact]
    public void AnAsset_BecomesATypedHandleUnderItsDomain()
    {
        string generated = GeneratorHarness.Emitted(
            GeneratorHarness.CompileWithAssets(logic: true, "audio/footstep-stone.ogg", "textures/hero.png").Updated,
            GeneratorHarness.GameAssetsFile);

        Assert.Contains(
            "public static global::Capsule.Assets.AudioHandle FootstepStone => new global::Capsule.Assets.AudioHandle(\"footstep-stone\");",
            generated,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static global::Capsule.Assets.TextureHandle Hero => new global::Capsule.Assets.TextureHandle(\"hero\");",
            generated,
            StringComparison.Ordinal);
    }

    // A domain a game has authored nothing in still exists, so a call site naming one always
    // compiles and only the asset it names can be missing.
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

    // The shipped tree keeps these two apart and one identifier cannot, so the build has to say so
    // rather than emit a member declared twice.
    [Fact]
    public void TwoNamesThatCollideAsOneIdentifier_FailTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/foot-step.ogg", "audio/foot_step.wav").Diagnostics;

        Assert.Equal("CAP016", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    // The same stem in two domains is two members on two classes, and never a collision.
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

    // A member may not carry the name of the class it is declared on, and a domain's class is
    // where every asset in that domain lands.
    [Fact]
    public void AnAssetNamedAfterItsDomain_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/audio.wav").Diagnostics;

        Assert.Equal("CAP018", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    // The registry is rendered as text, so the rules above are the only thing standing between an
    // authored file name and source the compiler rejects. This is the spec that reads the result
    // the way the compiler will.
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

    // The registry belongs to the assembly holding the game's classes. The shell ships the bytes
    // and would only shadow the registry its logic assemblies already publish.
    [Fact]
    public void TheShell_GetsNoRegistry()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: false, "textures/hero.png").Updated;

        Assert.Null(GeneratorHarness.Emission(compiled, GeneratorHarness.GameAssetsFile));
    }
}
