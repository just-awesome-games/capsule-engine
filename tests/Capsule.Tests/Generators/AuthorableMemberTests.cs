using System.Collections.Immutable;
using System.Globalization;
using Capsule.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Capsule.Tests.Generators;

// The generator checks each [Authorable] member where it is declared.
public sealed class AuthorableMemberTests
{
    // A tile type refuses more member forms than an entity does.
    private const string Tile = "Lift : Capsule.Tiles.TileType";

    [Theory]
    [InlineData("[Authorable] public readonly float Stops = 64f;", "is readonly. Drop readonly, or drop [Authorable]", "")]
    [InlineData("[Authorable] public int Stops => 1;", "has no setter. Add a set or init accessor of any access", "")]
    [InlineData("[Authorable] public static int Stops { get; set; }", "is static. A placement sets one entity's member", "")]
    [InlineData("[Authorable] public required int Stops { get; init; }", "is required, which only an entity reference carries. Drop required and write [Authorable(Required = true)]", "")]
    [InlineData("[Authorable] public System.Collections.Generic.List<int> Stops { get; set; } = [];", "has type 'System.Collections.Generic.List<int>', which a scene document cannot carry. An array is the one collection a document writes. Declare 'int[]'", "")]
    [InlineData("[Authorable] public Vector2?[] Stops { get; set; } = [];", "has type 'System.Numerics.Vector2?[]', whose elements may be null. Make the elements non-nullable", "")]
    [InlineData("[Authorable] public Sides Stops { get; set; } = Sides.Up;", "has type 'Game.Sides', which a scene document cannot carry", "public sealed class Sides { public static readonly Sides Up = new(); }")]
    [InlineData("[Authorable] public Tuning Stops { get; set; } = Tuning.Default;", "has type 'Game.Tuning', which a scene document cannot carry", "public record Tuning { public int Speed { get; set; } public static readonly Tuning Default = new(); }")]
    [InlineData("[Authorable] public Wide Stops { get; set; } = new();", "has type 'Game.Wide', whose [JsonConverter] names 'Game.Wrong'", "[System.Text.Json.Serialization.JsonConverter(typeof(Wrong))] public sealed class Wide; public sealed class Wrong : System.Text.Json.Serialization.JsonConverter<int> { public override int Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => 0; public override void Write(System.Text.Json.Utf8JsonWriter writer, int value, System.Text.Json.JsonSerializerOptions options) { } }")]
    [InlineData("[Authorable] public override int Stops => 1;", "has no setter. Add a set or init accessor of any access", "public abstract class Mount(EntitySpawn spawn) : Entity(spawn) { public virtual int Stops => 0; }", "Lift(EntitySpawn spawn) : Mount(spawn)")]
    [InlineData("[Authorable(Required = true)] public int Stops { get; init; }", "is Required = true, which a tile type refuses. Drop Required = true and give the member a default", "", Tile)]
    [InlineData("[Authorable] public Puck? Stops { get; init; }", "is an entity reference, which a tile type shared by every cell cannot hold. Drop [Authorable]", "public sealed class Puck(EntitySpawn spawn) : Entity(spawn);", Tile)]
    [InlineData("[Authorable] public Entity? Stops { get; init; }", "is an entity reference, which a tile type shared by every cell cannot hold. Drop [Authorable]", "", Tile)]
    [InlineData("[Authorable] public required int Stops { get; init; }", "is required, which a tile type refuses. Drop required and give the member a default", "", Tile)]
    public void AnAuthorableMemberNoPlacementCanSet_FailsAtTheMemberNamingTheFix(string member, string fix, string declarations, string lift = "Lift(EntitySpawn spawn) : Entity(spawn)")
    {
        string source = $$"""
            {{GeneratorHarness.Preamble}}

            {{declarations}}

            public sealed class {{lift}}
            {
                {{member}}
            }
            """;

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(GeneratorHarness.Compile(source).Diagnostics));
        Assert.Equal("CAP041", refused.Id);
        Assert.Equal("'Game.Lift.Stops' " + fix, refused.GetMessage(CultureInfo.InvariantCulture)[..("'Game.Lift.Stops' " + fix).Length]);
        AssertAt(refused, source, "Stops");
    }

    // The applier writes the field through an accessor the compiler cannot see.
    [Fact]
    public async Task AnAuthorableField_IsNotReportedAsNeverAssigned()
    {
        string source = $$"""
            {{GeneratorHarness.Preamble}}

            public sealed class Lift : Entity
            {
                public Lift(EntitySpawn spawn) : base(spawn) => Seen = _rise;

                public float Seen { get; }

                [Authorable]
                private float _rise;
            }
            """;
        CompilationWithAnalyzersOptions options = new(new AnalyzerOptions([]), null, false, false, reportSuppressedDiagnostics: true);

        ImmutableArray<Diagnostic> diagnostics = await GeneratorHarness.Compile(source).Updated
            .WithAnalyzers([new AuthorableSuppressor()], options)
            .GetAllDiagnosticsAsync();

        Assert.True(Assert.Single(diagnostics, static diagnostic => diagnostic.Id == "CS0649").IsSuppressed);
    }

    // The later member is the error, whether it sits in the same class or a derived one.
    [Fact]
    public void TwoAuthorableMembersTakingOneKey_FailAtTheLater()
    {
        string source = $$"""
            {{GeneratorHarness.Preamble}}

            public abstract class Page(EntitySpawn spawn) : Entity(spawn)
            {
                [Authorable]
                public string URL { get; set; } = "";
            }

            public sealed class Link(EntitySpawn spawn) : Page(spawn)
            {
                [Authorable]
                private string _url = "";
            }
            """;

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(GeneratorHarness.Compile(source).Diagnostics));
        Assert.Equal("CAP033", refused.Id);
        Assert.Equal(
            "'Game.Link._url' takes the key 'url', which the [Authorable] member 'URL' already takes. Rename one of them",
            refused.GetMessage(CultureInfo.InvariantCulture));
        AssertAt(refused, source, "_url");
    }

    // The diagnostic sits on the member's own name.
    private static void AssertAt(Diagnostic refused, string source, string member)
    {
        FileLinePositionSpan at = refused.Location.GetLineSpan();
        string line = source.ReplaceLineEndings("\n").Split('\n')[at.StartLinePosition.Line];

        Assert.Equal(member, line.Substring(at.StartLinePosition.Character, member.Length));
    }
}
