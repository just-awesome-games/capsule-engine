using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using Capsule.Rendering;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Capsule.Scenes.Spawning;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// A placement sets the members its class marks [Authorable], and the base constructor sets them before the
// derived body runs. The generator checks each document entry against its class.
public sealed class AuthorableTests
{
    private const string Room = "scenes/room.scene.json";

    private const string RectFix = "Write [left, top, right, bottom] with all four finite, right no less than left and bottom no less than top";

    private const string Game = """
        using System;
        using System.Numerics;
        using System.Text.Json;
        using System.Text.Json.Serialization;
        using Capsule.Rendering;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        public enum Biome { Cave, IceCave }

        public sealed record Tuning(float Speed)
        {
            public static readonly Tuning Default = new(1f);
            public static readonly Tuning Heavy = new(0.5f);
        }

        public abstract class Platform : Entity
        {
            protected Platform(EntitySpawn spawn) : base(spawn) { }

            [Authorable]
            public string? Label { get; set; } = "platform";
        }

        // The constructor records what it reads, so every assertion is about the body's view.
        public sealed class Lift : Platform
        {
            public Lift(EntitySpawn spawn) : base(spawn) => Seen = FormattableString.Invariant(
                $"{Rise}|{_swingTicks}|{Biome}|{Size?.ToString() ?? "none"}|{Label ?? "none"}|{Tint.B}|{Tuning.Speed}");

            [Authorable(Required = true)]
            public float Rise { get; init; }

            [Authorable]
            public Biome Biome { get; private set; } = Biome.Cave;

            [Authorable]
            public Vector2? Size { get; set; } = Vector2.One;

            [Authorable]
            internal ColorRgba Tint { get; set; } = new(1, 2, 3);

            [Authorable]
            public Tuning Tuning { get; set; } = Tuning.Default;

            public string Seen { get; }

            [Authorable]
            private int _swingTicks = 150;
        }

        public sealed class Lamp : Entity
        {
            public Lamp(EntitySpawn spawn) : base(spawn) => Glow = 5;

            [Authorable]
            public int Glow { get; set; }
        }

        [JsonConverter(typeof(RouteConverter))]
        public readonly record struct Route(string From, string To);

        public sealed class RouteConverter : JsonConverter<Route>
        {
            public override Route Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                string[] ends = reader.GetString()!.Split('>');

                return ends.Length == 2 ? new Route(ends[0], ends[1]) : throw new JsonException("Write a route as from>to.");
            }

            public override void Write(Utf8JsonWriter writer, Route value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.From + ">" + value.To);
        }

        public sealed class Gate(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable(Required = true)]
            public Route Route { get; set; }

            [Authorable]
            public Seal? Seal { get; set; }

            [Authorable]
            public Rect? Bounds { get; set; }
        }

        [JsonConverter(typeof(SealConverter))]
        public sealed record Seal;

        public sealed class SealConverter : JsonConverter<Seal>
        {
            public SealConverter() => throw new InvalidOperationException("The seal registry is closed.");

            public override Seal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new();

            public override void Write(Utf8JsonWriter writer, Seal value, JsonSerializerOptions options) { }
        }

        // A member of a generic base that only a derived class can set goes through a generic accessor
        // carrying the base's constraints.
        public interface IShape;

        public sealed class Square : IShape;

        public abstract class Held<T>(EntitySpawn spawn) : Entity(spawn)
            where T : class, IShape, new()
        {
            [Authorable]
            public int Stops { get; protected set; } = 1;
        }

        public sealed class Hook(EntitySpawn spawn) : Held<Square>(spawn);

        // A derived member of any kind hides the base member of its name.
        public sealed class Crank(EntitySpawn spawn) : Platform(spawn)
        {
            public new void Label() { }
        }

        // A private member does not hide the public one it shares a name with, as in C# member lookup.
        public sealed class Sign(EntitySpawn spawn) : Platform(spawn)
        {
            private new string? Label { get; set; } = "private";
        }

        // An override is the member it overrides, so the base's contract holds and its setter runs.
        public abstract class Rising(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable]
            public virtual float Rise { get; set; } = 1f;
        }

        public sealed class Balloon(EntitySpawn spawn) : Rising(spawn)
        {
            public override float Rise { get => base.Rise; set => base.Rise = value * 2f; }
        }

        // Placed from code only, since only code satisfies C#'s required on a member no placement sets.
        public sealed class Crane(EntitySpawn spawn) : Entity(spawn)
        {
            public required Entity Owner { get; init; }
        }
        """;

    [Fact]
    public void TheConstructorBody_SeesEveryAuthoredValue()
    {
        Assert.Equal(
            "96.5|90|IceCave|none|none|3|0.5",
            Seen(
                Composed(
                    """{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 96.5, "swingTicks": 90, "biome": "iceCave", "size": null, "label": null, "tuning": "heavy"}}""")));
    }

    [Fact]
    public void AnAbsentKey_KeepsTheInitializer()
    {
        Assert.Equal(
            "1|150|Cave|<1, 1>|platform|3|1",
            Seen(Composed("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1}}""")));
    }

    [Fact]
    public void ABodyWrite_WinsOverTheDocument()
    {
        Entity lamp = Composed("""{"id": 3, "type": "lamp", "x": 0, "y": 0, "properties": {"glow": 9}}""");

        Assert.Equal(5, lamp.GetType().GetProperty("Glow")!.GetValue(lamp));
    }

    [Fact]
    public void AConvertedType_IsReadThroughTheConverterItsTypeDeclares()
    {
        Entity gate = Composed("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "hall>vault"}}""");

        Assert.Equal("Route { From = hall, To = vault }", Text(gate.GetType().GetProperty("Route")!.GetValue(gate)));
    }

    [Fact]
    public void ARect_IsReadFromItsFourEdges()
    {
        Entity gate = Composed("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "bounds": [8, 16, 40.5, 48]}}""");

        Assert.Equal(new Rect(8f, 16f, 40.5f, 48f), gate.GetType().GetProperty("Bounds")!.GetValue(gate));
    }

    [Fact]
    public void AMemberOfAGenericBase_IsSet()
    {
        Entity hook = Composed("""{"id": 3, "type": "hook", "x": 0, "y": 0, "properties": {"stops": 5}}""");

        Assert.Equal(5, hook.GetType().BaseType!.GetProperty("Stops")!.GetValue(hook));
    }

    [Fact]
    public void AKeyOfAPublicBaseMember_SetsItPastAPrivateDerivedMemberOfTheSameName()
    {
        Entity sign = Composed("""{"id": 3, "type": "sign", "x": 0, "y": 0, "properties": {"label": "exit"}}""");

        Assert.Equal("exit", sign.GetType().BaseType!.GetProperty("Label")!.GetValue(sign));
    }

    [Fact]
    public void AnUnmarkedOverride_KeepsTheBaseContract_AndIsSetThroughTheOverride()
    {
        string entry = """{"id": 3, "type": "balloon", "x": 0, "y": 0, "properties": {"rise": 5}}""";
        (ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Room, Document(entry)));
        Entity balloon = Composed(entry);

        Assert.Empty(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(10f, balloon.GetType().GetProperty("Rise")!.GetValue(balloon));
    }

    // The class compiles clean and stays out of the registry. A document entry placing it fails (CAP035).
    [Fact]
    public void ACodeOnlyEntity_IsNotRegistered()
    {
        SpawnException failure = Assert.Throws<SpawnException>(() => Composed("""{"id": 3, "type": "crane", "x": 0, "y": 0}"""));

        Assert.Contains("A class with a C# required member other than an entity reference is placed in code only", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"id": 3, "type": "elevator", "x": 0, "y": 0}""", "CAP034", "'elevator', which no entity claims")]
    [InlineData("""{"id": 3, "type": "crane", "x": 0, "y": 0}""", "CAP035", "has the C# required members Owner, which only code can set")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1, "height": 2}}""", "CAP036", "authorable members are: label, rise, biome, size, tint, tuning, swingTicks")]
    [InlineData("""{"id": 3, "type": "crank", "x": 0, "y": 0, "properties": {"label": "a"}}""", "CAP036", "authorable members are: none")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1, "seen": "a"}}""", "CAP037", "sets 'seen', but 'Game.Lift.Seen' is not [Authorable]. Mark it [Authorable]")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1, "swingTicks": 1.5}}""", "CAP038", "takes int")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": null}}""", "CAP038", "or make the member nullable")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1, "biome": "lava"}}""", "CAP039", "Write one of: cave, iceCave")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1, "tuning": "light"}}""", "CAP039", "Write one of: default, heavy")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0}""", "CAP040", "omits 'rise'")]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "bounds": [8, 16, 40]}}""", "CAP038", RectFix)]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "bounds": [40, 16, 8, 48]}}""", "CAP038", RectFix)]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "bounds": [8, 48, 40, 16]}}""", "CAP038", RectFix)]
    public void AnEntryItsClassRefuses_FailsTheBuildAtTheEntryNamingTheDocumentEntryAndFix(string entry, string id, string fix)
    {
        (ImmutableArray<Diagnostic> diagnostics, _) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Room, Document(entry)));

        Diagnostic refused = Assert.Single(GeneratorHarness.Errors(diagnostics));
        Assert.Equal(id, refused.Id);
        string message = refused.GetMessage(CultureInfo.InvariantCulture);
        Assert.Contains("'scenes/room'", message, StringComparison.Ordinal);
        Assert.Contains("entity 3", message, StringComparison.Ordinal);
        Assert.Contains(fix, message, StringComparison.Ordinal);

        // The error opens the document at the entry's own brace.
        FileLinePositionSpan at = refused.Location.GetLineSpan();
        Assert.Equal(
            (Room, 0, Document(entry).IndexOf(entry, StringComparison.Ordinal)),
            (at.Path, at.StartLinePosition.Line, at.StartLinePosition.Character));
    }

    // The generator reads the build's facts from source, and the compiled game carries none of them.
    [Fact]
    public void ThePlacementFacts_StayOutOfTheCompiledAssembly()
    {
        string entry = """{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1}}""";
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(Game, logic: true, (Room, Document(entry)));
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Type documents = GeneratorHarness.Loaded(compiled).GetType("Capsule.Generated.Documents")!;

        Assert.Empty(documents.GetFields().SelectMany(static field => field.CustomAttributes));
    }

    // At load the applier reads only the members it sets, and a wrong value form, a missing required member
    // or a converter that throws each fails the load. An unknown key is the build's check alone (CAP036).
    [Theory]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": "high"}}""", "the member takes float. Write a finite number.")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"swingTicks": 5}}""", "omits 'rise'")]
    [InlineData("""{"id": 3, "type": "lift", "x": 0, "y": 0, "properties": {"rise": 1, "tuning": "light"}}""", "Write one of: default, heavy.")]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "vault"}}""", "Write a route as from>to.")]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": null}}""", "to null, which only a nullable member accepts")]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "seal": 1}}""", "SealConverter could not read as Seal: The seal registry is closed.")]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "bounds": [40, 16, 8, 48]}}""", "the member takes Rect. " + RectFix)]
    [InlineData("""{"id": 3, "type": "gate", "x": 0, "y": 0, "properties": {"route": "a>b", "bounds": [8, 48, 40, 16]}}""", "the member takes Rect. " + RectFix)]
    public void AnEntryItsClassRefusesAtLoad_ThrowsNamingTheDocumentEntryAndFix(string entry, string fix)
    {
        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(() => Composed(entry));

        Assert.StartsWith("scene document 'scenes/room': entity id 3 (", failure.Message, StringComparison.Ordinal);
        Assert.Contains(fix, failure.Message, StringComparison.Ordinal);
    }

    private static string Document(string entry) =>
        "{\"formatVersion\": 8, \"entities\": [" + entry + "], \"nextEntityId\": 4}";

    // Composes the room through the generated registry, compiled with no document so the build's check
    // does not stand in front of the load-time one.
    private static Entity Composed(string entry)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.Compile(Game);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly game = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)game.GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;

        return Assert.Single(registry.Create(new SceneKey("scenes/room"), SceneDocumentFile.Parse(Document(entry))).Entities.ToArray());
    }

    private static string? Seen(Entity lift) => Text(lift.GetType().GetProperty("Seen")!.GetValue(lift));

    private static string? Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture);
}
