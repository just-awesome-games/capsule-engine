using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Capsule.Assets;
using Capsule.Audio;
using Capsule.Scenes;
using Capsule.Scenes.Documents;
using Microsoft.CodeAnalysis;

namespace Capsule.Tests.Generators;

// An [Authorable] member may be an array, a [Flags] enum, or a texture, sound or scene the game ships, named
// by key. The build checks each element and each key against what it declared, and the load resolves a key
// to the declared asset.
public sealed class AuthorableArrayTests
{
    private const string Room = "scenes/room.scene.json";

    private const string Game = """
        #nullable enable
        using System;
        using System.Numerics;
        using Capsule.Assets;
        using Capsule.Audio;
        using Capsule.Scenes;
        using Capsule.Scenes.Spawning;

        namespace Game;

        [Flags]
        public enum Kinds { None = 0, Spikes = 1, Fire = 2, Both = Spikes | Fire }

        public sealed class Lift(EntitySpawn spawn) : Entity(spawn);

        public sealed class Shuttle(EntitySpawn spawn) : Entity(spawn);

        public sealed class Gate(EntitySpawn spawn) : Entity(spawn)
        {
            [Authorable]
            public Vector2[] Path { get; private set; } = [];

            [Authorable]
            public Kinds Kinds { get; set; }

            [Authorable]
            public TextureHandle Icon { get; set; }

            [Authorable]
            public AudioClip[]? Chimes { get; set; }

            [Authorable]
            public SceneKey Destination { get; set; }

            [Authorable]
            public required Lift[] Lifts { get; set; }
        }
        """;

    // What the build declared beside the room: one texture, one sound and one other scene.
    private static readonly (string Path, string? Content)[] Declared =
        [("textures/hazard.png", null), ("audio/chime.wav", null), ("scenes/hall.scene.json", null)];

    [Fact]
    public void EveryElementAndKey_ResolvesAtLoad_HoweverTheKeyIsSpelt()
    {
        Scene scene = Composed(
            """
            {"id": 1, "type": "lift", "x": 0, "y": 0},
            {"id": 2, "type": "gate", "x": 0, "y": 0, "path": [[0, 0], [48, -8]], "kinds": " spikes ,both", "icon": "Textures/Hazard.PNG",
              "chimes": ["Audio/Chime.wav"], "destination": "Scenes/Hall", "lifts": [3, 1]},
            {"id": 3, "type": "lift", "x": 0, "y": 0}
            """);
        Entity[] entities = scene.Entities.ToArray();
        Entity gate = entities[1];

        Assert.Equal([Vector2.Zero, new Vector2(48, -8)], (Vector2[])Member(gate, "Path")!);
        Assert.Equal(3, (int)Member(gate, "Kinds")!);
        Assert.Equal(new TextureHandle("textures/hazard", ".png"), Member(gate, "Icon"));
        Assert.Equal(new SceneKey("scenes/hall"), Member(gate, "Destination"));

        // The duration is the one the build declared, which the authored string does not carry.
        Assert.Equal([new AudioClip("audio/chime", ".wav", 0.5)], (AudioClip[])Member(gate, "Chimes")!);
        Assert.Equal([entities[2], entities[0]], (Entity[])Member(gate, "Lifts")!);
    }

    // The entity declares no asset of its own, and the scene still loads what its placement names.
    [Fact]
    public void AnAuthoredTextureAndSound_JoinTheScenesPreload()
    {
        AssetCollection preload = Composed(
            """
            {"id": 1, "type": "lift", "x": 0, "y": 0},
            {"id": 2, "type": "gate", "x": 0, "y": 0, "icon": "textures/hazard.png", "chimes": ["audio/chime.wav"], "lifts": [1]}
            """).CollectAssetPreloads();

        Assert.Equal([new TextureHandle("textures/hazard", ".png")], preload.Textures);
        Assert.Equal([new AudioClip("audio/chime", ".wav", 0.5)], preload.Clips);
    }

    // A collection is written as an array. A converter type or a game interface that is also enumerable keeps
    // its own form, so the collection refusal comes after every form a single member accepts.
    [Theory]
    [InlineData("System.Collections.Generic.List<int> Stops { get; set; } = [];", "Declare 'int[]'")]
    [InlineData("System.Collections.Generic.IReadOnlyList<int> Stops { get; set; } = [];", "Declare 'int[]'")]
    [InlineData("Route Stops { get; set; } = new();", null)]
    [InlineData("ITrack Stops { get; set; } = null!;", null)]
    public void OnlyACollectionNoOtherFormTakes_IsRefusedForAnArray(string member, string? fix)
    {
        string source = $$"""
            {{GeneratorHarness.Preamble}}

            [System.Text.Json.Serialization.JsonConverter(typeof(RouteConverter))]
            public sealed class Route : System.Collections.Generic.IEnumerable<int>
            {
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() { yield break; }

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }

            public sealed class RouteConverter : System.Text.Json.Serialization.JsonConverter<Route>
            {
                public override Route Read(ref System.Text.Json.Utf8JsonReader reader, System.Type type, System.Text.Json.JsonSerializerOptions options) => new();

                public override void Write(System.Text.Json.Utf8JsonWriter writer, Route value, System.Text.Json.JsonSerializerOptions options) { }
            }

            public interface ITrack : System.Collections.Generic.IEnumerable<int>;

            public sealed class Rail(EntitySpawn spawn) : Entity(spawn)
            {
                [Authorable]
                public {{member}}
            }
            """;

        IEnumerable<Diagnostic> errors = GeneratorHarness.Errors(GeneratorHarness.Compile(source).Diagnostics);

        if (fix is null)
        {
            Assert.Empty(errors);
        }
        else
        {
            Diagnostic refused = Assert.Single(errors);
            Assert.Equal("CAP041", refused.Id);
            Assert.EndsWith(fix, refused.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }
    }

    // A document the build never saw gets the same checks when the scene loads.
    [Theory]
    [InlineData("\"path\": [[0, 0], \"x\"], \"lifts\": [1]", "sets 'path' element 1 to the string \"x\", but the member takes Vector2.")]
    [InlineData("\"kinds\": \"spikes, lava\", \"lifts\": [1]", "whose \"lava\" names nothing the member accepts.")]
    [InlineData("\"icon\": \"textures/nope.png\", \"lifts\": [1]", "sets 'icon' to the string \"textures/nope.png\", but no TextureHandle keys as \"textures/nope.png\".")]
    [InlineData("\"lifts\": [1, 5]", "sets 'lifts' element 1 to entity 5, a Shuttle, but the member takes Lift.")]
    public void AnElementOrKeyTheLoadCannotResolve_FailsTheLoad(string properties, string fix)
    {
        string entries = $$$"""
            {"id": 1, "type": "lift", "x": 0, "y": 0},
            {"id": 2, "type": "gate", "x": 0, "y": 0, {{{properties}}}},
            {"id": 5, "type": "shuttle", "x": 0, "y": 0}
            """;

        SceneDocumentFormatException failure = Assert.Throws<SceneDocumentFormatException>(() => Composed(entries));

        Assert.StartsWith("scene document 'scenes/room': entities[1] ('gate') ", failure.Message, StringComparison.Ordinal);
        Assert.Contains(fix, failure.Message, StringComparison.Ordinal);
    }

    private static string Document(string entries) =>
        "{\"entities\": [" + entries + "]}";

    // Composes the room through the generated registry, compiled against the declared assets.
    private static Scene Composed(string entries)
    {
        (ImmutableArray<Diagnostic> diagnostics, Compilation compiled) = GeneratorHarness.CompileAgainstSources(Game, logic: true, Declared);
        Assert.Empty(GeneratorHarness.Errors(diagnostics));

        Assembly game = GeneratorHarness.Loaded(compiled);
        SceneRegistry registry = (SceneRegistry)game.GetType("Capsule.Generated.CapsuleScenes")!
            .GetProperty("Registry")!.GetValue(null)!;

        return registry.Create(new SceneKey("scenes/room"), SceneDocumentFile.Parse(Document(entries)));
    }

    private static object? Member(Entity entity, string name) => entity.GetType().GetProperty(name)!.GetValue(entity);
}
