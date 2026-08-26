using System.Collections.Immutable;
using Capsule.Scenes;
using Capsule.Scenes.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Capsule.Tests.Scenes;

/// <summary>
/// The generator hosted the way the compiler hosts it, over compilations built here. Every
/// mistake it is meant to catch is a compile error in a game, so each one is asserted as a
/// diagnostic rather than as a broken build.
/// </summary>
public sealed class LevelTypeGeneratorTests
{
    private const string Preamble = """
        using System.Numerics;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;
        """;

    private static readonly ImmutableArray<MetadataReference> References = LoadReferences();

    [Fact]
    public void ATypeName_BecomesItsKebabCasedId()
    {
        string generated = Generated($$"""
            {{Preamble}}

            [LevelType]
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);

            [LevelType]
            public sealed class HealthPickup(EntitySpawn spawn) : Entity(spawn.Position);

            [LevelType]
            public sealed class HTTPProbe(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Contains("\"player\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"health-pickup\"", generated, StringComparison.Ordinal);
        Assert.Contains("\"http-probe\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExplicitId_ReplacesTheConvention()
    {
        string generated = Generated($$"""
            {{Preamble}}

            [LevelType("player-spawn")]
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """);

        Assert.Contains("\"player-spawn\"", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("\"player\"", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGeneratedRegistry_CompilesAndResolvesTheLevelType()
    {
        Compilation compiled = Compile($$"""
            {{Preamble}}

            [LevelType("player-spawn")]
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """).Updated;

        Assert.Empty(Errors(compiled.GetDiagnostics()));
    }

    [Fact]
    public void TwoClassesClaimingOneLevelType_FailTheBuildNamingBoth()
    {
        ImmutableArray<Diagnostic> diagnostics = Compile($$"""
            {{Preamble}}

            [LevelType("chest")]
            public sealed class WoodChest(EntitySpawn spawn) : Entity(spawn.Position);

            [LevelType("chest")]
            public sealed class IronChest(EntitySpawn spawn) : Entity(spawn.Position);
            """).Diagnostics;

        Diagnostic collision = Assert.Single(Errors(diagnostics));
        Assert.Equal("CAP003", collision.Id);

        string message = collision.GetMessage(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains("Game.WoodChest", message, StringComparison.Ordinal);
        Assert.Contains("Game.IronChest", message, StringComparison.Ordinal);
        Assert.Contains("chest", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ALevelTypeWithoutASpawnConstructor_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = Compile($$"""
            {{Preamble}}

            [LevelType]
            public sealed class Player : Entity
            {
                public Player() : base(Vector2.Zero)
                {
                }
            }
            """).Diagnostics;

        Assert.Equal("CAP002", Assert.Single(Errors(diagnostics)).Id);
    }

    [Theory]
    [InlineData("public abstract class Hazard : Entity { protected Hazard(EntitySpawn spawn) : base(spawn.Position) { } }")]
    [InlineData("public sealed class Marker { public Marker(EntitySpawn spawn) { } }")]
    public void ALevelTypeThatIsNotAConcreteEntity_FailsTheBuild(string declaration)
    {
        ImmutableArray<Diagnostic> diagnostics = Compile($$"""
            {{Preamble}}

            [LevelType]
            {{declaration}}
            """).Diagnostics;

        Assert.Equal("CAP001", Assert.Single(Errors(diagnostics)).Id);
    }

    [Fact]
    public void ABlankId_FailsTheBuild()
    {
        ImmutableArray<Diagnostic> diagnostics = Compile($$"""
            {{Preamble}}

            [LevelType("  ")]
            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """).Diagnostics;

        Assert.Equal("CAP004", Assert.Single(Errors(diagnostics)).Id);
    }

    [Fact]
    public void AnAssemblyWithNoLevelTypes_GetsNoRegistry()
    {
        Compilation compiled = Compile($$"""
            {{Preamble}}

            public sealed class Player(EntitySpawn spawn) : Entity(spawn.Position);
            """).Updated;

        Assert.Null(compiled.GetTypeByMetadataName("Capsule.Scenes.Generated.LevelTypes"));
    }

    private static IEnumerable<Diagnostic> Errors(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                yield return diagnostic;
            }
        }
    }

    private static string Generated(string source)
    {
        Compilation updated = Compile(source).Updated;

        return updated.SyntaxTrees.Last().ToString();
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, Compilation Updated) Compile(string source)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "LevelTypeSpecs",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver.Create(new LevelTypeGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out ImmutableArray<Diagnostic> diagnostics);

        return (diagnostics, updated);
    }

    // Whatever this test host is running against, plus Capsule.Scenes itself: the generator asks
    // the compilation for Capsule.Scenes.Entity, so a spec without it would assert nothing.
    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        string trusted = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty;
        foreach (string path in trusted.Split(Path.PathSeparator))
        {
            if (path.Length > 0)
            {
                paths.Add(path);
            }
        }

        paths.Add(typeof(Entity).Assembly.Location);
        paths.Add(typeof(object).Assembly.Location);

        ImmutableArray<MetadataReference>.Builder references = ImmutableArray.CreateBuilder<MetadataReference>(paths.Count);
        foreach (string path in paths)
        {
            references.Add(MetadataReference.CreateFromFile(path));
        }

        return references.ToImmutable();
    }
}
