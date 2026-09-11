using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

public sealed class AssetGeneratorTests
{
    [Fact]
    public void AnAsset_BecomesATypedHandleUnderItsDomain()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: true, "textures/body-text.png", "textures/hero.png").Updated;

        INamedTypeSymbol capsuleAssets = compiled.GetTypeByMetadataName("Capsule.Assets.Generated.CapsuleAssets")!;
        Assert.NotNull(capsuleAssets);
        INamedTypeSymbol textures = capsuleAssets.GetTypeMembers().First(t => t.Name == "Textures");
        Assert.NotNull(textures.GetMembers("BodyText").FirstOrDefault());
        Assert.NotNull(textures.GetMembers("Hero").FirstOrDefault());
    }

    [Fact]
    public void EveryDomain_IsDeclaredWhateverTheGameAuthored()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: true).Updated;

        INamedTypeSymbol capsuleAssets = compiled.GetTypeByMetadataName("Capsule.Assets.Generated.CapsuleAssets")!;
        Assert.NotNull(capsuleAssets);
        Assert.NotNull(capsuleAssets.GetTypeMembers().FirstOrDefault(t => t.Name == "Textures"));
    }

    [Fact]
    public void TwoNamesThatCollideAsOneIdentifier_FailTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, "textures/foot-step.png", "textures/foot_step.png").Diagnostics;

        Assert.Equal("CAP016", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Theory]
    [InlineData("textures/all.png")]
    public void AnAssetTakingANameItsDomainReserves_FailsTheBuild(string asset)
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, asset).Diagnostics;

        Assert.Equal("CAP018", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    // Boot loads exactly the textures the build registered, from one place it can read.
    [Fact]
    public void EveryTexture_IsDeclaredAndHeldByItsDomainsSet()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) =
            GeneratorHarness.CompileWithAssets(logic: true, "textures/hero.png", "textures/tiles.png");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleAssetsFile);
        Assert.Contains("ReadOnlySpan<global::Capsule.Assets.TextureHandle> All", generated, StringComparison.Ordinal);
        Assert.Contains("Hero,", generated, StringComparison.Ordinal);
        Assert.Contains("Tiles,", generated, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("textures/01-intro.png")]
    [InlineData("textures/hero sprite.png")]
    public void AFileNameThatCannotBecomeAnIdentifier_FailsTheBuild(string asset)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithAssets(logic: true, asset).Diagnostics;

        Assert.Equal("CAP017", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    // A source's directory under its domain root is its class path, its handle's name, and where
    // the build ships it.
    [Fact]
    public void ADirectoryUnderADomainRoot_BecomesANestedClass()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) =
            GeneratorHarness.CompileWithAssets(logic: true, "textures/enemies/bat.png", "textures/tiles.png");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));

        INamedTypeSymbol textures = compiled
            .GetTypeByMetadataName("Capsule.Assets.Generated.CapsuleAssets")!
            .GetTypeMembers()
            .First(type => type.Name == "Textures");

        // A file at the root stays where it was, so a flat tree compiles unchanged.
        Assert.NotNull(textures.GetMembers("Tiles").FirstOrDefault());

        INamedTypeSymbol enemies = textures.GetTypeMembers().First(type => type.Name == "Enemies");
        Assert.NotNull(enemies.GetMembers("Bat").FirstOrDefault());

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleAssetsFile);
        Assert.Contains("TextureHandle(\"enemies/bat\", \".png\")", generated, StringComparison.Ordinal);
    }

    // Every class carries every handle beneath it, its subdirectories' included; boot reads the
    // root's.
    [Fact]
    public void EveryClass_CarriesEveryHandleBeneathItTransitively()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileWithAssets(
            logic: true,
            "textures/enemies/cave/bat.png",
            "textures/enemies/slime.png",
            "textures/tiles.png");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Empty(GeneratorHarness.Errors(compiled.GetDiagnostics()));

        string generated = GeneratorHarness.Emitted(compiled, GeneratorHarness.CapsuleAssetsFile);

        // The root's set holds all three; the cave's holds only its own.
        Assert.Contains("\nEnemies.Cave.Bat,\n", Normalized(generated), StringComparison.Ordinal);
        Assert.Contains("\nEnemies.Slime,\n", Normalized(generated), StringComparison.Ordinal);
        Assert.Contains("\nCave.Bat,\n", Normalized(generated), StringComparison.Ordinal);
    }

    // A stem repeated in two directories is two assets; only one repeated in one collides, and a
    // directory collides with a file beside it just as a file does.
    [Theory]
    [InlineData("textures/enemies/bat.png", "textures/player/bat.png", null, null)]
    [InlineData("textures/enemies/a-b.png", "textures/enemies/a_b.png", "textures/enemies/a-b.png", "textures/enemies/a_b.png")]
    [InlineData("textures/bat/wing.png", "textures/bat.png", "textures/bat.png", "textures/bat/")]
    public void OnlyNamesInOneDirectoryCollide(string first, string second, string? names, string? andAlso)
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, first, second).Diagnostics;

        if (names is null)
        {
            Assert.Empty(GeneratorHarness.Errors(diagnostics));

            return;
        }

        Diagnostic error = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal("CAP016", error.Id);
        Assert.Contains(names, error.GetMessage(), StringComparison.Ordinal);
        Assert.Contains(andAlso!, error.GetMessage(), StringComparison.Ordinal);
    }

    // A member may not carry the name of the class it is declared on, and every class declares All.
    [Theory]
    [InlineData("textures/enemies/enemies.png")]
    [InlineData("textures/textures/tiles.png")]
    [InlineData("textures/enemies/all.png")]
    [InlineData("textures/all/tiles.png")]
    public void ANameItsEnclosingClassReserves_FailsTheBuild(string asset)
    {
        ImmutableArray<Diagnostic> diagnostics = GeneratorHarness.CompileWithAssets(logic: true, asset).Diagnostics;

        Assert.Equal("CAP018", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    [Fact]
    public void ADirectoryNameThatCannotBecomeAnIdentifier_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics =
            GeneratorHarness.CompileWithAssets(logic: true, "textures/01-intro/hero.png").Diagnostics;

        Assert.Equal("CAP017", Assert.Single(GeneratorHarness.Errors(diagnostics)).Id);
    }

    private static string Normalized(string generated) =>
        generated.Replace("\r\n", "\n", StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);

    // Audio ships like every other asset and reaches this generator the same way, but its registry
    // is the build tool's: the tool measures each clip's duration, which no compiler can read.
    [Fact]
    public void AnAudioSource_IsNoAssetOfThisGenerators()
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) =
            GeneratorHarness.CompileWithAssets(logic: true, "audio/steps/01 stone.wav", "textures/hero.png");

        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        INamedTypeSymbol capsuleAssets = compiled.GetTypeByMetadataName("Capsule.Assets.Generated.CapsuleAssets")!;
        Assert.Null(capsuleAssets.GetTypeMembers().FirstOrDefault(t => t.Name == "Audio"));
    }

    [Fact]
    public void TheShell_GetsNoRegistry()
    {
        Compilation compiled = GeneratorHarness.CompileWithAssets(logic: false, "textures/hero.png").Updated;

        Assert.Null(GeneratorHarness.Emission(compiled, GeneratorHarness.CapsuleAssetsFile));
    }
}
